namespace Seance.Protocol.Json;

/// <summary>
/// The literal strings the spec fixes for every enumeration value. One place, so a rename of a C#
/// member cannot quietly change the wire format and a human-facing renderer cannot drift from it.
/// </summary>
public static class WireNames
{
    public static string Of(TriState value) => value switch
    {
        TriState.Yes => "yes",
        TriState.No => "no",
        _ => "unknown",
    };

    public static string Of(Reliability value) => value switch
    {
        Reliability.Authoritative => "authoritative",
        Reliability.Indicative => "indicative",
        _ => "unreliable",
    };

    public static string Of(SurfaceKind value) => value switch
    {
        SurfaceKind.LocalSeat => "localSeat",
        SurfaceKind.Ssh => "ssh",
        SurfaceKind.RemoteDesktop => "remoteDesktop",
        _ => "unknown",
    };

    public static string Of(EventReason value) => value switch
    {
        EventReason.SurfaceConnected => "surfaceConnected",
        EventReason.SurfaceDisconnected => "surfaceDisconnected",
        EventReason.LockChanged => "lockChanged",
        EventReason.ConsoleChanged => "consoleChanged",
        EventReason.IdleChanged => "idleChanged",
        EventReason.PinChanged => "pinChanged",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown event reason."),
    };

    public static string Of(BlockedReason value) => value switch
    {
        BlockedReason.OffConsole => "offConsole",
        BlockedReason.Locked => "locked",
        BlockedReason.Absent => "absent",
        BlockedReason.PinnedUnavailable => "pinnedUnavailable",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown blocked reason."),
    };

    public static string Of(VerifiedBy value) => value switch
    {
        VerifiedBy.Kernel => "kernel",
        VerifiedBy.Token => "token",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown verification source."),
    };

    public static bool TryParse(string text, out TriState value)
    {
        switch (text)
        {
            case "yes": value = TriState.Yes; return true;
            case "no": value = TriState.No; return true;
            case "unknown": value = TriState.Unknown; return true;
            default: value = TriState.Unknown; return false;
        }
    }

    public static bool TryParse(string text, out Reliability value)
    {
        switch (text)
        {
            case "authoritative": value = Reliability.Authoritative; return true;
            case "indicative": value = Reliability.Indicative; return true;
            case "unreliable": value = Reliability.Unreliable; return true;
            default: value = Reliability.Unreliable; return false;
        }
    }

    public static bool TryParse(string text, out SurfaceKind value)
    {
        switch (text)
        {
            case "localSeat": value = SurfaceKind.LocalSeat; return true;
            case "ssh": value = SurfaceKind.Ssh; return true;
            case "remoteDesktop": value = SurfaceKind.RemoteDesktop; return true;
            case "unknown": value = SurfaceKind.Unknown; return true;
            default: value = SurfaceKind.Unknown; return false;
        }
    }

    public static bool TryParse(string text, out EventReason value)
    {
        switch (text)
        {
            case "surfaceConnected": value = EventReason.SurfaceConnected; return true;
            case "surfaceDisconnected": value = EventReason.SurfaceDisconnected; return true;
            case "lockChanged": value = EventReason.LockChanged; return true;
            case "consoleChanged": value = EventReason.ConsoleChanged; return true;
            case "idleChanged": value = EventReason.IdleChanged; return true;
            case "pinChanged": value = EventReason.PinChanged; return true;
            default: value = default; return false;
        }
    }

    public static bool TryParse(string text, out BlockedReason value)
    {
        switch (text)
        {
            case "offConsole": value = BlockedReason.OffConsole; return true;
            case "locked": value = BlockedReason.Locked; return true;
            case "absent": value = BlockedReason.Absent; return true;
            case "pinnedUnavailable": value = BlockedReason.PinnedUnavailable; return true;
            default: value = default; return false;
        }
    }

    public static bool TryParse(string text, out VerifiedBy value)
    {
        switch (text)
        {
            case "kernel": value = VerifiedBy.Kernel; return true;
            case "token": value = VerifiedBy.Token; return true;
            default: value = default; return false;
        }
    }
}
