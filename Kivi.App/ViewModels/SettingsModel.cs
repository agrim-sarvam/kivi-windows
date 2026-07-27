using System.Collections.Generic;
using System.Linq;

namespace Kivi.App.ViewModels;

/// <summary>Settings taxonomy — port of model/settingsPanes.ts.</summary>
public static class SettingsModel
{
    public sealed record Pane(string Id, string Title, string Subtitle, string[] Keywords);
    public sealed record Group(string Id, string Title, bool ShowsReset, Pane[] Panes);

    public static readonly Group[] Groups =
    {
        new("behaves", "how kivi behaves", true, new[]
        {
            new Pane("general", "general", "the fundamentals, your shortcuts, and your privacy.",
                new[] { "language", "code-mix", "hinglish", "launch at login", "welcome", "demo", "shortcut", "kivi key", "hey kivi", "cancel take", "paste last", "privacy", "personalization", "retry failed dictations" }),
            new Pane("orb", "the orb", "the always-present surface — make it yours.",
                new[] { "orb", "theme", "size", "dock", "placement", "free", "resting", "do not disturb", "dnd", "sounds", "haptics", "cues", "state colors" }),
            new Pane("system", "system settings", "your mic, the permissions kivi needs, and how it reads the screen.",
                new[] { "system", "microphone", "mic", "input device", "level meter", "accessibility", "permission", "screen context", "press enter", "keep in orb", "clipboard", "dictation" }),
        }),
        new("team", "you & your team", false, new[]
        {
            new Pane("plan", "plan & billing", "start free. upgrade when kivi's part of how you work.",
                new[] { "plan", "billing", "free", "pro", "enterprise", "upgrade", "subscription", "payment" }),
            new Pane("invite", "invite friends", "take flight — give a friend their wings, grow your flock.",
                new[] { "invite", "friends", "referral", "flock", "take flight", "wings" }),
            new Pane("org", "org & workspace", "your team and who can do what.",
                new[] { "org", "organization", "workspace", "team", "members", "roles", "admin", "invite link" }),
            new Pane("account", "account", "who you are on kivi.",
                new[] { "account", "profile", "picture", "email", "name", "sign out", "delete account" }),
            new Pane("advanced", "advanced", "for power users — and the things most people never touch.",
                new[] { "advanced", "endpoint", "software update", "sparkle", "calm motion", "idle timeout", "reset all" }),
        }),
    };

    public const string DefaultPane = "general";

    public static Group[] Filter(string query)
    {
        var q = query.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(q)) return Groups;
        return Groups
            .Select(g => g with { Panes = g.Panes.Where(p => p.Title.ToLowerInvariant().Contains(q) || p.Keywords.Any(k => k.Contains(q))).ToArray() })
            .Where(g => g.Panes.Length > 0)
            .ToArray();
    }

    public static (Pane pane, bool showsReset)? Find(string id)
    {
        foreach (var g in Groups)
            foreach (var p in g.Panes)
                if (p.Id == id) return (p, g.ShowsReset);
        return null;
    }
}
