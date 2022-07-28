using System.Text.RegularExpressions;

using TrainingServer;
using TrainingServer.Extensibility;

namespace BasicIcao;

public class Plugin : IPlugin
{
#if DEBUG
	public string FriendlyName => "Basic Heading, Altitude, Speed, and Squawk Control (DEBUG)";
#else
	public string FriendlyName => "Basic Heading, Altitude, Speed, and Squawk Control";
#endif
	public string Maintainer => "Wes (644899)";

	private readonly Regex _headingRegex, _altitudeRegex, _speedRegex, _squawkRegex;

	public Plugin()
	{
		RegexOptions rxo = RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture;

		string[] regexes = new[]
		{
			@"^FH\s*(?<hdg>\d+(\.\d+)?)",
			@"^[CD]\s*(?<alt>\d+)",
			@"^SPD\s*(?<spd>\d+)",
			@"^SQK?\s*(?<sqk>\d{4})"
		};

		if (File.Exists("commands.re") && File.ReadAllLines("commands.re").Length >= 4)
			regexes = File.ReadAllLines("commands.re");
		else
			File.WriteAllLines("commands.re", regexes);

		_headingRegex	= new(regexes[0], rxo);
		_altitudeRegex	= new(regexes[1], rxo);
		_speedRegex		= new(regexes[2], rxo);
		_squawkRegex	= new(regexes[3], rxo);
	}

	private bool TryBreakUp(string message, out object[] fragments, out ushort? squawk)
	{
		List<object> frags = new();
		squawk = null;

		while (!string.IsNullOrWhiteSpace(message))
		{
			while (message.Any() && char.IsPunctuation(message[0]))
				message = message[1..];
			message = message.Trim();

			if (_headingRegex.IsMatch(message))
			{
				var match = _headingRegex.Match(message);
				frags.Add(float.Parse(match.Groups["hdg"].Value));
				message = message[match.Length..];
			}
			else if (_altitudeRegex.IsMatch(message))
			{
				var match = _altitudeRegex.Match(message);
				frags.Add(int.Parse(match.Groups["alt"].Value) * 100);
				message = message[match.Length..];
			}
			else if (_speedRegex.IsMatch(message))
			{
				var match = _speedRegex.Match(message);
				frags.Add(uint.Parse(match.Groups["spd"].Value));
				message = message[match.Length..];
			}
			else if (_squawkRegex.IsMatch(message))
			{
				var match = _squawkRegex.Match(message);
				squawk = ushort.Parse(match.Groups["sqk"].Value);
				message = message[match.Length..];
			}
			else
			{
				fragments = frags.ToArray();
				return false;
			}
		}

		fragments = frags.ToArray();
		return frags.Any() || squawk is not null;
	}

	public bool CheckIntercept(string aircraftCallsign, string sender, string message) =>
		message.Trim().Equals("DIE", StringComparison.InvariantCultureIgnoreCase) || TryBreakUp(message, out _, out _);

	public string? MessageReceived(IAircraft aircraft, string sender, string message)
	{
		if (message.Trim().Equals("DIE", StringComparison.InvariantCultureIgnoreCase))
		{
			aircraft.Kill();
			return "Goodbye!";
		}

		_ = TryBreakUp(message, out object[] fragments, out ushort? squawk);
		List<string> msgBacks = new();

		if (squawk is not null)
		{
			try { aircraft.Squawk = squawk.Value; msgBacks.Add("Squawking"); msgBacks.Add(squawk.Value.ToString()); }
			catch { System.Diagnostics.Debug.WriteLine("Invalid squawk " + squawk); }
		}
		else
			msgBacks.Add("Flying");

		foreach (object frag in fragments)
			switch (frag)
			{
				case float hdg:
					aircraft.TurnCourse(hdg);
					msgBacks.Add($"heading {hdg:000.00}");
					break;

				case int alt:
					aircraft.RestrictAltitude(alt, alt, 1000);
					msgBacks.Add($"altitude {alt / 100:000}");
					break;

				case uint spd:
					aircraft.RestrictSpeed(spd, spd, 5f);
					msgBacks.Add($"speed {spd:000}");
					break;

				default:
					System.Diagnostics.Debug.WriteLine("Unknown fragment " + frag.ToString());
					break;
			}

		return msgBacks[0] + " " + string.Join(", then ", msgBacks.Skip(1));
	}
}