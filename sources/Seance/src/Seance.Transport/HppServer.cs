using Microsoft.Extensions.Logging;
using Seance.Core;
using Seance.Protocol;

namespace Seance.Transport;

/// <summary>
/// Accepts connections on one endpoint and serves each of them.
/// </summary>
public sealed class HppServer(
    IHppListener listener,
    PresenceEngine engine,
    ICallerResolver callerResolver,
    ILogger<HppServer> logger)
{
    public HppEndpoint Endpoint => listener.Endpoint;

    /// <summary>
    /// Serves until cancelled. Each connection runs on its own task and ends with its client.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        listener.Start();
        logger.LogInformation("Listening on {Endpoint}", listener.Endpoint);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var connection = await listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
                _ = ServeAsync(connection, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down.
        }
        finally
        {
            await listener.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task ServeAsync(HppTransportConnection connection, CancellationToken cancellationToken)
    {
        var caller = callerResolver.Resolve(connection.Peer);
        logger.LogDebug("Client connected: pid {Pid}", caller.Pid);

        try
        {
            var channel = new NdjsonChannel(connection.Stream);
            await new PresenceConnection(channel, engine, caller, logger).RunAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "A connection ended badly.");
        }
    }
}

/// <summary>
/// Builds the listener that matches an endpoint.
/// </summary>
public static class HppListenerFactory
{
    public static IHppListener Create(HppEndpoint endpoint) => endpoint.Kind switch
    {
        EndpointKind.NamedPipe => new NamedPipeHppListener(endpoint),
        EndpointKind.UnixSocket => new UnixSocketHppListener(endpoint),

        // REQ-TCP-1: a TCP endpoint is never on by default, and this daemon does not offer one yet.
        EndpointKind.Tcp => throw new NotSupportedException("The TCP binding is not implemented yet."),
        _ => throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint.Kind, "Unknown endpoint kind."),
    };
}
