using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

using TrainingServer;
using TrainingServer.Extensibility;

namespace FreezeScene;

/* WIP: Freeze all
 * 
 * 
public class Plugin : IServerPlugin
{
#if DEBUG
	public string FriendlyName => "Freeze my screen server plugin (DEBUG)";
#else
	public string FriendlyName => "Aircraft Spawner";
#endif
	public string Maintainer => "Álex (605126)";


	public bool CheckIntercept(string _, string message) => message.Trim().Equals("FREEZE ALL", StringComparison.InvariantCultureIgnoreCase);

	public string? MessageReceived(IServer server, string sender, string message)
	{
		if (message.Trim().Equals("FREEZE ALL", StringComparison.InvariantCultureIgnoreCase))
		{
		}
		return "";

	}
}*/

public class Plugin2 : IPlugin
{
#if DEBUG
	public string FriendlyName => "Freeze my screen (DEBUG)";
#else
	public string FriendlyName => "Freeze my screen";
#endif
	public string Maintainer => "Álex (605126)";

	public bool CheckIntercept(string aircraftCallsign, string sender, string message) =>
		message.Trim().Equals("FREEZE", StringComparison.InvariantCultureIgnoreCase);


	public string? MessageReceived(IAircraft aircraft, string sender, string message)
	{

		if (message.Trim().Equals("FREEZE", StringComparison.InvariantCultureIgnoreCase))
		{
			try
			{
				aircraft.Paused = !aircraft.Paused;
				return (aircraft.Paused ? "Aircraft freezed" : "Aircraft unfreezed");
			}
			catch
			{
				System.Diagnostics.Debug.WriteLine("Error freezing ->" + aircraft.Callsign);
			}

		}
		return "";
	}
}