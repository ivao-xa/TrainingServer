using System.Collections.Concurrent;
using System.Text.RegularExpressions;

using TrainingServer;
using TrainingServer.Extensibility;

namespace Airbridge;

public class Plugin : IServerPlugin, IPlugin
{
	public string FriendlyName => "Automatic Airbridge Tool";

	public string Maintainer => "Wes (644899)";

	private readonly ConcurrentDictionary<string, BridgeData> _acData = new();
	private readonly Dictionary<string, Coordinate> _fixes = new();
	private readonly Dictionary<string, CancellationTokenSource> _bridges = new();

	private IServer? _server;

	public Plugin()
	{
		if (!File.Exists("fixes.fix"))
			throw new FileNotFoundException("Could not find fixes file (fixes.fix).");

		static decimal getDeg(string input)
		{
			int multiplier = input.Any() && "SW-".Contains(input[0]) ? -1 : 1;
			input = input.TrimStart("SWEN-".ToCharArray());

			if (input.Count(c => c == '.') < 2)
				return multiplier * decimal.Parse(input);

			string[] segments = input.Split('.');

			if (segments.Length > 4)
				throw new ArgumentException("Not sure how to handle a coordinate with so many decimals!", nameof(input));

			decimal retval = 0m;
			if (segments.Length == 4)
				retval = decimal.Parse($"{segments[2]}.{segments[3]}");

			foreach (string segment in segments.Take(2).Reverse())
			{
				retval /= 60m;
				retval += decimal.Parse(segment);
			}

			return multiplier * retval;
		}

		_fixes = new(
			File.ReadAllLines("fixes.fix")
			.Select(l => l.Split(';', StringSplitOptions.TrimEntries))
			.Where(l => l.Length >= 3)
			.Select(l => (l[0], getDeg(l[1]), getDeg(l[2])))
			.Select(v => new KeyValuePair<string, Coordinate>(v.Item1.ToUpperInvariant(), new() { Latitude = (double)v.Item2, Longitude = (double)v.Item3 }))
			.DistinctBy(kvp => kvp.Key)
		);
	}

	#region IServerPlugin
	public bool CheckIntercept(string sender, string message) =>
		message.Trim().ToLower().Split().First() == "bridge";

	private (string Name, Coordinate Position, (int? Below, int? Above) Altitude)[] GetRouting(string[] routing)
	{
		List<(string, Coordinate, (int?, int?))> bridge = new();

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
			else if (!_fixes.TryGetValue(fix, out Coordinate coord))
				throw new ArgumentException($"Unknown waypoint. Are you sure {fix} is defined in your fixes file? Feel free to try a different route.");
			else
				bridge.Add((fix, coord, restriction));
		}

