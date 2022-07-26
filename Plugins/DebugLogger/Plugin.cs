using System.Reflection;

using TrainingServer;
using TrainingServer.Extensibility;

namespace DebugLogger;

public class Plugin : IPlugin, IServerPlugin
{
#if DEBUG
	public string FriendlyName => "Plugin Development Logger (DEBUG)";
#else
	public string FriendlyName => "Plugin Development Logger";
#endif

	public string Maintainer => "Wes (644899)";

	public bool CheckIntercept(string aircraftCallsign, string sender, string message) => LogMsg(sender, aircraftCallsign, message);

	public bool CheckIntercept(string sender, string message) => LogMsg(sender, "SERVER", message);

	private static bool LogMsg(string from, string to, string msg)
	{
		Console.WriteLine($"{from} -> {to}: {msg}");
		return false;
	}

	public string? MessageReceived(IAircraft aircraft, string sender, string message) => throw new NotImplementedException();

	public string? MessageReceived(IServer server, string sender, string message) => throw new NotImplementedException();

	public void CatchAll(string recipient, string source, string message) => LogMsg(source, recipient, message);
}