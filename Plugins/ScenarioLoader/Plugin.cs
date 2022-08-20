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
        if (!File.Exists(path))
            return $"File {path} not found.";
        else
        {
            executingTask = Task.Run(async () => await ExecuteFileAsync(server, path));

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
            try {
                switch (parts[0].ToUpper())
                {
                    case "SPAWN":

                        if (parts.Length > 11)
                        {
                            string joined = string.Join(' ', parts);

                            string[] split_flight = joined.Split(';');

                            foreach (string str in split_flight) {

                                Console.WriteLine(str + '\n');
                            }

                            string[] spawn_data = split_flight[0].Split(' ');
                            string flightdata_joined = split_flight[1];
                            string remarks = split_flight[2];
                            string route = split_flight[3];

                            string[] flightdata = flightdata_joined.Split(' ');

                            IAircraft? aircraft = server.SpawnAircraft(
                                                        parts[1],
                                                        new(char.Parse(flightdata[0]), char.Parse(flightdata[1]), flightdata[2], flightdata[3], flightdata[4], new(), new(), flightdata[7], flightdata[8], 0, 0, 0, 0, "NONE", remarks, route),
                                                        new() { Latitude = double.Parse(spawn_data[3]), Longitude = double.Parse(spawn_data[4]) },
                                                        float.Parse(spawn_data[6]),
                                                        uint.Parse(spawn_data[8]),
                                                        int.Parse(spawn_data[10]) * 100
                                                    );

                            if (aircraft is not null)
                                _aircraft.Add(spawn_data[1], aircraft);
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
                        }


                        Console.WriteLine($"{ DateTime.Now:HH:mm:ss:ff} | Created aircraft | " + parts[1]);
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
                        Console.WriteLine($"{ DateTime.Now:HH:mm:ss:ff} | All aircraft destroyed.");
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
                                Console.WriteLine($"{ DateTime.Now:HH: mm: ss: ff} | {parts[0]} | DIE");
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