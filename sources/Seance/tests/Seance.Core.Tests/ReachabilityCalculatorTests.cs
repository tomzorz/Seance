using Seance.Core.Providers;
using Seance.Protocol;

namespace Seance.Core.Tests;

/// <summary>
/// REQ-RCH-1 fixes this procedure, so these tests are the conformance suite for it: two daemons that
/// see the same snapshot, pin and caller must answer identically.
/// </summary>
public class ReachabilityCalculatorTests
{
    [Fact]
    public void No_surfaces_at_all_is_absent()
    {
        var result = ReachabilityCalculator.Compute([], null, CallerContexts.Anonymous);

        Assert.Empty(result.Reachable);
        Assert.Equal(BlockedReason.Absent, result.BlockedBy);
    }

    [Fact]
    public void Disconnected_surfaces_are_not_candidates()
    {
        var result = ReachabilityCalculator.Compute(
            [FakeSurfaces.LocalSeat(connected: false), FakeSurfaces.Ssh(connected: false)],
            null,
            CallerContexts.Anonymous);

        Assert.Empty(result.Reachable);
        Assert.Equal(BlockedReason.Absent, result.BlockedBy);
    }

    [Fact]
    public void A_live_unlocked_seat_is_reachable()
    {
        var seat = FakeSurfaces.LocalSeat();

        var result = ReachabilityCalculator.Compute([seat], null, CallerContexts.Anonymous);

        Assert.Equal(seat, Assert.Single(result.Reachable));
        Assert.Null(result.BlockedBy);
    }

    [Fact]
    public void An_off_console_seat_blocks_with_off_console()
    {
        var result = ReachabilityCalculator.Compute(
            [FakeSurfaces.LocalSeat(onConsole: TriState.No)],
            null,
            CallerContexts.Anonymous);

        Assert.Empty(result.Reachable);
        Assert.Equal(BlockedReason.OffConsole, result.BlockedBy);
    }

    [Fact]
    public void A_locked_seat_blocks_with_locked()
    {
        var result = ReachabilityCalculator.Compute(
            [FakeSurfaces.LocalSeat(locked: TriState.Yes)],
            null,
            CallerContexts.Anonymous);

        Assert.Empty(result.Reachable);
        Assert.Equal(BlockedReason.Locked, result.BlockedBy);
    }

    [Fact]
    public void Off_console_wins_over_locked_when_both_removed_candidates()
    {
        var result = ReachabilityCalculator.Compute(
            [FakeSurfaces.LocalSeat("1", locked: TriState.Yes), FakeSurfaces.LocalSeat("2", onConsole: TriState.No)],
            null,
            CallerContexts.Anonymous);

        Assert.Empty(result.Reachable);
        Assert.Equal(BlockedReason.OffConsole, result.BlockedBy);
    }

    [Fact]
    public void Unknown_on_console_removes_nothing()
    {
        // Exclusion needs a definite negative (REQ-TRI-1). An unknowable signal is not a negative.
        var seat = FakeSurfaces.LocalSeat(onConsole: TriState.Unknown, locked: TriState.Unknown);

        var result = ReachabilityCalculator.Compute([seat], null, CallerContexts.Anonymous);

        Assert.Equal(seat, Assert.Single(result.Reachable));
        Assert.Null(result.BlockedBy);
    }

    [Fact]
    public void Ssh_surfaces_survive_lock_and_console_state()
    {
        // Step 4 names localSeat and remoteDesktop only: an ssh session has no screen to lock.
        var ssh = FakeSurfaces.Ssh();

        var result = ReachabilityCalculator.Compute([ssh], null, CallerContexts.Anonymous);

        Assert.Equal(ssh, Assert.Single(result.Reachable));
    }

    [Fact]
    public void A_remote_desktop_off_console_is_removed()
    {
        var result = ReachabilityCalculator.Compute(
            [FakeSurfaces.RemoteDesktop(onConsole: TriState.No)],
            null,
            CallerContexts.Anonymous);

        Assert.Empty(result.Reachable);
        Assert.Equal(BlockedReason.OffConsole, result.BlockedBy);
    }

