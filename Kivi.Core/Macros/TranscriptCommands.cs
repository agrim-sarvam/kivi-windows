using System.Text.RegularExpressions;
namespace Kivi.Core.Macros;

public readonly record struct TranscriptCommandResult(string Transcript, bool ShouldPressEnter);

public static class TranscriptCommands
{
    private static readonly Regex PressEnter =
        new(@"(?:^|[ \t\r\n,;:\-]+)press[ \t\r\n]+enter[\s\p{P}]*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static TranscriptCommandResult Parse(string transcript, bool pressEnterEnabled)
    {
        if (!pressEnterEnabled) return new(transcript.Trim(), false);
        var m = PressEnter.Match(transcript);
        if (!m.Success) return new(transcript.Trim(), false);
        return new(transcript.Remove(m.Index, m.Length).Trim(), true);
    }
}
