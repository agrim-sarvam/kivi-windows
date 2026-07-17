using System.Text.RegularExpressions;

namespace Kivi.Core.Polish;

/// <summary>
/// Deterministic regex-based text cleanup for dictation transcripts.
/// Ported from mrinalwadhwa/freeflow's PolishPipeline.swift (Stage 1 —
/// dictated punctuation substitution — plus filler/noise stripping,
/// context-field sanitization, and the clean-transcript skip gate).
/// </summary>
public static class PolishPipeline
{
    private sealed record Rule(string Pattern, string Replacement);

    // Ported verbatim (order preserved) from
    // _reference/mrinalwadhwa-freeflow/FreeFlowKit/Sources/FreeFlowKit/Services/PolishPipeline.swift
    // `punctuationRules`. The Swift source wraps some replacements in
    // <keep>...</keep> (protect: true) so a downstream LLM stage preserves
    // them verbatim; this port keeps only the literal replacement char,
    // per the brief's Rule(Pattern, Replacement) shape.
    private static readonly Rule[] PunctuationRules =
    {
        // Paragraph and line breaks.
        new(@"\bnew paragraph\b", "\n\n"),
        new(@"\bnew line\b", "\n"),
        new(@"\bnewline\b", "\n"),
        // "period" and "full stop" are handled by the model, not
        // deterministically — they collide with nouns ("billing period",
        // "came to a full stop").
        new(@"\bquestion mark\b", "?"),
        new(@"\bexclamation point\b", "!"),
        new(@"\bexclamation mark\b", "!"),
        // Inline punctuation.
        new(@"\bcomma\b", ","),
        new(@"\bcolon\b", ":"),
        new(@"\bsemicolon\b", ";"),
        // Dashes.
        new(@"\bem dash\b", "—"),
        new(@"\ben dash\b", "–"),
        new(@"\bhyphen\b", "-"),
        new(@"\bminus\s+(?:sign|symbol)\b", "-"),
        // Brackets, quotes, and parens. "parent" is a common STT
        // misrecognition for "paren" because "paren" isn't a standalone
        // English word, so we accept it as an alias.
        new(@"\bopen paren(?:t|thesis)?\b", "("),
        new(@"\bclose paren(?:t|thesis)?\b", ")"),
        new(@"\bopen quote\b", "“"),
        new(@"\b(?:close|end) quote\b", "”"),
        new(@"\bunquote\b", "”"),
        new(@"\b(?:apostrophe|single quote)\b", "'"),
        new(@"\bopen bracket\b", "["),
        new(@"\bclose bracket\b", "]"),
        new(@"\b(?:open )?angle bracket\b", "<"),
        new(@"\bless[- ]than sign\b", "<"),
        new(@"\bclose angle bracket\b", ">"),
        new(@"\bgreater[- ]than sign\b", ">"),
        // Symbols.
        new(@"\bdot dot dot\b", "…"),
        new(@"\bellipsis\b", "…"),
        new(@"\b(?:ampersand|and sign|and symbol)\b", "&"),
        new(@"\b(?:at sign|at symbol)\b", "@"),
        new(@"\bhashtag\b", "#"),
        new(@"\b(?:back ?slash|slash en)\b", "\\"),
        new(@"\bforward slash\b", "/"),
        new(@"\b(?:asterisk|asterisk sign)\b", "*"),
        new(@"\bunderscore\b", "_"),
        new(@"\b(?:percent sign|per cent|percentage symbol)\b", "%"),
        new(@"\bdollar sign\b", "$"),
        new(@"\b(?:equals sign|equals symbol)\b", "="),
        new(@"\b(?:plus sign|plus symbol)\b", "+"),
        // Special symbols.
        new(@"\btrademark sign\b", "™"),
        new(@"\btm\b", "™"),
        new(@"\bcopyright sign\b", "©"),
        new(@"\bcopyright symbol\b", "©"),
        new(@"\bdegrees?\s+fahrenheit\b", "°F"),
        new(@"\bdegrees?\s+f\b", "°F"),
        new(@"\bdegrees?\s+celsius\b", "°C"),
        new(@"\bdegrees?\s+centigrade\b", "°C"),
        new(@"\b(?:degree sign|degree symbol)\b", "°"),
    };

