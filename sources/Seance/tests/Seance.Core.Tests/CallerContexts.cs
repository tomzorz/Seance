using Seance.Protocol;

namespace Seance.Core.Tests;

/// <summary>
/// Caller contexts for the reachability tests.
/// </summary>
internal static class CallerContexts
{
    /// <summary>
    /// A kernel-verified caller the daemon could not place: the shape every step-2 connection has.
    /// </summary>
    public static CallerContext Anonymous { get; } = CallerContext.UnresolvedKernel();

    public static CallerContext From(SurfaceKind originKind, string? originSessionId = null) => new()
    {
        Pid = 4242,
        UserId = "1000",
        OriginKind = originKind,
        OriginSessionId = originSessionId,
        Ancestry = [new AncestryEntry { Pid = 4242, Name = "presence" }],
        VerifiedBy = VerifiedBy.Kernel,
    };
}
