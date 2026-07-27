using Kivi.Core.Rest;
using Xunit;

namespace Kivi.Core.Tests;

/// <summary>/v1/edit responds camelCase — read `text`, not `edited` (map §5.2).</summary>
public class RestClientTests
{
    [Fact]
    public void ParseEditResult_Reads_CamelCase_Text_Field()
    {
        const string json =
            "{\"requestId\":\"r-1\",\"text\":\"Make it formal, please.\",\"mode\":\"custom\"," +
            "\"editRequestText\":\"make it formal\",\"resolvedPersonaSlug\":\"global\"," +
            "\"resolvedPreset\":\"formal\",\"resolvedPresetIds\":[\"p1\",\"p2\"]," +
            "\"modelUsed\":\"gemma-4-sarvam-flow\",\"latencyMs\":812}";
        var r = KiviRestClient.ParseEditResult(json);
        Assert.Equal("r-1", r.RequestId);
        Assert.Equal("Make it formal, please.", r.Text); // read `text`, NOT `edited`
        Assert.Equal("custom", r.Mode);
        Assert.Equal("global", r.ResolvedPersonaSlug);
        Assert.Equal(new[] { "p1", "p2" }, r.ResolvedPresetIds!);
        Assert.Equal("gemma-4-sarvam-flow", r.ModelUsed);
        Assert.Equal(812, r.LatencyMs);
    }

    [Fact]
    public void ParseEditResult_Does_Not_Read_A_SnakeCase_Or_Edited_Field()
    {
        // A server that (wrongly) used snake_case or `edited` would leave Text null — proving we
        // key strictly off the camelCase `text`.
        var r = KiviRestClient.ParseEditResult("{\"edited\":\"wrong key\",\"request_id\":\"snake\"}");
        Assert.Null(r.Text);
        Assert.Null(r.RequestId);
    }
}
