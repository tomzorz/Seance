using Seance.Core.Providers;
using Seance.Protocol;

namespace Seance.Transport.Tests;

/// <summary>
/// The protocol end to end: a client library talking to a daemon over a real socket.
/// </summary>
public class PresenceClientTests
{
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Query_returns_the_surfaces_the_daemon_observed()
    {
        await using var daemon = await DaemonHarness.StartAsync(
            FakeSurfaces.LocalSeat("1", displayServer: "wayland"),
            FakeSurfaces.Ssh("7", idleMs: 180_424));

        await using var client = await daemon.ConnectAsync();
        var snapshot = await client.QueryAsync();

        Assert.Equal(2, snapshot.Surfaces.Count);
        Assert.Equal(SurfaceKind.LocalSeat, snapshot.Surfaces[0].Kind);
        Assert.Equal("wayland", snapshot.Surfaces[0].DisplayServer);
        Assert.Equal(180_424, snapshot.Surfaces[1].IdleMs);
        Assert.Equal("10.0.0.12", snapshot.Surfaces[1].RemoteHost);
        Assert.Null(snapshot.Pin);
        Assert.Equal("seance", snapshot.ServerInfo.Name);
    }

    [Fact]
    public async Task Unknown_signals_survive_the_round_trip_as_unknown()
    {
        // The single most load-bearing rule of the protocol: unknowable never turns into "no"
        // somewhere in the plumbing (REQ-TRI-1, REQ-TRI-2).
        await using var daemon = await DaemonHarness.StartAsync(
            FakeSurfaces.LocalSeat(onConsole: TriState.Unknown, locked: TriState.Unknown, idleMs: null));

        await using var client = await daemon.ConnectAsync();
        var surface = Assert.Single((await client.QueryAsync()).Surfaces);

        Assert.Equal(TriState.Unknown, surface.OnConsole);
        Assert.Equal(TriState.Unknown, surface.Locked);
        Assert.Null(surface.IdleMs);
        Assert.Equal(Reliability.Unreliable, surface.Confidence.Locked);
    }

    [Fact]
    public async Task Reach_orders_surfaces_and_reports_them_over_the_wire()
    {
        await using var daemon = await DaemonHarness.StartAsync(
            FakeSurfaces.LocalSeat("1", idleMs: 900_000),
            FakeSurfaces.Ssh("7", idleMs: 10));

        await using var client = await daemon.ConnectAsync();
        var reachability = await client.ReachAsync();

        Assert.Equal(["7", "1"], reachability.Reachable.Select(surface => surface.SessionId));
        Assert.Null(reachability.BlockedBy);
    }

    [Fact]
    public async Task An_unreachable_machine_comes_back_as_a_result()
    {
        // REQ-RCH-3: unreachable is an answer, so the client must not see an exception here.
        await using var daemon = await DaemonHarness.StartAsync(FakeSurfaces.LocalSeat(onConsole: TriState.No));

        await using var client = await daemon.ConnectAsync();
        var reachability = await client.ReachAsync();

        Assert.Empty(reachability.Reachable);
        Assert.Equal(BlockedReason.OffConsole, reachability.BlockedBy);
    }

    [Fact]
    public async Task Context_reports_a_kernel_verified_connection()
    {
        await using var daemon = await DaemonHarness.StartAsync(FakeSurfaces.LocalSeat());

        await using var client = await daemon.ConnectAsync();
        var caller = await client.ContextAsync();

        Assert.Equal(VerifiedBy.Kernel, caller.VerifiedBy);
        Assert.Null(caller.ClientName);

        // Origin inference is platform work that has not landed; admitting that is the correct
        // answer, not guessing localSeat.
        Assert.Equal(SurfaceKind.Unknown, caller.OriginKind);
        Assert.Empty(caller.Ancestry);
    }

    [Fact]
    public async Task Pinning_and_unpinning_round_trip()
    {
        await using var daemon = await DaemonHarness.StartAsync(FakeSurfaces.LocalSeat(), FakeSurfaces.Ssh());

        await using var client = await daemon.ConnectAsync();

        var pin = await client.PinAsync(SurfaceKind.Ssh, TimeSpan.FromMinutes(30));
        Assert.Equal(SurfaceKind.Ssh, pin.Kind);
        Assert.NotNull(pin.ExpiresAt);

        // REQ-PIN-3: the pin changes reachability, never the reported surfaces.
        var snapshot = await client.QueryAsync();
        Assert.Equal(2, snapshot.Surfaces.Count);
        Assert.Equal(SurfaceKind.Ssh, snapshot.Pin?.Kind);

        Assert.Equal("2", Assert.Single((await client.ReachAsync()).Reachable).SessionId);

        await client.UnpinAsync();
        Assert.Null((await client.QueryAsync()).Pin);
    }

