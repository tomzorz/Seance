using Seance.Core.Providers;
using Seance.Protocol;

namespace Seance.Core.Tests;

/// <summary>
/// The engine end of the protocol, driven by scripted surfaces rather than an operating system.
/// </summary>
public class PresenceEngineTests
{
    private static (PresenceEngine Engine, FakeSurfaceProvider Provider, TestTimeProvider Time) Build(
        params Surface[] surfaces)
    {
        var provider = new FakeSurfaceProvider(surfaces);
        var time = new TestTimeProvider();
        var engine = new PresenceEngine(provider, new PinStore(time), time);
        return (engine, provider, time);
    }

    private static async Task<PresenceEngine> StartedAsync(params Surface[] surfaces)
    {
        var (engine, _, _) = Build(surfaces);
        await engine.InitializeAsync(CancellationToken.None);
        return engine;
    }

    private static List<PresenceEventParams> Drain(PresenceSubscription subscription)
    {
        var events = new List<PresenceEventParams>();
        while (subscription.Events.TryRead(out var notification)) events.Add(notification);
        return events;
    }

    [Fact]
    public async Task A_query_reports_the_surfaces_the_provider_observed()
    {
        var engine = await StartedAsync(FakeSurfaces.LocalSeat(), FakeSurfaces.Ssh());

        var snapshot = engine.Snapshot();

        Assert.Equal(2, snapshot.Surfaces.Count);
        Assert.Null(snapshot.Pin);
        Assert.Equal("seance", snapshot.ServerInfo.Name);
        Assert.True(snapshot.Capabilities.CanEnumerateSessions);
    }

    [Fact]
    public async Task Subscribing_hands_back_the_current_snapshot()
    {
        // REQ-MTH-5: the subscription and its baseline state are one exchange, so no transition can
        // fall between a query and a following subscribe.
        var engine = await StartedAsync(FakeSurfaces.LocalSeat());

        var (subscription, snapshot) = engine.Subscribe();
        using (subscription)
        {
            Assert.Single(snapshot.Surfaces);
            Assert.Empty(Drain(subscription));
        }
    }

    [Fact]
    public async Task Adopting_the_first_observation_publishes_nothing()
    {
        var (engine, _, _) = Build(FakeSurfaces.LocalSeat());

        await engine.InitializeAsync(CancellationToken.None);
        var (subscription, _) = engine.Subscribe();

        using (subscription)
        {
            await engine.RefreshAsync(CancellationToken.None);
            Assert.Empty(Drain(subscription));
        }
    }

    [Fact]
    public async Task A_lock_change_reaches_every_subscriber_with_the_full_surface_list()
    {
        var (engine, provider, _) = Build(FakeSurfaces.LocalSeat("1"), FakeSurfaces.Ssh("7"));
        await engine.InitializeAsync(CancellationToken.None);

        var (first, _) = engine.Subscribe();
        var (second, _) = engine.Subscribe();

        using (first)
        using (second)
        {
            provider.Update("1", seat => seat with { Locked = TriState.Yes });
            await engine.RefreshAsync(CancellationToken.None);

            foreach (var subscription in new[] { first, second })
            {
                var notification = Assert.Single(Drain(subscription));
                Assert.Equal(EventReason.LockChanged, notification.Reason);

                // REQ-EVT-3: full snapshots, never deltas.
                Assert.Equal(2, notification.Surfaces.Count);
                Assert.Contains(notification.Surfaces, surface => surface.SessionId == "7");
            }
        }
    }

    [Fact]
    public async Task A_closed_subscription_stops_receiving_events()
    {
        var (engine, provider, _) = Build(FakeSurfaces.LocalSeat("1"));
        await engine.InitializeAsync(CancellationToken.None);

        var (subscription, _) = engine.Subscribe();
        subscription.Dispose();

        provider.Update("1", seat => seat with { Locked = TriState.Yes });
        await engine.RefreshAsync(CancellationToken.None);

        Assert.Empty(Drain(subscription));
    }

    [Fact]
    public async Task Pinning_publishes_at_once()
    {
        // REQ-DEB-3: a pin change is discrete and unambiguous, so it never waits for a window.
        var engine = await StartedAsync(FakeSurfaces.LocalSeat());
        var (subscription, _) = engine.Subscribe();

        using (subscription)
        {
            engine.SetPin(SurfaceKind.Ssh, null);

            var notification = Assert.Single(Drain(subscription));
            Assert.Equal(EventReason.PinChanged, notification.Reason);
            Assert.Equal(SurfaceKind.Ssh, notification.Pin?.Kind);
        }
    }

