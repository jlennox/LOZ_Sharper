using System;

namespace z1;

internal sealed class ProgramOptions
{
    public static readonly ProgramOptions Default = new();

    private readonly string[] _args;
    private int _index = 0;

    public bool UseTempProfile { get; set; }
    public string? ProfileName { get; set; }
    public int? RandomizerSeed { get; set; }

    private ProgramOptions()
    {
        _args = [];
    }

    public ProgramOptions(string[] args)
    {
        _args = ReparseArgs(args).ToArray();

        while (_index < _args.Length)
        {
            var name = _args[_index++];
            switch (name)
            {
                // Serves as a short circuit, "comment out the rest"
                case "--ignore":
                    return;
                case "--temp-profile":
                    UseTempProfile = true;
                    break;
                case "--profile":
                    ProfileName = Consume(name);
                    break;
                case "--randomize":
                    RandomizerSeed = ConsumeInt(name);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown argument \"{name}\".");
            }
        }

        if (UseTempProfile && ProfileName is not null)
        {
            throw new InvalidOperationException("Cannot specify both --temp-profile and --profile.");
        }
    }

    // Reparse arguments to split on '='
    internal static IEnumerable<string> ReparseArgs(string[] args)
    {
        // Scan for = signs that are outside of quotes. If one is found, split that argument into two.
        foreach (var arg in args)
        {
            var inQuotes = false;
            var returned = false;
            for (var i = 0; i < arg.Length && !returned; i++)
            {
                var chr = arg[i];
                switch (chr)
                {
                    case '\'':
                        i++;
                        continue;
                    case '"':
                        i++;
                        inQuotes = !inQuotes;
                        continue;
                    case '=' when !inQuotes:
                        yield return arg[..i];
                        yield return arg[(i + 1)..];
                        returned = true;
                        break;
                }
            }

            if (!returned) yield return arg;
        }
    }

    private int ConsumeInt(string name)
    {
        var value = Consume(name);
        return int.TryParse(value, out var result)
            ? result
            : throw new InvalidOperationException($"Invalid integer value \"{value}\" for argument \"{name}\".");
    }

    private string Consume(string name)
    {
        return _index >= _args.Length
            ? throw new InvalidOperationException($"Missing value for argument \"{name}\".")
            : _args[_index++];
    }
}