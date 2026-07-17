using System.Text.RegularExpressions;
namespace Kivi.Core.Macros;

public static class MacroMatcher
{
    public static string Normalize(string text)
        => Regex.Replace(text.ToLowerInvariant(), @"\p{P}", "").Trim();

    public static VoiceMacro? FindMatch(string transcript, IReadOnlyList<VoiceMacro> macros)
    {
        var norm = Normalize(transcript);
        if (norm.Length == 0) return null;
        foreach (var m in macros)
            if (Normalize(m.Command) == norm) return m;
        return null;
    }
}
