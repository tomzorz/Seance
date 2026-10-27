namespace Seance.Protocol;

/// <summary>
/// The description of the process on the other end of one connection (HPP section 4.7). Resolved
/// from transport peer credentials, never from environment variables (REQ-CTX-1, REQ-CTX-2).
/// </summary>
public sealed record CallerContext
{
    public int? Pid { get; init; }

    /// <summary>
    /// The platform-native user identifier: a decimal uid on Linux and macOS, a SID string on Windows.
    /// </summary>
    public string? UserId { get; init; }

    public string? ExecutablePath { get; init; }

    /// <summary>
    /// The surface kind the client originated from, inferred from process ancestry. Unknown when
    /// inference fails.
    /// </summary>
    public required SurfaceKind OriginKind { get; init; }

    public string? OriginSessionId { get; init; }

    /// <summary>
    /// The process chain, ordered from the client toward the root.
    /// </summary>
    public required IReadOnlyList<AncestryEntry> Ancestry { get; init; }

    public required VerifiedBy VerifiedBy { get; init; }

    /// <summary>
    /// The client name of the authenticated token. Null on kernel-verified transports.
    /// </summary>
    public string? ClientName { get; init; }

    /// <summary>
    /// The caller context reported when the transport gives the daemon nothing kernel-verified: the
    /// token-authenticated shape required by REQ-CTX-3.
    /// </summary>
    public static CallerContext ForToken(string clientName) => new()
    {
        OriginKind = SurfaceKind.Unknown,
        Ancestry = [],
        VerifiedBy = VerifiedBy.Token,
        ClientName = clientName,
    };

    /// <summary>
    /// A kernel-verified connection whose peer could not be resolved any further.
    /// </summary>
    public static CallerContext UnresolvedKernel(int? pid = null, string? userId = null) => new()
    {
        Pid = pid,
        UserId = userId,
        OriginKind = SurfaceKind.Unknown,
        Ancestry = [],
        VerifiedBy = VerifiedBy.Kernel,
    };
}

/// <summary>
/// One process in a caller ancestry chain.
/// </summary>
public sealed record AncestryEntry
{
    public required int Pid { get; init; }

    public required string Name { get; init; }
}
