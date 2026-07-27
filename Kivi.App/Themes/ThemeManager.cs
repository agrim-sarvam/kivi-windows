using System;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using Application = System.Windows.Application;

namespace Kivi.App.Themes;

public enum Appearance { System, Light, Dark }
public enum Mood { Light, Dark }

/// <summary>
/// Appearance authority — mirrors main-window/theme.tsx. Swaps the merged Canon
/// light/dark ResourceDictionary app-wide (a 240ms crossfade is applied by the shell).
/// System follows the Windows app theme (registry AppsUseLightTheme). Persists under
/// the "kiviAppearance" user setting (registry, HKCU\Software\Kivi).
/// </summary>
public sealed class ThemeManager
{
    public static ThemeManager Instance { get; } = new();

    private const string RegKey = @"Software\Kivi";
    private const string RegValue = "kiviAppearance";

    private Appearance _appearance = Appearance.Dark; // default = dark (Canon dark canvas)

    public event EventHandler? MoodChanged;

    public Appearance Appearance
    {
        get => _appearance;
        set
        {
            if (_appearance == value) return;
            _appearance = value;
            Persist(value);
            Apply();
        }
    }

    public Mood Mood => _appearance switch
    {
        Appearance.Light => Mood.Light,
        Appearance.Dark => Mood.Dark,
        _ => SystemMood(),
    };

    private ThemeManager()
    {
        _appearance = Load();
    }

    private static Mood SystemMood()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var v = k?.GetValue("AppsUseLightTheme");
            if (v is int i) return i == 0 ? Mood.Dark : Mood.Light;
        }
        catch { /* ignore */ }
        return Mood.Dark;
    }

    /// <summary>Swap the theme dictionary in App.Resources.MergedDictionaries.</summary>
    public void Apply()
    {
        var app = Application.Current;
        if (app == null) return;

        var mood = Mood;
        var uri = new Uri(
            mood == Mood.Dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative);
        var next = new ResourceDictionary { Source = uri };

        var merged = app.Resources.MergedDictionaries;
        // Remove any prior theme dict (identified by a marker key).
        for (int i = merged.Count - 1; i >= 0; i--)
        {
            var d = merged[i];
            if (d.Contains("IsDarkMood"))
                merged.RemoveAt(i);
        }
        merged.Add(next);
        MoodChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool IsDark => Mood == Mood.Dark;

    private static Appearance Load()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RegKey);
            var s = k?.GetValue(RegValue) as string;
            return s switch
            {
                "light" => Appearance.Light,
                "dark" => Appearance.Dark,
                "system" => Appearance.System,
                _ => Appearance.Dark,
            };
        }
        catch { return Appearance.Dark; }
    }

    private static void Persist(Appearance a)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RegKey);
            k?.SetValue(RegValue, a.ToString().ToLowerInvariant());
        }
        catch { /* ignore */ }
    }
}
