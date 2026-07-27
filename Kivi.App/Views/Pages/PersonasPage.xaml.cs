// WPF/WinForms type disambiguation (project enables both UseWPF and UseWindowsForms).
using UserControl = System.Windows.Controls.UserControl;
using Button = System.Windows.Controls.Button;
using Control = System.Windows.Controls.Control;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Size = System.Windows.Size;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using FontFamily = System.Windows.Media.FontFamily;
using Pen = System.Windows.Media.Pen;
using Application = System.Windows.Application;
using Orientation = System.Windows.Controls.Orientation;
using ComboBox = System.Windows.Controls.ComboBox;
using UniformGrid = System.Windows.Controls.Primitives.UniformGrid;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Kivi.App.Themes;
using Kivi.App.ViewModels;

namespace Kivi.App.Views.Pages;

/// <summary>
/// Personas (the live Styles page) — SHELL + OVERVIEW for M5. Content column 980, hpad 26,
/// cloth-red italic accents. The detail pane (700px) + centered sheets (create voice /
/// marketplace / preset library) and the full personalization REST wiring are DEFERRED to P6.
/// </summary>
public partial class PersonasPage : UserControl
{
    // Personas-only accents (personas.css --pz-cloth): light rgb(166,64,46) / dark rgb(224,138,118).
    private static readonly Color ClothLight = Color.FromRgb(166, 64, 46);
    private static readonly Color ClothDark = Color.FromRgb(224, 138, 118);

    public PersonasPage()
    {
        InitializeComponent();
        var cloth = new SolidColorBrush(ThemeManager.Instance.IsDark ? ClothDark : ClothLight);
        cloth.Freeze();
        ClothWord.Foreground = cloth;

        // your apps: seed usage order (highest first)
        int i = 0;
        foreach (var app in AppsRanked())
        {
            AppList.Children.Add(BuildAppRow(app, i > 0));
            i++;
        }

        foreach (var v in PageData.Personas)
            VoiceCards.Children.Add(BuildVoiceCard(v, cloth));
    }

    private static PageData.PersonaApp[] AppsRanked()
    {
        // flatten all persona apps in seed order (seedAppUsage decrements useCount).
        var list = new System.Collections.Generic.List<PageData.PersonaApp>();
        foreach (var p in PageData.Personas)
            foreach (var a in p.Apps)
                if (!list.Exists(x => x.Name == a.Name)) list.Add(a);
        return list.GetRange(0, Math.Min(5, list.Count)).ToArray();
    }

    private UIElement BuildAppRow(PageData.PersonaApp app, bool hasRule)
    {
        var outer = new StackPanel();
        if (hasRule) outer.Children.Add(new Border { Height = 1, Background = MakeHairline(0.55) });
        var grid = new Grid { MinHeight = 52 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(AppMark(app, 27));
        var name = new TextBlock { Text = app.Name, FontFamily = (FontFamily)FindResource("FontBody"), FontSize = 16, Foreground = (Brush)FindResource("InkPrimary"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
        Grid.SetColumn(name, 1);
        var chev = new TextBlock { Text = "›", FontSize = 15, Foreground = (Brush)FindResource("InkTertiary"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(chev, 2);
        grid.Children.Add(name);
        grid.Children.Add(chev);
        outer.Children.Add(grid);
        return outer;
    }

    private Border AppMark(PageData.PersonaApp app, double size)
    {
        var color = (Color)ColorConverter.ConvertFromString(app.Color);
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 4),
            Background = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
            Child = new TextBlock
            {
                Text = app.Name.Substring(0, 1), Foreground = Brushes.White, FontSize = size * 0.42,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    private UIElement BuildVoiceCard(PageData.PersonaVoice v, Brush cloth)
    {
        var card = new Border { Background = (Brush)FindResource("Surface1"), BorderBrush = (Brush)FindResource("Hairline"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(22, 17, 22, 17), Margin = new Thickness(0, 0, 0, 12) };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new TextBlock { Text = v.Name, FontFamily = (FontFamily)FindResource("FontSerif"), FontWeight = FontWeights.Medium, FontSize = 30, LineHeight = 33, Foreground = (Brush)FindResource("InkPrimary") });
        var statement = new TextBlock { FontFamily = (FontFamily)FindResource("FontBody"), FontSize = 14, Foreground = (Brush)FindResource("InkSecondary"), Margin = new Thickness(0, 7, 0, 0), TextWrapping = TextWrapping.Wrap };
        statement.Inlines.Add(new Run(v.Prefix));
        statement.Inlines.Add(new Run(v.Accent) { Foreground = cloth, FontStyle = FontStyles.Italic });
        if (!string.IsNullOrEmpty(v.Suffix)) statement.Inlines.Add(new Run(v.Suffix));
        left.Children.Add(statement);
        Grid.SetColumn(left, 0);

        var apps = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        int shown = Math.Min(4, v.Apps.Length);
        for (int i = 0; i < shown; i++)
        {
            var m = AppMark(v.Apps[i], 27);
            m.Margin = new Thickness(0, 0, 13, 0);
            apps.Children.Add(m);
        }
        if (v.Apps.Length > 4)
            apps.Children.Add(new TextBlock { Text = $"+{v.Apps.Length - 4}", FontSize = 12, Foreground = (Brush)FindResource("InkTertiary"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        apps.Children.Add(new TextBlock { Text = "→", FontSize = 16, Foreground = (Brush)FindResource("InkTertiary"), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(apps, 1);

        grid.Children.Add(left);
        grid.Children.Add(apps);
        card.Child = grid;
        return card;
    }

    private Brush MakeHairline(double op)
    {
        var c = (Color)FindResource("HairlineColor");
        var b = new SolidColorBrush(c) { Opacity = op };
        b.Freeze();
        return b;
    }
}
