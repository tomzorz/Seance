# Seance

*Is anybody there?*

Seance is a per-user presence daemon for Windows, Linux, and macOS: it knows whether you're sitting at the machine, connected over SSH, driving a remote desktop, or gone, and it knows where the process asking came from. It is the reference implementation of the [Human Presence Protocol](https://github.com/tomzorz/HumanPresenceProtocol/blob/main/SPEC.md) (HPP/1), written in C# on .NET 8.

The motivating use: a helper that "opens a file" should launch a local viewer when you're at the keyboard, but spin up a tunnel and a web server when the request arrives over SSH. Seance answers the question; the helper picks the behavior.

## Status

The protocol works end to end, against a synthetic surface provider. No platform provider exists yet, so on a real machine the daemon reports one seat with every signal `unknown` and every capability `false` — which is the honest answer for a daemon that cannot read the platform, and exactly what a client should be able to cope with.

What is done: the domain model, the reachability procedure, pins, event debouncing, the NDJSON JSON-RPC server, the unix-socket and named-pipe bindings, the client library, and the `presence` CLI. What is next: the logind provider, then WTS, then caller resolution, then CoreGraphics. Build order and platform strategy are in [docs/implementation-notes.md](docs/implementation-notes.md).

## Trying it

Start a daemon on a throwaway endpoint and talk to it:

```bash
dotnet run --project sources/Seance/src/Seance.Daemon -- --fake --endpoint pipe:hpp1-scratch
```

```bash
dotnet run --project sources/Seance/src/Seance.Cli -- query --endpoint pipe:hpp1-scratch
```

On Linux and macOS use `--endpoint unix:/tmp/hpp1-scratch.sock` instead. Drop `--endpoint` on both sides to use the platform default, and set `HPP_ENDPOINT` to point clients somewhere else without passing a switch.

```
presence query      every surface, the pin, and what the daemon can read
presence reach      which surfaces can reach you, most likely first
presence context    what the daemon knows about the calling process
presence watch      follow presence events until interrupted
presence pin ssh    override the derived state, optionally --for 30m
presence unpin      drop the pin
```

Every command takes `--json` and prints the protocol object as it came off the wire, so `presence query --json | jq` works without a protocol library.

## Layout

The repo follows the [archetype](https://github.com/tomzorz/archetype) skeleton; the solution lives in `sources/Seance/`:

```
sources/Seance/
  src/
    Seance.Protocol/         The wire: data model, enum spellings, NDJSON framing,
                             endpoint discovery. Shared by both ends, depends on nothing.
    Seance.Core/             All the interesting logic, zero OS calls.
                             Snapshot diffing, debounce, pins, reachability, fan-out.
    Seance.Platform/         Shared helpers for the platform providers.
    Seance.Platform.Windows/ WTS sessions via CsWin32.
    Seance.Platform.Linux/   systemd-logind over D-Bus.
    Seance.Platform.MacOS/   CoreGraphics polling + utmpx.
    Seance.Transport/        Listeners, connection dispatch, caller resolution.
    Seance.Daemon/           Generic Host entry point, DI wiring.
    Seance.Client/           Client library (NuGet-shaped).
    Seance.Cli/              The `presence` command.
  tests/
    Seance.Protocol.Tests/   Wire format and framing conformance.
    Seance.Core.Tests/       The bulk of the coverage, driven by a fake provider.
    Seance.Transport.Tests/  End to end over a real socket, including hand-written frames.
```

`Seance.Core` is where the bulk of the test coverage sits: it is driven entirely by synthetic surface lists and never touches an OS API. Platform providers are deliberately thin. No test mocks anything — the fake provider is scripted input feeding real logic, and the transport tests talk to a real daemon over a real socket.

## Protocol

Seance speaks HPP/1: newline-delimited JSON-RPC 2.0 over `$XDG_RUNTIME_DIR/hpp1.sock` (Linux/macOS), `\\.\pipe\hpp1-<sessionId>` (Windows), and an optional token-authenticated TCP listener for cross-boundary clients like WSL. The spec is normative; where this implementation and the spec disagree, the spec wins or the spec gets fixed — never a silent fork.

The TCP binding is not implemented yet, which is why `presence.authenticate` answers "method not found": the spec requires that method only of a daemon that offers TCP.
