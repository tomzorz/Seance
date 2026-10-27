using System.Globalization;
using Seance.Protocol;
using Seance.Protocol.Json;

namespace Seance.Cli;

/// <summary>
/// Hand-rolled argument parsing. Six subcommands and three switches do not need a parser library,
/// and the daemon side of this repo publishes NativeAOT where fewer dependencies is the whole point.
/// </summary>
internal sealed record CommandLine
{
    public required string Command { get; init; }

    public IReadOnlyList<string> Arguments { get; init; } = [];

    public bool Json { get; init; }

    public HppEndpoint? Endpoint { get; init; }

    public TimeSpan? Duration { get; init; }

    public static CommandLine Parse(string[] args)
    {
        string? command = null;
        var positional = new List<string>();
        var json = false;
        HppEndpoint? endpoint = null;
        TimeSpan? duration = null;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--json":
                    json = true;
                    continue;

                case "--endpoint":
                    endpoint = HppEndpoint.Parse(Value(args, ref index, "--endpoint"));
                    continue;

                case "--for":
                    duration = ParseDuration(Value(args, ref index, "--for"));
                    continue;

                case "-h":
                case "--help":
                    command ??= "help";
                    continue;

                case "--version":
                    command ??= "version";
                    continue;

                default:
                    if (argument.StartsWith('-')) throw new FormatException($"'{argument}' is not an option this command knows.");
                    if (command is null) command = argument;
                    else positional.Add(argument);
                    continue;
            }
        }

        return new CommandLine
        {
            Command = command ?? "help",
            Arguments = positional,
            Json = json,
            Endpoint = endpoint,
            Duration = duration,
        };
    }

    /// <summary>
    /// Accepts the wire names and the obvious shorthands, because nobody wants to type camelCase at
    /// a shell prompt.
    /// </summary>
    public static SurfaceKind ParseKind(string text) => text.ToLowerInvariant() switch
    {
        "localseat" or "local" or "seat" or "here" => SurfaceKind.LocalSeat,
        "ssh" => SurfaceKind.Ssh,
        "remotedesktop" or "remote" or "rdp" or "vnc" => SurfaceKind.RemoteDesktop,
        _ => throw new FormatException($"'{text}' is not a surface kind. Use {WireNames.Of(SurfaceKind.LocalSeat)}, {WireNames.Of(SurfaceKind.Ssh)} or {WireNames.Of(SurfaceKind.RemoteDesktop)}."),
    };

    /// <summary>
    /// A unit is required: "--for 30" reads as thirty of something, and guessing which is how you
    /// end up pinned for thirty milliseconds.
    /// </summary>
    public static TimeSpan ParseDuration(string text)
    {
        var (digits, unit) = Split(text);

        if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
            throw new FormatException($"'{text}' is not a positive duration.");

        return unit switch
        {
            "ms" => TimeSpan.FromMilliseconds(amount),
            "s" => TimeSpan.FromSeconds(amount),
            "m" => TimeSpan.FromMinutes(amount),
            "h" => TimeSpan.FromHours(amount),
            "d" => TimeSpan.FromDays(amount),
            _ => throw new FormatException($"'{text}' needs a unit: ms, s, m, h or d."),
        };
    }

    private static (string Digits, string Unit) Split(string text)
    {
        var boundary = 0;
        while (boundary < text.Length && char.IsAsciiDigit(text[boundary])) boundary++;
        return (text[..boundary], text[boundary..].ToLowerInvariant());
    }

    private static string Value(string[] args, ref int index, string name)
    {
        if (index + 1 >= args.Length) throw new FormatException($"{name} needs a value.");
        return args[++index];
    }
}
