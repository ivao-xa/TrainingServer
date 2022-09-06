using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

using TrainingServer;
using TrainingServer.Extensibility;

namespace FreezeScene;
public class Plugin : IPlugin
{
#if DEBUG
	public string FriendlyName => "Pause my screen (DEBUG)";
#else
	public string FriendlyName => "Pause my screen";
#endif
	public string Maintainer => "Álex (605126)";

	private readonly Regex _pause, _resume;

	public Plugin()
	{

		string[] regexes = new[]
		{
            @"^PAUSE$",
            @"^RESUME$"
        };

		if (File.Exists("PauseMyScreenCommands.re") && File.ReadAllLines("PauseMyScreenCommands.re").Length >= 1)
			regexes = File.ReadAllLines("PauseMyScreenCommands.re");
		else
			File.WriteAllLines("PauseMyScreenCommands.re", regexes);

		_pause = new(regexes[0], RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
		_resume= new(regexes[1], RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
	}

	private bool CheckMessage(string message, out string? command) 
	{
		if (_pause.IsMatch(message))
		{
			command = "pause";
			return true;
		}
		else if (_resume.IsMatch(message))
		{
			command = "resume";
			return true;
		}
		else
		{
			command = null;
			return false;
		}
	}

	public bool CheckIntercept(string aircraftCallsign, string sender, string message) =>
		CheckMessage(message, out _);

	public string? MessageReceived(IAircraft aircraft, string sender, string message)
	{
		_ = CheckMessage(message, out var command);


		switch (command) {
			case "pause":
				aircraft.Paused = true;
				break;
			case "resume":
				aircraft.Paused = false;
				break;
		}
		
		return (aircraft.Paused ? "Aircraft paused" : "Aircraft resumed");
	}
}