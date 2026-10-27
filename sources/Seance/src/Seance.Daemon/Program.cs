using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Seance.Core;
using Seance.Core.Providers;
using Seance.Daemon;
using Seance.Protocol;
using Seance.Transport;

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine(
        """
        seance - the presence daemon behind HPP/1

        usage: seance [options]

        options:
          --endpoint <endpoint>   listen here instead of the platform default
                                  (unix:<path>, pipe:<name>)
          --fake                  use the fake surface provider
          --debounce-ms <n>       idle debounce window, default 5000
          --poll-ms <n>           poll interval for providers that cannot push, default 1000
          -h, --help              this text

        The client-side HPP_ENDPOINT variable overrides the default for clients; --endpoint is the
        matching switch for the daemon.
        """);

    return 0;
}

HppEndpoint endpoint;
PresenceOptions presenceOptions;
bool forceFake;

try
{
    endpoint = ReadOption("--endpoint") is { } value ? HppEndpoint.Parse(value) : HppEndpoint.PlatformDefault();
    forceFake = args.Contains("--fake");
    presenceOptions = new PresenceOptions
    {
        IdleDebounce = TimeSpan.FromMilliseconds(ReadNumber("--debounce-ms") ?? 5000),
        PollInterval = TimeSpan.FromMilliseconds(ReadNumber("--poll-ms") ?? 1000),
    };
}
catch (Exception exception) when (exception is FormatException or ArgumentException)
{
    Console.Error.WriteLine($"seance: {exception.Message}");
    return 2;
}

var builder = Host.CreateApplicationBuilder();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(presenceOptions);
builder.Services.AddSingleton(services => new PinStore(services.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton(services => SurfaceProviderFactory.Create(
    forceFake,
    services.GetRequiredService<ILoggerFactory>().CreateLogger("Seance.Daemon.Providers")));

builder.Services.AddSingleton(services => new PresenceEngine(
    services.GetRequiredService<ISurfaceProvider>(),
    services.GetRequiredService<PinStore>(),
    services.GetRequiredService<TimeProvider>(),
    services.GetRequiredService<PresenceOptions>()));

builder.Services.AddSingleton<ICallerResolver>(_ => new UnresolvedCallerResolver());
builder.Services.AddSingleton(_ => HppListenerFactory.Create(endpoint));
builder.Services.AddSingleton(services => new HppServer(
    services.GetRequiredService<IHppListener>(),
    services.GetRequiredService<PresenceEngine>(),
    services.GetRequiredService<ICallerResolver>(),
    services.GetRequiredService<ILogger<HppServer>>()));

builder.Services.AddHostedService(services => new PresenceDaemon(
    services.GetRequiredService<PresenceEngine>(),
    services.GetRequiredService<ISurfaceProvider>(),
    services.GetRequiredService<HppServer>(),
    services.GetRequiredService<ILogger<PresenceDaemon>>()));

using var host = builder.Build();

try
{
    await host.RunAsync();
    return 0;
}
catch (Exception exception)
{
    host.Services.GetRequiredService<ILogger<Program>>().LogCritical(exception, "The daemon stopped.");
    return 1;
}

string? ReadOption(string name)
{
    var index = Array.IndexOf(args, name);
    if (index < 0) return null;
    if (index + 1 >= args.Length) throw new FormatException($"{name} needs a value.");
    return args[index + 1];
}

int? ReadNumber(string name)
{
    if (ReadOption(name) is not { } text) return null;
    if (!int.TryParse(text, out var value) || value <= 0) throw new FormatException($"{name} needs a positive number of milliseconds.");
    return value;
}

/// <summary>
/// Named so the host logger has something to hang a category on.
/// </summary>
public partial class Program;