		return bridge.ToArray();
	}

	public string? MessageReceived(IServer server, string sender, string message)
	{
		IAircraft? query = null;
		for (int suffix = 0; query is null; ++suffix)
			query = server.SpawnAircraft(
				callsign: "BRIDGE LOADER" + (suffix == 0 ? "" : " " + suffix),
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
			if (!_fixes.TryGetValue(message, out Coordinate coord))
				return $"Unknown starting waypoint. Are you sure {message} is defined in your fixes file? What's a nearby waypoint?";

			_acData[aircraft.Callsign] = curAc with { Origin = (message, coord) };
			return $"Okay! What's the other endpoint?";
		}
		else if (curAc.Destination is null)
		{
			if (!_fixes.TryGetValue(message, out Coordinate coord))
				return $"Unknown destination waypoint. Are you sure {message} is defined in your fixes file? What's a nearby waypoint?";

			_acData[aircraft.Callsign] = curAc with { Destination = (message, coord) };
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

		_ = Task.Run(async () =>
		{
			HashSet<IAircraft> spawnedAircraft = new();

			while (!token.IsCancellationRequested)
			{
				int alt = Random.Shared.Next(data.Altitude.Min / 10, data.Altitude.Max / 10 + 1) * 10;
				uint speed = (uint)Random.Shared.Next(
					alt switch
					{
						>= 500 => 35,
						>= 180 => 25,
						_ => 8
					},
					alt switch
					{
						>= 600 => 120,
						>= 350 => 57,
						>= 180 => 40,
						_ => 25
					} + 1
				) * 10;

				double dy = data.Origin.Item2.Latitude - data.Destination.Item2.Latitude,
					   dx = data.Origin.Item2.Longitude - data.Destination.Item2.Longitude;

				IAircraft? ac = null;
				while (ac is null)
					// Keep trying until you get a workable callsign.
					ac = _server!.SpawnAircraft(
						"N" + new string(Enumerable.Range(0, Random.Shared.Next(0, 4)).Select(_ => Random.Shared.Next(0, 10).ToString().Single()).Prepend(Random.Shared.Next(1, 10).ToString().Single()).ToArray()),
						new('I', 'S', "1/UNKN/?-?/?", "N" + speed.ToString("#000"), data.Origin.Item1, DateTime.UtcNow, DateTime.UtcNow, (alt < 180 ? "A" : "F") + alt.ToString("000"), data.Destination.Item1, 3, 0, 4, 0, "", "CS/BOT RMK/Airbridge bot", string.Join(' ', data.Route!.Select(i => i.Item1))),
						data.Origin.Item2,
						(float)(Math.Atan2(dy, dx) * 180 / Math.PI), // Rough approx of starting heading. Actual aircraft logic will correct this quickly.
						Math.Min(speed, 100),
						Math.Min(alt, 1000)
					);

				// Spawn the aircraft and set its initial altitude and speed restrictions
				spawnedAircraft.Add(ac);
				ac.RestrictAltitude(alt * 100, alt * 100, (uint)Random.Shared.Next(1000, 3001));
				ac.RestrictSpeed(Math.Min(200, speed), Math.Min(200, speed), 2.5f); // Accelerate up to 200kts below 10k

				// Wait until above 10000 to set high speed.
				if (alt >= 100)
				{
					try { _ = Task.Run(async () => { while (!token.IsCancellationRequested && ac.Altitude < 10000) await Task.Delay(500, token); ac.RestrictSpeed(speed, speed, 2.5f); }, token); }
					catch (TaskCanceledException) { break; }
				}

				Regex headingExpr = new(@"^H\d\d\d$", RegexOptions.Compiled);
				foreach (var fix in data.Route)
					if (headingExpr.IsMatch(fix.Item1))
					{
						ac.TurnCourse((float)fix.Item2.Latitude);
						ac.FlyForever();
					}
					else
						ac.FlyDirect(fix.Item2);

				Coordinate endpoint = data.Route.Any() ? data.Route.Last().Item2 : data.Destination.Item2;

				// Kill it when it gets within 0.03 deg Euclidean distance from endpoint.
				try { _ = Task.Run(async () => { while (Math.Sqrt(Math.Pow(endpoint.Latitude - ac.Position.Latitude, 2) + Math.Pow(endpoint.Longitude - ac.Position.Longitude, 2)) > 0.03) await Task.Delay(500, token); ac.Kill(); }, token); }
				catch (TaskCanceledException) { break; }

				// Pause randomly before spawning another aircraft.
				try { await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(60, 300)), token); }
				catch (TaskCanceledException) { break; }
			}

			foreach (var ac in spawnedAircraft)
				ac.Kill();
		}, token);

		return cts;
	}
	#endregion

	private record struct BridgeData((string, Coordinate)? Origin, (string, Coordinate)? Destination, (string Name, Coordinate Position, (int? Below, int? Above) Altitude)[]? Route, (int Max, int Min) Altitude) { }

	private record struct FinalBridgeData((string, Coordinate) Origin, (string, Coordinate) Destination, (string Name, Coordinate Position, (int? Below, int? Above) Altitude)[] Route, (int Max, int Min) Altitude)
	{
		public static implicit operator FinalBridgeData(BridgeData data) =>
			new(data.Origin!.Value, data.Destination!.Value, data.Route!, data.Altitude);
	}
}