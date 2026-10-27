namespace Seance.Protocol;

/// <summary>
/// The wire framing of HPP: one JSON object per line, UTF-8, terminated by a single line feed
/// (REQ-RPC-2). A carriage return immediately before the line feed is accepted and dropped, because
/// interactive tools on Windows emit CRLF (REQ-RPC-4).
/// </summary>
/// <remarks>
/// This framing is what keeps the protocol shell-scriptable, so it is deliberately hand-rolled
/// rather than delegated to a JSON-RPC library: two lines into a socket are a complete exchange, and
/// the daemon publishes NativeAOT where reflection-driven RPC stacks fight back.
/// </remarks>
public sealed class NdjsonChannel(Stream stream, int maxFrameBytes = NdjsonChannel.DefaultMaxFrameBytes) : IAsyncDisposable
{
    /// <summary>
    /// The largest message the daemon will assemble. Presence messages are small; anything past this
    /// is a client that lost the plot.
    /// </summary>
    public const int DefaultMaxFrameBytes = 1024 * 1024;

    private const byte LineFeed = 0x0A;
    private const byte CarriageReturn = 0x0D;

    private static readonly ReadOnlyMemory<byte> Terminator = new([LineFeed]);

    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private byte[] _buffer = new byte[4096];
    private int _offset;
    private int _count;

    /// <summary>
    /// Reads the next message, or null when the peer closed the connection.
    /// </summary>
    /// <exception cref="InvalidDataException">A single message exceeded the frame limit.</exception>
    public async ValueTask<byte[]?> ReadFrameAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var newline = Array.IndexOf(_buffer, LineFeed, _offset, _count);
            if (newline >= 0)
            {
                var end = newline;
                if (end > _offset && _buffer[end - 1] == CarriageReturn) end--;

                var frame = _buffer[_offset..end];
                _count -= newline - _offset + 1;
                _offset = newline + 1;
                return frame;
            }

            if (_count >= maxFrameBytes) throw new InvalidDataException($"A message exceeded {maxFrameBytes} bytes without a line feed.");

            MakeRoom();

            var read = await stream.ReadAsync(_buffer.AsMemory(_offset + _count), cancellationToken).ConfigureAwait(false);

            // A partial line at end of stream is not a message: the line feed is part of the frame.
            if (read == 0) return null;

            _count += read;
        }
    }

    /// <summary>
    /// Writes one message and its terminator. Safe to call from the request loop and the event pump
    /// at the same time.
    /// </summary>
    public async ValueTask WriteFrameAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(Terminator, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _writeLock.Dispose();
        await stream.DisposeAsync().ConfigureAwait(false);
    }

    private void MakeRoom()
    {
        if (_offset > 0)
        {
            Array.Copy(_buffer, _offset, _buffer, 0, _count);
            _offset = 0;
        }

        if (_count < _buffer.Length) return;

        Array.Resize(ref _buffer, Math.Min(_buffer.Length * 2, maxFrameBytes + 1));
    }
}
