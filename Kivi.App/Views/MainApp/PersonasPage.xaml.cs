// Kivi.App/Views/MainApp/PersonasPage.xaml.cs
using Kivi.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Kivi.App.Views.MainApp;

/// <summary>
/// UI-only per the design spec: backed entirely by WorkspaceMockData.Personas (in-memory,
/// resets on restart). Add/edit/delete mutate the in-memory list so the page feels
/// functional, but nothing persists to disk and nothing affects real dictation behavior --
/// no per-app auto-detection, no prompt wiring. Real backend work is a future spec.
/// </summary>
public sealed partial class PersonasPage : Page
{
    private int _selectedIndex;
    private readonly List<Button> _personaButtons = new();

    public PersonasPage()
    {
        InitializeComponent();
        RenderList();
        if (WorkspaceMockData.Personas.Count > 0) RenderDetail(0);
    }

    private void RenderList()
    {
        PersonaListPanel.Children.Clear();
        _personaButtons.Clear();

        for (int i = 0; i < WorkspaceMockData.Personas.Count; i++)
        {
            int index = i;
            var persona = WorkspaceMockData.Personas[i];
            var button = new Button
            {
                Content = persona.Name,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 9, 12, 9),
                FontFamily = new FontFamily((string)Application.Current.Resources["KiviFontFamily"]),
                FontSize = 13.5,
            };
            button.Click += (_, _) => RenderDetail(index);
            _personaButtons.Add(button);
            PersonaListPanel.Children.Add(button);
        }

        HighlightSelected();
    }

    private void HighlightSelected()
    {
        var warmTint = (Brush)Application.Current.Resources["KiviWarmTintBrush"];
        var accent = (Brush)Application.Current.Resources["KiviAccentBrush"];
        var textSecondary = (Brush)Application.Current.Resources["KiviTextSecondaryBrush"];
        var transparent = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));

        for (int i = 0; i < _personaButtons.Count; i++)
        {
            bool selected = i == _selectedIndex;
            _personaButtons[i].Background = selected ? warmTint : transparent;
            _personaButtons[i].Foreground = selected ? accent : textSecondary;
        }
    }

    private void RenderDetail(int index)
    {
        _selectedIndex = index;
        HighlightSelected();

        var persona = WorkspaceMockData.Personas[index];
        DetailNameText.Text = persona.Name;

        var textSecondary = (Brush)Application.Current.Resources["KiviTextSecondaryBrush"];
        var textPrimary = (Brush)Application.Current.Resources["KiviTextPrimaryBrush"];
        var surfaceAlt = (Brush)Application.Current.Resources["KiviSurfaceAltBrush"];
        var stroke = (Brush)Application.Current.Resources["KiviStrokeBrush"];
        var textTertiary = (Brush)Application.Current.Resources["KiviTextTertiaryBrush"];

        AssignedAppsPanel.Children.Clear();
        if (persona.AssignedApps.Count == 0)
        {
            AssignedAppsPanel.Children.Add(new TextBlock { Text = "No apps assigned yet.", Foreground = textTertiary, FontSize = 13 });
        }
        foreach (var app in persona.AssignedApps)
        {
            AssignedAppsPanel.Children.Add(new Border
            {
                Background = surfaceAlt,
                BorderBrush = stroke,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(12, 5, 12, 5),
                Child = new TextBlock { Text = app, Foreground = textSecondary, FontSize = 12.5 },
            });
        }

        ToneRulesPanel.Children.Clear();
        if (persona.ToneRules.Count == 0)
        {
            ToneRulesPanel.Children.Add(new TextBlock { Text = "No rules yet.", Foreground = textTertiary, FontSize = 13 });
        }
        foreach (var rule in persona.ToneRules)
        {
            ToneRulesPanel.Children.Add(new TextBlock { Text = "•  " + rule, Foreground = textPrimary, FontSize = 13, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap });
        }

        AttachedPresetsPanel.Children.Clear();
        if (persona.AttachedPresetNames.Count == 0)
        {
            AttachedPresetsPanel.Children.Add(new TextBlock { Text = "No presets attached.", Foreground = textTertiary, FontSize = 13 });
        }
        foreach (var preset in persona.AttachedPresetNames)
        {
            AttachedPresetsPanel.Children.Add(new Border
            {
                Background = surfaceAlt,
                BorderBrush = stroke,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(12, 5, 12, 5),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock { Text = preset, Foreground = textSecondary, FontSize = 12.5 },
            });
        }
    }

    private void ClearDetail()
    {
        DetailNameText.Text = "";
        AssignedAppsPanel.Children.Clear();
        ToneRulesPanel.Children.Clear();
        AttachedPresetsPanel.Children.Clear();
    }

    private async void OnNewPersona(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox { PlaceholderText = "Persona name" };
        var dialog = new ContentDialog
        {
            Title = "New persona",
            Content = nameBox,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(nameBox.Text))
        {
            WorkspaceMockData.Personas.Add(new PersonaModel { Name = nameBox.Text.Trim() });
            RenderList();
            RenderDetail(WorkspaceMockData.Personas.Count - 1);
        }
    }

    private async void OnDeletePersona(object sender, RoutedEventArgs e)
    {
        if (WorkspaceMockData.Personas.Count == 0) return;
        var persona = WorkspaceMockData.Personas[_selectedIndex];
        var dialog = new ContentDialog
        {
            Title = $"Delete \"{persona.Name}\"?",
            Content = "The persona and its rules will be removed. Apps assigned to it fall back to casual. This can't be undone.",
            PrimaryButtonText = "Delete persona",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot,
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            WorkspaceMockData.Personas.RemoveAt(_selectedIndex);
            if (_selectedIndex >= WorkspaceMockData.Personas.Count) _selectedIndex = Math.Max(0, WorkspaceMockData.Personas.Count - 1);
            RenderList();
            if (WorkspaceMockData.Personas.Count > 0) RenderDetail(_selectedIndex);
            else ClearDetail();
        }
    }
}
