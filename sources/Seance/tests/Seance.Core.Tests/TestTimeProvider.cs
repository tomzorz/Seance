namespace Seance.Core.Tests;

/// <summary>
/// A clock the test drives by hand. Not a mock: the code under test reads a real
/// <see cref="TimeProvider"/> and gets a real answer, the test just decides what "now" is.
/// </summary>
public sealed class TestTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public TestTimeProvider()
        : this(new DateTimeOffset(2026, 8, 17, 14, 3, 7, TimeSpan.Zero))
    {
    }

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan amount) => _now += amount;
}
