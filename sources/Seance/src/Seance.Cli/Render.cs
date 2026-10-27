using System.Globalization;
using System.Text;
using Seance.Protocol;
using Seance.Protocol.Json;

namespace Seance.Cli;

/// <summary>
/// Turns protocol objects into something worth reading in a terminal. The machine-readable view is
/// the JSON the daemon already sent, which is what --json prints.
/// </summary>
internal static class Render
{
    public static string Snapshot(PresenceSnapshot snapshot)
    {
        var text = new StringBuilder();
        text.Append(Surfaces(snapshot.Surfaces));
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"pin       {PinText(snapshot.Pin)}");
        text.AppendLine(CultureInfo.InvariantCulture, $"can read  {CapabilityText(snapshot.Capabilities)}");
        text.AppendLine(CultureInfo.InvariantCulture, $"observed  {Timestamp(snapshot.GeneratedAt)} by {snapshot.ServerInfo.Name} {snapshot.ServerInfo.Version}");
        return text.ToString();
    }

    public static string Reachability(Reachability reachability)
    {
        if (reachability.Reachable.Count == 0)
        {
            return reachability.BlockedBy switch
            {
                BlockedReason.OffConsole => "nothing can reach you: every seat is switched away from" + Environment.NewLine,
                BlockedReason.Locked => "nothing can reach you: every seat is locked" + Environment.NewLine,
                BlockedReason.PinnedUnavailable => "nothing can reach you: the pinned surface is not here" + Environment.NewLine,
                _ => "nothing can reach you: no surface is connected" + Environment.NewLine,
            };
        }

        List<string[]> rows = [["", "KIND", "SESSION", "IDLE", "DETAIL"]];
        for (var index = 0; index < reachability.Reachable.Count; index++)
        {
            var surface = reachability.Reachable[index];
            rows.Add([
                $"{(index + 1).ToString(CultureInfo.InvariantCulture)}.",
                WireNames.Of(surface.Kind),
                surface.SessionId,
                Idle(surface.IdleMs),
                Detail(surface),
            ]);
        }

        return Table(rows);
    }

    public static string Caller(CallerContext caller)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"pid          {caller.Pid?.ToString(CultureInfo.InvariantCulture) ?? "?"}");
        text.AppendLine(CultureInfo.InvariantCulture, $"user         {caller.UserId ?? "?"}");
        text.AppendLine(CultureInfo.InvariantCulture, $"executable   {caller.ExecutablePath ?? "?"}");
        text.AppendLine(CultureInfo.InvariantCulture, $"origin       {WireNames.Of(caller.OriginKind)}{(caller.OriginSessionId is { } session ? $" (session {session})" : string.Empty)}");
        text.AppendLine(CultureInfo.InvariantCulture, $"verified by  {(caller.VerifiedBy == VerifiedBy.Kernel ? "kernel" : $"token ({caller.ClientName})")}");

        var ancestry = caller.Ancestry.Count == 0
            ? "?"
            : string.Join(" < ", caller.Ancestry.Select(entry => $"{entry.Name}({entry.Pid.ToString(CultureInfo.InvariantCulture)})"));

        text.AppendLine(CultureInfo.InvariantCulture, $"ancestry     {ancestry}");
        return text.ToString();
    }

    public static string Event(PresenceEventParams notification)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"{Timestamp(notification.GeneratedAt)}  {WireNames.Of(notification.Reason)}");
        text.Append(Indent(Surfaces(notification.Surfaces)));
        if (notification.Pin is { } pin) text.AppendLine(CultureInfo.InvariantCulture, $"  pin {PinText(pin)}");
        return text.ToString();
    }

    private static string Surfaces(IReadOnlyList<Surface> surfaces)
    {
        if (surfaces.Count == 0) return "no surfaces" + Environment.NewLine;

        List<string[]> rows = [["KIND", "SESSION", "CONSOLE", "LOCKED", "IDLE", "DETAIL"]];
        foreach (var surface in surfaces)
        {
            rows.Add([
                WireNames.Of(surface.Kind),
                surface.SessionId + (surface.Connected ? string.Empty : " (gone)"),
                TriStateText(surface.OnConsole),
                TriStateText(surface.Locked),
                Idle(surface.IdleMs),
                Detail(surface),
            ]);
        }

        return Table(rows);
    }

    private static string Detail(Surface surface)
    {
        var parts = new List<string>(2);
        if (surface.RemoteHost is { } host) parts.Add(host);
        if (surface.Protocol is { } protocol) parts.Add($"via {protocol}");
        if (surface.DisplayServer is { } display) parts.Add(display);
        return string.Join(" ", parts);
    }

    private static string Table(List<string[]> rows)
    {
        var widths = new int[rows[0].Length];
        foreach (var row in rows)
        {
            for (var column = 0; column < row.Length; column++) widths[column] = Math.Max(widths[column], row[column].Length);
        }

        var text = new StringBuilder();
        foreach (var row in rows)
        {
            for (var column = 0; column < row.Length; column++)
            {
                var last = column == row.Length - 1;
                text.Append(last ? row[column] : row[column].PadRight(widths[column] + 2));
            }

            text.AppendLine();
        }

        return text.ToString();
    }

    private static string Indent(string block) =>
        string.Join(
            Environment.NewLine,
            block.TrimEnd().Split(Environment.NewLine).Select(line => "  " + line)) + Environment.NewLine;

    public static string Pin(Pin pin) => "pin  " + PinText(pin) + Environment.NewLine;

    private static string PinText(Pin? pin) => pin is null
        ? "none"
        : $"{WireNames.Of(pin.Kind)} until {(pin.ExpiresAt is { } expiry ? Timestamp(expiry) : "removed")}";

    private static string CapabilityText(PresenceCapabilities capabilities)
    {
        var readable = new List<string>(5);
        if (capabilities.CanEnumerateSessions) readable.Add("sessions");
        if (capabilities.CanDetectRemoteDesktop) readable.Add("remote-desktop");
        if (capabilities.CanReadIdle) readable.Add("idle");
        if (capabilities.CanReadLockState) readable.Add("lock");
        if (capabilities.HasHumanPresenceSensor) readable.Add("human-presence");
        return readable.Count == 0 ? "nothing" : string.Join(" ", readable);
    }

    private static string Idle(long? idleMs)
    {
        if (idleMs is not { } milliseconds) return "?";

        var span = TimeSpan.FromMilliseconds(milliseconds);
        if (span.TotalSeconds < 10) return $"{span.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)}s";
        if (span.TotalMinutes < 1) return $"{((int)span.TotalSeconds).ToString(CultureInfo.InvariantCulture)}s";
        if (span.TotalHours < 1) return $"{span.Minutes.ToString(CultureInfo.InvariantCulture)}m{span.Seconds.ToString(CultureInfo.InvariantCulture)}s";
        return $"{((int)span.TotalHours).ToString(CultureInfo.InvariantCulture)}h{span.Minutes.ToString(CultureInfo.InvariantCulture)}m";
    }

    private static string Timestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static string TriStateText(TriState value) => value switch
    {
        TriState.Yes => "yes",
        TriState.No => "no",
        _ => "?",
    };
}
