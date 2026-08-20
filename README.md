# Seance

*Is anybody there?*

Seance is a per-user presence daemon for Windows, Linux, and macOS: it knows whether you're sitting at the machine, connected over SSH, driving a remote desktop, or gone, and it knows where the process asking came from. It is the reference implementation of the [Human Presence Protocol](https://github.com/tomzorz/HumanPresenceProtocol/blob/main/SPEC.md) (HPP/1), written in C# on .NET 8.

The motivating use: a helper that "opens a file" should launch a local viewer when you're at the keyboard, but spin up a tunnel and a web server when the request arrives over SSH. Seance answers the question; the helper picks the behavior.

## Status

Skeleton. The protocol spec is drafted; no behavior code exists yet. Build order and platform strategy are in [docs/implementation-notes.md](docs/implementation-notes.md).

## Layout

The repo follows the [archetype](https://github.com/tomzorz/archetype) skeleton; the solution lives in `sources/Seance/`:

```
sources/Seance/
  src/
    Seance.Core/             All the interesting logic, zero OS calls.
                             Hysteresis, transitions, pins, reachability, fan-out.
    Seance.Platform/         Provider abstractions (ISurfaceProvider, ICallerResolver).
    Seance.Platform.Windows/ WTS sessions via CsWin32.
    Seance.Platform.Linux/   systemd-logind over D-Bus.
    Seance.Platform.MacOS/   CoreGraphics polling + utmpx.
    Seance.Transport/        Socket/pipe server, JSON-RPC, wire contracts.
    Seance.Daemon/           Generic Host entry point, DI wiring.
    Seance.Client/           Client library (NuGet-shaped).
    Seance.Cli/              The `presence` command.
  tests/
    Seance.Core.Tests/       The bulk of testing lives here, driven by a fake provider.
```

`Seance.Core` is where roughly 80% of test coverage should sit: it is driven entirely by synthetic surface lists and never touches an OS API. Platform providers are deliberately thin.

## Protocol

Seance speaks HPP/1: newline-delimited JSON-RPC 2.0 over `$XDG_RUNTIME_DIR/hpp1.sock` (Linux/macOS), `\\.\pipe\hpp1-<sessionId>` (Windows), and an optional token-authenticated TCP listener for cross-boundary clients like WSL. The spec is normative; where this implementation and the spec disagree, the spec wins or the spec gets fixed — never a silent fork.
