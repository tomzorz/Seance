using System.IO.Pipes;
using System.Net.Sockets;
using Seance.Protocol;

namespace Seance.Client;

/// <summary>
/// Opens a stream to a daemon endpoint. The counterpart of the daemon's listeners, kept separate
/// from <see cref="PresenceClient"/> so anything that speaks the wire by hand can reuse it.
/// </summary>
public static class HppDialer
{
    public static async Task<Stream> ConnectAsync(
        HppEndpoint endpoint,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        switch (endpoint.Kind)
        {
            case EndpointKind.NamedPipe:
            {
                var pipe = new NamedPipeClientStream(
                    ".",
                    endpoint.Value,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attempt.CancelAfter(timeout ?? TimeSpan.FromSeconds(5));

                try
                {
                    await pipe.ConnectAsync(attempt.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    await pipe.DisposeAsync().ConfigureAwait(false);
                    throw new TimeoutException($"Connecting to {endpoint} timed out.");
                }

                return pipe;
            }

            case EndpointKind.UnixSocket:
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(endpoint.Value), cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }

            case EndpointKind.Tcp:
                throw new NotSupportedException("The TCP binding is not implemented yet.");

            default:
                throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint.Kind, "Unknown endpoint kind.");
        }
    }
}
