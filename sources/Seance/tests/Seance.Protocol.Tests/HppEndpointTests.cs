using Seance.Protocol;

namespace Seance.Protocol.Tests;

/// <summary>
/// Endpoint discovery (HPP section 7.1). Everything here lives in one class on purpose: the
/// environment variable is process-wide, and xunit runs the tests of one class in sequence.
/// </summary>
public class HppEndpointTests
{
    [Fact]
    public void A_unix_endpoint_keeps_its_path()
    {
        var endpoint = HppEndpoint.Parse("unix:/run/user/1000/hpp1.sock");

        Assert.Equal(EndpointKind.UnixSocket, endpoint.Kind);
        Assert.Equal("/run/user/1000/hpp1.sock", endpoint.Value);
        Assert.Equal("unix:/run/user/1000/hpp1.sock", endpoint.ToString());
    }

    [Fact]
    public void A_pipe_endpoint_carries_the_name_without_the_prefix()
    {
        var endpoint = HppEndpoint.Parse("pipe:hpp1-2");

        Assert.Equal(EndpointKind.NamedPipe, endpoint.Kind);
        Assert.Equal("hpp1-2", endpoint.Value);
        Assert.Equal("pipe:hpp1-2", endpoint.ToString());
    }

    [Fact]
    public void A_tcp_endpoint_splits_host_from_port()
    {
        var endpoint = HppEndpoint.Parse("tcp:127.0.0.1:18512");

        Assert.Equal(EndpointKind.Tcp, endpoint.Kind);
        Assert.Equal("127.0.0.1", endpoint.Value);
        Assert.Equal(18512, endpoint.Port);
        Assert.Equal("tcp:127.0.0.1:18512", endpoint.ToString());
    }

    [Fact]
    public void A_bracketed_ipv6_host_survives_the_round_trip()
    {
        var endpoint = HppEndpoint.Parse("tcp:[::1]:18512");

        Assert.Equal("::1", endpoint.Value);
        Assert.Equal(18512, endpoint.Port);
        Assert.Equal("tcp:[::1]:18512", endpoint.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("hpp1.sock")]
    [InlineData("smtp:localhost")]
    [InlineData("unix:")]
    [InlineData("pipe:")]
    [InlineData("tcp:127.0.0.1")]
    [InlineData("tcp:127.0.0.1:0")]
    [InlineData("tcp:127.0.0.1:99999")]
    [InlineData("tcp:127.0.0.1:http")]
    [InlineData("tcp:[::1]18512")]
    public void Anything_else_is_not_an_endpoint(string text)
    {
        Assert.False(HppEndpoint.TryParse(text, out _, out var error));
        Assert.NotNull(error);
        Assert.Throws<FormatException>(() => HppEndpoint.Parse(text));
    }

    [Fact]
    public void The_default_endpoint_carries_the_generation()
    {
        // Generations are discovered by endpoint name and nothing else (HPP section 8).
        Assert.Contains(HppEndpoint.Generation, HppEndpoint.PlatformDefault().Value, StringComparison.Ordinal);
    }

    [Fact]
    public void The_default_matches_the_platform_binding()
    {
        var endpoint = HppEndpoint.PlatformDefault();

        Assert.Equal(
            OperatingSystem.IsWindows() ? EndpointKind.NamedPipe : EndpointKind.UnixSocket,
            endpoint.Kind);
    }

    [Fact]
    public void The_environment_variable_wins_over_the_platform_default()
    {
        // REQ-DISC-1, and the flag that stops a client from quietly falling back (REQ-DISC-3).
        var original = Environment.GetEnvironmentVariable(HppEndpoint.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(HppEndpoint.EnvironmentVariable, "unix:/tmp/relay.sock");

            var endpoint = HppEndpoint.Resolve();

            Assert.Equal("/tmp/relay.sock", endpoint.Value);
            Assert.True(endpoint.FromEnvironment);
        }
        finally
        {
            Environment.SetEnvironmentVariable(HppEndpoint.EnvironmentVariable, original);
        }
    }

    [Fact]
    public void An_unset_variable_falls_back_to_the_platform_default()
    {
        var original = Environment.GetEnvironmentVariable(HppEndpoint.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(HppEndpoint.EnvironmentVariable, null);

            var endpoint = HppEndpoint.Resolve();

            Assert.Equal(HppEndpoint.PlatformDefault().Value, endpoint.Value);
            Assert.False(endpoint.FromEnvironment);
        }
        finally
        {
            Environment.SetEnvironmentVariable(HppEndpoint.EnvironmentVariable, original);
        }
    }
}
