using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Seance.Core;
using Seance.Core.Providers;
using Seance.Transport;

namespace Seance.Daemon;

/// <summary>
/// Runs the engine and the server for the life of the process.
/// </summary>
public sealed class PresenceDaemon(
    PresenceEngine engine,
    ISurfaceProvider provider,
    HppServer server,
    ILogger<PresenceDaemon> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Adopt the starting state before anyone can connect, so the first subscriber does not get
        // told that surfaces which were there all along just appeared.
        await engine.InitializeAsync(stoppingToken).ConfigureAwait(false);

        await Task.WhenAll(
            server.RunAsync(stoppingToken),
            ObserveAsync(stoppingToken)).ConfigureAwait(false);
    }

    private async Task ObserveAsync(CancellationToken stoppingToken)
    {
        if (!provider.Capabilities.IsPushCapable)
        {
            await PollAsync(stoppingToken).ConfigureAwait(false);
            return;
        }

        await Task.WhenAll(
            WatchAsync(stoppingToken),
            ExpirePinsAsync(stoppingToken)).ConfigureAwait(false);
    }

    private async Task WatchAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var signal in provider.WatchAsync(stoppingToken).ConfigureAwait(false))
            {
                logger.LogTrace("Change signalled by {Source}", signal.Source);
                await engine.RefreshAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    private async Task PollAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(engine.Options.PollInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await engine.RefreshAsync(stoppingToken).ConfigureAwait(false);
                engine.CheckPinExpiry();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    /// <summary>
    /// A pin expiry is a state change nothing else would notice on a push-driven provider: no
    /// platform signal fires when a timestamp passes.
    /// </summary>
    private async Task ExpirePinsAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(engine.Options.PinExpiryCheckInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false)) engine.CheckPinExpiry();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }
}
