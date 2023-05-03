using System.Text.RegularExpressions;

using TrainingServer;
using TrainingServer.Extensibility;

namespace XAControl;

public class Plugin : IPlugin
{
	public string FriendlyName => "FAA-inspired aircraft control commands";

	public string Maintainer => "Wes (644899)";

	private readonly Dictionary<string, Coordinate> _fixes = new();

	readonly private Dictionary<string, Command> _commands = new()
	{
		{ "PD", Command.Direct }, { "DCT", Command.Direct }, { "LD", Command.Direct }, { "RD", Command.Direct },
		{ "FH", Command.Heading }, { "HDG", Command.Heading }, { "L", Command.Heading }, { "R", Command.Heading },
		{ "DM", Command.Altitude }, { "D", Command.Altitude },
		{ "CM", Command.Altitude }, { "C", Command.Altitude },
		{ "SQ", Command.Squawk }, { "SQK", Command.Squawk },
		{ "S", Command.Speed }, { "SPD", Command.Speed },
		{ "CON", Command.Continue }, { "CONTINUE", Command.Continue },
		{ "RON", Command.ResumeOwnNavigation }, { "OWN", Command.ResumeOwnNavigation },
		{ "DIE", Command.Die }, { "END", Command.Die }
	};

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

	public bool CheckIntercept(string aircraftCallsign, string sender, string message) =>
		_commands.ContainsKey(message.TrimStart().Split()[0].ToUpperInvariant());

	public string? MessageReceived(IAircraft aircraft, string sender, string message)
	{
		string[] parts = message.Trim().ToUpperInvariant().Split();
		Command command = _commands[parts[0]];

		switch (command)
		{
			case Command.Direct when parts.Length > 0 && _fixes.TryGetValue(parts[1], out Coordinate coord):
				aircraft.Interrupt();
				aircraft.FlyDirect(coord, turnDirection: parts[0] switch { "LD" => TurnDirection.Left, "RD" => TurnDirection.Right, _ => null });
				break;

			case Command.Heading when parts.Length > 0 && uint.TryParse(parts[1], out uint hdg):
				aircraft.Interrupt();
				aircraft.TurnCourse(hdg, turnDirection: parts[0] switch { "L" => TurnDirection.Left, "R" => TurnDirection.Right, _ => null });
				break;

			case Command.Altitude when parts.Length > 0 && int.TryParse(parts[1], out int alt):
				aircraft.RestrictAltitude(alt * 100, alt * 100, (uint)(aircraft.Altitude > alt ? 2000 : 1000));
				break;

			case Command.Squawk when parts.Length > 0 && parts[1].Length == 4 && parts[1].All(char.IsDigit):
				aircraft.Squawk = ushort.Parse(parts[1]);
				break;

			case Command.Speed when parts.Length > 0 && uint.TryParse(parts[1], out uint spd):
				aircraft.RestrictSpeed(spd, spd, aircraft.GroundSpeed > spd ? 2.5f : 5f);
				break;

			case Command.Continue:
				// Allow both "CON" and "CON 3"
				foreach (var _ in Enumerable.Range(0, parts.Length > 1 ? uint.TryParse(parts[1], out uint ct) ? (int)ct : 1 : 1))
					aircraft.Continue();

				return "Continuing";

			case Command.ResumeOwnNavigation:
				return aircraft.ResumeOwnNavigation() ? "Own navigation" : "Not sure what you want us to resume…";

			case Command.Die:
				aircraft.Kill();
				return "Good day.";

			default:
				return "Unable to execute command";
		}

		return "Wilco";
	}

	private enum Command
	{
		Direct,
		Heading,
		Altitude,
		Squawk,
		Speed,
		Continue,
		ResumeOwnNavigation,
		Die
	}
}