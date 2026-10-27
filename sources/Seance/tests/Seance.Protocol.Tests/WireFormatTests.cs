using System.Text.Json;
using Seance.Protocol;
using Seance.Protocol.Json;

namespace Seance.Protocol.Tests;

/// <summary>
/// The wire format itself. Other implementations read these bytes, so the field names and value
/// spellings are a contract, not an implementation detail.
/// </summary>
public class WireFormatTests
{
    private const string SpecExampleSurface =
        """
        {"kind":"localSeat","sessionId":"2","connected":true,
         "onConsole":"yes","locked":"no","idleMs":4210,
         "remoteHost":null,"protocol":null,"displayServer":"wayland",
         "displayPowered":"yes","humanPresent":"unknown",
         "confidence":{"locked":"unreliable","onConsole":"authoritative",
                       "idle":"indicative","kind":"authoritative"},
         "observedAt":"2026-08-17T14:03:07Z"}
        """;

    [Fact]
    public void A_surface_carries_exactly_the_members_the_spec_lists()
    {
        var json = JsonSerializer.Serialize(Example(), HppJsonContext.Default.Surface);

        using var document = JsonDocument.Parse(json);
        var members = document.RootElement.EnumerateObject().Select(member => member.Name).ToArray();

        Assert.Equal(
            [
                "kind", "sessionId", "connected", "onConsole", "locked", "idleMs", "remoteHost",
                "protocol", "displayServer", "displayPowered", "humanPresent", "confidence", "observedAt",
            ],
            members);
    }

    [Fact]
    public void The_spec_example_surface_deserializes()
    {
        var surface = JsonSerializer.Deserialize(SpecExampleSurface, HppJsonContext.Default.Surface);

        Assert.NotNull(surface);
        Assert.Equal(SurfaceKind.LocalSeat, surface.Kind);
        Assert.Equal("2", surface.SessionId);
        Assert.True(surface.Connected);
        Assert.Equal(TriState.Yes, surface.OnConsole);
        Assert.Equal(TriState.No, surface.Locked);
        Assert.Equal(4210, surface.IdleMs);
        Assert.Null(surface.RemoteHost);
        Assert.Equal("wayland", surface.DisplayServer);
        Assert.Equal(TriState.Unknown, surface.HumanPresent);
        Assert.Equal(Reliability.Authoritative, surface.Confidence.OnConsole);
        Assert.Equal(Reliability.Indicative, surface.Confidence.Idle);
        Assert.Equal(new DateTimeOffset(2026, 8, 17, 14, 3, 7, TimeSpan.Zero), surface.ObservedAt);
    }

    [Fact]
    public void Nulls_are_written_out_rather_than_omitted()
    {
        // A client reading remoteHost has to find the member: "absent" and "null" would otherwise be
        // two spellings of the same fact and implementations would disagree about which they emit.
        var json = JsonSerializer.Serialize(Example() with { IdleMs = null }, HppJsonContext.Default.Surface);

        Assert.Contains("\"idleMs\":null", json, StringComparison.Ordinal);
        Assert.Contains("\"remoteHost\":null", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Timestamps_are_utc_with_a_trailing_z()
    {
        var json = JsonSerializer.Serialize(
            Example() with { ObservedAt = new DateTimeOffset(2026, 8, 17, 16, 3, 7, TimeSpan.FromHours(2)) },
            HppJsonContext.Default.Surface);

        Assert.Contains("\"observedAt\":\"2026-08-17T14:03:07.000Z\"", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TriState.Yes, "yes")]
    [InlineData(TriState.No, "no")]
    [InlineData(TriState.Unknown, "unknown")]
    public void Tri_states_use_the_spelling_the_spec_fixes(TriState value, string expected)
    {
        Assert.Equal(expected, WireNames.Of(value));
        Assert.Equal($"\"{expected}\"", JsonSerializer.Serialize(value, HppJsonContext.Default.Surface.Options));
    }

    [Fact]
    public void An_uninitialised_tri_state_reads_as_unknown()
    {
        // REQ-TRI-2 in the type system: the zero value of the enum must not be a definite negative,
        // because a forgotten assignment would otherwise become a confident lie.
        Assert.Equal(TriState.Unknown, default(TriState));
        Assert.Equal(SurfaceKind.Unknown, default(SurfaceKind));
        Assert.Equal(Reliability.Unreliable, default(Reliability));
    }

    [Fact]
    public void A_value_outside_the_enumeration_is_rejected()
    {
        var json = SpecExampleSurface.Replace("\"onConsole\":\"yes\"", "\"onConsole\":\"maybe\"", StringComparison.Ordinal);

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, HppJsonContext.Default.Surface));
    }

