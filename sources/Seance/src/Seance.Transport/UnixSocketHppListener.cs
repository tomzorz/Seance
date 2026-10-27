using System.Net.Sockets;
using Seance.Protocol;

namespace Seance.Transport;

/// <summary>
/// The Linux and macOS binding: a unix domain socket in the runtime directory (HPP section 7.2).
/// </summary>
public sealed class UnixSocketHppListener(HppEndpoint endpoint) : IHppListener
{
    private Socket? _socket;

    public HppEndpoint Endpoint { get; } = endpoint.Kind == EndpointKind.UnixSocket
        ? endpoint
        : throw new ArgumentException($"{endpoint} is not a unix socket endpoint.", nameof(endpoint));

    public void Start()
    {
        var path = Endpoint.Value;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        RemoveStaleSocket(path);

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        socket.Bind(new UnixDomainSocketEndPoint(path));
        socket.Listen(16);

        // REQ-UDS-3: the socket belongs to the owning user and nobody else.
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        _socket = socket;
    }

    public async Task<HppTransportConnection> AcceptAsync(CancellationToken cancellationToken)
    {
        var socket = _socket ?? throw new InvalidOperationException("The listener has not been started.");
        var accepted = await socket.AcceptAsync(cancellationToken).ConfigureAwait(false);
        return new HppTransportConnection(new NetworkStream(accepted, ownsSocket: true), TransportPeer.Unknown);
    }

    public ValueTask DisposeAsync()
    {
        _socket?.Dispose();
        _socket = null;

        try
        {
            File.Delete(Endpoint.Value);
        }
        catch (IOException)
        {
            // Someone else already tidied up, or the directory went away with the login session.
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// REQ-UDS-5: a socket file left behind by a dead daemon blocks the bind, and deleting one that
    /// a live daemon still holds would steal its endpoint. Connecting to it is the only way to tell
    /// the two apart.
    /// </summary>
    private static void RemoveStaleSocket(string path)
    {
        if (!File.Exists(path)) return;

        using (var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified))
        {
            try
            {
                probe.Connect(new UnixDomainSocketEndPoint(path));
            }
            catch (SocketException)
            {
                File.Delete(path);
                return;
            }
        }

        throw new IOException($"Another daemon is already listening on {path}.");
    }
}
