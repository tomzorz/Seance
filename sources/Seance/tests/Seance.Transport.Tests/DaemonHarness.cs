using Microsoft.Extensions.Logging.Abstractions;
using Seance.Client;
using Seance.Core;
using Seance.Core.Providers;
using Seance.Protocol;

namespace Seance.Transport.Tests;

/// <summary>
/// A real daemon on a real endpoint: a real listener, real framing, real JSON. Only the surfaces are
/// synthetic, and they feed the same engine the shipping daemon runs.
/// </summary>
internal sealed class DaemonHarness : IAsyncDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _serving;

    private DaemonHarness(HppEndpoint endpoint, FakeSurfaceProvider provider, PresenceEngine engine)
    {
        Endpoint = endpoint;
        Provider = provider;
        Engine = engine;

        var server = new HppServer(
            HppListenerFactory.Create(endpoint),
            engine,
            new UnresolvedCallerResolver(),
            NullLogger<HppServer>.Instance);

        // The listener claims its endpoint synchronously, so the endpoint is live once this returns.
        _serving = server.RunAsync(_stopping.Token);
    }

    public HppEndpoint Endpoint { get; }

    public FakeSurfaceProvider Provider { get; }

    public PresenceEngine Engine { get; }

    public static async Task<DaemonHarness> StartAsync(params Surface[] surfaces)
    {
        var provider = new FakeSurfaceProvider(surfaces);
        var engine = new PresenceEngine(provider, new PinStore(TimeProvider.System), TimeProvider.System);
        await engine.InitializeAsync(CancellationToken.None);

        return new DaemonHarness(UniqueEndpoint(), provider, engine);
    }

    public Task<PresenceClient> ConnectAsync() => PresenceClient.ConnectAsync(Endpoint);

    /// <summary>
    /// A connection with no client library on it, for testing what the daemon does with input a
    /// well-behaved client would never send.
    /// </summary>
    public async Task<NdjsonChannel> ConnectRawAsync() => new(await HppDialer.ConnectAsync(Endpoint));

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();

        try
        {
            await _serving.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception)
        {
            // The accept loop ends by cancellation, which surfaces however the listener sees fit.
        }

        _stopping.Dispose();
    }

    private static HppEndpoint UniqueEndpoint()
    {
        var name = $"hpp1-test-{Guid.NewGuid():N}";

        return OperatingSystem.IsWindows()
            ? new HppEndpoint { Kind = EndpointKind.NamedPipe, Value = name }
            : new HppEndpoint { Kind = EndpointKind.UnixSocket, Value = Path.Combine(Path.GetTempPath(), name + ".sock") };
    }
}
