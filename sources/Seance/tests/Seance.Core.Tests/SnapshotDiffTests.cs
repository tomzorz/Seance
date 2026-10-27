using Seance.Core.Providers;
using Seance.Protocol;

namespace Seance.Core.Tests;

/// <summary>
/// What the daemon publishes, and what it deliberately stays quiet about (HPP sections 5.3, 6.3).
/// </summary>
public class SnapshotDiffTests
{
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(5);

    [Fact]
    public void An_unchanged_snapshot_publishes_nothing()
    {
        var before = new[] { FakeSurfaces.LocalSeat(idleMs: 4_000) };
        var after = new[] { FakeSurfaces.LocalSeat(idleMs: 4_000) };

        Assert.Empty(SnapshotDiff.Compare(before, after, Debounce));
    }

    [Fact]
    public void A_new_session_is_a_connect()
    {
        var reasons = SnapshotDiff.Compare(
            [FakeSurfaces.LocalSeat()],
            [FakeSurfaces.LocalSeat(), FakeSurfaces.Ssh()],
            Debounce);

        Assert.Equal([EventReason.SurfaceConnected], reasons);
    }

    [Fact]
    public void A_vanished_session_is_a_disconnect()
    {
        var reasons = SnapshotDiff.Compare(
            [FakeSurfaces.LocalSeat(), FakeSurfaces.Ssh()],
            [FakeSurfaces.LocalSeat()],
            Debounce);

        Assert.Equal([EventReason.SurfaceDisconnected], reasons);
    }

    [Fact]
    public void A_session_that_detaches_in_place_is_a_disconnect()
    {
        var reasons = SnapshotDiff.Compare(
            [FakeSurfaces.Ssh(connected: true)],
            [FakeSurfaces.Ssh(connected: false)],
            Debounce);

        Assert.Equal([EventReason.SurfaceDisconnected], reasons);
    }

    [Fact]
    public void A_session_that_was_already_gone_does_not_disconnect_twice()
    {
        var reasons = SnapshotDiff.Compare(
            [FakeSurfaces.Ssh(connected: false)],
            [],
            Debounce);

        Assert.Empty(reasons);
    }

    [Fact]
    public void A_lock_change_is_published()
    {
        var reasons = SnapshotDiff.Compare(
            [FakeSurfaces.LocalSeat(locked: TriState.No)],
            [FakeSurfaces.LocalSeat(locked: TriState.Yes)],
            Debounce);

        Assert.Equal([EventReason.LockChanged], reasons);
    }

    [Fact]
    public void A_console_change_is_published()
    {
        var reasons = SnapshotDiff.Compare(
            [FakeSurfaces.LocalSeat(onConsole: TriState.Yes)],
            [FakeSurfaces.LocalSeat(onConsole: TriState.No)],
            Debounce);

        Assert.Equal([EventReason.ConsoleChanged], reasons);
    }

    [Fact]
    public void A_signal_that_becomes_unknowable_is_still_a_change()
    {
        // Losing a signal changes what a client can conclude, so it is worth an event.
        var reasons = SnapshotDiff.Compare(
            [FakeSurfaces.LocalSeat(locked: TriState.No)],
            [FakeSurfaces.LocalSeat(locked: TriState.Unknown)],
            Debounce);

        Assert.Equal([EventReason.LockChanged], reasons);
    }

    [Fact]
    public void Rising_idle_time_publishes_nothing()
    {
        // Idle time is a counter that rises every second, so "the value changed" cannot be a
        // trigger (REQ-EVT-5).
        var reasons = SnapshotDiff.Compare(
            [FakeSurfaces.LocalSeat(idleMs: 1_000)],
            [FakeSurfaces.LocalSeat(idleMs: 600_000)],
            Debounce);

        Assert.Empty(reasons);
    }

    [Fact]
    public void Coming_back_after_the_debounce_window_is_an_idle_change()
    {
        var reasons = SnapshotDiff.Compare(
            [FakeSurfaces.LocalSeat(idleMs: 5_000)],
            [FakeSurfaces.LocalSeat(idleMs: 20)],
            Debounce);

        Assert.Equal([EventReason.IdleChanged], reasons);
    }

    [Fact]
    public void A_jiggle_inside_the_debounce_window_publishes_nothing()
    {
        // This is the flapping REQ-DEB-1 exists to prevent: without it every client debounces
        // separately and they all disagree.
        var reasons = SnapshotDiff.Compare(
            [FakeSurfaces.LocalSeat(idleMs: 4_999)],
            [FakeSurfaces.LocalSeat(idleMs: 0)],
            Debounce);

        Assert.Empty(reasons);
    }

    [Fact]
    public void An_unreadable_idle_time_never_fabricates_an_idle_change()
    {
        var appearing = SnapshotDiff.Compare(
            [FakeSurfaces.LocalSeat(idleMs: null)],
            [FakeSurfaces.LocalSeat(idleMs: 0)],
            Debounce);

        var vanishing = SnapshotDiff.Compare(
            [FakeSurfaces.LocalSeat(idleMs: 600_000)],
            [FakeSurfaces.LocalSeat(idleMs: null)],
            Debounce);

        Assert.Empty(appearing);
        Assert.Empty(vanishing);
    }

    [Fact]
    public void A_disconnect_suppresses_the_field_changes_that_came_with_it()
    {
        // A session that went away reports that it went away, not that its lock state moved on the
        // way out.
        var reasons = SnapshotDiff.Compare(
            [FakeSurfaces.LocalSeat(onConsole: TriState.Yes, locked: TriState.No, idleMs: 10_000)],
            [FakeSurfaces.LocalSeat(onConsole: TriState.No, locked: TriState.Unknown, idleMs: 0, connected: false)],
            Debounce);

        Assert.Equal([EventReason.SurfaceDisconnected], reasons);
    }

    [Fact]
    public void Several_changes_publish_in_a_fixed_order()
    {
        var before = new[]
        {
            FakeSurfaces.LocalSeat("1", onConsole: TriState.Yes, locked: TriState.No, idleMs: 60_000),
            FakeSurfaces.Ssh("7"),
        };

        var after = new[]
        {
            FakeSurfaces.LocalSeat("1", onConsole: TriState.No, locked: TriState.Yes, idleMs: 5),
            FakeSurfaces.RemoteDesktop("9"),
        };

        var reasons = SnapshotDiff.Compare(before, after, Debounce);

        Assert.Equal(
            [
                EventReason.SurfaceDisconnected,
                EventReason.SurfaceConnected,
                EventReason.ConsoleChanged,
                EventReason.LockChanged,
                EventReason.IdleChanged,
            ],
            reasons);
    }
}
