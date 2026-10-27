using System.Text;
using System.Text.Json;
using Seance.Core.Providers;
using Seance.Protocol;

namespace Seance.Transport.Tests;

/// <summary>
/// What the daemon does with input a well-behaved client would never send. The protocol promises to
/// be shell-scriptable, which means hand-written lines are a supported way in and the failure
/// answers have to be useful.
/// </summary>
public class WireProtocolTests
{
    [Fact]
    public async Task A_hand_written_line_gets_a_hand_readable_answer()
    {
        await using var daemon = await DaemonHarness.StartAsync(FakeSurfaces.LocalSeat());
        await using var channel = await daemon.ConnectRawAsync();

        var response = await ExchangeAsync(channel, """{"jsonrpc":"2.0","id":1,"method":"presence.query"}""");

        Assert.Equal("2.0", response.GetProperty("jsonrpc").GetString());
        Assert.Equal(1, response.GetProperty("id").GetInt32());
        Assert.Equal(1, response.GetProperty("result").GetProperty("surfaces").GetArrayLength());
    }

    [Fact]
    public async Task A_string_id_comes_back_as_the_same_string()
    {
        await using var daemon = await DaemonHarness.StartAsync(FakeSurfaces.LocalSeat());
        await using var channel = await daemon.ConnectRawAsync();

        var response = await ExchangeAsync(channel, """{"jsonrpc":"2.0","id":"abc","method":"presence.reach"}""");

        Assert.Equal("abc", response.GetProperty("id").GetString());
    }

    [Fact]
    public async Task A_line_ending_in_crlf_is_accepted()
    {
        // REQ-RPC-4: interactive tools on Windows emit CRLF, and refusing them would make the
        // shell-scriptable claim a lie on half the platforms this runs on.
        await using var daemon = await DaemonHarness.StartAsync(FakeSurfaces.LocalSeat());
        await using var channel = await daemon.ConnectRawAsync();

        var request = Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"presence.query\"}\r");
        await channel.WriteFrameAsync(request, CancellationToken.None);

