using Seance.Protocol;

namespace Seance.Core.Providers;

/// <summary>
/// The one thing a platform has to do: enumerate the surfaces of the owning user.
/// </summary>
/// <remarks>
/// The contract is deliberately "enumerate surfaces" rather than "read idle time" plus "am I
/// remote". What varies across platforms is not just how a signal is read, it is what a session
/// fundamentally is: Windows has WTS sessions, Linux has logind sessions, macOS barely has the
/// concept. Each provider builds the list however it likes.
/// </remarks>
public interface ISurfaceProvider
{
    /// <summary>
    /// What this provider can actually deliver on the running machine.
    /// </summary>
    ProviderCapabilities Capabilities { get; }

    /// <summary>
    /// Observes the surfaces of the owning user right now (REQ-SCOPE-1: only the owning user).
    /// </summary>
    Task<IReadOnlyList<Surface>> SnapshotAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Yields one signal whenever the platform reports that something may have changed. The engine
    /// answers each signal by taking a fresh snapshot and diffing it.
    /// </summary>
    /// <remarks>
    /// Providers report "something moved", never which event to publish. Deriving the event reason
    /// from a snapshot diff keeps that logic in one place, so two providers cannot disagree about
    /// what counts as a lock change. Providers that cannot push return an empty sequence and set
    /// <see cref="ProviderCapabilities.IsPushCapable"/> to false, which puts the engine on a poll
    /// loop instead.
    /// </remarks>
    IAsyncEnumerable<ProviderChangeSignal> WatchAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The wire capabilities of a provider (REQ-CAP-1) plus the one flag that stays inside the daemon.
/// </summary>
public sealed record ProviderCapabilities
{
    /// <summary>
    /// The capabilities object clients read.
    /// </summary>
    public required PresenceCapabilities Wire { get; init; }

    /// <summary>
    /// The provider pushes change signals through <see cref="ISurfaceProvider.WatchAsync"/>. Never
    /// crosses the wire: clients always get push events regardless of how the daemon sources them.
    /// </summary>
    public bool IsPushCapable { get; init; }
}

/// <summary>
/// A provider's notice that the platform state may have moved.
/// </summary>
/// <param name="Source">Where the signal came from, for logging only.</param>
public readonly record struct ProviderChangeSignal(string Source);
