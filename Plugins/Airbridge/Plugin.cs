using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.RegularExpressions;

using TrainingServer;
using TrainingServer.Extensibility;

using CIFPReader;

using CifpCoord = CIFPReader.Coordinate;

namespace Airbridge;

public class Plugin : IServerPlugin, IPlugin
{

    public string FriendlyName => "Automatic Airbridge Tool";

	public string Maintainer => "Wes (644899)";

    private readonly CIFP _cifp = CIFP.Load();

    private readonly ConcurrentDictionary<string, BridgeData> _acData = new();
	private readonly Dictionary<string, CancellationTokenSource> _bridges = new();

	private IServer? _server;

	public Plugin() { }

	#region IServerPlugin
	public bool CheckIntercept(string sender, string message) =>
		message.Trim().ToLower().Split().First() == "bridge";

	private (string Name, CifpCoord Position, (int? Below, int? Above) Altitude)[] GetRouting(string[] routing)
	{
		List<(string, CifpCoord, (int?, int?))> bridge = new();

		Regex fixAlt = new(@"/([AB])(\d{3})");
		foreach (string iFix in routing)
		{
			string fix = iFix.Trim().ToUpperInvariant();
			(int? Below, int? Above) restriction = (null, null);

			foreach (var match in fixAlt.Matches(fix).Cast<Match>())
			{
				if (match.Groups[0].Value == "A" && int.TryParse(match.Groups[1].Value, out int a))
					restriction = (restriction.Below, a);
				else if (match.Groups[0].Value == "B" && int.TryParse(match.Groups[1].Value, out int b))
					restriction = (b, restriction.Above);
				else
					continue;

				fix = fix.Replace(match.Value, "");
			}

			if (fix.Any() && fix[0] == 'H' && uint.TryParse(fix[1..], out uint heading) && heading <= 360)
				bridge.Add((fix, new() { Latitude = heading, Longitude = heading }, restriction));
			else if (_cifp.Procedures.TryGetValue(fix, out var procs) && procs.Count == 1)
				bridge.AddRange(procs.Single().SelectRoute(null, null).Where(i => i.Endpoint is ICoordinate).Select(wp => (fix, ((ICoordinate)wp.Endpoint!).GetCoordinate(), restriction)));
			else if (!_cifp.Fixes.TryConcretize(fix, out NamedCoordinate? coord, refString: iFix == routing[0] && routing.Length > 1 ? routing[1] : routing[0]))
				throw new ArgumentException($"Unknown waypoint. Are you sure {fix} is defined in your fixes file? Feel free to try a different route.");
			else
				bridge.Add((fix, coord.Value.GetCoordinate(), restriction));
		}

		return bridge.ToArray();
	}

	public string? MessageReceived(IServer server, string sender, string message)
	{
		IAircraft? query = null;
		for (int suffix = 0; query is null; ++suffix)
			query = server.SpawnAircraft(
				callsign: "SEED" + (suffix == 0 ? "" : " " + suffix),
				flightplan: new('?', '?', "1/UNKN/?-?/?", "??", "ZZZZ", new(), new(), "A000", "ZZZZ", 0, 0, 0, 0, "ZZZZ", "", ""),
				startingPosition: new() { Latitude = 0, Longitude = 0 },
				startingCourse: 0f, 0, 0
			);

		_server ??= server;

		string[] parts = message.Trim().ToUpperInvariant().Split().Skip(1).ToArray();
		string[][]? bridges =
			File.Exists("bridges.txt")
			? File.ReadAllLines("bridges.txt").Select(l => l.ToUpperInvariant().Split()).Where(b => b.Any()).ToArray()
			: null;

		if (parts.Length == 2 && bridges is not null && bridges.Any(b => b[0] == parts[0] && b[^1] == parts[1]))
		{
			// Premade scenario.
			string[] routing = bridges.First(b => b[0] == parts[0] && b[^1] == parts[1]);

			var bridge = GetRouting(routing);

			_acData[query.Callsign] = new((bridge[0].Name, bridge[0].Position), (bridge[^1].Name, bridge[^1].Position), bridge.Skip(1).SkipLast(1).ToArray(), (0, 0));

			query.SendTextMessage(server, sender, $"Loaded pre-defined airbridge from {routing[0]} to {routing[^1]}. What is the maximum bridge altitude in hundreds of feet?");
		}
		else
		{
			// Interactive mode.
			_acData[query.Callsign] = new(null, null, null, (0, 0));

			query.SendTextMessage(server, sender, "Hello! Where will your airbridge start?");
		}

		return $"Loader created. Check for a PM from {query.Callsign} to set airbridge parameters.";
	}
	#endregion