    [Fact]
    public void Fast_user_switching_leaves_nothing_reachable()
    {
        // The scenario the empty case exists for: a live session that nobody can see, and no remote
        // channel either.
        var result = ReachabilityCalculator.Compute(
            [FakeSurfaces.LocalSeat(onConsole: TriState.No, idleMs: 1200), FakeSurfaces.Ssh(connected: false)],
            null,
            CallerContexts.Anonymous);

        Assert.Empty(result.Reachable);
        Assert.Equal(BlockedReason.OffConsole, result.BlockedBy);
    }

    [Fact]
    public void A_pin_removes_every_other_kind()
    {
        var ssh = FakeSurfaces.Ssh();
        var pin = new Pin { Kind = SurfaceKind.Ssh, PinnedAt = DateTimeOffset.UnixEpoch };

        var result = ReachabilityCalculator.Compute([FakeSurfaces.LocalSeat(), ssh], pin, CallerContexts.Anonymous);

        Assert.Equal(ssh, Assert.Single(result.Reachable));
    }

    [Fact]
    public void A_pin_with_no_matching_surface_blocks_as_pinned_unavailable()
    {
        var pin = new Pin { Kind = SurfaceKind.RemoteDesktop, PinnedAt = DateTimeOffset.UnixEpoch };

        var result = ReachabilityCalculator.Compute(
            [FakeSurfaces.LocalSeat(), FakeSurfaces.Ssh()],
            pin,
            CallerContexts.Anonymous);

        Assert.Empty(result.Reachable);
        Assert.Equal(BlockedReason.PinnedUnavailable, result.BlockedBy);
    }

    [Fact]
    public void A_pinned_surface_that_later_steps_remove_blocks_with_that_reason()
    {
        // The pinned kind exists, so step 2 passes; step 4 then takes it away and step 6 explains.
        var pin = new Pin { Kind = SurfaceKind.LocalSeat, PinnedAt = DateTimeOffset.UnixEpoch };

        var result = ReachabilityCalculator.Compute(
            [FakeSurfaces.LocalSeat(locked: TriState.Yes), FakeSurfaces.Ssh()],
            pin,
            CallerContexts.Anonymous);

        Assert.Empty(result.Reachable);
        Assert.Equal(BlockedReason.Locked, result.BlockedBy);
    }

    [Fact]
    public void A_pinned_kind_that_exists_only_disconnected_is_absent()
    {
        var pin = new Pin { Kind = SurfaceKind.Ssh, PinnedAt = DateTimeOffset.UnixEpoch };

        var result = ReachabilityCalculator.Compute(
            [FakeSurfaces.Ssh(connected: false)],
            pin,
            CallerContexts.Anonymous);

        Assert.Empty(result.Reachable);
        Assert.Equal(BlockedReason.Absent, result.BlockedBy);
    }

    [Fact]
    public void The_caller_own_session_sorts_first()
    {
        var seat = FakeSurfaces.LocalSeat("1", idleMs: 0);
        var ssh = FakeSurfaces.Ssh("7", idleMs: 180_000);

        var result = ReachabilityCalculator.Compute(
            [seat, ssh],
            null,
            CallerContexts.From(SurfaceKind.Ssh, originSessionId: "7"));

        Assert.Equal(["7", "1"], result.Reachable.Select(surface => surface.SessionId));
    }

    [Fact]
    public void The_caller_own_kind_sorts_ahead_of_strangers()
    {
        // A request that arrived over ssh gets answered over ssh first, even when the session id
        // could not be resolved.
        var seat = FakeSurfaces.LocalSeat("1", idleMs: 0);
        var ssh = FakeSurfaces.Ssh("7", idleMs: 180_000);

        var result = ReachabilityCalculator.Compute([seat, ssh], null, CallerContexts.From(SurfaceKind.Ssh));

        Assert.Equal(["7", "1"], result.Reachable.Select(surface => surface.SessionId));
    }