        var response = await ReadAsync(channel);
        Assert.True(response.TryGetProperty("result", out _));
    }

    [Fact]
    public async Task Broken_json_is_a_parse_error()
    {
        await using var daemon = await DaemonHarness.StartAsync(FakeSurfaces.LocalSeat());
        await using var channel = await daemon.ConnectRawAsync();

        var response = await ExchangeAsync(channel, """{"jsonrpc":"2.0","id":1,"method":""");

        Assert.Equal(JsonRpcErrorCodes.ParseError, ErrorCode(response));
        Assert.Equal(JsonValueKind.Null, response.GetProperty("id").ValueKind);
    }

    [Fact]
    public async Task A_batch_is_refused_without_being_processed()
    {
        // REQ-RPC-5: this generation carries no batches, and a minimal implementation should not
        // have to know what one is.
        await using var daemon = await DaemonHarness.StartAsync(FakeSurfaces.LocalSeat());
        await using var channel = await daemon.ConnectRawAsync();

        var response = await ExchangeAsync(channel, """[{"jsonrpc":"2.0","id":1,"method":"presence.query"}]""");

        Assert.Equal(JsonRpcErrorCodes.InvalidRequest, ErrorCode(response));
    }

    [Fact]
    public async Task An_id_that_is_neither_a_number_nor_a_string_is_refused()
    {
        await using var daemon = await DaemonHarness.StartAsync(FakeSurfaces.LocalSeat());
        await using var channel = await daemon.ConnectRawAsync();

        var response = await ExchangeAsync(channel, """{"jsonrpc":"2.0","id":{"nested":true},"method":"presence.query"}""");

        Assert.Equal(JsonRpcErrorCodes.InvalidRequest, ErrorCode(response));
    }

    [Fact]
    public async Task A_message_without_the_jsonrpc_member_is_refused()
    {
        await using var daemon = await DaemonHarness.StartAsync(FakeSurfaces.LocalSeat());
        await using var channel = await daemon.ConnectRawAsync();

        var response = await ExchangeAsync(channel, """{"id":1,"method":"presence.query"}""");

        Assert.Equal(JsonRpcErrorCodes.InvalidRequest, ErrorCode(response));
    }

    [Fact]
    public async Task An_unknown_method_is_reported_as_unknown()
    {
        await using var daemon = await DaemonHarness.StartAsync(FakeSurfaces.LocalSeat());
        await using var channel = await daemon.ConnectRawAsync();

        var response = await ExchangeAsync(channel, """{"jsonrpc":"2.0","id":1,"method":"presence.summon"}""");

        Assert.Equal(JsonRpcErrorCodes.MethodNotFound, ErrorCode(response));
    }

    [Fact]
    public async Task Authenticate_is_unknown_while_no_tcp_endpoint_exists()
    {
        // REQ-MTH-1 requires presence.authenticate only from a daemon that offers TCP, and this one
        // does not offer TCP yet.
        await using var daemon = await DaemonHarness.StartAsync(FakeSurfaces.LocalSeat());
        await using var channel = await daemon.ConnectRawAsync();

        var response = await ExchangeAsync(
            channel,
            """{"jsonrpc":"2.0","id":1,"method":"presence.authenticate","params":{"token":"x"}}""");

        Assert.Equal(JsonRpcErrorCodes.MethodNotFound, ErrorCode(response));
    }

    [Fact]
    public async Task Positional_parameters_are_refused()
    {
        // REQ-RPC-7: by-name parameters only in this generation.
        await using var daemon = await DaemonHarness.StartAsync(FakeSurfaces.LocalSeat());
        await using var channel = await daemon.ConnectRawAsync();

        var response = await ExchangeAsync(channel, """{"jsonrpc":"2.0","id":1,"method":"presence.pin","params":["ssh"]}""");

        Assert.Equal(JsonRpcErrorCodes.InvalidParams, ErrorCode(response));
    }

    [Fact]
    public async Task A_pin_kind_outside_the_enumeration_is_an_invalid_parameter()
    {
        await using var daemon = await DaemonHarness.StartAsync(FakeSurfaces.LocalSeat());
        await using var channel = await daemon.ConnectRawAsync();

        var response = await ExchangeAsync(
            channel,
            """{"jsonrpc":"2.0","id":1,"method":"presence.pin","params":{"kind":"telepathy"}}""");

        Assert.Equal(JsonRpcErrorCodes.InvalidParams, ErrorCode(response));
    }

    [Fact]
    public async Task Pinning_to_unknown_is_an_invalid_parameter()
    {
        // REQ-MTH-4 names the three pinnable kinds, and "unknown" is a valid surface kind that is
        // not one of them.
        await using var daemon = await DaemonHarness.StartAsync(FakeSurfaces.LocalSeat());
        await using var channel = await daemon.ConnectRawAsync();

        var response = await ExchangeAsync(
            channel,
            """{"jsonrpc":"2.0","id":1,"method":"presence.pin","params":{"kind":"unknown"}}""");

        Assert.Equal(JsonRpcErrorCodes.InvalidParams, ErrorCode(response));
    }

    [Fact]
    public async Task A_pin_with_no_duration_member_lasts_until_removed()
    {
        // REQ-MTH-3: omitted and null mean the same thing.
        await using var daemon = await DaemonHarness.StartAsync(FakeSurfaces.LocalSeat());
        await using var channel = await daemon.ConnectRawAsync();

        var response = await ExchangeAsync(
            channel,
            """{"jsonrpc":"2.0","id":1,"method":"presence.pin","params":{"kind":"ssh"}}""");

        Assert.Equal(JsonValueKind.Null, response.GetProperty("result").GetProperty("expiresAt").ValueKind);
    }

    [Fact]
    public async Task Unknown_members_in_a_request_are_ignored()
    {
        // The additive-extension promise of REQ-DM-2 read from the other direction: a generation-1
        // daemon must not choke on a member it has never heard of.
        await using var daemon = await DaemonHarness.StartAsync(FakeSurfaces.LocalSeat());
        await using var channel = await daemon.ConnectRawAsync();

        var response = await ExchangeAsync(
            channel,
            """{"jsonrpc":"2.0","id":1,"method":"presence.pin","params":{"kind":"ssh","mood":"anxious"}}""");

        Assert.True(response.TryGetProperty("result", out _));
    }

    [Fact]
    public async Task A_notification_is_acted_on_and_never_answered()
    {
        await using var daemon = await DaemonHarness.StartAsync(FakeSurfaces.LocalSeat());
        await using var channel = await daemon.ConnectRawAsync();

        await channel.WriteFrameAsync(
            Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","method":"presence.pin","params":{"kind":"ssh"}}"""),
            CancellationToken.None);

        // The next request would be answered before any response to the notification, so a reply to
        // the query with the pin already in place proves both halves.
        var response = await ExchangeAsync(channel, """{"jsonrpc":"2.0","id":9,"method":"presence.query"}""");

        Assert.Equal(9, response.GetProperty("id").GetInt32());
        Assert.Equal("ssh", response.GetProperty("result").GetProperty("pin").GetProperty("kind").GetString());
    }

    [Fact]
    public async Task An_empty_line_is_ignored_rather_than_answered()
    {
        await using var daemon = await DaemonHarness.StartAsync(FakeSurfaces.LocalSeat());
        await using var channel = await daemon.ConnectRawAsync();

        await channel.WriteFrameAsync(ReadOnlyMemory<byte>.Empty, CancellationToken.None);

        var response = await ExchangeAsync(channel, """{"jsonrpc":"2.0","id":3,"method":"presence.query"}""");
        Assert.Equal(3, response.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task Every_response_is_one_line()
    {
        // REQ-RPC-3: no unescaped line feed inside a message, which is what makes line-oriented
        // tooling work at all.
        await using var daemon = await DaemonHarness.StartAsync(FakeSurfaces.LocalSeat(), FakeSurfaces.Ssh());
        await using var channel = await daemon.ConnectRawAsync();

        await channel.WriteFrameAsync(
            Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","id":1,"method":"presence.query"}"""),
            CancellationToken.None);

        var frame = await channel.ReadFrameAsync(CancellationToken.None);

        Assert.NotNull(frame);
        Assert.DoesNotContain((byte)'\n', frame);
    }

    private static async Task<JsonElement> ExchangeAsync(NdjsonChannel channel, string request)
    {
        await channel.WriteFrameAsync(Encoding.UTF8.GetBytes(request), CancellationToken.None);
        return await ReadAsync(channel);
    }

    private static async Task<JsonElement> ReadAsync(NdjsonChannel channel)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var frame = await channel.ReadFrameAsync(timeout.Token);
        Assert.NotNull(frame);
        return JsonDocument.Parse(frame).RootElement.Clone();
    }

    private static int ErrorCode(JsonElement response) => response.GetProperty("error").GetProperty("code").GetInt32();
}