    [Fact]
    public void Members_the_specification_does_not_define_are_ignored()
    {
        // REQ-DM-2 is what makes additive extension safe within a generation.
        var json = SpecExampleSurface.Replace("{\"kind\"", "{\"mood\":\"pensive\",\"kind\"", StringComparison.Ordinal);

        var surface = JsonSerializer.Deserialize(json, HppJsonContext.Default.Surface);

        Assert.Equal(SurfaceKind.LocalSeat, surface?.Kind);
    }

    [Fact]
    public void A_snapshot_carries_exactly_the_members_the_spec_lists()
    {
        var snapshot = new PresenceSnapshot
        {
            Surfaces = [Example()],
            Pin = null,
            Capabilities = new PresenceCapabilities
            {
                CanEnumerateSessions = true,
                CanDetectRemoteDesktop = true,
                CanReadIdle = true,
                CanReadLockState = false,
                HasHumanPresenceSensor = false,
            },
            GeneratedAt = new DateTimeOffset(2026, 8, 17, 14, 3, 7, TimeSpan.Zero),
            ServerInfo = new ServerInfo { Name = "seance", Version = "0.1.0" },
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(snapshot, HppJsonContext.Default.PresenceSnapshot));
        var members = document.RootElement.EnumerateObject().Select(member => member.Name).ToArray();

        Assert.Equal(["surfaces", "pin", "capabilities", "generatedAt", "serverInfo"], members);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("pin").ValueKind);
    }

    [Fact]
    public void An_empty_reachability_reports_its_reason()
    {
        var json = JsonSerializer.Serialize(
            new Reachability { Reachable = [], BlockedBy = BlockedReason.PinnedUnavailable },
            HppJsonContext.Default.Reachability);

        Assert.Equal("""{"reachable":[],"blockedBy":"pinnedUnavailable"}""", json);
    }

    [Fact]
    public void A_reachability_with_surfaces_has_no_reason()
    {
        var json = JsonSerializer.Serialize(
            new Reachability { Reachable = [Example()], BlockedBy = null },
            HppJsonContext.Default.Reachability);

        Assert.Contains("\"blockedBy\":null", json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_token_verified_caller_reports_nothing_kernel_verified()
    {
        // REQ-CTX-3: a token tells the daemon a client name and nothing else, and the context must
        // not imply otherwise.
        var caller = CallerContext.ForToken("wsl");

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(caller, HppJsonContext.Default.CallerContext));
        var root = document.RootElement;

        Assert.Equal(JsonValueKind.Null, root.GetProperty("pid").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("userId").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("executablePath").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("originSessionId").ValueKind);
        Assert.Equal("unknown", root.GetProperty("originKind").GetString());
        Assert.Equal(0, root.GetProperty("ancestry").GetArrayLength());
        Assert.Equal("token", root.GetProperty("verifiedBy").GetString());
        Assert.Equal("wsl", root.GetProperty("clientName").GetString());
    }

    [Fact]
    public void An_event_carries_the_reason_the_surfaces_the_pin_and_the_time()
    {
        var notification = new PresenceEventParams
        {
            Reason = EventReason.SurfaceDisconnected,
            Surfaces = [Example()],
            Pin = null,
            GeneratedAt = new DateTimeOffset(2026, 8, 17, 14, 9, 31, TimeSpan.Zero),
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(notification, HppJsonContext.Default.PresenceEventParams));
        var members = document.RootElement.EnumerateObject().Select(member => member.Name).ToArray();

        Assert.Equal(["reason", "surfaces", "pin", "generatedAt"], members);
        Assert.Equal("surfaceDisconnected", document.RootElement.GetProperty("reason").GetString());
    }

    private static Surface Example() =>
        JsonSerializer.Deserialize(SpecExampleSurface, HppJsonContext.Default.Surface)!;
}