	#region IPlugin
	public bool CheckIntercept(string aircraftCallsign, string sender, string message) => _acData.ContainsKey(aircraftCallsign);

	/// <summary>Set the airbridge's data fields.</summary>
	public string? MessageReceived(IAircraft aircraft, string sender, string message)
	{
		if (_server is null)
			// Server must have created the aircraft that responded to the query.
			throw new Exception("Impossible.");

		if (!_acData.TryGetValue(aircraft.Callsign, out BridgeData curAc))
			return "This airbridge has been terminated. Please send the command 'bridge' on 123.45 to start a new one.";

		message = message.Trim().ToUpperInvariant();

		// Check what value is next and populate it.
		if (curAc.Origin is null)
		{
			if (!_cifp.Fixes.TryConcretize(message, out NamedCoordinate? coord))
				return $"Unknown starting waypoint. Are you sure {message} is defined in your fixes file? What's a nearby waypoint?";

			_acData[aircraft.Callsign] = curAc with { Origin = (message, coord.Value.GetCoordinate()) };
			return $"Okay! What's the other endpoint?";
		}
		else if (curAc.Destination is null)
		{
			if (!_cifp.Fixes.TryConcretize(message, out NamedCoordinate? coord))
				return $"Unknown destination waypoint. Are you sure {message} is defined in your fixes file? What's a nearby waypoint?";

			_acData[aircraft.Callsign] = curAc with { Destination = (message, coord.Value.GetCoordinate()) };
			return $"{curAc.Origin.Value.Item1} to {message}, got it. What fixes are on the route?";
		}
		else if (curAc.Route is null)
		{
			var route = GetRouting(message.Split());

			_acData[aircraft.Callsign] = curAc with { Route = route };
			return $"Great! What's the maximum altitude for this route in hundreds of feet?";
		}
		else if (curAc.Altitude is (0, 0))
		{
			if (!int.TryParse(message, out int maxAlt))
				return $"Hmm, that didn't look like a valid altitude. Try again? Make sure it's a whole number of hundreds of feet.";

			_acData[aircraft.Callsign] = curAc with { Altitude = (maxAlt, 0) };
			return $"Okay, what's the minimum altitude in hundreds of feet?";
		}
		else if (curAc.Altitude.Min == 0)
		{
			if (!int.TryParse(message, out int minAlt))
				return $"Hmm, that didn't look like a valid altitude. Try again? Make sure it's a whole number of hundreds of feet.";

			_acData[aircraft.Callsign] = curAc with { Altitude = (curAc.Altitude.Max, minAlt) };

			// Start the bridge!
			_bridges[aircraft.Callsign] = SpawnBridge(_acData[aircraft.Callsign]);

			return $"Got it! Starting the bridge now. Send me a PM at any time to cancel this airbridge.";
		}
		else
		{
			// Stop the bridge.
			_bridges[aircraft.Callsign].Cancel();
			_bridges.Remove(aircraft.Callsign);
			_acData.Remove(aircraft.Callsign, out _);
			return "Airbridge terminated. Hope you had a good session!";
		}
	}

