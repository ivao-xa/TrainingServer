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
            }
            catch { }
            return "Scenario unloaded successfully.";
        }
        else if (isRunning)
            return "A scenario is already running.";

        string path = string.Join(' ', message.ToLower().Trim().Split()[1..]);
        if (!File.Exists(path))
            return $"File {path} not found.";
        else
        {
            executingTask = ExecuteFileAsync(server, path);

            return "Scenario loaded successfully.";
        }
    }

    private bool isRunning = false;
    private Task? executingTask = null;

    private async Task ExecuteFileAsync(IServer server, string path)
    {
        isRunning = true;
        foreach (string command in File.ReadAllLines(path).Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l)))
        {
            string[] parts = command.Split();
            try
            {
                switch (parts[0].ToUpper())
                {
                    case "SPAWN":

                        if (parts.Length > 11)
                        {
                            string joined = string.Join(' ', parts);

                            string[] splitFlight = joined.Split(';');

                            string[] spawnData = splitFlight[0].Split(' ');
                            string flightdataJoined = splitFlight[1];
                            string remarks = splitFlight[2];
                            string route = splitFlight[3];

                            string[] flightData = flightdataJoined.Split(' ');

                            if (flightData[0].Length == 1 && flightData[1].Length == 1)
                            {
                                IAircraft? aircraft = server.SpawnAircraft(
                                                        parts[1],
                                                        new(flightData[0].Single(), flightData[1].Single(), flightData[2], flightData[3], flightData[4], new(), new(), flightData[7], flightData[8], 0, 0, 0, 0, "ZZZZ", remarks, route),
                                                        new() { Latitude = double.Parse(spawnData[3]), Longitude = double.Parse(spawnData[4]) },
                                                        float.Parse(spawnData[6]),
                                                        uint.Parse(spawnData[8]),
                                                        int.Parse(spawnData[10]) * 100
                                                    );

                                if (aircraft is not null)
                                    _aircraft.Add(spawnData[1], aircraft);
                                Console.WriteLine($"{DateTime.Now:HH:mm:ss:ff} | Created aircraft | " + parts[1]);

                            }
                            else
                            {
                                Console.WriteLine($"{DateTime.Now:HH:mm:ss:ff} | FPL params wrong | {parts[1]}");
                            }
                        }
                        else
                        {
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
                            Console.WriteLine($"{DateTime.Now:HH:mm:ss:ff} | Created aircraft | " + parts[1]);

                        }


                        break;

                    case "DELAY":
                        Console.WriteLine($"{DateTime.Now:HH:mm:ss:ff} | DELAY | {parts[1]}s");
                        await Task.Delay(TimeSpan.FromSeconds(double.Parse(parts[1])));
                        Console.WriteLine($"{DateTime.Now:HH:mm:ss:ff} | Resuming scenario...");
                        break;

                    case "DIEALL":
                        foreach (IAircraft a in _aircraft.Values)
                            a.Kill();
                        _aircraft.Clear();
                        Console.WriteLine($"{DateTime.Now:HH:mm:ss:ff} | All aircraft destroyed.");
                        break;

                    case not null when parts.Length > 2 && _aircraft.ContainsKey(parts[0]) && int.TryParse(parts[2], out int val):

                        IAircraft ac = _aircraft[parts[0]];
                        string instruction = parts[1];

                        switch (instruction.ToUpper())
                        {
                            case "FH":
                                ac.TurnCourse(val);
                                Console.WriteLine($"{DateTime.Now:HH:mm:ss:ff} | {parts[0]} | FLY HEADING | H{val}");
                                break;
                            case "C":
                                ac.RestrictAltitude(val * 100, val * 100, 1500);
                                Console.WriteLine($"{DateTime.Now:HH:mm:ss:ff} | {parts[0]} | CLIMB TO | F{val}");
                                break;
                            case "D":
                                ac.RestrictAltitude(val * 100, val * 100, 1000);
                                Console.WriteLine($"{DateTime.Now:HH:mm:ss:ff} | {parts[0]} | DESCEND TO | F{val}");
                                break;
                            case "SPD" when val >= 0:
                                ac.RestrictSpeed((uint)val, (uint)val, ac.GroundSpeed > (uint)val ? 2.5f : 5f);
                                Console.WriteLine($"{DateTime.Now:HH:mm:ss:ff} | {parts[0]} | SPEED | N{val}");
                                break;
                            case "SQK" when val is >= 0 and <= 7700:
                            case "SQ" when val is >= 0 and <= 7700:
                                ac.Squawk = (ushort)val;
                                Console.WriteLine($"{DateTime.Now:HH:mm:ss:ff} | {parts[0]} | SQUAWK | {val}");
                                break;
                            case "DIE":
                                ac.Kill();
                                _aircraft.Remove(parts[0]);
                                Console.WriteLine($"{DateTime.Now:HH: mm: ss: ff} | {parts[0]} | DIE");
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