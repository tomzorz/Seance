using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Seance.Protocol;

/// <summary>
/// The transport an endpoint string names (HPP section 7.1).
/// </summary>
public enum EndpointKind
{
    UnixSocket,
    NamedPipe,
    Tcp,
}

/// <summary>
/// An address where the daemon accepts connections (HPP section 7.1). The generation lives in the
/// endpoint name, which is the only cross-generation compatibility signal (HPP section 8).
/// </summary>
public sealed record HppEndpoint
{
    /// <summary>
    /// The generation prefix that every default endpoint name carries.
    /// </summary>
    public const string Generation = "hpp1";

    /// <summary>
    /// The environment variable a client must check before falling back to a platform default
    /// (REQ-DISC-1).
    /// </summary>
    public const string EnvironmentVariable = "HPP_ENDPOINT";

    /// <summary>
    /// The default TCP port: 0x4850, the ASCII bytes HP (REQ-TCP-6).
    /// </summary>
    public const int DefaultTcpPort = 18512;

    public required EndpointKind Kind { get; init; }

    /// <summary>
    /// The socket path for a unix endpoint, the pipe name for a pipe endpoint, the host for a TCP
    /// endpoint.
    /// </summary>
    public required string Value { get; init; }

    /// <summary>
    /// The TCP port. Zero for the other kinds.
    /// </summary>
    public int Port { get; init; }

    /// <summary>
    /// The endpoint came from <see cref="EnvironmentVariable"/>, so a failed connection must be
    /// reported rather than retried against a platform default (REQ-DISC-3).
    /// </summary>
    public bool FromEnvironment { get; init; }

    public override string ToString() => Kind switch
    {
        EndpointKind.UnixSocket => $"unix:{Value}",
        EndpointKind.NamedPipe => $"pipe:{Value}",
        EndpointKind.Tcp => Value.Contains(':', StringComparison.Ordinal)
            ? $"tcp:[{Value}]:{Port}"
            : $"tcp:{Value}:{Port}",
        _ => throw new InvalidOperationException($"Unknown endpoint kind {Kind}."),
    };

    /// <summary>
    /// Reads <see cref="EnvironmentVariable"/> and falls back to the platform default only when the
    /// variable is unset (REQ-DISC-1, REQ-DISC-3).
    /// </summary>
    public static HppEndpoint Resolve()
    {
        var configured = Environment.GetEnvironmentVariable(EnvironmentVariable);
        return string.IsNullOrWhiteSpace(configured)
            ? PlatformDefault()
            : Parse(configured) with { FromEnvironment = true };
    }

    /// <summary>
    /// The endpoint the daemon listens on when nothing overrides it: a named pipe scoped to the
    /// current Windows session (REQ-PIPE-1), otherwise a unix socket in the runtime directory
    /// (REQ-UDS-1, REQ-UDS-2).
    /// </summary>
    public static HppEndpoint PlatformDefault()
    {
        if (OperatingSystem.IsWindows())
        {
            using var self = Process.GetCurrentProcess();
            return new HppEndpoint
            {
                Kind = EndpointKind.NamedPipe,
                Value = $"{Generation}-{self.SessionId.ToString(CultureInfo.InvariantCulture)}",
            };
        }

        var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        var path = string.IsNullOrWhiteSpace(runtimeDir)
            ? $"/tmp/{Generation}-{CurrentUserId().ToString(CultureInfo.InvariantCulture)}.sock"
            : System.IO.Path.Combine(runtimeDir, $"{Generation}.sock");

        return new HppEndpoint { Kind = EndpointKind.UnixSocket, Value = path };
    }

    public static HppEndpoint Parse(string text) =>
        TryParse(text, out var endpoint, out var error)
            ? endpoint
            : throw new FormatException(error);

    /// <summary>
    /// Parses one of the three endpoint forms of REQ-DISC-2.
    /// </summary>
    public static bool TryParse(string text, out HppEndpoint endpoint, out string? error)
    {
        endpoint = null!;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "An endpoint must not be empty.";
            return false;
        }

        var separator = text.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0)
        {
            error = $"'{text}' is not an endpoint. Expected unix:<path>, pipe:<name> or tcp:<host>:<port>.";
            return false;
        }

        var scheme = text[..separator];
        var rest = text[(separator + 1)..];

        switch (scheme)
        {
            case "unix":
                if (rest.Length == 0)
                {
                    error = "A unix endpoint needs a socket path.";
                    return false;
                }

                endpoint = new HppEndpoint { Kind = EndpointKind.UnixSocket, Value = rest };
                return true;

            case "pipe":
                if (rest.Length == 0)
                {
                    error = "A pipe endpoint needs a pipe name.";
                    return false;
                }

                endpoint = new HppEndpoint { Kind = EndpointKind.NamedPipe, Value = rest };
                return true;

            case "tcp":
                return TryParseTcp(rest, out endpoint, out error);

            default:
                error = $"'{scheme}' is not a known endpoint scheme. Expected unix, pipe or tcp.";
                return false;
        }
    }

    private static bool TryParseTcp(string rest, out HppEndpoint endpoint, out string? error)
    {
        endpoint = null!;
        error = null;

        string host;
        string portText;

        if (rest.StartsWith('['))
        {
            var close = rest.IndexOf(']', StringComparison.Ordinal);
            if (close < 0 || close + 2 > rest.Length || rest[close + 1] != ':')
            {
                error = $"'tcp:{rest}' is not a bracketed IPv6 endpoint. Expected tcp:[host]:port.";
                return false;
            }

            host = rest[1..close];
            portText = rest[(close + 2)..];
        }
        else
        {
            var colon = rest.LastIndexOf(':');
            if (colon <= 0)
            {
                error = $"'tcp:{rest}' is missing a port. Expected tcp:host:port.";
                return false;
            }

            host = rest[..colon];
            portText = rest[(colon + 1)..];
        }

        if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
        {
            error = $"'{portText}' is not a TCP port.";
            return false;
        }

        endpoint = new HppEndpoint { Kind = EndpointKind.Tcp, Value = host, Port = port };
        return true;
    }

    private static uint CurrentUserId() => NativeMethods.geteuid();

    private static class NativeMethods
    {
        [DllImport("libc", EntryPoint = "geteuid")]
        internal static extern uint geteuid();
    }
}
