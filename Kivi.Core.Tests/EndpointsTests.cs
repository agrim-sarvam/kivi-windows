using Kivi.Core.Wire;
using Xunit;

namespace Kivi.Core.Tests;

/// <summary>Endpoint resolution / normalization + anonymous-on-loopback (map §1).</summary>
public class EndpointsTests
{
    [Fact]
    public void Local_Is_Loopback_And_Anonymous()
    {
        var e = Endpoints.Local;
        Assert.Equal("ws://127.0.0.1:8788/v1/dictate/stream", e.WebSocketUrl.ToString());
        Assert.Equal("http://127.0.0.1:8788/", e.RestBase.ToString());
        Assert.True(e.AllowsAnonymous);
    }

    [Fact]
    public void Qa_Is_Default_Wss_And_Not_Anonymous()
    {
        var e = Endpoints.Default;
        Assert.Equal(KiviEndpointKind.Qa, e.Kind);
        Assert.Equal("wss://kivi.aws-qa.sarvam.ai/v1/dictate/stream", e.WebSocketUrl.ToString());
        Assert.Equal("https://kivi.aws-qa.sarvam.ai/", e.RestBase.ToString());
        Assert.False(e.AllowsAnonymous);
    }

    [Fact]
    public void RestBase_Derives_From_WsHost()
    {
        Assert.Equal("https://kivi.sarvam.ai/", Endpoints.Prod.RestBase.ToString());
        Assert.Equal("https://kivi.aws-staging.sarvam.ai/", Endpoints.Staging.RestBase.ToString());
    }

    [Fact]
    public void RestUri_Builds_From_Base_Plus_Path()
    {
        Assert.Equal("http://127.0.0.1:8788/v1/edit", Endpoints.Local.RestUri("v1/edit").ToString());
        Assert.Equal("http://127.0.0.1:8788/ready", Endpoints.Local.RestUri("ready").ToString());
    }

    [Theory]
    [InlineData("qa", KiviEndpointKind.Qa)]
    [InlineData("staging", KiviEndpointKind.Staging)]
    [InlineData("prod", KiviEndpointKind.Prod)]
    [InlineData("local", KiviEndpointKind.Local)]
    [InlineData("production", KiviEndpointKind.Qa)] // legacy alias
    public void Parse_Known_Storage_Values(string stored, KiviEndpointKind kind)
    {
        Assert.Equal(kind, Endpoints.Parse(stored).Kind);
    }

    [Fact]
    public void Custom_Loopback_Is_Anonymous()
    {
        var e = Endpoints.Custom("127.0.0.1:9999");
        Assert.Equal("ws://127.0.0.1:9999/v1/dictate/stream", e.WebSocketUrl.ToString());
        Assert.True(e.AllowsAnonymous);
    }

    [Fact]
    public void Custom_Forces_Canonical_Path_And_Strips_Query()
    {
        var e = Endpoints.Custom("https://example.com/some/other/path?token=x#frag");
        Assert.Equal("wss://example.com/v1/dictate/stream", e.WebSocketUrl.ToString());
        Assert.False(e.AllowsAnonymous);
    }

    [Fact]
    public void Custom_Maps_Http_To_Ws()
    {
        var e = Endpoints.Custom("http://myhost.internal");
        Assert.StartsWith("ws://", e.WebSocketUrl.ToString());
        Assert.Equal("http://myhost.internal/", e.RestBase.ToString());
    }

    [Fact]
    public void Custom_Collapses_To_Known_Case()
    {
        var e = Endpoints.Custom("wss://kivi.sarvam.ai/v1/dictate/stream");
        Assert.Equal(KiviEndpointKind.Prod, e.Kind);
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("localhost", true)]
    [InlineData("::1", true)]
    [InlineData("[::1]", true)]
    [InlineData("LOCALHOST", true)]
    [InlineData("kivi.sarvam.ai", false)]
    public void IsLoopbackHost_Classifies(string host, bool expected)
    {
        Assert.Equal(expected, Endpoints.IsLoopbackHost(host));
    }
}
