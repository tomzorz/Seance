using Microsoft.Extensions.Logging;
using Seance.Core.Providers;
using Seance.Protocol;

namespace Seance.Daemon;

/// <summary>
/// Picks the provider for the running platform.
/// </summary>
/// <remarks>
/// The real providers land one platform at a time (logind, then WTS, then CoreGraphics). Until a
/// platform has one, the daemon runs on the placeholder below rather than refusing to start, so the
/// protocol is exercisable end to end everywhere.
/// </remarks>
public static class SurfaceProviderFactory
{
    public static ISurfaceProvider Create(bool forceFake, ILogger logger)
    {
        if (forceFake)
        {
            logger.LogInformation("Using the fake surface provider because it was asked for.");
            return Placeholder();
        }

        logger.LogWarning(
            "No surface provider exists for {Platform} yet, so presence is reported as unknown.",
            Environment.OSVersion.Platform);

        return Placeholder();
    }

    /// <summary>
    /// A daemon that cannot read the platform says so: one seat, every signal unknown, every
    /// capability false. Reporting a confident "you are here" would be the exact lie REQ-TRUTH-1
    /// forbids.
    /// </summary>
    private static FakeSurfaceProvider Placeholder()
    {
        var seat = FakeSurfaces.LocalSeat(
            onConsole: TriState.Unknown,
            locked: TriState.Unknown,
            idleMs: null,
            observedAt: DateTimeOffset.UtcNow);

        return new FakeSurfaceProvider(seat)
        {
            Capabilities = new ProviderCapabilities
            {
                Wire = new PresenceCapabilities
                {
                    CanEnumerateSessions = false,
                    CanDetectRemoteDesktop = false,
                    CanReadIdle = false,
                    CanReadLockState = false,
                    HasHumanPresenceSensor = false,
                },
                IsPushCapable = true,
            },
        };
    }
}
