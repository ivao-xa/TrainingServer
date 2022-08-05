using TrainingServer;
using TrainingServer.Extensibility;

namespace ScenarioLoader;

public class Plugin : IServerPlugin
{
    public string FriendlyName => "Scenario Loader";

    public string Maintainer => "Niko (639233)";

    public bool CheckIntercept(string sender, string message)
    {
        message = message.ToLower().Trim();
        string[] trimmedMsg = message.Split();
        return trimmedMsg.Length >= 2 && trimmedMsg[0] == "load" && File.Exists(string.Join(' ', trimmedMsg[1..]));
    }

    public string? MessageReceived(IServer server, string sender, string message)
    {
        string path = string.Join(' ', message.ToLower().Trim().Split()[1..]);
        _ = Task.Run(async () => await ExecuteFileAsync(server, path));

        return "Scenario loaded successfully.";
    }

    private async Task ExecuteFileAsync(IServer server, string path)
    {

        foreach (string command in File.ReadAllLines(path).Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l)))
        {
            string[] parts = command.Split();
            try {
                switch (parts[0].ToUpper())
                {
                    case "SPAWN":
                        Console.WriteLine("Creating aircraft: " + parts[1]);
                        server.SpawnAircraft(
                            parts[1],
                            new('I', 'S', "1/A320/M-SDG/LB1", "N450", "LJLJ", new(), new(), "F320", "LJMB", 0, 0, 0, 0, "????", "RMK/PLUGIN GENERATED AIRCRAFT. FLIGHT PLAN MAY BE INACCURATE.", "DCT"),
                            new() { Latitude = double.Parse(parts[3]), Longitude = double.Parse(parts[4]) },
                            float.Parse(parts[6]),
                            uint.Parse(parts[8]),
                            int.Parse(parts[10])
                        );
                        break;

                    case "DELAY":
                        await Task.Delay(TimeSpan.FromSeconds(double.Parse(parts[1])));
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

    }
}