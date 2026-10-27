using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using Seance.Core;
using Seance.Protocol;
using Seance.Protocol.Json;

namespace Seance.Transport;

/// <summary>
/// Serves one client connection: reads requests, answers them, and pumps events once the client
/// subscribes.
/// </summary>
public sealed class PresenceConnection(
    NdjsonChannel channel,
    PresenceEngine engine,
    CallerContext caller,
    ILogger logger)
{
    private const string JsonRpcVersion = "2.0";

    private PresenceSubscription? _subscription;

    /// <summary>
    /// Reads and answers requests until the peer closes the connection. A subscription ends with the
    /// connection that opened it (REQ-EVT-4).
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var connectionScope = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task? eventPump = null;

        try
        {
            while (true)
            {
                var frame = await channel.ReadFrameAsync(connectionScope.Token).ConfigureAwait(false);
                if (frame is null) break;
                if (frame.Length == 0) continue;

                await HandleFrameAsync(frame, connectionScope.Token).ConfigureAwait(false);

                if (_subscription is { } subscription && eventPump is null)
                    eventPump = PumpEventsAsync(subscription, connectionScope);
            }
        }
        catch (OperationCanceledException) when (connectionScope.IsCancellationRequested)
        {
            // The daemon is shutting down or the connection died under the event pump.
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            logger.LogDebug(exception, "Connection ended: {Reason}", exception.Message);
        }
        finally
        {
            await connectionScope.CancelAsync().ConfigureAwait(false);
            _subscription?.Dispose();
            if (eventPump is not null) await Quietly(eventPump).ConfigureAwait(false);
            await channel.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task Quietly(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The connection is already on its way out.
        }
    }

    private async Task PumpEventsAsync(PresenceSubscription subscription, CancellationTokenSource connectionScope)
    {
        try
        {
            await foreach (var notification in subscription.Events.ReadAllAsync(connectionScope.Token).ConfigureAwait(false))
                await WriteNotificationAsync(notification, connectionScope.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected when the connection closes.
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // The client stopped reading. Take the whole connection down rather than pile up events.
            await connectionScope.CancelAsync().ConfigureAwait(false);
        }
    }

    private async Task HandleFrameAsync(byte[] frame, CancellationToken cancellationToken)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(frame);
        }
        catch (JsonException exception)
        {
            await WriteErrorAsync(null, JsonRpcErrorCodes.ParseError, exception.Message, cancellationToken).ConfigureAwait(false);
            return;
        }

        using (document)
        {
            var root = document.RootElement;

            // REQ-RPC-5: a batch is a top-level array, and this generation does not carry them.
            if (root.ValueKind == JsonValueKind.Array)
            {
                await WriteErrorAsync(null, JsonRpcErrorCodes.InvalidRequest, "Batches are not supported.", cancellationToken).ConfigureAwait(false);
                return;
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                await WriteErrorAsync(null, JsonRpcErrorCodes.InvalidRequest, "A message must be a JSON object.", cancellationToken).ConfigureAwait(false);
                return;
            }

            var hasId = root.TryGetProperty("id", out var id);

            // REQ-RPC-6: an id is a number or a string. A request without one is a notification, and
            // JSON-RPC forbids answering those at all.
            if (hasId && id.ValueKind is not (JsonValueKind.Number or JsonValueKind.String))
            {
                await WriteErrorAsync(null, JsonRpcErrorCodes.InvalidRequest, "A request id must be a number or a string.", cancellationToken).ConfigureAwait(false);
                return;
            }

            JsonElement? replyTo = hasId ? id : null;

            if (!root.TryGetProperty("jsonrpc", out var version) || version.ValueKind != JsonValueKind.String || version.GetString() != JsonRpcVersion)
            {
                await WriteErrorAsync(replyTo, JsonRpcErrorCodes.InvalidRequest, "Every message carries jsonrpc 2.0.", cancellationToken).ConfigureAwait(false);
                return;
            }

            if (!root.TryGetProperty("method", out var method) || method.ValueKind != JsonValueKind.String)
            {
                await WriteErrorAsync(replyTo, JsonRpcErrorCodes.InvalidRequest, "A request needs a method name.", cancellationToken).ConfigureAwait(false);
                return;
            }

            JsonElement? parameters = null;
            if (root.TryGetProperty("params", out var raw) && raw.ValueKind != JsonValueKind.Null)
            {
                // REQ-RPC-7: by-name parameters only.
                if (raw.ValueKind != JsonValueKind.Object)
                {
                    await WriteErrorAsync(replyTo, JsonRpcErrorCodes.InvalidParams, "Parameters are passed as a JSON object.", cancellationToken).ConfigureAwait(false);
                    return;
                }

                parameters = raw;
            }

            await DispatchAsync(method.GetString()!, parameters, replyTo, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task DispatchAsync(string method, JsonElement? parameters, JsonElement? replyTo, CancellationToken cancellationToken)
    {
        switch (method)
        {
            case HppMethods.Query:
                await WriteResultAsync(replyTo, engine.Snapshot(), HppJsonContext.Default.PresenceSnapshot, cancellationToken).ConfigureAwait(false);
                return;

            case HppMethods.Context:
                await WriteResultAsync(replyTo, caller, HppJsonContext.Default.CallerContext, cancellationToken).ConfigureAwait(false);
                return;

            case HppMethods.Reach:
                // REQ-RCH-3: unreachable is a result, never an error.
                await WriteResultAsync(replyTo, engine.Reach(caller), HppJsonContext.Default.Reachability, cancellationToken).ConfigureAwait(false);
                return;

            case HppMethods.Subscribe:
                await SubscribeAsync(replyTo, cancellationToken).ConfigureAwait(false);
                return;

            case HppMethods.Pin:
                await PinAsync(parameters, replyTo, cancellationToken).ConfigureAwait(false);
                return;

            case HppMethods.Unpin:
                engine.RemovePin();
                await WriteResultAsync(replyTo, Acknowledgment.Instance, HppJsonContext.Default.Acknowledgment, cancellationToken).ConfigureAwait(false);
                return;

            default:
                await WriteErrorAsync(replyTo, JsonRpcErrorCodes.MethodNotFound, $"'{method}' is not a method of this generation.", cancellationToken).ConfigureAwait(false);
                return;
        }
    }

    private async Task SubscribeAsync(JsonElement? replyTo, CancellationToken cancellationToken)
    {
        // REQ-MTH-5: the subscription and its baseline snapshot are one exchange. Subscribing twice
        // on one connection keeps the first stream rather than duplicating every event.
        if (_subscription is not null)
        {
            await WriteResultAsync(replyTo, engine.Snapshot(), HppJsonContext.Default.PresenceSnapshot, cancellationToken).ConfigureAwait(false);
            return;
        }

        var (subscription, snapshot) = engine.Subscribe();
        _subscription = subscription;
        await WriteResultAsync(replyTo, snapshot, HppJsonContext.Default.PresenceSnapshot, cancellationToken).ConfigureAwait(false);
    }

    private async Task PinAsync(JsonElement? parameters, JsonElement? replyTo, CancellationToken cancellationToken)
    {
        PinParams? request;
        try
        {
            request = parameters?.Deserialize(HppJsonContext.Default.PinParams);
        }
        catch (JsonException exception)
        {
            await WriteErrorAsync(replyTo, JsonRpcErrorCodes.InvalidParams, exception.Message, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (request is null)
        {
            await WriteErrorAsync(replyTo, JsonRpcErrorCodes.InvalidParams, "A pin needs a kind.", cancellationToken).ConfigureAwait(false);
            return;
        }

        // REQ-MTH-4: unknown is a surface kind but not a pinnable one.
        if (request.Kind == SurfaceKind.Unknown)
        {
            await WriteErrorAsync(replyTo, JsonRpcErrorCodes.InvalidParams, "A pin needs one of localSeat, ssh or remoteDesktop.", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (request.DurationMs is <= 0)
        {
            await WriteErrorAsync(replyTo, JsonRpcErrorCodes.InvalidParams, "A pin duration must be positive, or absent to last until removed.", cancellationToken).ConfigureAwait(false);
            return;
        }

        var duration = request.DurationMs is { } milliseconds ? TimeSpan.FromMilliseconds(milliseconds) : (TimeSpan?)null;
        var pin = engine.SetPin(request.Kind, duration);
        await WriteResultAsync(replyTo, pin, HppJsonContext.Default.Pin, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask WriteResultAsync<T>(JsonElement? replyTo, T value, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        // A request without an id is a notification: it is acted on, and it is never answered.
        if (replyTo is not { } id) return;

        var buffer = new ArrayBufferWriter<byte>(512);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", JsonRpcVersion);
            writer.WritePropertyName("id");
            id.WriteTo(writer);
            writer.WritePropertyName("result");
            JsonSerializer.Serialize(writer, value, typeInfo);
            writer.WriteEndObject();
        }

        await channel.WriteFrameAsync(buffer.WrittenMemory, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask WriteErrorAsync(JsonElement? replyTo, int code, string message, CancellationToken cancellationToken)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", JsonRpcVersion);
            writer.WritePropertyName("id");
            if (replyTo is { } id) id.WriteTo(writer);
            else writer.WriteNullValue();

            writer.WriteStartObject("error");
            writer.WriteNumber("code", code);
            writer.WriteString("message", message);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        await channel.WriteFrameAsync(buffer.WrittenMemory, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask WriteNotificationAsync(PresenceEventParams notification, CancellationToken cancellationToken)
    {
        var buffer = new ArrayBufferWriter<byte>(1024);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", JsonRpcVersion);
            writer.WriteString("method", HppMethods.Event);
            writer.WritePropertyName("params");
            JsonSerializer.Serialize(writer, notification, HppJsonContext.Default.PresenceEventParams);
            writer.WriteEndObject();
        }

        await channel.WriteFrameAsync(buffer.WrittenMemory, cancellationToken).ConfigureAwait(false);
    }
}
