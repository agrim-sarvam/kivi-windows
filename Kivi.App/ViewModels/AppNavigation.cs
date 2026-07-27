using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Kivi.App.ViewModels;

/// <summary>The app's top-level sections — port of model/sections.ts AppSection.</summary>
public enum AppSection
{
    Record, History, Clipboard, Styles, Presets, Memory,
    Shortcuts, Analytics, SharedTerms, Leaderboard, Settings,
}

public enum RailIconName { MicDot, Clock, Sparkle, Bolt, Brush, Bars, Layers, Trophy, Gear }

public sealed record RailItemSpec(AppSection Section, RailIconName Icon, string Label);
public sealed record RailGroup(string Id, string? Title, IReadOnlyList<RailItemSpec> Items);

/// <summary>
/// The single routing authority — mirrors AppNavigation.swift / model/sections.ts.
/// Lowercase titles; .presets coalesces to .styles; settings is footer-only.
/// </summary>
public sealed partial class AppNavigation : ObservableObject
{
    public static AppSection DefaultSection => AppSection.Record;

    [ObservableProperty]
    private AppSection _section = AppSection.Record;

    [ObservableProperty]
    private bool _railCollapsed;

    public AppNavigation()
    {
        RailCollapsed = LoadCollapsed();
    }

    /// <summary>navigate() with .presets -> .styles coalescing.</summary>
    [RelayCommand]
    public void Navigate(AppSection section) => Section = Resolve(section);

    [RelayCommand]
    public void ToggleCollapse()
    {
        RailCollapsed = !RailCollapsed;
        PersistCollapsed(RailCollapsed);
    }

    public static AppSection Resolve(AppSection s) => s == AppSection.Presets ? AppSection.Styles : s;

    public static string SectionTitle(AppSection s) => s switch
    {
        AppSection.Memory => "dictionary",
        AppSection.SharedTerms => "shared terms",
        _ => s.ToString().ToLowerInvariant(),
    };

    public static readonly IReadOnlyList<RailGroup> Groups = new List<RailGroup>
    {
        new("capture", null, new List<RailItemSpec>
        {
            new(AppSection.Record, RailIconName.MicDot, "record"),
            new(AppSection.History, RailIconName.Clock, "history"),
        }),
        new("your-space", "your space", new List<RailItemSpec>
        {
            new(AppSection.Memory, RailIconName.Sparkle, "dictionary"),
            new(AppSection.Shortcuts, RailIconName.Bolt, "shortcuts"),
            new(AppSection.Styles, RailIconName.Brush, "styles"),
            new(AppSection.Analytics, RailIconName.Bars, "analytics"),
        }),
        new("team-space", "team space", new List<RailItemSpec>
        {
            new(AppSection.SharedTerms, RailIconName.Layers, "shared terms"),
            new(AppSection.Leaderboard, RailIconName.Trophy, "leaderboard"),
        }),
    };

    private const string RegKey = @"Software\Kivi";
    private const string RegValue = "kiviRailCollapsed";

    private static bool LoadCollapsed()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegKey);
            return (k?.GetValue(RegValue) as string) == "1";
        }
        catch { return false; }
    }

    private static void PersistCollapsed(bool v)
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RegKey);
            k?.SetValue(RegValue, v ? "1" : "0");
        }
        catch { /* ignore */ }
    }
}
