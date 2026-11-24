namespace Deskbook.Cli;

/// <summary>Thrown for a command line that cannot be understood. Exit code 2.</summary>
public sealed class UsageException(string message) : Exception(message);

/// <summary>
/// The parsed command line: words (the command and its arguments), "--name value" options, and "--json".
/// Deliberately small: the tool has few commands and each takes few options.
/// </summary>
public sealed class CliArgs
{
    public IReadOnlyList<string> Words { get; }
    public bool Json { get; }
    private readonly Dictionary<string, string> _options;

    private CliArgs(List<string> words, Dictionary<string, string> options, bool json)
    {
        Words = words;
        _options = options;
        Json = json;
    }

    public static CliArgs Parse(string[] args)
    {
        var words = new List<string>();
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        var json = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--json") { json = true; continue; }
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new UsageException($"option {arg} needs a value");
                }
                if (!options.TryAdd(arg[2..], args[++i])) throw new UsageException($"option {arg} is given twice");
                continue;
            }
            words.Add(arg);
        }
        return new CliArgs(words, options, json);
    }

    public string? Option(string name) => _options.GetValueOrDefault(name);

    public string RequiredOption(string name) =>
        Option(name) ?? throw new UsageException($"missing --{name}");

    /// <summary>Fails when an option was given that the command does not know, so a typo is never ignored.</summary>
    public void AllowOnly(params string[] names)
    {
        var unknown = _options.Keys.Except(names).ToList();
        if (unknown.Count > 0) throw new UsageException("unknown option(s): " + string.Join(", ", unknown.Select(n => "--" + n)));
    }
}
