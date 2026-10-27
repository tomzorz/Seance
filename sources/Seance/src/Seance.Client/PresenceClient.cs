using System.Buffers;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Channels;
using Seance.Protocol;
using Seance.Protocol.Json;

namespace Seance.Client;

/// <summary>
/// A client for one HPP daemon. Calls are multiplexed over a single connection, and events arrive
/// on <see cref="Events"/> once <see cref="SubscribeAsync"/> has been called.
/// </summary>
public sealed class PresenceClient : IAsyncDisposable
{
    private const string JsonRpcVersion = "2.0";

    private readonly NdjsonChannel _connection;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly Channel<PresenceEventParams> _events = Channel.CreateUnbounded<PresenceEventParams>();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _readLoop;

    private long _nextId;

    private PresenceClient(NdjsonChannel connection, HppEndpoint endpoint)
    {
        _connection = connection;
        Endpoint = endpoint;
        _readLoop = ReadLoopAsync();
    }

    /// <summary>
    /// The endpoint this client is talking to.
    /// </summary>
    public HppEndpoint Endpoint { get; }

    /// <summary>
    /// Events published since the subscription started (REQ-EVT-1). Empty until
    /// <see cref="SubscribeAsync"/> is called.
    /// </summary>
    public ChannelReader<PresenceEventParams> Events => _events.Reader;

    /// <summary>
    /// Connects to a daemon. With no endpoint given, HPP_ENDPOINT decides and the platform default
    /// applies only when it is unset (REQ-DISC-1).
    /// </summary>
    public static async Task<PresenceClient> ConnectAsync(
        HppEndpoint? endpoint = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var target = endpoint ?? HppEndpoint.Resolve();

        try
        {
            var stream = await HppDialer.ConnectAsync(target, timeout, cancellationToken).ConfigureAwait(false);
            return new PresenceClient(new NdjsonChannel(stream), target);
        }
        catch (Exception exception) when (exception is IOException or SocketException or TimeoutException)
        {
            // REQ-DISC-3: an endpoint that came from the environment expresses explicit intent, so a
            // failure is reported rather than papered over with a platform default.
            var source = target.FromEnvironment ? $" (from {HppEndpoint.EnvironmentVariable})" : string.Empty;
            throw new IOException($"No presence daemon answered at {target}{source}.", exception);
        }
    }

    /// <summary>
    /// presence.query: the surfaces, the pin and the capabilities as they are right now.
    /// </summary>
    public Task<PresenceSnapshot> QueryAsync(CancellationToken cancellationToken = default) =>
        InvokeAsync(HppMethods.Query, HppJsonContext.Default.PresenceSnapshot, cancellationToken);

    /// <summary>
    /// presence.context: what the daemon knows about this process.
    /// </summary>
    public Task<CallerContext> ContextAsync(CancellationToken cancellationToken = default) =>
        InvokeAsync(HppMethods.Context, HppJsonContext.Default.CallerContext, cancellationToken);

    /// <summary>
    /// presence.reach: which surfaces can reach the user, most likely first. An empty result is an
    /// answer, not a failure (REQ-RCH-2, REQ-RCH-3).
    /// </summary>
    public Task<Reachability> ReachAsync(CancellationToken cancellationToken = default) =>
        InvokeAsync(HppMethods.Reach, HppJsonContext.Default.Reachability, cancellationToken);

    /// <summary>
    /// presence.subscribe: starts the event stream and returns the snapshot it starts from, in one
    /// exchange (REQ-MTH-5).
    /// </summary>
    public Task<PresenceSnapshot> SubscribeAsync(CancellationToken cancellationToken = default) =>
        InvokeAsync(HppMethods.Subscribe, HppJsonContext.Default.PresenceSnapshot, cancellationToken);

