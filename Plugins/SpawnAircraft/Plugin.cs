using System.Text.RegularExpressions;

using TrainingServer;
using TrainingServer.Extensibility;

namespace SpawnAircraft;

public class Plugin : IServerPlugin
{
	public string FriendlyName => "Aircraft Spawner";
	public string Maintainer => "Wes (644899)";

	private readonly Regex _spawnCommand;

	public Plugin()
	{
		string regex = @"^(?<callsign>\w+) AT (?<lat>[+-]?\d+(\.\d+)?)[ /;](?<lon>[+-]?\d+(\.\d+)?);?( HDG (?<heading>\d+(.\d+)?))?( SPD (?<speed>\d+))?( ALT (?<altitude>-?\d+))?$";

		if (File.Exists("spawner.re"))
			regex = File.ReadAllText("spawner.re").Trim();
		else
			File.WriteAllText("spawner.re", regex);

		_spawnCommand = new(regex, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture);
	}

	public bool CheckIntercept(string _, string message) => _spawnCommand.IsMatch(message.Trim());

	public string? MessageReceived(IServer server, string sender, string message)
	{
		var command = _spawnCommand.Match(message).Groups;

		string callsign = command["callsign"].Value;
		double lat = double.Parse(command["lat"].Value), lon = double.Parse(command["lon"].Value);
		float heading = 180f; uint speed = 100; int altitude = 100;

		if (float.TryParse(command["heading"].Value, out float hdg))
			heading = hdg;

		if (uint.TryParse(command["speed"].Value, out uint spd))
			speed = spd;

		if (int.TryParse(command["altitude"].Value, out int alt))
			altitude = alt * 100;

		if (server.SpawnAircraft(callsign, new() { Latitude = lat, Longitude = lon }, heading, speed, altitude))
			return $"Spawned aircraft {callsign}.";
		else
			return $"Aicraft with callsign {callsign} already exists. Spawning failed.";
	}
}