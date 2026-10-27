using System.Text.Json;
using Seance.Cli;
using Seance.Client;
using Seance.Protocol;
using Seance.Protocol.Json;

const string Usage = """
    presence - ask the local presence daemon where the human is

    usage: presence <command> [options]

    commands:
      query              every surface, the pin and what the daemon can read
      reach              which surfaces can reach the user, most likely first
      context            what the daemon knows about this process
      watch              follow presence events until interrupted
      pin <kind>         override the derived state; kind is localSeat, ssh or remoteDesktop
      unpin              drop the active pin

    options:
      --json             print the protocol objects instead of a table
      --endpoint <ep>    talk to this endpoint (unix:<path>, pipe:<name>)
      --for <duration>   how long a pin lasts, for example 30m; omit to last until removed
      -h, --help         this text
      --version          print the client version

    The endpoint comes from HPP_ENDPOINT when it is set, otherwise from the platform default.
    """;

CommandLine command;
try
{
    command = CommandLine.Parse(args);
}
catch (FormatException exception)
{
    Console.Error.WriteLine($"presence: {exception.Message}");
    return 2;
}

switch (command.Command)
{
    case "help":
        Console.WriteLine(Usage);
        return 0;

    case "version":
        Console.WriteLine(typeof(PresenceClient).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");
        return 0;
}

try
{
    await using var client = await PresenceClient.ConnectAsync(command.Endpoint);

    switch (command.Command)
    {
        case "query":
            Write(await client.QueryAsync(), HppJsonContext.Default.PresenceSnapshot, Render.Snapshot);
            return 0;

        case "reach":
            Write(await client.ReachAsync(), HppJsonContext.Default.Reachability, Render.Reachability);
            return 0;

        case "context":
            Write(await client.ContextAsync(), HppJsonContext.Default.CallerContext, Render.Caller);
            return 0;

        case "pin":
        {
            if (command.Arguments.Count != 1)
            {
                Console.Error.WriteLine("presence: pin needs exactly one kind, for example 'presence pin ssh'.");
                return 2;
            }

            var pin = await client.PinAsync(CommandLine.ParseKind(command.Arguments[0]), command.Duration);
            Write(pin, HppJsonContext.Default.Pin, Render.Pin);
            return 0;
        }

        case "unpin":
            await client.UnpinAsync();
            Write(Acknowledgment.Instance, HppJsonContext.Default.Acknowledgment, _ => "pin removed" + Environment.NewLine);
            return 0;

        case "watch":
            return await WatchAsync(client, command.Json);

        default:
            Console.Error.WriteLine($"presence: '{command.Command}' is not a command. Try 'presence --help'.");
            return 2;
    }
}
catch (FormatException exception)
{
    Console.Error.WriteLine($"presence: {exception.Message}");
    return 2;
}
catch (JsonRpcException exception)
{
    Console.Error.WriteLine($"presence: the daemon refused that: {exception.ErrorMessage}");
    return 1;
}
catch (Exception exception) when (exception is IOException or NotSupportedException)
{
    Console.Error.WriteLine($"presence: {exception.Message}");
    return 1;
}

void Write<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo, Func<T, string> render)
{
    Console.Write(command.Json ? JsonSerializer.Serialize(value, typeInfo) + Environment.NewLine : render(value));
}

static async Task<int> WatchAsync(PresenceClient client, bool json)
{
    using var stopping = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        stopping.Cancel();
    };

    var snapshot = await client.SubscribeAsync(stopping.Token);

    // REQ-MTH-5 hands back the baseline with the subscription, so print it: a watcher that starts
    // by showing nothing until something moves is useless for the first minute.
    Console.Write(json
        ? JsonSerializer.Serialize(snapshot, HppJsonContext.Default.PresenceSnapshot) + Environment.NewLine
        : Render.Snapshot(snapshot));

    try
    {
        await foreach (var notification in client.Events.ReadAllAsync(stopping.Token))
        {
            Console.Write(json
                ? JsonSerializer.Serialize(notification, HppJsonContext.Default.PresenceEventParams) + Environment.NewLine
                : Environment.NewLine + Render.Event(notification));
        }
    }
    catch (OperationCanceledException)
    {
        // Interrupted, which is how watching ends.
    }

    return 0;
}