    /// <summary>
    /// presence.pin: override the derived state until the pin expires or is removed.
    /// </summary>
    public Task<Pin> PinAsync(SurfaceKind kind, TimeSpan? duration = null, CancellationToken cancellationToken = default) =>
        InvokeAsync(
            HppMethods.Pin,
            new PinParams { Kind = kind, DurationMs = duration is { } span ? (long)span.TotalMilliseconds : null },
            HppJsonContext.Default.PinParams,
            HppJsonContext.Default.Pin,
            cancellationToken);

    /// <summary>
    /// presence.unpin: drop the active pin.
    /// </summary>
    public Task UnpinAsync(CancellationToken cancellationToken = default) =>
        InvokeAsync(HppMethods.Unpin, HppJsonContext.Default.Acknowledgment, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync().ConfigureAwait(false);
        await _connection.DisposeAsync().ConfigureAwait(false);

        try
        {
            await _readLoop.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The read loop ends by having its connection pulled out from under it.
        }

        _lifetime.Dispose();
    }

    private Task<T> InvokeAsync<T>(string method, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken) =>
        InvokeAsync(method, null, typeInfo, cancellationToken);

    private Task<TResult> InvokeAsync<TParams, TResult>(
        string method,
        TParams parameters,
        JsonTypeInfo<TParams> parametersInfo,
        JsonTypeInfo<TResult> typeInfo,
        CancellationToken cancellationToken) =>
        InvokeAsync(
            method,
            writer => JsonSerializer.Serialize(writer, parameters, parametersInfo),
            typeInfo,
            cancellationToken);

    private async Task<T> InvokeAsync<T>(
        string method,
        Action<Utf8JsonWriter>? writeParameters,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;

        try
        {
            var buffer = new ArrayBufferWriter<byte>(256);
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WriteString("jsonrpc", JsonRpcVersion);
                writer.WriteNumber("id", id);
                writer.WriteString("method", method);

                if (writeParameters is not null)
                {
                    writer.WritePropertyName("params");
                    writeParameters(writer);
                }

                writer.WriteEndObject();
            }

            await _connection.WriteFrameAsync(buffer.WrittenMemory, cancellationToken).ConfigureAwait(false);

            var result = await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return result.Deserialize(typeInfo)
                   ?? throw new JsonException($"The daemon answered {method} with a null result.");
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (true)
            {
                var frame = await _connection.ReadFrameAsync(_lifetime.Token).ConfigureAwait(false);
                if (frame is null) break;
                if (frame.Length == 0) continue;

                Dispatch(frame);
            }

            Fail(new IOException("The daemon closed the connection."));
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
        finally
        {
            _events.Writer.TryComplete();
        }
    }

    private void Dispatch(byte[] frame)
    {
        using var document = JsonDocument.Parse(frame);
        var root = document.RootElement;

        if (root.TryGetProperty("method", out var method) &&
            method.ValueKind == JsonValueKind.String &&
            method.GetString() == HppMethods.Event)
        {
            if (root.TryGetProperty("params", out var parameters) &&
                parameters.Deserialize(HppJsonContext.Default.PresenceEventParams) is { } notification)
            {
                _events.Writer.TryWrite(notification);
            }

            return;
        }

        if (!root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number) return;
        if (!_pending.TryRemove(id.GetInt64(), out var completion)) return;

        if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
        {
            var code = error.TryGetProperty("code", out var codeElement) ? codeElement.GetInt32() : JsonRpcErrorCodes.InternalError;
            var message = error.TryGetProperty("message", out var messageElement)
                ? messageElement.GetString() ?? "The daemon reported an error."
                : "The daemon reported an error.";

            completion.TrySetException(new JsonRpcException(code, message));
            return;
        }

        completion.TrySetResult(root.TryGetProperty("result", out var result) ? result.Clone() : default);
    }

    private void Fail(Exception exception)
    {
        foreach (var key in _pending.Keys)
        {
            if (_pending.TryRemove(key, out var completion)) completion.TrySetException(exception);
        }
    }

}