    [Fact]
    public async Task A_pin_without_a_duration_has_no_expiry()
    {
        await using var daemon = await DaemonHarness.StartAsync(FakeSurfaces.LocalSeat());

        await using var client = await daemon.ConnectAsync();
        var pin = await client.PinAsync(SurfaceKind.LocalSeat);

        Assert.Null(pin.ExpiresAt);
    }

    [Fact]
    public async Task Pinning_to_an_unpinnable_kind_is_an_invalid_parameter()
    {
        await using var daemon = await DaemonHarness.StartAsync(FakeSurfaces.LocalSeat());

        await using var client = await daemon.ConnectAsync();

        var failure = await Assert.ThrowsAsync<JsonRpcException>(() => client.PinAsync(SurfaceKind.Unknown));
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, failure.Code);
    }

    [Fact]
    public async Task Subscribing_returns_the_snapshot_and_then_streams_events()
    {
        await using var daemon = await DaemonHarness.StartAsync(FakeSurfaces.LocalSeat("1"));

        await using var client = await daemon.ConnectAsync();
        var snapshot = await client.SubscribeAsync();
        Assert.Single(snapshot.Surfaces);

        daemon.Provider.Update("1", seat => seat with { Locked = TriState.Yes });
        await daemon.Engine.RefreshAsync(CancellationToken.None);

        var notification = await ReadEventAsync(client.Events);

        Assert.Equal(EventReason.LockChanged, notification.Reason);
        Assert.Equal(TriState.Yes, Assert.Single(notification.Surfaces).Locked);
    }

    [Fact]
    public async Task Events_carry_the_whole_surface_list()
    {
        // REQ-EVT-3: a client that misses one notification re-evaluates from complete state.
        await using var daemon = await DaemonHarness.StartAsync(FakeSurfaces.LocalSeat("1"), FakeSurfaces.Ssh("7"));

        await using var client = await daemon.ConnectAsync();
        await client.SubscribeAsync();

        daemon.Provider.Update("1", seat => seat with { OnConsole = TriState.No });
        await daemon.Engine.RefreshAsync(CancellationToken.None);

        var notification = await ReadEventAsync(client.Events);

        Assert.Equal(EventReason.ConsoleChanged, notification.Reason);
        Assert.Equal(2, notification.Surfaces.Count);
    }

    [Fact]
    public async Task A_pin_set_by_one_client_reaches_another()
    {
        await using var daemon = await DaemonHarness.StartAsync(FakeSurfaces.LocalSeat());

        await using var watcher = await daemon.ConnectAsync();
        await watcher.SubscribeAsync();

        await using var setter = await daemon.ConnectAsync();
        await setter.PinAsync(SurfaceKind.Ssh);

        var notification = await ReadEventAsync(watcher.Events);

        Assert.Equal(EventReason.PinChanged, notification.Reason);
        Assert.Equal(SurfaceKind.Ssh, notification.Pin?.Kind);
    }

    [Fact]
    public async Task A_client_that_never_subscribes_gets_no_events()
    {
        await using var daemon = await DaemonHarness.StartAsync(FakeSurfaces.LocalSeat("1"));

        await using var client = await daemon.ConnectAsync();
        await client.QueryAsync();

        daemon.Provider.Update("1", seat => seat with { Locked = TriState.Yes });
        await daemon.Engine.RefreshAsync(CancellationToken.None);

        // Give a stray notification every chance to show up before declaring it absent.
        await client.QueryAsync();

        Assert.False(client.Events.TryRead(out _));
    }

    [Fact]
    public async Task Several_clients_are_served_at_the_same_time()
    {
        await using var daemon = await DaemonHarness.StartAsync(FakeSurfaces.LocalSeat());

        await using var first = await daemon.ConnectAsync();
        await using var second = await daemon.ConnectAsync();
        await using var third = await daemon.ConnectAsync();

        var snapshots = await Task.WhenAll(first.QueryAsync(), second.QueryAsync(), third.QueryAsync());

        Assert.All(snapshots, snapshot => Assert.Single(snapshot.Surfaces));
    }

    [Fact]
    public async Task Calls_on_one_connection_are_multiplexed()
    {
        await using var daemon = await DaemonHarness.StartAsync(FakeSurfaces.LocalSeat(), FakeSurfaces.Ssh());

        await using var client = await daemon.ConnectAsync();

        var query = client.QueryAsync();
        var reach = client.ReachAsync();
        var context = client.ContextAsync();

        await Task.WhenAll(query, reach, context);

        Assert.Equal(2, (await query).Surfaces.Count);
        Assert.Equal(2, (await reach).Reachable.Count);
        Assert.Equal(VerifiedBy.Kernel, (await context).VerifiedBy);
    }

    private static async Task<PresenceEventParams> ReadEventAsync(
        System.Threading.Channels.ChannelReader<PresenceEventParams> events)
    {
        using var timeout = new CancellationTokenSource(EventTimeout);
        return await events.ReadAsync(timeout.Token);
    }
}