    private static readonly string[] Fillers =
        { "um", "eh", "mmm", "uhh", "hm", "umm", "mm", "uh", "uhhh", "uhm", "ah", "hmm", "mh", "ehh" };
    private static readonly string[] NoisePhrases =
        { "uh huh", "uh-huh", "mm hmm", "mm-hmm" };

    /// <summary>
    /// Replace spoken punctuation commands ("comma", "question mark", ...)
    /// with the literal symbols they name. Applied via a MatchEvaluator so
    /// replacement text (e.g. backslash) is inserted literally, never
    /// interpreted as a regex replacement template.
    /// </summary>
    public static string SubstituteDictatedPunctuation(string input)
    {
        var result = input;
        foreach (var rule in PunctuationRules)
            result = Regex.Replace(result, rule.Pattern, m => rule.Replacement, RegexOptions.IgnoreCase);
        return result;
    }

    public static string StripNoisePhrases(string input)
    {
        var pattern = @"\b(" + string.Join("|", NoisePhrases.Select(Regex.Escape)) + @")\b[,.]?\s*";
        return Collapse(Regex.Replace(input, pattern, "", RegexOptions.IgnoreCase));
    }

    public static string StripFillerSounds(string input)
    {
        var pattern = @"\b(" + string.Join("|", Fillers.Select(Regex.Escape)) + @")\b[,.]?\s*";
        return Collapse(Regex.Replace(input, pattern, "", RegexOptions.IgnoreCase));
    }

    /// <summary>
    /// Strip prompt-injection markers from a context field: ChatML
    /// delimiters, &lt;keep&gt; tags, and role-like line prefixes
    /// (SYSTEM:/USER:/ASSISTANT:).
    /// </summary>
    public static string SanitizeContextField(string text)
    {
        var result = text
            .Replace("<|im_start|>", "").Replace("<|im_end|>", "")
            .Replace("<keep>", "").Replace("</keep>", "");
        result = Regex.Replace(result, @"(?:^|\n)\s*(SYSTEM|USER|ASSISTANT)\s*:", "", RegexOptions.IgnoreCase);
        return result.Trim();
    }

    /// <summary>
    /// Whether the last non-whitespace character is sentence-ending
    /// punctuation (., ?, !).
    /// </summary>
    public static bool EndsAtSentenceBoundary(string text)
    {
        for (int i = text.Length - 1; i >= 0; i--)
        {
            if (char.IsWhiteSpace(text[i])) continue;
            return text[i] is '.' or '?' or '!';
        }
        return false;
    }

    /// <summary>
    /// The clean-transcript skip gate: bypass the LLM polish stage when
    /// the text already satisfies four conditions — starts capitalized,
    /// ends at a sentence boundary, contains no filler words, and has no
    /// repeated adjacent word.
    /// </summary>
    public static bool IsClean(string text)
    {
        var t = text.Trim();
        if (t.Length == 0) return false;
        if (!char.IsUpper(t[0])) return false;
        if (!EndsAtSentenceBoundary(t)) return false;
        var fillerRe = @"\b(" + string.Join("|", Fillers.Select(Regex.Escape)) + @")\b";
        if (Regex.IsMatch(t, fillerRe, RegexOptions.IgnoreCase)) return false;
        if (Regex.IsMatch(t, @"\b(\w+)\s+\1\b", RegexOptions.IgnoreCase)) return false; // repeated adjacent word
        return true;
    }

    private static string Collapse(string s) => Regex.Replace(s, " {2,}", " ").Trim();
}
