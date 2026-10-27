using System.IO.Pipes;
using Seance.Protocol;

namespace Seance.Transport;

/// <summary>
/// The Windows binding: a named pipe scoped to one interactive session (HPP section 7.3).
/// </summary>
/// <remarks>
/// One daemon runs per session, so the pipe name carries the session id and a client that knows its
/// own session id can find its own daemon (REQ-PIPE-1).
/// </remarks>
public sealed class NamedPipeHppListener(HppEndpoint endpoint) : IHppListener
{
    private NamedPipeServerStream? _pending;
    private Task? _pendingConnection;

    public HppEndpoint Endpoint { get; } = endpoint.Kind == EndpointKind.NamedPipe
        ? endpoint
        : throw new ArgumentException($"{endpoint} is not a named pipe endpoint.", nameof(endpoint));

    public void Start() => Arm();

    public async Task<HppTransportConnection> AcceptAsync(CancellationToken cancellationToken)
    {
        if (_pending is null || _pendingConnection is null) Arm();

        var connected = _pending!;
        var waiting = _pendingConnection!;

        await waiting.WaitAsync(cancellationToken).ConfigureAwait(false);

        // Arm the next instance straight away, so a client that connects while this one is being
        // served does not find the pipe missing.
        _pending = null;
        _pendingConnection = null;
        Arm();

        return new HppTransportConnection(connected, TransportPeer.Unknown);
    }

    public async ValueTask DisposeAsync()
    {
        var pending = _pending;
        var waiting = _pendingConnection;
        _pending = null;
        _pendingConnection = null;

        if (pending is not null) await pending.DisposeAsync().ConfigureAwait(false);

        if (waiting is null) return;

        try
        {
            await waiting.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Disposing the pipe is how the armed wait is cancelled, so it throws by design.
        }
    }

    private void Arm()
    {
        // CurrentUserOnly restricts the pipe security descriptor to the owning user (REQ-PIPE-2)
        // without hand-building an ACL.
        var server = new NamedPipeServerStream(
            Endpoint.Value,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        _pending = server;
        _pendingConnection = server.WaitForConnectionAsync(CancellationToken.None);
    }
}
