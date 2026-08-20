# Implementation notes

Working notes for building Seance. The protocol itself — data model, semantics, wire format, transport bindings — lives in the [HPP spec](https://github.com/tomzorz/HumanPresenceProtocol/blob/main/SPEC.md) and is not restated here; requirement IDs below (REQ-*) refer to it. This document covers what the spec deliberately leaves to implementations: how to actually get the signals out of each OS, the process model, and the build order.

## Provider contract

```csharp
public interface ISurfaceProvider
{
    Task<IReadOnlyList<Surface>> SnapshotAsync(CancellationToken ct);
    IAsyncEnumerable<PresenceEvent> WatchAsync(CancellationToken ct);
    ProviderCapabilities Capabilities { get; }
}
```

The trait is deliberately `enumerate surfaces`, not `get idle time` + `is remote`. What varies across platforms is not just how you detect idle — it's what a session fundamentally is. Windows has WTS sessions, Linux has logind sessions, macOS barely has the concept. Let each provider construct the list however it likes.

`ProviderCapabilities` carries one internal flag beyond the wire capabilities (REQ-CAP-1): `IsPushCapable`. When false, Core drives a poll loop against `SnapshotAsync`; when true, the provider pushes through `WatchAsync`. This flag never crosses the wire — clients always get push events regardless of how the daemon sources them.

Providers are selected at startup via `RuntimeInformation.IsOSPlatform(...)` and registered through DI. Guard each platform assembly with `[SupportedOSPlatform("windows")]` / `("linux")` / `("macos")`.

## Linux — easiest, do this first

Primary source is **systemd-logind** over D-Bus (`org.freedesktop.login1`). One dependency covers nearly everything:

| Need | logind property |
|---|---|
| Session list | `ListSessions()` → per-session object paths |
| SSH vs local | `Remote` (bool), `RemoteHost`, `Service` (= `sshd`), `Class` |
| Session type | `Type` — `x11` / `wayland` / `tty` / `unspecified` |
| Idle | `IdleHint`, `IdleSinceHint` |
| Lock | `LockedHint` — **weak, see below** |
| On-console | `Active` — authoritative; only one session per seat is Active |

`Active` maps directly onto `onConsole` and is the same concept exactly. It comes from logind itself, so mark it `authoritative`.

`LockedHint` is *compositor-reported* and many compositors never set it. When absent, emit `unknown`, not `no` (REQ-TRI-1), and mark `confidence.locked = "unreliable"`. Do not use lock state as a proxy for on-console on this platform — the reliability ordering is the inverse of Windows.

Push events come free via `PropertiesChanged` signals — set `IsPushCapable = true`.

**Library:** `Tmds.DBus.Protocol` (newer, low-allocation) or `Tmds.DBus` (higher-level, easier). Either is fine; prefer the former if going NativeAOT. Verify it trims cleanly *early*, before the codebase depends on it heavily.

**Known gap — Wayland idle precision.** logind's `IdleHint` is coarse: a bool set by the compositor on its own threshold, not a millisecond counter. The accurate source is `ext-idle-notify-v1`, a Wayland wire protocol with no usable managed .NET client. Decision: **ship with `IdleHint` first.** Only if coarseness proves a real problem, add a small C shim exposing `ext-idle-notify-v1` and `DllImport` it. Do not block v1 on this.

For X11 sessions, `XScreenSaverQueryInfo` via P/Invoke against `libXss` gives precise idle and is easy.

Fallback for non-systemd systems: parse `utmp`. Low priority.

**Remote desktop detection:** xrdp/Xvnc sessions appear as distinct logind sessions with `Remote=true`. The case logind *misses* is a VNC server attached to an existing local display (e.g. `x11vnc`) — not a separate session. Detect by checking for a listening socket on 5900–5905 owned by a process in the session; report with `confidence.kind = "unreliable"` per REQ-TRUTH-2.

**Caller resolution:** `Socket.GetRawSocketOption(SOL_SOCKET /*1*/, SO_PEERCRED /*17*/, buffer)` → unpack `struct ucred { pid_t pid; uid_t uid; gid_t gid; }`. Then walk parents via field 4 (`ppid`) of `/proc/{pid}/stat`, reading `/proc/{pid}/comm` for names. Watch out: `/proc/{pid}/stat` field 2 (`comm`) is parenthesised and may contain spaces — parse from the last `)` rather than splitting naively.

## Windows — good APIs, one architectural note

**Interop:** use **CsWin32** (`Microsoft.Windows.CsWin32`, source generator). Add a `NativeMethods.txt` listing the wanted APIs; it generates correct signatures. Do not hand-write `DllImport` blocks.

Needed entries:

```
WTSEnumerateSessions
WTSQuerySessionInformation
WTSFreeMemory
WTSRegisterSessionNotification
WTSINFOEXW
GetLastInputInfo
GetSystemMetrics
PowerSettingRegisterNotification
GetNamedPipeClientProcessId
```

| Need | API |
|---|---|
| Session list | `WTSEnumerateSessions` |
| RDP vs console | `WTSQuerySessionInformation(..., WTSClientProtocolType)` — `0` = console, `2` = RDP |
| Quick RDP check | `GetSystemMetrics(SM_REMOTESESSION)` |
| Remote host | `WTSQuerySessionInformation(..., WTSClientAddress)` |
| Idle | `GetLastInputInfo` (ms since last input) |
| On-console | `WTSQuerySessionInformation(WTS_CURRENT_SESSION, WTSConnectState)` → `WTSActive` vs `WTSDisconnected` |
| Locked | `WTSQuerySessionInformation(WTS_CURRENT_SESSION, WTSSessionInfoEx)` → `WTSINFOEXW` → `WTSINFOEX_LEVEL1.SessionFlags` |
| Lock/connect events | `WTSRegisterSessionNotification` |
| Presence power setting | `GUID_SESSION_USER_PRESENCE` via `PowerSettingRegisterNotification` |

Both connect state and lock state on **the daemon's own session** are readable without elevation — no session-0 involvement, so no service model needed. Querying *other users'* sessions in detail needs `WTS_QUERY_INFORMATION` rights, and that is out of scope anyway (REQ-SCOPE-1).

`WTSSessionInfoEx` gives lock state as a pollable value rather than an event you must track statefully — prefer it over inferring lock from session-change history. **Trap:** `WTS_SESSIONSTATE_LOCK` / `WTS_SESSIONSTATE_UNLOCK` are documented as inverted on Win7/2008R2 relative to Win8+. Version-gate the interpretation.

**Process model (resolved during design):** per-session daemon launched at logon, no Windows Service. The session-0 problem (`GetLastInputInfo` is per-session; a service gets no idle data for interactive sessions) evaporates when the daemon runs inside the session it reports on. Fast user switching needed a new *field* (`onConsole`), not a new architecture.

**Known blind spot:** `SM_REMOTESESSION` and `WTSClientProtocolType` catch RDP only. TeamViewer, AnyDesk, VNC, RustDesk all drive the physical console session and look completely local. Detect by process/service name; report as low-confidence. Document this clearly in the client-facing docs.

**Caller resolution:** `GetNamedPipeClientProcessId`, then walk ancestry via `CreateToolhelp32Snapshot` / `Process32First` / `Process32Next` reading `th32ParentProcessID`.

## macOS — most interop friction, do this last

No sanctioned session enumeration. Model a single `localSeat` surface plus any SSH sessions from `utmpx`.

| Need | Approach |
|---|---|
| Idle | `CGEventSourceSecondsSinceLastEventType(kCGEventSourceStateHIDSystemState /*1*/, kCGAnyInputEventType /*0xFFFFFFFF*/)` → returns `double` seconds. Easy `DllImport`. |
| Lock state | Poll `CGSessionCopyCurrentDictionary()`, read `CGSSessionScreenIsLocked` — **weak, see below** |
| On-console | `kCGSSessionOnConsoleKey`, same dictionary — free, already polling it |
| SSH sessions | `utmpx` via `getutxent` |
| Remote desktop | **No API.** Check for `screensharingd` / ARD agent process. Report `unknown` when absent, `yes` when found. |
| HPD sensor | Not available. Always `unknown`. |

**Explicitly rejected approach:** the `com.apple.screenIsLocked` distributed notification is the "correct" event-driven path, but it requires `NSDistributedNotificationCenter` — Objective-C, meaning `objc_msgSend` interop or a native shim. **Poll `CGSessionCopyCurrentDictionary` at 1–2 Hz instead.** Accurate enough for this use case and avoids all Obj-C interop. Revisit only if polling cost becomes a real complaint.

**Lock state is the weak signal here.** Fast user switching away does not reliably set `CGSSessionScreenIsLocked`. `kCGSSessionOnConsoleKey` is the dependable field for "is this session being displayed," and it lives in the dictionary you are already polling. Mark `confidence.locked = "indicative"`, `confidence.onConsole = "authoritative"`.

Set `IsPushCapable = false`; core drives the poll loop.

`DllImport` paths:

```
/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics
/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation
```

**Caller resolution:** `LOCAL_PEERCRED` via `getsockopt` (level `SOL_LOCAL` = 0, option `LOCAL_PEERCRED` = 1) → `struct xucred`. Not surfaced by .NET, needs a `DllImport`. Ancestry via `sysctl` with `KERN_PROC_PID` reading `kp_eproc.e_ppid`.

## Hosting, packaging, autostart

**Host:** Generic Host + `BackgroundService`. Free lifetime management, logging, config, graceful shutdown.

**Autostart:** systemd user unit (Linux), LaunchAgent (macOS), Registry `Run` key or Task Scheduler at-logon (Windows).

**Publish NativeAOT.** A login-time daemon shouldn't pay JIT startup cost, and AOT keeps RSS in single-digit MB. This constrains reflection: use `System.Text.Json` **source generators from the start** — retrofitting them later is painful. Verify `Tmds.DBus.Protocol` and `StreamJsonRpc` trim cleanly early, before the codebase depends on them heavily. If StreamJsonRpc fights AOT, the protocol is simple enough that a hand-rolled NDJSON JSON-RPC loop over source-generated serializers is a legitimate fallback.

## Build order

1. **Core types + hysteresis + pin logic**, with a `FakeSurfaceProvider`. Full test coverage. No OS code.
2. **Transport + CLI** against the fake provider. End-to-end query/subscribe working on one machine.
3. **Linux provider** (logind). Easiest real platform, validates the abstraction.
4. **Windows provider** (CsWin32 + WTS). Per-session daemon.
5. **Caller resolution** on both, including ancestry walking.
6. **macOS provider.** Most interop friction; benefits from a settled interface.
7. **Client library + CLI** polish, including the client-side interpretation helpers (OSC emission on empty reachability).

**Test fast user switching manually on every platform that has a provider.** Two accounts, switch between them, confirm the switched-away session reports `onConsole = "no"` promptly and that `presence.reach` returns an empty `reachable` with `blockedBy: "offConsole"` rather than a stale `localSeat`. This is the single most likely source of wrong-surface bugs and it does not show up in unit tests against `FakeSurfaceProvider` — though the *handling* of that state absolutely should be covered there.

**Set up CI runners on all three OSes at step 3, not later.** This project is deliberately "different code everywhere behind one interface," which is the weakest case for .NET's write-once story. The failure mode to avoid is discovering months in that `ISurfaceProvider` silently assumed something only logind offers.

## Open questions

1. **Where do wire contracts live?** The handoff put them in `Seance.Transport`, but `Seance.Client` needs the same types without dragging in the server. Likely answer: contracts move to `Seance.Core` (they are the protocol's data model, which Core already owns) or a small `Seance.Protocol` project. Decide at build-order step 2.
2. **Wayland idle shim** — build the C shim, or accept coarse `IdleHint`? Deferred past v1, but flag early if `IdleHint` turns out to be unusable.
3. **OSC emission helper.** The daemon carries no fallback channels (reachability is facts-only per spec §5.5); emitting OSC into the client's own terminal on empty reachability is a `Seance.Client` convenience. Design at step 7.

(The old "policy configuration format" question dissolved when `recommend()` became the deterministic `presence.reach`: there is no daemon policy to configure.)
