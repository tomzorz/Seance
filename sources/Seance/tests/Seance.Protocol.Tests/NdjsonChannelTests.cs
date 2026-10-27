using System.Text;
using Seance.Protocol;

namespace Seance.Protocol.Tests;

/// <summary>
/// The framing rules of HPP section 6.1, exercised over a plain stream.
/// </summary>
public class NdjsonChannelTests
{
    [Fact]
    public async Task Frames_are_split_on_the_line_feed()
    {
        var channel = Reading("{\"a\":1}\n{\"b\":2}\n");

        Assert.Equal("{\"a\":1}", await ReadTextAsync(channel));
        Assert.Equal("{\"b\":2}", await ReadTextAsync(channel));
        Assert.Null(await channel.ReadFrameAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_carriage_return_before_the_line_feed_is_dropped()
    {
        // REQ-RPC-4.
        var channel = Reading("{\"a\":1}\r\n");

        Assert.Equal("{\"a\":1}", await ReadTextAsync(channel));
    }

    [Fact]
    public async Task A_line_that_never_ends_is_not_a_frame()
    {
        // The line feed is part of the frame, so a peer that closes mid-line sent nothing.
        var channel = Reading("{\"a\":1}");

        Assert.Null(await channel.ReadFrameAsync(CancellationToken.None));
    }

    [Fact]
    public async Task An_empty_line_is_an_empty_frame()
    {
        var channel = Reading("\n{\"a\":1}\n");

        Assert.Empty((await channel.ReadFrameAsync(CancellationToken.None))!);
        Assert.Equal("{\"a\":1}", await ReadTextAsync(channel));
    }

    [Fact]
    public async Task A_frame_larger_than_the_buffer_is_reassembled()
    {
        var payload = new string('x', 100_000);
        var channel = Reading($"\"{payload}\"\n");

        Assert.Equal($"\"{payload}\"", await ReadTextAsync(channel));
    }

    [Fact]
    public async Task A_frame_past_the_limit_is_refused_rather_than_buffered_forever()
    {
        var channel = new NdjsonChannel(new MemoryStream(Encoding.UTF8.GetBytes(new string('x', 5_000))), maxFrameBytes: 1_024);

        await Assert.ThrowsAsync<InvalidDataException>(async () => await channel.ReadFrameAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Writing_appends_exactly_one_line_feed()
    {
        var stream = new MemoryStream();
        var channel = new NdjsonChannel(stream);

        await channel.WriteFrameAsync(Encoding.UTF8.GetBytes("{\"a\":1}"), CancellationToken.None);
        await channel.WriteFrameAsync(Encoding.UTF8.GetBytes("{\"b\":2}"), CancellationToken.None);

        Assert.Equal("{\"a\":1}\n{\"b\":2}\n", Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Fact]
    public async Task Concurrent_writes_do_not_interleave()
    {
        // The daemon answers requests and pushes events on the same connection, so this is the
        // normal case rather than an edge one.
        var stream = new MemoryStream();
        var channel = new NdjsonChannel(stream);

        var writes = Enumerable.Range(0, 50).Select(index =>
            channel.WriteFrameAsync(Encoding.UTF8.GetBytes(new string((char)('a' + index % 26), 200)), CancellationToken.None).AsTask());

        await Task.WhenAll(writes);

        var lines = Encoding.UTF8.GetString(stream.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(50, lines.Length);
        Assert.All(lines, line => Assert.Equal(200, line.Length));
        Assert.All(lines, line => Assert.Single(line.Distinct()));
    }

    private static NdjsonChannel Reading(string content) =>
        new(new MemoryStream(Encoding.UTF8.GetBytes(content)));

    private static async Task<string> ReadTextAsync(NdjsonChannel channel) =>
        Encoding.UTF8.GetString((await channel.ReadFrameAsync(CancellationToken.None))!);
}
