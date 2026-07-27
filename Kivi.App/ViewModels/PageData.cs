using System;
using System.Collections.Generic;
using System.Globalization;

namespace Kivi.App.ViewModels;

/// <summary>
/// Stub/seed data for the M5 pages, ported verbatim from main-window/model/*.ts. Real data
/// (SQLite history, REST memory/shortcuts/personas/leaderboard/usage) is wired in P6; here the
/// pages render faithfully against these seed literals.
/// </summary>
public static class PageData
{
    private static readonly CultureInfo InIN = CultureInfo.GetCultureInfo("en-IN");
    /// <summary>en-IN Indian digit grouping (3;2) — format.ts formatCount.</summary>
    public static string FormatCount(long n) => n.ToString("N0", InIN);

    // ---------- Record ----------
    public sealed record Take(string Text, string App);
    public static readonly Take[] RecentTakes =
    {
        new("let's ship the electron shell today — rail, record, history, settings all wired.", "slack"),
        new("reminder: the paper grain is off under reduce transparency, quieter in dark.", "notes"),
        new("thanks for the review — pushing the fix now and cutting an alpha after.", "mail"),
    };
    public const int TodayWordCount = 1240;
    public const string TodayAppSpread = "from slack, notes, mail";
    public const string TakesSummary = "3 takes today · from slack, notes, mail";

    public static readonly Dictionary<string, string[]> GreetingPools = new()
    {
        ["early"] = new[] { "early bird? him too.", "up early — say the word.", "morning's quiet. perfect." },
        ["morning"] = new[] { "good morning — the mic's warm.", "morning! ready when you are." },
        ["midday"] = new[] { "afternoon — right where you left off.", "midday. words flowing?" },
        ["evening"] = new[] { "evening — home stretch.", "long day of yapping? welcome home.", "back again. it's good here." },
        ["late"] = new[] { "still up? he's listening.", "night owl hours — keep it low." },
    };

    public static string GreetingPoolKey(int hour) => hour switch
    {
        >= 5 and < 9 => "early",
        >= 9 and < 12 => "morning",
        >= 12 and < 17 => "midday",
        >= 17 and < 22 => "evening",
        _ => "late",
    };

    // ---------- History ----------
    public sealed record HistRow(string Text, string App, string AppColor, string Time);
    public sealed record DayGroup(string Title, HistRow[] Rows);
    public static readonly DayGroup[] History =
    {
        new("today", new[]
        {
            new HistRow("let's ship the electron shell today — rail, record, history, settings all wired.", "slack", "#602861", "2:14 pm"),
            new HistRow("reminder: the paper grain is off under reduce transparency, and quieter in dark.", "notes", "#B9902E", "11:52 am"),
            new HistRow("thanks for the review — pushing the fix now and cutting an alpha after.", "mail", "#3478F6", "9:07 am"),
        }),
        new("yesterday", new[]
        {
            new HistRow("the orb never travels — it shrinks in place while the box unfurls below it.", "slack", "#602861", "6:41 pm"),
            new HistRow("canon rule: one accent per surface, under eight percent coverage, red is errors only.", "notes", "#B9902E", "3:20 pm"),
        }),
        new("earlier this week", new[]
        {
            new HistRow("season mix is load-bearing for the wordmark and every page title — no fallback.", "docs", "#34C759", "mon 4:05 pm"),
        }),
    };

    // ---------- Memory (dictionary) ----------
    public sealed record MemTerm(string Term, string Note, bool Imported);
    public const int MemoryPageSize = 8;
    public static readonly MemTerm[] Terms =
    {
        new("Aaditya Kshatriya", "colleague — not \"aditya shatriya\"", false),
        new("Sarvam AI", "", false),
        new("kivi", "always lowercase", false),
        new("Pranav Sridhar", "", true),
        new("VPIO", "apple voice-processing i/o", false),
        new("Bengaluru", "not \"bangalore\"", false),
        new("zero-data retention", "zdr", false),
        new("Meghana", "", true),
        new("Speex", "the experimental aec engine", false),
        new("Electron", "", false),
        new("Kshetra", "", false),
        new("namaste", "keep transliterated, not translated", false),
    };

    // ---------- Shortcuts ----------
    public sealed record Shortcut(string Trigger, string Replacement, bool Imported);
    public static readonly Shortcut[] Shortcuts =
    {
        new("my sign-off", "Warm regards,\nPranav Sridhar\nSarvam AI", false),
        new("standup update", "yesterday: shipped the electron shell.\ntoday: the remaining workspace pages.\nblockers: none.", false),
        new("thanks note", "thank you so much — really appreciate the quick turnaround on this.", true),
        new("address block", "Sarvam AI\n3rd Floor, Prestige Tech Park\nBengaluru 560103", false),
        new("meeting ask", "do you have 20 minutes this week to sync? happy to work around your calendar.", false),
    };

    // ---------- Shared terms ----------
    public sealed record SharedTerm(string Term, string Definition, string AddedBy);
    public static readonly SharedTerm[] SharedTerms =
    {
        new("Canon", "the current design system — forest-green, one accent, flat surfaces.", "abhigyan"),
        new("kivi-service", "the rust backend (STT, formatting, personalization). lowercase, hyphenated.", "rohan"),
        new("orb", "the floating dictation surface. lowercase, never \"the Orb\".", "diya"),
        new("persona", "a per-app writing voice. plural personas; not \"profile\".", "abhigyan"),
        new("Sarvam", "the company name — always capitalized, never \"sarvam ai\" mid-sentence.", "meera"),
        new("take", "one dictation. \"a take\", not \"a recording\".", "kabir"),
        new("VPIO", "apple voice-processing i/o — the default echo-cancellation engine.", "vikram"),
        new("ZDR", "zero-data retention — the privacy mode where nothing is stored server-side.", "ananya"),
    };

