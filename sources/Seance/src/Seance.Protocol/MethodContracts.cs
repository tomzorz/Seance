namespace Seance.Protocol;

/// <summary>
/// The params of presence.pin (HPP section 6.2).
/// </summary>
public sealed record PinParams
{
    public required SurfaceKind Kind { get; init; }

    /// <summary>
    /// Null or omitted means the pin lasts until removed (REQ-MTH-3).
    /// </summary>
    public long? DurationMs { get; init; }
}

/// <summary>
/// The params of presence.authenticate (HPP section 7.4).
/// </summary>
public sealed record AuthenticateParams
{
    public required string Token { get; init; }
}

/// <summary>
/// The result of presence.authenticate.
/// </summary>
public sealed record AuthenticateResult
{
    public required string ClientName { get; init; }
}

/// <summary>
/// The acknowledgment result of presence.unpin: an empty object.
/// </summary>
public sealed record Acknowledgment
{
    public static readonly Acknowledgment Instance = new();
}
