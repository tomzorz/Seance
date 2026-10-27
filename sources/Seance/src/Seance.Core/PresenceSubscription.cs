using System.Threading.Channels;
using Seance.Protocol;

namespace Seance.Core;

/// <summary>
/// One connection's event stream. Ends when it is disposed, which is what closing a connection does
/// (REQ-EVT-4).
/// </summary>
public sealed class PresenceSubscription : IDisposable
{
    private readonly Channel<PresenceEventParams> _channel;
    private readonly Action<PresenceSubscription> _onDispose;
    private bool _disposed;

    internal PresenceSubscription(int capacity, Action<PresenceSubscription> onDispose)
    {
        _channel = Channel.CreateBounded<PresenceEventParams>(new BoundedChannelOptions(capacity)
        {
            // A wedged client must not stall the daemon, and the newest state is the state that
            // matters. Full snapshots per event make the dropped ones survivable.
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

        _onDispose = onDispose;
    }

    /// <summary>
    /// The events published since this subscription started.
    /// </summary>
    public ChannelReader<PresenceEventParams> Events => _channel.Reader;

    internal void Publish(PresenceEventParams notification) => _channel.Writer.TryWrite(notification);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _onDispose(this);
        _channel.Writer.TryComplete();
    }
}
