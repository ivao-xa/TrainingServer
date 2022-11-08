using System.Text.RegularExpressions;
using TrainingServer;
using TrainingServer.Extensibility;

namespace FixRewriter;

public class Rewriter : IRewriter
{
#if DEBUG
    public string FriendlyName => "FIX Rewriter (DEBUG)";
#else
    public string FriendlyName => "FIX Rewriter";
#endif
    public string Maintainer => "Niko (639233)";

    public Regex Pattern { get; }

    private readonly Dictionary<string, string> _rewrites = new();

    public Rewriter()
    {
        if (!File.Exists("fixes.fix"))
            File.WriteAllText("fixes.fix", string.Empty);

        foreach (string l in File.ReadAllLines("fixes.fix").Select(l => l.Trim()))
        {
            int splitPoint = l.IndexOf(' ');
            if (splitPoint < 0)
            {
                Console.Error.WriteLine("Could not find fix from line: " + l);
                continue;
            }

            _rewrites.Add(l[..splitPoint], l[(splitPoint + 1)..].Trim());
        }
        Pattern = new(string.Join('|', _rewrites.Keys.Select(k => Regex.Escape(k))), RegexOptions.CultureInvariant | RegexOptions.Compiled);
    }

    public string Rewrite(string message)
    {
        foreach (var kvp in _rewrites)
            message = message.Replace(kvp.Key, kvp.Value);

        return message;
    }
}