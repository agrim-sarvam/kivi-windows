using System.Text.Json.Nodes;
using Kivi.Core.Wire;
using Xunit;

namespace Kivi.Core.Tests;

/// <summary>
/// Unit tests for the byte-exact wire invariants (docs/maps/service-client-wire.md §4).
/// These are protocol contracts, not preferences.
/// </summary>
public class WireEncodingTests
{
    // ---- A3 trap: formatting_enabled is ALWAYS emitted ----

    [Fact]
    public void BuildContext_Always_Emits_FormattingEnabled_When_Default_True()
    {
        var obj = WireEncoder.BuildContext("sess-1");
        Assert.True(obj.ContainsKey("formatting_enabled"));
        Assert.True(obj["formatting_enabled"]!.GetValue<bool>());
    }

    [Fact]
    public void BuildContext_Always_Emits_FormattingEnabled_Even_When_False()
    {
        var obj = WireEncoder.BuildContext("sess-1", new ContextOptions { FormattingEnabled = false });
        Assert.True(obj.ContainsKey("formatting_enabled"));
        Assert.False(obj["formatting_enabled"]!.GetValue<bool>());
    }

    // ---- A3 trap: closed-enum guard for general_app_style_preset ----

    [Theory]
    [InlineData("verbatim")]
    [InlineData("casual")]
    [InlineData("transliteration")]
    [InlineData("formal")]
    public void BuildContext_Accepts_The_Four_Valid_Presets(string preset)
    {
        var obj = WireEncoder.BuildContext("s", new ContextOptions { GeneralAppStylePreset = preset });
        Assert.Equal(preset, obj["general_app_style_preset"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("custom")]
    [InlineData("free_flowing")]
    [InlineData("VERBATIM")] // case-sensitive
    [InlineData("")]
    [InlineData("nonsense")]
    public void BuildContext_Omits_Invalid_Preset_So_Message_Does_Not_Fail(string bad)
    {
        var obj = WireEncoder.BuildContext("s", new ContextOptions { GeneralAppStylePreset = bad });
        Assert.False(obj.ContainsKey("general_app_style_preset"));
    }

    [Fact]
    public void GeneralAppStylePreset_IsValid_Rejects_Slugs()
    {
        Assert.True(GeneralAppStylePreset.IsValid("formal"));
        Assert.False(GeneralAppStylePreset.IsValid("custom"));
        Assert.False(GeneralAppStylePreset.IsValid("free_flowing"));
        Assert.False(GeneralAppStylePreset.IsValid(null));
    }

    // ---- transcription_mode default = codemix ----

    [Fact]
    public void BuildContext_Defaults_TranscriptionMode_To_Codemix()
    {
        var obj = WireEncoder.BuildContext("s");
        Assert.Equal("codemix", obj["transcription_mode"]!.GetValue<string>());
    }

    // ---- JSON is snake_case + sorted keys + no slash escape ----

    [Fact]
    public void Encode_Produces_Sorted_Keys()
    {
        var obj = new JsonObject { ["zebra"] = 1, ["alpha"] = 2, ["mango"] = 3 };
        var json = WireEncoder.Encode(obj);
        Assert.Equal("{\"alpha\":2,\"mango\":3,\"zebra\":1}", json);
    }

    [Fact]
    public void Encode_Sorts_Keys_Recursively()
    {
        var obj = new JsonObject
        {
            ["b"] = new JsonObject { ["y"] = 1, ["x"] = 2 },
            ["a"] = 3,
        };
        var json = WireEncoder.Encode(obj);
        Assert.Equal("{\"a\":3,\"b\":{\"x\":2,\"y\":1}}", json);
    }

    [Fact]
    public void Encode_Does_Not_Escape_Slashes()
    {
        var obj = new JsonObject { ["u"] = "a/b/c" };
        var json = WireEncoder.Encode(obj);
        Assert.Contains("a/b/c", json);
        Assert.DoesNotContain("a\\/b", json);
    }

    [Fact]
    public void Encode_Context_Is_Deterministic_And_SnakeCase()
    {
        var json = WireEncoder.Encode(WireEncoder.BuildContext("SESSION-UUID"));
        // Sorted keys, snake_case, no slash escaping.
        Assert.Equal(
            "{\"auto_persona_resolution\":true,\"client_capabilities\":{\"spoken_shortcuts_v1\":true}," +
            "\"formatting_enabled\":true,\"session_id\":\"SESSION-UUID\"," +
            "\"supports_formatting_progress\":true,\"transcription_mode\":\"codemix\",\"type\":\"context\"}",
            json);
    }

    // ---- MVP end_of_speech ----

    [Fact]
    public void EndOfSpeech_Mvp_Is_Type_Only()
    {
        Assert.Equal("{\"type\":\"end_of_speech\"}", WireEncoder.Encode(WireEncoder.BuildEndOfSpeech()));
    }

    [Fact]
    public void Cancel_And_Ping_Are_Literal()
    {
        Assert.Equal("{\"type\":\"cancel\"}", WireEncoder.Encode(WireEncoder.BuildCancel()));
        Assert.Equal("{\"type\":\"ping\"}", WireEncoder.Encode(WireEncoder.BuildPing()));
    }

    // ---- audio frame size ----

    [Fact]
    public void Audio_Frame_Is_3200_Bytes()
    {
        Assert.Equal(3200, DictationAudio.FrameBytes);
        Assert.Equal(1600, DictationAudio.FrameSamples);
        Assert.Equal(16000, DictationAudio.SampleRate);
        Assert.Equal(DictationAudio.FrameSamples * 2, DictationAudio.FrameBytes);
    }

    // ---- budget constants byte-exact ----

    [Fact]
    public void Budgets_Are_Byte_Exact()
    {
        Assert.Equal(4000, DictationBudgets.AckTimeoutMs);
        Assert.Equal(4000, DictationBudgets.AuthRefreshTimeoutMs);
        Assert.Equal(20000, DictationBudgets.PingIntervalMs);
        Assert.Equal(2, DictationBudgets.PongMissLimit);
        Assert.Equal(50, DictationBudgets.MaxPendingAudioFrames);
        Assert.Equal(20000, DictationBudgets.FinalTimeoutMs);
        Assert.Equal(180, DictationBudgets.AuthRefreshLeadSeconds);
    }
}