    [Fact]
    public void Session_match_beats_kind_match()
    {
        var mine = FakeSurfaces.Ssh("7", idleMs: 90_000);
        var sameKind = FakeSurfaces.Ssh("8", idleMs: 10);

        var result = ReachabilityCalculator.Compute(
            [sameKind, mine],
            null,
            CallerContexts.From(SurfaceKind.Ssh, originSessionId: "7"));

        Assert.Equal(["7", "8"], result.Reachable.Select(surface => surface.SessionId));
    }

    [Fact]
    public void Within_a_group_the_least_idle_surface_comes_first()
    {
        var busy = FakeSurfaces.Ssh("a", idleMs: 10);
        var stale = FakeSurfaces.Ssh("b", idleMs: 900_000);

        var result = ReachabilityCalculator.Compute([stale, busy], null, CallerContexts.Anonymous);

        Assert.Equal(["a", "b"], result.Reachable.Select(surface => surface.SessionId));
    }

    [Fact]
    public void An_unreadable_idle_time_sorts_last()
    {
        var unreadable = FakeSurfaces.Ssh("a", idleMs: null);
        var stale = FakeSurfaces.Ssh("b", idleMs: 900_000);

        var result = ReachabilityCalculator.Compute([unreadable, stale], null, CallerContexts.Anonymous);

        Assert.Equal(["b", "a"], result.Reachable.Select(surface => surface.SessionId));
    }

    [Fact]
    public void Equal_idle_times_break_by_session_id_in_byte_order()
    {
        var first = FakeSurfaces.Ssh("A", idleMs: 500);
        var second = FakeSurfaces.Ssh("a", idleMs: 500);
        var third = FakeSurfaces.Ssh("b", idleMs: 500);

        var result = ReachabilityCalculator.Compute([third, second, first], null, CallerContexts.Anonymous);

        Assert.Equal(["A", "a", "b"], result.Reachable.Select(surface => surface.SessionId));
    }

    [Fact]
    public void The_input_order_does_not_change_the_answer()
    {
        Surface[] surfaces =
        [
            FakeSurfaces.LocalSeat("1", idleMs: 4_210),
            FakeSurfaces.Ssh("7", idleMs: 180_424),
            FakeSurfaces.Ssh("8", idleMs: 4_210),
            FakeSurfaces.RemoteDesktop("9", onConsole: TriState.Unknown, idleMs: null),
        ];

        var caller = CallerContexts.From(SurfaceKind.Ssh);
        var forward = ReachabilityCalculator.Compute(surfaces, null, caller);
        var reversed = ReachabilityCalculator.Compute(surfaces.Reverse().ToList(), null, caller);

        Assert.Equal(
            forward.Reachable.Select(surface => surface.SessionId),
            reversed.Reachable.Select(surface => surface.SessionId));
    }

    [Fact]
    public void An_unplaceable_caller_matches_no_kind_group()
    {
        // Draft 2 of the spec excludes an unknown originKind from step 5.2: an unclassified caller
        // and an unclassified surface have nothing in common, so the busier seat wins on idle time
        // instead of the mystery surface winning on a false kind match.
        var mystery = FakeSurfaces.Unknown("z", idleMs: 60_000);
        var seat = FakeSurfaces.LocalSeat("1", idleMs: 10);

        var result = ReachabilityCalculator.Compute([seat, mystery], null, CallerContexts.Anonymous);

        Assert.Equal(["1", "z"], result.Reachable.Select(surface => surface.SessionId));
    }

    [Fact]
    public void A_placeable_caller_still_matches_its_own_kind()
    {
        var mystery = FakeSurfaces.Unknown("z", idleMs: 10);
        var ssh = FakeSurfaces.Ssh("7", idleMs: 60_000);

        var result = ReachabilityCalculator.Compute([mystery, ssh], null, CallerContexts.From(SurfaceKind.Ssh));

        Assert.Equal(["7", "z"], result.Reachable.Select(surface => surface.SessionId));
    }
}