    [Fact]
    public async Task Unpinning_publishes_at_once()
    {
        var engine = await StartedAsync(FakeSurfaces.LocalSeat());
        engine.SetPin(SurfaceKind.Ssh, null);

        var (subscription, _) = engine.Subscribe();
        using (subscription)
        {
            engine.RemovePin();

            var notification = Assert.Single(Drain(subscription));
            Assert.Equal(EventReason.PinChanged, notification.Reason);
            Assert.Null(notification.Pin);
        }
    }

    [Fact]
    public async Task An_expired_pin_publishes_once_the_daemon_notices()
    {
        var (engine, _, time) = Build(FakeSurfaces.LocalSeat());
        await engine.InitializeAsync(CancellationToken.None);
        engine.SetPin(SurfaceKind.Ssh, TimeSpan.FromMinutes(5));

        var (subscription, _) = engine.Subscribe();
        using (subscription)
        {
            engine.CheckPinExpiry();
            Assert.Empty(Drain(subscription));

            time.Advance(TimeSpan.FromMinutes(5));
            engine.CheckPinExpiry();

            var notification = Assert.Single(Drain(subscription));
            Assert.Equal(EventReason.PinChanged, notification.Reason);
            Assert.Null(notification.Pin);
        }
    }

    [Fact]
    public async Task An_expired_pin_is_noticed_by_a_plain_refresh_too()
    {
        var (engine, _, time) = Build(FakeSurfaces.LocalSeat());
        await engine.InitializeAsync(CancellationToken.None);
        engine.SetPin(SurfaceKind.Ssh, TimeSpan.FromMinutes(5));

        var (subscription, _) = engine.Subscribe();
        using (subscription)
        {
            time.Advance(TimeSpan.FromMinutes(6));
            await engine.RefreshAsync(CancellationToken.None);

            Assert.Equal([EventReason.PinChanged], Drain(subscription).Select(notification => notification.Reason));
        }
    }

    [Fact]
    public async Task A_query_reports_the_pin_without_changing_the_surfaces()
    {
        // REQ-PIN-3: the client sees the derived surfaces and the pin, and decides for itself.
        var engine = await StartedAsync(FakeSurfaces.LocalSeat(), FakeSurfaces.Ssh());

        engine.SetPin(SurfaceKind.Ssh, null);
        var snapshot = engine.Snapshot();

        Assert.Equal(2, snapshot.Surfaces.Count);
        Assert.Equal(SurfaceKind.Ssh, snapshot.Pin?.Kind);
    }

    [Fact]
    public async Task Reach_applies_the_active_pin()
    {
        var engine = await StartedAsync(FakeSurfaces.LocalSeat("1"), FakeSurfaces.Ssh("7"));

        engine.SetPin(SurfaceKind.Ssh, null);
        var reachability = engine.Reach(CallerContexts.Anonymous);

        Assert.Equal("7", Assert.Single(reachability.Reachable).SessionId);
    }

    [Fact]
    public async Task Reach_stops_applying_an_expired_pin()
    {
        var (engine, _, time) = Build(FakeSurfaces.LocalSeat("1"));
        await engine.InitializeAsync(CancellationToken.None);
        engine.SetPin(SurfaceKind.Ssh, TimeSpan.FromMinutes(5));

        Assert.Equal(BlockedReason.PinnedUnavailable, engine.Reach(CallerContexts.Anonymous).BlockedBy);

        time.Advance(TimeSpan.FromMinutes(6));

        Assert.Equal("1", Assert.Single(engine.Reach(CallerContexts.Anonymous).Reachable).SessionId);
    }

    [Fact]
    public async Task An_unreachable_machine_is_an_answer_not_a_failure()
    {
        // REQ-RCH-3: the empty case is a result, and the engine never throws for it.
        var engine = await StartedAsync(FakeSurfaces.LocalSeat(onConsole: TriState.No));

        var reachability = engine.Reach(CallerContexts.Anonymous);

        Assert.Empty(reachability.Reachable);
        Assert.Equal(BlockedReason.OffConsole, reachability.BlockedBy);
    }

    [Fact]
    public async Task A_slow_subscriber_loses_the_oldest_events_not_the_newest_state()
    {
        var provider = new FakeSurfaceProvider(FakeSurfaces.LocalSeat("1"));
        var time = new TestTimeProvider();
        var engine = new PresenceEngine(
            provider,
            new PinStore(time),
            time,
            new PresenceOptions { SubscriptionQueueCapacity = 2 });

        await engine.InitializeAsync(CancellationToken.None);
        var (subscription, _) = engine.Subscribe();

        using (subscription)
        {
            for (var i = 0; i < 5; i++)
            {
                var locked = i % 2 == 0 ? TriState.Yes : TriState.No;
                provider.Update("1", seat => seat with { Locked = locked });
                await engine.RefreshAsync(CancellationToken.None);
            }

            var events = Drain(subscription);

            Assert.Equal(2, events.Count);
            Assert.Equal(TriState.Yes, events[^1].Surfaces[0].Locked);
        }
    }
}
