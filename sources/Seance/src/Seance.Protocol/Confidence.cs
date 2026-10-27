namespace Seance.Protocol;

/// <summary>
/// Per-signal reliability for one surface (HPP section 4.4). Each member reports the actual signal
/// source in use, never a per-platform constant (REQ-CONF-1).
/// </summary>
public sealed record Confidence
{
    public required Reliability Locked { get; init; }

    public required Reliability OnConsole { get; init; }

    public required Reliability Idle { get; init; }

    public required Reliability Kind { get; init; }
}