	private CancellationTokenSource SpawnBridge(FinalBridgeData data)
	{
		CancellationTokenSource cts = new();
		CancellationToken token = cts.Token;
        HttpClient cli = new();

        _ = Task.Run(async () =>
		{
			HashSet<IAircraft> spawnedAircraft = new();

			while (!token.IsCancellationRequested)
			{
				//random altitude between minimum and maximum altitude given.
				int alt = Random.Shared.Next(data.Altitude.Min); //int alt = Random.Shared.Next(data.Altitude.Min / 10, data.Altitude.Max / 10 + 1) * 10;
                // set speed
                uint speed = 250;
                //uint speed = (uint)Random.Shared.Next(
                //	alt switch
                //	{
                //		>= 500 => 35,
                //		>= 180 => 25,
                //		_ => 8
                //	},
                //	alt switch
                //	{
                //		>= 600 => 120,
                //		>= 350 => 57,
                //		>= 180 => 40,
                //		_ => 25
                //	} + 1
                //) * 10;

                decimal dy = data.Origin.Item2.Latitude - data.Destination.Item2.Latitude,
					    dx = data.Origin.Item2.Longitude - data.Destination.Item2.Longitude;

				IAircraft? ac = null;


                //Select callsign from file
                List<string> callsignPrefixList = new List<string>();
                string PrefixfilePath = "config/callsignPrefixes.txt";
                if (File.Exists(PrefixfilePath))
                {
                    callsignPrefixList.AddRange(File.ReadLines(PrefixfilePath));
                }
                else
                {
                    Console.WriteLine("File not found: " + PrefixfilePath);
                    return; // Exit the program if the file is not found
                }
                Random random = new Random();
                int randomIndex = random.Next(0, callsignPrefixList.Count);
                string callsignPrefix = callsignPrefixList[randomIndex];



                //select aircraft type from file
                List<string> acTypeList = new List<string>();
                string filePath = "config/aircraftTypes.txt";
                if (File.Exists(filePath))
                {
                    acTypeList.AddRange(File.ReadLines(filePath));
                }
                else
                {
                    Console.WriteLine("File not found: " + filePath);
                    return;
                }
                Random randomAcType = new Random();
                int randomAcTypeIndex = randomAcType.Next(0, acTypeList.Count);
                string acType = acTypeList[randomAcTypeIndex];

                //select speed from file
                List<string> speedList = new List<string>();
                string SpeedfilePath = "config/speed.txt";
                if (File.Exists(SpeedfilePath))
                {
                    speedList.AddRange(File.ReadLines(SpeedfilePath));
                }
                else
                {
                    Console.WriteLine("File not found: " + SpeedfilePath);
                    return;
                }
                Random setRandomSpeed = new Random();
                int randomSpeedIndex = setRandomSpeed.Next(0, speedList.Count);
                string randomSpeedString = speedList[randomSpeedIndex];
                if (uint.TryParse(randomSpeedString, out uint randomSpeed))
                {
                    // The parsing was successful, and randomSpeed is now a uint.
                }
                else
                {
                    Console.WriteLine("Failed to parse randomSpeed as a uint.");
                }


                // Create aircraft and flightplan
                while (ac is null)
					ac = _server!.SpawnAircraft(

						callsignPrefix + new string(Enumerable.Range(0, Random.Shared.Next(0, 4)).Select(_ => Random.Shared.Next(0, 10).ToString().Single()).Prepend(Random.Shared.Next(1, 10).ToString().Single()).ToArray()),
						new('I', 'S', acType + "-?/?", "N" + speed.ToString("#000"), data.Origin.Item1, DateTime.UtcNow, DateTime.UtcNow, (alt < 180 ? "A" : "F") + alt.ToString("000"), data.Destination.Item1, 3, 0, 4, 0, "", "SEL/ABCD", string.Join(' ', data.Route!.Select(i => i.Item1))),
						new() { Latitude = (double)data.Origin.Item2.Latitude, Longitude = (double)data.Origin.Item2.Longitude },
						(float?)data.Origin.Item2.GetBearingDistance(data.Destination.Item2).bearing?.Degrees ?? 0f, // Rough approx of starting heading. Actual aircraft logic will correct this quickly.
						speed = randomSpeed,
						alt = data.Altitude.Min * 100
					);

                // Spawn the aircraft and set its initial altitude, climb rate, and speed restrictions
                // spawnedAircraft.Add(ac);
                // ac.RestrictAltitude(alt * 100, alt * 100, (uint)Random.Shared.Next(1800, 2200));
                // ac.RestrictSpeed(Math.Min(250, speed), Math.Min(250, speed), 2.5f); // Accelerate up to 250 below 10k

                spawnedAircraft.Add(ac);
                ac.RestrictAltitude(alt, alt, (uint)Random.Shared.Next(1800, 2200));
                // ac.RestrictSpeed(Math.Min(250, speed), Math.Min(250, speed), 2.5f); // Accelerate up to 250 below 10k

                // Wait until above 10000 to set high speed.
                //	if (alt >= 100)
                //	{
                //		try { _ = Task.Run(async () => { while (!token.IsCancellationRequested && ac.Altitude < 10000) await Task.Delay(500, token); ac.RestrictSpeed(speed, speed, 2.5f); }, token); }
                //		catch (TaskCanceledException) { break; }
                //	}

                Regex headingExpr = new(@"^H\d\d\d$", RegexOptions.Compiled);
				foreach (var fix in data.Route)
					if (headingExpr.IsMatch(fix.Item1))
					{
						ac.TurnCourse((float)fix.Item2.Latitude);
						ac.FlyForever();
					}
					else
						ac.FlyDirect(new() { Latitude = (double)fix.Item2.Latitude, Longitude = (double)fix.Item2.Longitude });

				CifpCoord endpoint = data.Route.Any() ? data.Route.Last().Item2 : data.Destination.Item2;

                //SQ Codes from API
                //	if (File.Exists("config/apikey.txt"))
                //	{
                //		var req = await cli.PostAsJsonAsync($"https://api.ivao.aero/v2/airports/{data.Origin.Item1}/squawks/generate?apiKey={File.ReadAllText("api.key").Trim()}", new SquawkRequest() { originIcao = data.Origin.Item1, destinationIcao = data.Destination.Item1 });
                //		var rep = await req.Content.ReadFromJsonAsync<SquawkReply>();
                //		ac.Squawk = ushort.Parse(rep?.code ?? "1000");
                //	}

                //SQ Codes (random)
                Random randomSq = new Random();
                int fourDigitNumber = 0;
                for (int i = 0; i < 4; i++)
                {
                    int digit = randomSq.Next(0, 8);
                    fourDigitNumber = fourDigitNumber * 10 + digit;
                    if (i == 3 && (fourDigitNumber == 2000 || fourDigitNumber == 1200 || fourDigitNumber == 7000 || fourDigitNumber == 7500 || fourDigitNumber == 7600 || fourDigitNumber == 7700))
                    {
                        i = 0;
                        fourDigitNumber = 0;
                    }
                }
                ushort ushortNumber = (ushort)fourDigitNumber;
                ac.Squawk = ushortNumber;

                // Kill it when it gets within 0.03 deg Euclidean distance from endpoint.
                try { _ = Task.Run(async () => { while (endpoint.DistanceTo(new((decimal)ac.Position.Latitude, (decimal)ac.Position.Longitude)) > 0.03m) await Task.Delay(500, token); ac.Kill(); }, token); }
				catch (TaskCanceledException) { break; }

                // Pause randomly before spawning another aircraft.
                string filePathDelay = "config/spawnDelay.txt";
                if (File.Exists(filePathDelay))
                {
                    string delayString = File.ReadAllText(filePathDelay);
                    if (int.TryParse(delayString, out int delayInSeconds))
                    {
                        try { await Task.Delay(TimeSpan.FromSeconds(delayInSeconds), token); }
                        catch (TaskCanceledException) { break; }
                    }
                    else
                    {
                        Console.WriteLine("Invalid delay duration format in the file.");
                    }
                }
                else
                {
                    Console.WriteLine("File not found: " + filePathDelay);
                }
            }

			foreach (var ac in spawnedAircraft)
				ac.Kill();
		}, token);

		return cts;
	}
	#endregion

	private record struct BridgeData((string, CifpCoord)? Origin, (string, CifpCoord)? Destination, (string Name, CifpCoord Position, (int? Below, int? Above) Altitude)[]? Route, (int Max, int Min) Altitude) { }

	private record struct FinalBridgeData((string, CifpCoord) Origin, (string, CifpCoord) Destination, (string Name, CifpCoord Position, (int? Below, int? Above) Altitude)[] Route, (int Max, int Min) Altitude)
	{
		public static implicit operator FinalBridgeData(BridgeData data) =>
			new(data.Origin!.Value, data.Destination!.Value, data.Route!, data.Altitude);
	}
}

internal class SquawkRequest
{
	public string originIcao { get; set; } = "";
	public string destinationIcao { get; set; } = "";
	public string flightRules { get; set; } = "I";
	public bool military { get; set; } = false;
}

internal class SquawkReply
{
    public string originMatch { get; set; } = "";
    public string destinationMatch { get; set; } = "";
    public string code { get; set; } = "2000";
}