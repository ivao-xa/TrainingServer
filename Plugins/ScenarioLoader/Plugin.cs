using TrainingServer;
using TrainingServer.Extensibility;

namespace ScenarioLoader;

public class Plugin : IServerPlugin
{
    public string FriendlyName => "Scenario Loader";

    public string Maintainer => "Niko (639233)";

    private readonly Dictionary<string, IAircraft> _aircraft = new();

    public bool CheckIntercept(string sender, string message)
    {
        message = message.ToLower().Trim();

        if (isRunning && message == "unload")
            return true;

        string[] trimmedMsg = message.Split();
        return trimmedMsg.Length >= 2 && trimmedMsg[0] == "load" && File.Exists(string.Join(' ', trimmedMsg[1..]));
    }

    public string? MessageReceived(IServer server, string sender, string message)
    {
        if (isRunning && message == "unload")
        {
            isRunning = false;
            try
            {
                executingTask?.Wait(1);
            } catch { }
            return "Scenario unloaded successfully.";
        }
        else if (isRunning) 
            return "A scenario is already running.";

        string path = string.Join(' ', message.ToLower().Trim().Split()[1..]);
        executingTask = Task.Run(async () => await ExecuteFileAsync(server, path));

        return "Scenario loaded successfully.";
    }

    private bool isRunning = false;
    private Task? executingTask = null;

    private async Task ExecuteFileAsync(IServer server, string path)
    {
        isRunning = true;
        foreach (string command in File.ReadAllLines(path).Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l)))
        {
            string[] parts = command.Split();
            try {
                switch (parts[0].ToUpper())
                {
                    case "SPAWN":
                        Console.WriteLine("Creating aircraft: " + parts[1]);
                        IAircraft? aircraft = server.SpawnAircraft(
                            parts[1],
                            new('I', 'S', "1/A320/M-SDG/LB1", "N450", "LJLJ", new(), new(), "F320", "LJMB", 0, 0, 0, 0, "????", "RMK/PLUGIN GENERATED AIRCRAFT. FLIGHT PLAN MAY BE INACCURATE.", "DCT"),
                            new() { Latitude = double.Parse(parts[3]), Longitude = double.Parse(parts[4]) },
                            float.Parse(parts[6]),
                            uint.Parse(parts[8]),
                            int.Parse(parts[10]) * 100
                        );

                        if (aircraft is not null)
                            _aircraft.Add(parts[1], aircraft);

                        break;

                    case "DELAY":
                        await Task.Delay(TimeSpan.FromSeconds(double.Parse(parts[1])));
                        break;

                    case "DIEALL":
                        foreach (IAircraft a in _aircraft.Values)
                            a.Kill();
                        _aircraft.Clear();
                        break;

                    case not null when parts.Length > 2 && _aircraft.ContainsKey(parts[0]) && int.TryParse(parts[2], out int val):
                        
                        IAircraft ac = _aircraft[parts[0]];
                        string instruction = parts[1];

                        switch (instruction.ToUpper())
                        {
                            case "FH":
                                ac.TurnCourse(val);
                                break;
                            case "C":
                                ac.RestrictAltitude(val * 100, val * 100, 1500);
                                break;
                            case "D":
                                ac.RestrictAltitude(val * 100, val * 100, 1000);
                                break;
                            case "SPD" when val >= 0:
                                ac.RestrictSpeed((uint)val, (uint)val, ac.GroundSpeed > (uint)val ? 2.5f : 5f);
                                break;
                            case "SQK" when val is >= 0 and <= 7700:
                            case "SQ" when val is >= 0 and <= 7700:
                                ac.Squawk = (ushort)val;
                                break;
                            case "DIE":
                                ac.Kill();
                                _aircraft.Remove(parts[0]);
                                break;
                        }

                        break;

                    default:
                        Console.WriteLine("Something went wrong!");
                        break;
                }
            }
            catch (Exception exc)
            {
                Console.WriteLine(exc.StackTrace);
            }
        }

        isRunning = false;

    }
}