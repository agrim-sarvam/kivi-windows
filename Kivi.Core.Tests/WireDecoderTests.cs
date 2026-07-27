using Kivi.Core.Wire;
using Xunit;

namespace Kivi.Core.Tests;

/// <summary>Decoder tolerance + is_final-absent semantics (map §4.3).</summary>
public class WireDecoderTests
{
    [Fact]
    public void Decode_NonJson_Returns_Null()
    {
        Assert.Null(WireDecoder.Decode("not json"));
    }

    [Fact]
    public void Decode_NonObject_Returns_Null()
    {
        Assert.Null(WireDecoder.Decode("[1,2,3]"));
    }

    [Fact]
    public void Decode_Missing_Type_Returns_Null()
    {
        Assert.Null(WireDecoder.Decode("{\"foo\":\"bar\"}"));
    }

    [Fact]
    public void Decode_Unknown_Type_Is_Ignored_Not_Dropped()
    {
        var m = WireDecoder.Decode("{\"type\":\"some_new_type\",\"x\":1}");
        Assert.NotNull(m);
        Assert.Equal(ServerMessageKind.Unknown, m!.Kind);
        Assert.Equal("some_new_type", m.RawType);
    }

    [Fact]
    public void Interim_IsFinal_Absent_Means_True()
    {
        var m = WireDecoder.Decode("{\"type\":\"interim\",\"segment_idx\":2,\"text\":\"hello\"}");
        Assert.Equal(ServerMessageKind.Interim, m!.Kind);
        Assert.True(m.Ok); // is_final absent ⇒ true
        Assert.Equal(2, m.SegmentIdx);
        Assert.Equal("hello", m.Text);
    }

    [Fact]
    public void Interim_IsFinal_False_Is_Not_Settled()
    {
        var m = WireDecoder.Decode("{\"type\":\"interim\",\"segment_idx\":0,\"text\":\"partial\",\"is_final\":false}");
        Assert.Equal(ServerMessageKind.Interim, m!.Kind);
        Assert.False(m.Ok);
    }

    [Fact]
    public void Ack_Extracts_SessionId()
    {
        var m = WireDecoder.Decode("{\"type\":\"ack\",\"session_id\":\"abc-123\"}");
        Assert.Equal(ServerMessageKind.Ack, m!.Kind);
        Assert.Equal("abc-123", m.SessionId);
    }

    [Fact]
    public void Final_Extracts_FormattedText_And_Fallback()
    {
        var m = WireDecoder.Decode(
            "{\"type\":\"final\",\"request_id\":\"r1\",\"formatted_text\":\"Hello world.\"," +
            "\"raw_transcript\":\"hello world\",\"route\":\"llm_small\"," +
            "\"latency\":{\"stt_segments_ms\":[100,200],\"formatting_ms\":50,\"total_ms\":400}," +
            "\"usage\":{\"billable_word_count\":2,\"monthly_word_limit\":50000}}");
        Assert.Equal(ServerMessageKind.Final, m!.Kind);
        var f = m.Final!;
        Assert.Equal("Hello world.", f.FormattedText);
        Assert.Equal("Hello world.", f.PasteText);
        Assert.Equal("hello world", f.RawTranscript);
        Assert.Equal("llm_small", f.Route);
        Assert.Equal(2, f.Usage!.BillableWordCount);
        Assert.Equal(new double[] { 100, 200 }, f.Latency!.SttSegmentsMs!);
    }

    [Fact]
    public void Final_PasteText_Falls_Back_To_Raw_When_Formatted_Empty()
    {
        var m = WireDecoder.Decode("{\"type\":\"final\",\"raw_transcript\":\"raw only\"}");
        Assert.Equal("raw only", m!.Final!.PasteText);
    }

    [Fact]
    public void EosAck_Extracts_ExpectedFormatMs()
    {
        var m = WireDecoder.Decode("{\"type\":\"eos_ack\",\"raw_words\":12,\"expected_format_ms\":1500}");
        Assert.Equal(ServerMessageKind.EosAck, m!.Kind);
        Assert.Equal(12, m.RawWords);
        Assert.Equal(1500, m.ExpectedFormatMs);
    }

    [Fact]
    public void Error_Extracts_Code_And_Message()
    {
        var m = WireDecoder.Decode("{\"type\":\"error\",\"code\":\"SERVICE_BUSY\",\"message\":\"over cap\"}");
        Assert.Equal(ServerMessageKind.Error, m!.Kind);
        Assert.Equal("SERVICE_BUSY", m.ErrorCode);
        Assert.Equal("over cap", m.ErrorMessage);
    }

    [Fact]
    public void OutputSuspect_Read_From_Metadata_Too()
    {
        var m = WireDecoder.Decode("{\"type\":\"final\",\"formatted_text\":\"x\",\"metadata\":{\"output_suspect\":true}}");
        Assert.True(m!.Final!.OutputSuspect);
    }
}
