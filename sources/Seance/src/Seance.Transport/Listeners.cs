using Seance.Protocol;

namespace Seance.Transport;

/// <summary>
/// Accepts client connections on one endpoint.
/// </summary>
public interface IHppListener : IAsyncDisposable
{
    /// <summary>
    /// Where this listener accepts connections.
    /// </summary>
    HppEndpoint Endpoint { get; }

    /// <summary>
    /// Claims the endpoint. Throws when another live daemon already holds it.
    /// </summary>
    void Start();

    /// <summary>
    /// Waits for the next client.
    /// </summary>
    Task<HppTransportConnection> AcceptAsync(CancellationToken cancellationToken);
}

/// <summary>
/// One accepted connection, plus whatever the operating system said about its peer.
/// </summary>
public sealed record HppTransportConnection(Stream Stream, TransportPeer Peer);

/// <summary>
/// What the transport itself knows about the process at the other end. Kernel-reported only: no
/// environment variables are consulted, ever (REQ-CTX-2).
/// </summary>
public sealed record TransportPeer
{
    public static readonly TransportPeer Unknown = new();

    public int? ProcessId { get; init; }

    public string? UserId { get; init; }
}

/// <summary>
/// Turns a transport peer into the caller context clients read.
/// </summary>
public interface ICallerResolver
{
    CallerContext Resolve(TransportPeer peer);
}

/// <summary>
/// Reports exactly what the transport handed over and infers nothing further. Ancestry walking and
/// origin inference are platform work that has not landed yet, and inventing an origin would be
/// worse than admitting it is unknown (REQ-TRUTH-1).
/// </summary>
public sealed class UnresolvedCallerResolver : ICallerResolver
{
    public CallerContext Resolve(TransportPeer peer) => CallerContext.UnresolvedKernel(peer.ProcessId, peer.UserId);
}
