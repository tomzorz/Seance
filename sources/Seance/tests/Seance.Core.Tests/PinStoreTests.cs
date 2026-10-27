using Seance.Protocol;

namespace Seance.Core.Tests;

/// <summary>
/// The pin is the owning user's correction for wrong heuristics (HPP section 5.4).
/// </summary>
public class PinStoreTests
{
    [Fact]
    public void Nothing_is_pinned_to_begin_with()
    {
        var store = new PinStore(new TestTimeProvider());

        Assert.Null(store.Current);
    }

    [Fact]
    public void A_pin_without_a_duration_lasts_until_removed()
    {
        var time = new TestTimeProvider();
        var store = new PinStore(time);

        var pin = store.Set(SurfaceKind.Ssh, null);
        time.Advance(TimeSpan.FromDays(30));

        Assert.Null(pin.ExpiresAt);
        Assert.Equal(pin, store.Current);
    }

    [Fact]
    public void A_pin_stops_applying_after_its_expiry()
    {
        var time = new TestTimeProvider();
        var store = new PinStore(time);

        store.Set(SurfaceKind.LocalSeat, TimeSpan.FromMinutes(10));
        time.Advance(TimeSpan.FromMinutes(9));
        Assert.NotNull(store.Current);

        time.Advance(TimeSpan.FromMinutes(1));
        Assert.Null(store.Current);
    }

    [Fact]
    public void The_expiry_timestamp_is_the_moment_the_pin_stops()
    {
        var time = new TestTimeProvider();
        var store = new PinStore(time);

        var pin = store.Set(SurfaceKind.LocalSeat, TimeSpan.FromMinutes(10));

        Assert.Equal(time.GetUtcNow().AddMinutes(10), pin.ExpiresAt);
        Assert.Equal(time.GetUtcNow(), pin.PinnedAt);
    }

    [Fact]
    public void A_new_pin_replaces_the_active_one()
    {
        var store = new PinStore(new TestTimeProvider());

        store.Set(SurfaceKind.Ssh, TimeSpan.FromHours(1));
        var replacement = store.Set(SurfaceKind.LocalSeat, null);

        Assert.Equal(replacement, store.Current);
        Assert.Equal(SurfaceKind.LocalSeat, store.Current?.Kind);
    }

    [Fact]
    public void Clearing_removes_the_pin()
    {
        var store = new PinStore(new TestTimeProvider());

        store.Set(SurfaceKind.Ssh, null);
        store.Clear();

        Assert.Null(store.Current);
    }

    [Fact]
    public void Clearing_nothing_is_not_an_error()
    {
        var store = new PinStore(new TestTimeProvider());

        store.Clear();

        Assert.Null(store.Current);
    }

    [Fact]
    public void Unknown_is_not_a_pinnable_kind()
    {
        var store = new PinStore(new TestTimeProvider());

        Assert.Throws<ArgumentOutOfRangeException>(() => store.Set(SurfaceKind.Unknown, null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_pin_that_would_be_born_expired_is_rejected(int milliseconds)
    {
        var store = new PinStore(new TestTimeProvider());

        Assert.Throws<ArgumentOutOfRangeException>(
            () => store.Set(SurfaceKind.Ssh, TimeSpan.FromMilliseconds(milliseconds)));
    }
}