    // ---------- Leaderboard (weekly, all activity) ----------
    public sealed record LbEntry(int Rank, string Name, bool You, long RankedWords, long Dictate, long Edit, long DictateEdit);
    public static readonly LbEntry[] Leaderboard =
    {
        new(1, "ananya", false, 21530, 18420, 3110, 2040),
        new(2, "rohan", false, 20440, 16180, 4260, 3380),
        new(3, "meera", false, 18220, 15240, 2980, 1920),
        new(4, "abhigyan", true, 17480, 13960, 3520, 2610),
        new(5, "diya", false, 15100, 11240, 3860, 2980),
        new(6, "kabir", false, 14990, 12880, 2110, 1440),
        new(7, "arjun", false, 12400, 10420, 1980, 1220),
        new(8, "sara", false, 11920, 9280, 2640, 1880),
        new(9, "vikram", false, 9660, 8140, 1520, 990),
        new(10, "nisha", false, 9230, 7020, 2210, 1640),
    };
    public const string LbRange = "jul 21 to jul 27";
    public const string LbUpdated = "updated 4:12 pm";
    public const string LbDaysLeft = "5 days left";

    // ---------- Analytics ----------
    public static readonly int[] AnalyticsWords =
    {
        120, 340, 210, 0, 90, 480, 520, 260, 610, 300, 140, 0, 380, 720, 540, 410,
        260, 90, 0, 470, 880, 640, 520, 300, 180, 0, 240, 560, 700, 430,
    };
    public sealed record DayBucket(string Label, int Words, int Wpm, int Seconds, int Captures);
    public static DayBucket[] AnalyticsBuckets()
    {
        var arr = new DayBucket[AnalyticsWords.Length];
        for (int i = 0; i < arr.Length; i++)
        {
            int words = AnalyticsWords[i];
            int day = 24 + i;
            string label = day <= 30 ? $"jun {day}" : $"jul {day - 30}";
            int wpm = words == 0 ? 0 : 96 + ((i * 7) % 34);
            int captures = words == 0 ? 0 : Math.Max(1, (int)Math.Round(words / 90.0));
            int seconds = words == 0 ? 0 : (int)Math.Round((words / (double)wpm) * 60);
            arr[i] = new DayBucket(label, words, wpm, seconds, captures);
        }
        return arr;
    }
    public sealed record TopApp(string Name, long Words, string Color);
    public static readonly TopApp[] AnalyticsApps =
    {
        new("slack", 12840, "#602861"),
        new("code", 9210, "#2C82C9"),
        new("mail", 6120, "#3478F6"),
        new("chrome", 4380, "#34A853"),
        new("notes", 2960, "#E5B93C"),
        new("discord", 1540, "#5865F2"),
    };
    public sealed record MemStat(string Value, string Label);
    public static readonly MemStat[] MemoryStats =
    {
        new(FormatCount(148), "dictionary"),
        new(FormatCount(92), "discovered"),
        new(FormatCount(56), "added by you"),
        new("mar 2026", "active since"),
    };

    // ---------- Personas ----------
    public sealed record PersonaApp(string Name, string Color);
    public sealed record PersonaVoice(string Name, string Prefix, string Accent, string Suffix, PersonaApp[] Apps);
    public static readonly PersonaVoice[] Personas =
    {
        new("developer", "terse, and your ", "code", " stays code", new[]
        {
            new PersonaApp("cursor", "#3B7DD8"), new PersonaApp("code", "#2C82C9"),
            new PersonaApp("xcode", "#1B7EE0"), new PersonaApp("terminal", "#4A4A4A"),
            new PersonaApp("zed", "#6E56CF"), new PersonaApp("iterm", "#2E8B57"),
        }),
        new("work messaging", "clear, quick, and ", "work-ready", "", new[]
        {
            new PersonaApp("slack", "#611F69"), new PersonaApp("teams", "#4B53BC"),
            new PersonaApp("discord", "#5865F2"), new PersonaApp("zoom", "#2D8CFF"),
        }),
        new("personal messaging", "casual — the way you'd tell a ", "friend", "", new[]
        {
            new PersonaApp("whatsapp", "#25D366"), new PersonaApp("telegram", "#2AABEE"),
            new PersonaApp("signal", "#3A76F0"), new PersonaApp("messages", "#34C759"),
        }),
        new("email", "composed from hello to ", "sign-off", "", new[]
        {
            new PersonaApp("mail", "#1F8FFF"), new PersonaApp("outlook", "#0A6ED1"),
            new PersonaApp("superhuman", "#4B36CC"), new PersonaApp("spark", "#E8402A"),
        }),
        new("other apps", "a clean voice for ", "everything else", "", new[]
        {
            new PersonaApp("notion", "#000000"), new PersonaApp("linear", "#5E6AD2"),
            new PersonaApp("notes", "#E5B93C"), new PersonaApp("figma", "#A259FF"),
        }),
    };
}
