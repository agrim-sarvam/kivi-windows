# Main App Window Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Flesh out `MainAppWindow`'s six workspace-adjacent sidebar sections (Record, History, Personas, Presets, Memory, Analytics) plus Settings, per the design mockups. Record, History, Settings, and Analytics are real and end-to-end functional; Personas, Presets, and Memory get complete, navigable UI backed by in-memory mock data only.

**Architecture:** `Kivi.Core` gains a new persisted transcript store (`ITranscriptStore`/`JsonTranscriptStore`, mirroring the existing `IAppConfigStore`/`JsonAppConfigStore` pattern) that `DictationOrchestrator` writes to on every completed dictation, and that `HistoryPage`/`AnalyticsPage` both read from. `Kivi.App` gains new `Page`s wired into `MainAppWindow`'s existing `ContentFrame`/`SidebarNavItem` pattern (already used for Record/History), turning the three currently-`IsStub="True"` workspace items (personas/presets/memory) into real navigable pages backed by static in-memory view-models. Settings becomes a new page (distinct from the onboarding `ConfigPage`, which stays as the first-run flow) that surfaces the same underlying `AppConfig`/`ConfigViewModel` plus the new hotkey/language/privacy controls from the mockup.

**Tech Stack:** C#/.NET, WinUI3, `CommunityToolkit.Mvvm`, `System.Text.Json` (transcript persistence), existing `Kivi.Core.Tests`/xUnit for the new store.

## Global Constraints

- Record, History, Settings, Analytics: fully real, no mock data, no "coming soon" placeholders.
- Personas, Presets, Memory: complete UI matching the mockups, backed by in-memory mock data seeded at launch; add/edit/delete mutate the in-memory list only — nothing persists across restarts, nothing affects real dictation/polish behavior.
- All six sidebar items (plus the pre-existing "leaderboard" stub, out of scope/untouched) are real, clickable, navigable — none hidden.
- "What do you primarily use typing for" (`AppConfig.PrimaryUseCase`, added in the onboarding plan) is never wired into the polish prompt — Analytics/Settings may display it, but no page in this plan changes prompt-building logic.
- Never log transcript content in application logs (existing project-wide rule — the new transcript store persists transcripts to disk by design, which is different from logging them; keep these separate).
- Follow existing design-token usage (`KiviSurfaceBrush`, `KiviTextPrimaryBrush`, etc. from `Kivi.App/Themes/Tokens.xaml`) rather than hardcoding colors, matching every existing page in `Kivi.App/Views/MainApp/`.

---

### Task 1: `ITranscriptStore` in `Kivi.Core`

The foundation both History and Analytics depend on. Mirrors `IAppConfigStore`/`JsonAppConfigStore`'s exact shape and file-safety conventions (corrupt file → empty list, never throw).

**Files:**
- Create: `Kivi.Core/History/TranscriptEntry.cs`
- Create: `Kivi.Core/History/ITranscriptStore.cs`
- Create: `Kivi.Core/History/JsonTranscriptStore.cs`
- Test: `Kivi.Core.Tests/JsonTranscriptStoreTests.cs`

**Interfaces:**
- Produces:
  - `sealed record TranscriptEntry(string Id, string Text, DateTimeOffset Timestamp, string AppName, string? LanguageCode, int WordCount, bool WasRewrite)`
  - `interface ITranscriptStore { IReadOnlyList<TranscriptEntry> LoadAll(); void Append(TranscriptEntry entry); void Clear(); }`
  - `sealed class JsonTranscriptStore : ITranscriptStore`, constructor `JsonTranscriptStore(string? filePath = null)`, defaulting to `%APPDATA%\Kivi\history.json` (same folder as `settings.json`/`key.dat`).

- [ ] **Step 1: Write the failing tests**

Create `Kivi.Core.Tests/JsonTranscriptStoreTests.cs`:

```csharp
using Kivi.Core.History;
using Xunit;

public class JsonTranscriptStoreTests
{
    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"kivi-history-test-{Guid.NewGuid():N}.json");

    [Fact]
    public void LoadAll_ReturnsEmpty_WhenFileDoesNotExist()
    {
        var store = new JsonTranscriptStore(TempPath());
        Assert.Empty(store.LoadAll());
    }

    [Fact]
    public void Append_ThenLoadAll_RoundTripsEntry()
    {
        var path = TempPath();
        try
        {
            var store = new JsonTranscriptStore(path);
            var entry = new TranscriptEntry("1", "hello world", DateTimeOffset.UtcNow, "Slack", "en-IN", 2, false);
            store.Append(entry);

            var loaded = new JsonTranscriptStore(path).LoadAll();

            Assert.Single(loaded);
            Assert.Equal("hello world", loaded[0].Text);
            Assert.Equal("Slack", loaded[0].AppName);
            Assert.Equal(2, loaded[0].WordCount);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Append_Twice_KeepsBothEntries_NewestLast()
    {
        var path = TempPath();
        try
        {
            var store = new JsonTranscriptStore(path);
            store.Append(new TranscriptEntry("1", "first", DateTimeOffset.UtcNow.AddMinutes(-5), "Slack", "en-IN", 1, false));
            store.Append(new TranscriptEntry("2", "second", DateTimeOffset.UtcNow, "Mail", "en-IN", 1, false));

            var loaded = store.LoadAll();

            Assert.Equal(2, loaded.Count);
            Assert.Equal("first", loaded[0].Text);
            Assert.Equal("second", loaded[1].Text);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        var path = TempPath();
        try
        {
            var store = new JsonTranscriptStore(path);
            store.Append(new TranscriptEntry("1", "hello", DateTimeOffset.UtcNow, "Slack", "en-IN", 1, false));
            store.Clear();

            Assert.Empty(store.LoadAll());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LoadAll_ReturnsEmpty_WhenFileIsCorrupt()
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path, "{ this is not valid json");
            var store = new JsonTranscriptStore(path);
            Assert.Empty(store.LoadAll());
        }
        finally { File.Delete(path); }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Kivi.Core.Tests --filter "FullyQualifiedName~JsonTranscriptStoreTests"`
Expected: FAIL — types don't exist.

- [ ] **Step 3: Implement `TranscriptEntry`**

Create `Kivi.Core/History/TranscriptEntry.cs`:

```csharp
namespace Kivi.Core.History;

public sealed record TranscriptEntry(
    string Id,
    string Text,
    DateTimeOffset Timestamp,
    string AppName,
    string? LanguageCode,
    int WordCount,
    bool WasRewrite);
```

- [ ] **Step 4: Implement `ITranscriptStore`**

Create `Kivi.Core/History/ITranscriptStore.cs`:

```csharp
namespace Kivi.Core.History;

/// <summary>
/// Persists dictation transcripts so History/Analytics survive restarts. Never stores
/// the API key or any secret -- unrelated to ISecretStore/IAppConfigStore, but follows
/// the same "never throw on a corrupt file" resilience contract as JsonAppConfigStore.
/// </summary>
public interface ITranscriptStore
{
    IReadOnlyList<TranscriptEntry> LoadAll();
    void Append(TranscriptEntry entry);
    void Clear();
}
```

- [ ] **Step 5: Implement `JsonTranscriptStore`**

Create `Kivi.Core/History/JsonTranscriptStore.cs`:

```csharp
using System.Text.Json;

namespace Kivi.Core.History;

public sealed class JsonTranscriptStore : ITranscriptStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string _filePath;
    private readonly object _lock = new();

    public JsonTranscriptStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Kivi",
            "history.json");
    }

    public IReadOnlyList<TranscriptEntry> LoadAll()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(_filePath)) return Array.Empty<TranscriptEntry>();
                var json = File.ReadAllText(_filePath);
                var entries = JsonSerializer.Deserialize<List<TranscriptEntry>>(json, SerializerOptions);
                return entries ?? new List<TranscriptEntry>();
            }
            catch
            {
                return Array.Empty<TranscriptEntry>();
            }
        }
    }

    public void Append(TranscriptEntry entry)
    {
        lock (_lock)
        {
            var entries = LoadAll().ToList();
            entries.Add(entry);
            Save(entries);
        }
    }

    public void Clear()
    {
        lock (_lock) { Save(new List<TranscriptEntry>()); }
    }

    private void Save(List<TranscriptEntry> entries)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(entries, SerializerOptions);
        File.WriteAllText(_filePath, json);
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test Kivi.Core.Tests --filter "FullyQualifiedName~JsonTranscriptStoreTests"`
Expected: PASS — 5 tests.

- [ ] **Step 7: Commit**

```bash
git add Kivi.Core/History/TranscriptEntry.cs Kivi.Core/History/ITranscriptStore.cs Kivi.Core/History/JsonTranscriptStore.cs Kivi.Core.Tests/JsonTranscriptStoreTests.cs
git commit -m "feat(core): add ITranscriptStore/JsonTranscriptStore for persisted dictation history"
```

---

### Task 2: Wire `DictationOrchestrator` to append completed dictations to the store

**Files:**
- Modify: `Kivi.Core/Orchestration/DictationOrchestrator.cs`
- Test: `Kivi.Core.Tests/OrchestratorTests.cs`

**Interfaces:**
- Consumes: `ITranscriptStore` (Task 1), `IScreenContextProvider` (existing — for `AppName`; check `Kivi.Core/Abstractions/IScreenContextProvider.cs`'s `CaptureContextAsync` return shape — it currently returns a free-text context string, not a structured app name, so this task must add a lightweight app-name extraction or a new narrower method; read `DictationOrchestrator.cs` in full first to see whether it already captures a foreground app name anywhere before assuming a new capability is needed).
- Produces: `DictationOrchestrator` constructor gains an `ITranscriptStore transcriptStore` parameter (additive — check current constructor signature first and add it in a position that doesn't break existing named-parameter call sites, or use constructor injection order matching existing DI registration in `App.xaml.cs`).

- [ ] **Step 1: Read `DictationOrchestrator.cs` in full**

Read `Kivi.Core/Orchestration/DictationOrchestrator.cs` end to end before editing. Identify: (a) the constructor's current parameter list and DI registration call in `App.xaml.cs:90`, (b) the exact point where a dictation completes successfully with final cleaned text (likely where it transitions to `RecordingState.Done` or back to `Idle` after a successful `CleanupAsync` call), (c) whether any foreground-app-name string is already available at that point via `IScreenContextProvider` or elsewhere, and (d) whether a `RewritePending`/`RewriteReview` completion path also needs a transcript-store append (per spec: Analytics tracks a "dictation vs hey-kivi rewrite" breakdown, so `WasRewrite` must be set correctly from whichever code path produced the entry).

- [ ] **Step 2: Write the failing test**

Read `Kivi.Core.Tests/OrchestratorTests.cs` in full first to match its existing fake/stub conventions (it likely already has fake `ISttEngine`/`IPolishClient`/etc. implementations to construct an orchestrator for testing). Add a test using the same fakes plus a new in-memory fake `ITranscriptStore`:

```csharp
private sealed class FakeTranscriptStore : Kivi.Core.History.ITranscriptStore
{
    public List<Kivi.Core.History.TranscriptEntry> Entries { get; } = new();
    public IReadOnlyList<Kivi.Core.History.TranscriptEntry> LoadAll() => Entries;
    public void Append(Kivi.Core.History.TranscriptEntry entry) => Entries.Add(entry);
    public void Clear() => Entries.Clear();
}

[Fact]
public async Task CompletedDictation_AppendsEntryToTranscriptStore()
{
    var transcriptStore = new FakeTranscriptStore();
    // Construct the orchestrator using this test file's existing fake STT/polish/audio/hotkey
    // setup (whatever helper method or constructor call the file already uses), passing
    // transcriptStore as the new dependency. Drive a full dictation cycle exactly the way
    // an existing "successful dictation" test in this file already does (reuse that test's
    // setup verbatim, adding only the transcriptStore assertion).

    // ... drive the same recording-start -> speech -> cleanup-success sequence used by
    // an existing passing test in this file ...

    Assert.Single(transcriptStore.Entries);
    Assert.False(transcriptStore.Entries[0].WasRewrite);
}
```

Adapt this test's body to match whatever test-driving pattern (e.g. a `Fakes/` helper class per the `Kivi.Core.Tests/Fakes` directory noted in the repo listing) `OrchestratorTests.cs` already uses for its other "full successful dictation cycle" tests — do not invent a new driving mechanism if one already exists in the file.

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test Kivi.Core.Tests --filter "FullyQualifiedName~OrchestratorTests.CompletedDictation_AppendsEntryToTranscriptStore"`
Expected: FAIL — constructor doesn't accept `ITranscriptStore` yet.

- [ ] **Step 4: Add the dependency and the append call**

In `Kivi.Core/Orchestration/DictationOrchestrator.cs`: add an `ITranscriptStore _transcriptStore` field, add it as a constructor parameter, and at the point identified in Step 1 where a dictation completes successfully with final text, call:

```csharp
_transcriptStore.Append(new Kivi.Core.History.TranscriptEntry(
    Guid.NewGuid().ToString("N"),
    finalText,
    DateTimeOffset.UtcNow,
    appName, // from Step 1's investigation -- use "" if no app-name signal exists yet, do not fabricate one
    _config.TranscriptionLanguage,
    finalText.Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length,
    wasRewrite));
```

Use the orchestrator's already-existing field names for the config/final-text/wasRewrite variables — match whatever the surrounding code already calls them (do not rename existing local variables to fit this snippet; adapt the snippet to the file's actual variable names instead).

- [ ] **Step 5: Update the DI registration**

In `Kivi.App/App.xaml.cs`, register the store and pass it through:

```csharp
services.AddSingleton<Kivi.Core.History.ITranscriptStore>(_ =>
    new Kivi.Core.History.JsonTranscriptStore());
```

Confirm `services.AddSingleton<IDictationOrchestrator, DictationOrchestrator>();` (line 90) still resolves correctly via constructor injection now that `DictationOrchestrator` takes one more constructor parameter — DI containers resolve constructor parameters automatically by type, so no other change should be needed here, but verify by building.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test Kivi.Core.Tests --filter "FullyQualifiedName~OrchestratorTests"`
Expected: PASS — all tests in the file, including the new one.

- [ ] **Step 7: Build the App project**

Run: `dotnet build Kivi.App`
Expected: builds clean.

- [ ] **Step 8: Commit**

```bash
git add Kivi.Core/Orchestration/DictationOrchestrator.cs Kivi.Core.Tests/OrchestratorTests.cs Kivi.App/App.xaml.cs
git commit -m "feat(core): append completed dictations to the transcript store"
```

---

### Task 3: Wire `RecordPage` to the real dictation pipeline

**Files:**
- Modify: `Kivi.App/Views/MainApp/RecordPage.xaml.cs`
- Modify: `Kivi.App/Views/MainApp/RecordPage.xaml` (minor — bind `DictationBox` to a live transcript property; the existing static hint-text markup mostly stays, only the `TextBox`'s bound `Text` behavior changes)

**Interfaces:**
- Consumes: `IDictationOrchestrator` (existing, via `Kivi.App.App.Services.GetRequiredService<IDictationOrchestrator>()`, same access pattern as `WalkthroughPage` in the onboarding plan), `OverlayViewModel`-equivalent live state (reuse `OverlayViewModel` itself — it's a generic `ObservableObject` wrapper over the orchestrator, not overlay-specific despite its name, so `RecordPage` can construct its own instance the same way `App.xaml.cs`'s `ShowOrb()` does).
- Produces: no new public interface — `RecordPage` becomes a live view of orchestrator state, matching the mockup's live-transcript-box behavior.

- [ ] **Step 1: Update `RecordPage.xaml.cs`**

```csharp
// Kivi.App/Views/MainApp/RecordPage.xaml.cs
using Kivi.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;

namespace Kivi.App.Views.MainApp;

/// <summary>
/// Live view of the real dictation pipeline: DictationBox mirrors the orchestrator's
/// partial transcript while listening, and shows the final cleaned text once done.
/// The hotkey (Right Ctrl) works globally regardless of which window has focus, so this
/// page's only job is to render state -- it doesn't need to own any hotkey logic itself.
/// </summary>
public sealed partial class RecordPage : Page
{
    private readonly OverlayViewModel _vm;

    public RecordPage()
    {
        InitializeComponent();
        var orchestrator = Kivi.App.App.Services.GetRequiredService<Kivi.Core.Orchestration.IDictationOrchestrator>();
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        _vm = new OverlayViewModel(orchestrator, dispatcher);
        _vm.PropertyChanged += OnVmPropertyChanged;
        RenderState();
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => RenderState();

    private void RenderState()
    {
        if (_vm.IsListening && !string.IsNullOrEmpty(_vm.PartialTranscript))
        {
            DictationBox.Text = _vm.PartialTranscript;
        }
    }
}
```

- [ ] **Step 2: Confirm `OverlayViewModel` doesn't need changes**

Check `Kivi.App/ViewModels/OverlayViewModel.cs` (already read in full during planning) — it already exposes `PartialTranscript`, `IsListening`, and implements `ObservableObject` (so `PropertyChanged` is available for free via `CommunityToolkit.Mvvm`). No changes needed to this file for this task.

- [ ] **Step 3: Build**

Run: `dotnet build Kivi.App`
Expected: builds clean.

- [ ] **Step 4: Manual smoke test**

Run the app, open `MainAppWindow` to the Record page, hold Right Ctrl and speak. Confirm `DictationBox` updates live with the partial transcript while listening. Confirm a completed dictation also shows up as a new row in the History page (Task 4) after navigating there.

- [ ] **Step 5: Commit**

```bash
git add Kivi.App/Views/MainApp/RecordPage.xaml.cs Kivi.App/Views/MainApp/RecordPage.xaml
git commit -m "feat(app): wire RecordPage to live orchestrator state"
```

---

### Task 4: Wire `HistoryPage` to real persisted transcripts

**Files:**
- Modify: `Kivi.App/Views/MainApp/HistoryPage.xaml.cs`

**Interfaces:**
- Consumes: `ITranscriptStore` (Task 1, via `Kivi.App.App.Services.GetRequiredService<ITranscriptStore>()`).
- Produces: no new public interface — replaces the `SampleRows` hardcoded array with real data from the store, keeping every existing rendering/selection method (`BuildRows`, `Select`) intact since they're pure UI-construction logic unrelated to the data source.

- [ ] **Step 1: Replace the hardcoded `SampleRows` with real store-backed data**

In `Kivi.App/Views/MainApp/HistoryPage.xaml.cs`, replace the class:

```csharp
// Kivi.App/Views/MainApp/HistoryPage.xaml.cs
using Kivi.Core.History;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Kivi.App.Views.MainApp;

public sealed partial class HistoryPage : Page
{
    private readonly IReadOnlyList<TranscriptEntry> _entries;
    private readonly List<Border> _rowBorders = new();

    public HistoryPage()
    {
        InitializeComponent();
        var store = Kivi.App.App.Services.GetRequiredService<ITranscriptStore>();
        // Newest first, matching the mockup's reverse-chronological History list.
        _entries = store.LoadAll().Reverse().ToList();
        BuildRows();
        if (_entries.Count > 0) Select(0);
        else ShowEmptyState();
    }

    private void ShowEmptyState()
    {
        DetailText.Text = "No dictations yet — hold Right Ctrl anywhere to start.";
        DetailWordCount.Text = "";
        DetailApp.Text = "";
        DetailTime.Text = "";
    }

    private void BuildRows()
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            int index = i;

            var border = new Border
            {
                Padding = new Thickness(12, 10, 12, 10),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(3, 0, 0, 0),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

            var appCol = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            appCol.Children.Add(new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Color.FromArgb(255, 0xF0, 0x65, 0x3B)) });
            appCol.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(entry.AppName) ? "Unknown" : entry.AppName,
                FontFamily = new FontFamily((string)Application.Current.Resources["KiviFontFamily"]),
                FontSize = 12.5,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["KiviTextSecondaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            });
            Grid.SetColumn(appCol, 0);

            var textBlock = new TextBlock
            {
                Text = entry.Text,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
                FontFamily = new FontFamily((string)Application.Current.Resources["KiviFontFamily"]),
                FontSize = 13,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["KiviTextPrimaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(textBlock, 1);

            var timeBlock = new TextBlock
            {
                Text = entry.Timestamp.LocalDateTime.ToString("h:mm tt"),
                HorizontalAlignment = HorizontalAlignment.Right,
                FontFamily = new FontFamily((string)Application.Current.Resources["KiviFontFamily"]),
                FontSize = 12,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["KiviTextTertiaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(timeBlock, 2);

            grid.Children.Add(appCol);
            grid.Children.Add(textBlock);
            grid.Children.Add(timeBlock);
            border.Child = grid;

            var button = new Button
            {
                Content = border,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            button.Click += (_, _) => Select(index);

            _rowBorders.Add(border);
            RowsPanel.Children.Add(button);
        }
    }

    private void Select(int index)
    {
        var accent = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["KiviAccentBrush"];
        var warmTint = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["KiviWarmTintBrush"];
        var transparent = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));

        for (int i = 0; i < _rowBorders.Count; i++)
        {
            bool selected = i == index;
            _rowBorders[i].BorderBrush = selected ? accent : transparent;
            _rowBorders[i].Background = selected ? warmTint : transparent;
        }

        var entry = _entries[index];
        DetailText.Text = entry.Text;
        DetailWordCount.Text = $"{entry.WordCount} words";
        DetailApp.Text = string.IsNullOrEmpty(entry.AppName) ? "Unknown" : entry.AppName;
        DetailTime.Text = entry.Timestamp.LocalDateTime.ToString("MMM d, h:mm tt");
    }
}
```

Add `using System.Linq;` if `.Reverse().ToList()` doesn't resolve without it.

- [ ] **Step 2: Wire the existing search box to actually filter**

Read the current `HistoryPage.xaml`'s search `TextBox` (already present in markup per the file read during planning, at the "Search" section) — give it an `x:Name="SearchBox"` if it doesn't have one, and add a `TextChanged` handler in the code-behind that filters `_rowBorders`'/`RowsPanel.Children` visibility by substring match against each entry's `Text`/`AppName`. Keep this simple: iterate `_entries`/`RowsPanel.Children` in lockstep by index, set `Visibility.Collapsed` on non-matching rows' parent `Button`, `Visibility.Visible` otherwise.

- [ ] **Step 3: Build**

Run: `dotnet build Kivi.App`
Expected: builds clean.

- [ ] **Step 4: Manual smoke test**

Run the app, dictate a couple of phrases via Right Ctrl (from Record or anywhere), open History, confirm the real dictations appear (newest first), confirm clicking a row updates the detail card, confirm typing in search filters the list.

- [ ] **Step 5: Commit**

```bash
git add Kivi.App/Views/MainApp/HistoryPage.xaml.cs Kivi.App/Views/MainApp/HistoryPage.xaml
git commit -m "feat(app): wire HistoryPage to real persisted transcripts, add search filtering"
```

---

### Task 5: `AnalyticsPage` (real, derived from History)

**Files:**
- Create: `Kivi.App/Views/MainApp/AnalyticsPage.xaml`
- Create: `Kivi.App/Views/MainApp/AnalyticsPage.xaml.cs`
- Modify: `Kivi.App/Kivi.App.csproj` (register new page)
- Modify: `Kivi.App/Views/MainApp/MainAppWindow.xaml` (turn the `analytics` `SidebarNavItem` from `IsStub="True"` into a real nav item, matching the `NavRecord`/`NavHistory` pattern)
- Modify: `Kivi.App/Views/MainApp/MainAppWindow.xaml.cs` (add `NavAnalytics`/`OnNavAnalytics`, following the exact existing `OnNavRecord`/`OnNavHistory` pattern)

**Interfaces:**
- Consumes: `ITranscriptStore` (Task 1).
- Produces: no new public interface.

- [ ] **Step 1: Create `AnalyticsPage.xaml`**

Match the mockup's layout (`04 - mockups.png`'s "analytics" screen): stat tiles (total words, words/min, time spoken, dictation count), a words-over-time bar chart, a top-apps breakdown, and a dictation-type breakdown (dictation vs. hey-kivi rewrite). Follow `HistoryPage.xaml`'s existing structural conventions (Border/Grid/StackPanel with `KiviCardStyle`, theme-resource brushes) rather than introducing a new charting library — render the bar chart as a `StackPanel` of `Border` rectangles with heights proportional to per-day word counts (same low-tech approach `MainAppWindow.xaml`'s usage-meter bar already uses at lines 78-81).

```xml
<!-- Kivi.App/Views/MainApp/AnalyticsPage.xaml -->
<Page
    x:Class="Kivi.App.Views.MainApp.AnalyticsPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Background="{ThemeResource KiviSurfaceAltBrush}">
    <ScrollViewer Padding="32,28,32,28">
        <StackPanel Spacing="16" MaxWidth="920">
            <TextBlock Text="analytics" FontFamily="{ThemeResource KiviFontFamily}"
                       FontSize="{ThemeResource KiviFontSizeTitle}"
                       Foreground="{ThemeResource KiviTextPrimaryBrush}" />

            <Grid ColumnSpacing="12">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/><ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="*"/><ColumnDefinition Width="*"/>
                </Grid.ColumnDefinitions>
                <Border Grid.Column="0" Style="{StaticResource KiviCardStyle}" Padding="16">
                    <StackPanel Spacing="4">
                        <TextBlock Text="words" FontSize="11.5" Foreground="{ThemeResource KiviTextTertiaryBrush}"/>
                        <TextBlock x:Name="TotalWordsText" FontSize="24" Foreground="{ThemeResource KiviTextPrimaryBrush}"/>
                    </StackPanel>
                </Border>
                <Border Grid.Column="1" Style="{StaticResource KiviCardStyle}" Padding="16">
                    <StackPanel Spacing="4">
                        <TextBlock Text="words / min" FontSize="11.5" Foreground="{ThemeResource KiviTextTertiaryBrush}"/>
                        <TextBlock x:Name="WordsPerMinText" FontSize="24" Foreground="{ThemeResource KiviTextPrimaryBrush}"/>
                    </StackPanel>
                </Border>
                <Border Grid.Column="2" Style="{StaticResource KiviCardStyle}" Padding="16">
                    <StackPanel Spacing="4">
                        <TextBlock Text="time spoken" FontSize="11.5" Foreground="{ThemeResource KiviTextTertiaryBrush}"/>
                        <TextBlock x:Name="TimeSpokenText" FontSize="24" Foreground="{ThemeResource KiviTextPrimaryBrush}"/>
                    </StackPanel>
                </Border>
                <Border Grid.Column="3" Style="{StaticResource KiviCardStyle}" Padding="16">
                    <StackPanel Spacing="4">
                        <TextBlock Text="dictations" FontSize="11.5" Foreground="{ThemeResource KiviTextTertiaryBrush}"/>
                        <TextBlock x:Name="DictationCountText" FontSize="24" Foreground="{ThemeResource KiviTextPrimaryBrush}"/>
                    </StackPanel>
                </Border>
            </Grid>

            <Border Style="{StaticResource KiviCardStyle}" Padding="20">
                <StackPanel Spacing="12">
                    <TextBlock Text="words over time" FontFamily="{ThemeResource KiviMonoFontFamily}" FontSize="11"
                               Foreground="{ThemeResource KiviTextTertiaryBrush}"/>
                    <StackPanel x:Name="WordsOverTimePanel" Orientation="Horizontal" Spacing="6" Height="120" VerticalAlignment="Bottom"/>
                </StackPanel>
            </Border>

            <Border Style="{StaticResource KiviCardStyle}" Padding="20">
                <StackPanel Spacing="10">
                    <TextBlock Text="top apps" FontFamily="{ThemeResource KiviMonoFontFamily}" FontSize="11"
                               Foreground="{ThemeResource KiviTextTertiaryBrush}"/>
                    <StackPanel x:Name="TopAppsPanel" Spacing="8"/>
                </StackPanel>
            </Border>

            <Border Style="{StaticResource KiviCardStyle}" Padding="20">
                <StackPanel Spacing="10">
                    <TextBlock Text="dictation type" FontFamily="{ThemeResource KiviMonoFontFamily}" FontSize="11"
                               Foreground="{ThemeResource KiviTextTertiaryBrush}"/>
                    <StackPanel x:Name="DictationTypePanel" Orientation="Horizontal" Spacing="16"/>
                </StackPanel>
            </Border>
        </StackPanel>
    </ScrollViewer>
</Page>
```

- [ ] **Step 2: Create `AnalyticsPage.xaml.cs`**

```csharp
// Kivi.App/Views/MainApp/AnalyticsPage.xaml.cs
using Kivi.Core.History;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Kivi.App.Views.MainApp;

public sealed partial class AnalyticsPage : Page
{
    public AnalyticsPage()
    {
        InitializeComponent();
        var store = Kivi.App.App.Services.GetRequiredService<ITranscriptStore>();
        Render(store.LoadAll());
    }

    private void Render(IReadOnlyList<TranscriptEntry> entries)
    {
        int totalWords = entries.Sum(e => e.WordCount);
        TotalWordsText.Text = totalWords.ToString("N0");
        DictationCountText.Text = entries.Count.ToString("N0");

        // Words/min uses a fixed 150 wpm average speaking rate to estimate spoken duration,
        // since TranscriptEntry doesn't record actual audio duration (out of scope to add
        // audio-duration tracking in this pass -- word count is already captured).
        double estimatedMinutes = totalWords / 150.0;
        WordsPerMinText.Text = estimatedMinutes > 0 ? Math.Round(totalWords / estimatedMinutes).ToString("N0") : "0";
        TimeSpokenText.Text = FormatDuration(TimeSpan.FromMinutes(estimatedMinutes));

        RenderWordsOverTime(entries);
        RenderTopApps(entries);
        RenderDictationType(entries);
    }

    private static string FormatDuration(TimeSpan span)
        => span.TotalHours >= 1 ? $"{(int)span.TotalHours}h {span.Minutes}m" : $"{span.Minutes}m";

    private void RenderWordsOverTime(IReadOnlyList<TranscriptEntry> entries)
    {
        var accent = (Brush)Application.Current.Resources["KiviAccentBrush"];
        var byDay = entries
            .GroupBy(e => e.Timestamp.LocalDateTime.Date)
            .OrderBy(g => g.Key)
            .TakeLast(14)
            .Select(g => (Day: g.Key, Words: g.Sum(e => e.WordCount)))
            .ToList();

        int max = byDay.Count > 0 ? byDay.Max(d => d.Words) : 1;
        if (max == 0) max = 1;

        foreach (var (day, words) in byDay)
        {
            var bar = new Border
            {
                Width = 18,
                Height = Math.Max(4, 120.0 * words / max),
                CornerRadius = new CornerRadius(3),
                Background = accent,
                VerticalAlignment = VerticalAlignment.Bottom,
            };
            ToolTipService.SetToolTip(bar, $"{day:MMM d}: {words} words");
            WordsOverTimePanel.Children.Add(bar);
        }
    }

    private void RenderTopApps(IReadOnlyList<TranscriptEntry> entries)
    {
        var textSecondary = (Brush)Application.Current.Resources["KiviTextSecondaryBrush"];
        var accent = (Brush)Application.Current.Resources["KiviAccentBrush"];
        var stroke = (Brush)Application.Current.Resources["KiviStrokeBrush"];

        var byApp = entries
            .Where(e => !string.IsNullOrEmpty(e.AppName))
            .GroupBy(e => e.AppName)
            .Select(g => (App: g.Key, Count: g.Count()))
            .OrderByDescending(x => x.Count)
            .Take(5)
            .ToList();

        int max = byApp.Count > 0 ? byApp.Max(a => a.Count) : 1;

        foreach (var (app, count) in byApp)
        {
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var label = new TextBlock { Text = app, Foreground = textSecondary, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(label, 0);

            var track = new Border { Height = 8, CornerRadius = new CornerRadius(4), Background = stroke, VerticalAlignment = VerticalAlignment.Center };
            track.Child = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = Math.Max(4, 200.0 * count / max),
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = accent,
            };
            Grid.SetColumn(track, 1);

            row.Children.Add(label);
            row.Children.Add(track);
            TopAppsPanel.Children.Add(row);
        }
    }

    private void RenderDictationType(IReadOnlyList<TranscriptEntry> entries)
    {
        var textPrimary = (Brush)Application.Current.Resources["KiviTextPrimaryBrush"];
        int dictations = entries.Count(e => !e.WasRewrite);
        int rewrites = entries.Count(e => e.WasRewrite);

        DictationTypePanel.Children.Add(new TextBlock { Text = $"{dictations} dictations", Foreground = textPrimary, FontSize = 13 });
        DictationTypePanel.Children.Add(new TextBlock { Text = $"{rewrites} hey kivi", Foreground = textPrimary, FontSize = 13 });
    }
}
```

Add `using System.Linq;` at the top if `.Sum`/`.GroupBy`/`.TakeLast` don't resolve without it.

- [ ] **Step 3: Turn on the Analytics sidebar nav item**

In `Kivi.App/Views/MainApp/MainAppWindow.xaml`, replace line 62:

```xml
                    <local:SidebarNavItem x:Name="NavAnalytics" Glyph="&#xE9D9;" Label="analytics" Click="OnNavAnalytics"/>
```

In `Kivi.App/Views/MainApp/MainAppWindow.xaml.cs`, add (following the exact `OnNavRecord`/`OnNavHistory` pattern, and updating those two methods to also deactivate `NavAnalytics`):

```csharp
    private void OnNavAnalytics(object sender, RoutedEventArgs e)
    {
        NavRecord.IsActive = false;
        NavHistory.IsActive = false;
        NavAnalytics.IsActive = true;
        ContentFrame.Navigate(typeof(AnalyticsPage));
    }
```

And add `NavAnalytics.IsActive = false;` to the existing `OnNavRecord`/`OnNavHistory` methods so only one nav item is ever highlighted at a time.

- [ ] **Step 4: Register the new page in the csproj**

Same pattern as prior tasks — add `Page Include="Views\MainApp\AnalyticsPage.xaml"` and `Compile Update="Views\MainApp\AnalyticsPage.xaml.cs"` entries.

- [ ] **Step 5: Build**

Run: `dotnet build Kivi.App`
Expected: builds clean.

- [ ] **Step 6: Manual smoke test**

Dictate a few phrases across the session, open Analytics, confirm stat tiles show non-zero real numbers, the words-over-time bars render, top apps lists real app names captured from dictation.

- [ ] **Step 7: Commit**

```bash
git add Kivi.App/Views/MainApp/AnalyticsPage.xaml Kivi.App/Views/MainApp/AnalyticsPage.xaml.cs Kivi.App/Views/MainApp/MainAppWindow.xaml Kivi.App/Views/MainApp/MainAppWindow.xaml.cs Kivi.App/Kivi.App.csproj
git commit -m "feat(app): add real Analytics page derived from persisted transcript history"
```

---

### Task 6: `SettingsPage` (real, expands on `ConfigViewModel`)

Distinct from onboarding's `ConfigPage` (which stays as the first-run flow, unchanged) — this is the always-available Settings destination reachable from the sidebar and the tray's "Settings" command, matching the mockup's fuller layout (hotkeys, languages, behavior, privacy).

**Files:**
- Create: `Kivi.App/Views/MainApp/SettingsPage.xaml`
- Create: `Kivi.App/Views/MainApp/SettingsPage.xaml.cs`
- Modify: `Kivi.App/ViewModels/ConfigViewModel.cs` (add `SoundOnPaste`, `IncognitoDictation`, `PressAndHoldDelayMs` observable properties backing the mockup's additional Behavior/Privacy controls — check `AppConfig` for existing matching fields first; `PressEnterCommandEnabled`/`MetricsEnabled` already exist but sound-on-paste/incognito-dictation do not, so `AppConfig` needs matching new fields too)
- Modify: `Kivi.Core/Config/AppConfig.cs` (add `SoundOnPasteEnabled` (`bool`, default `true`), `IncognitoDictationEnabled` (`bool`, default `false`), `PressAndHoldDelayMs` (`int`, default `100`, per the mockup's "100 ms" shown value))
- Modify: `Kivi.App/Kivi.App.csproj` (register new page)
- Modify: `Kivi.App/Views/MainApp/MainAppWindow.xaml`/`.xaml.cs` (add a Settings sidebar nav item at the bottom, matching the mockup's sidebar footer position — check whether a dedicated "settings" row already exists in the XAML below the account footer; if not, add one following the same `SidebarNavItem` pattern)
- Test: `Kivi.Core.Tests/AppConfigTests.cs`

**Interfaces:**
- Consumes: `ConfigViewModel` (existing, extended), `AppConfig` (extended), `ITranscriptStore` (Task 1, for "Clear all history").
- Produces: no new public interface beyond the `AppConfig`/`ConfigViewModel` additions.

- [ ] **Step 1: Write the failing `AppConfig` test**

Add to `Kivi.Core.Tests/AppConfigTests.cs`:

```csharp
    [Fact]
    public void Default_HasSoundIncognitoAndPressHoldDelayDefaults()
    {
        var c = AppConfig.Default();
        Assert.True(c.SoundOnPasteEnabled);
        Assert.False(c.IncognitoDictationEnabled);
        Assert.Equal(100, c.PressAndHoldDelayMs);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Kivi.Core.Tests --filter "FullyQualifiedName~AppConfigTests.Default_HasSoundIncognitoAndPressHoldDelayDefaults"`
Expected: FAIL.

- [ ] **Step 3: Add the fields to `AppConfig`**

In `Kivi.Core/Config/AppConfig.cs`, add alongside the profile fields added in the onboarding plan (or after `RewriteHotkeyVirtualKeyCode` if this plan is executed independently of that one — check what's already present):

```csharp
    public bool SoundOnPasteEnabled { get; set; } = true;
    public bool IncognitoDictationEnabled { get; set; }
    public int PressAndHoldDelayMs { get; set; } = 100;
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Kivi.Core.Tests --filter "FullyQualifiedName~AppConfigTests"`
Expected: PASS.

- [ ] **Step 5: Extend `ConfigViewModel`**

In `Kivi.App/ViewModels/ConfigViewModel.cs`, add observable properties and their change handlers following the exact existing pattern (e.g. `OnScreenContextEnabledChanged`):

```csharp
    [ObservableProperty] private bool _soundOnPasteEnabled = true;
    [ObservableProperty] private bool _incognitoDictationEnabled;
    [ObservableProperty] private int _pressAndHoldDelayMs = 100;

    partial void OnSoundOnPasteEnabledChanged(bool value) => _config.SoundOnPasteEnabled = value;
    partial void OnIncognitoDictationEnabledChanged(bool value) => _config.IncognitoDictationEnabled = value;
    partial void OnPressAndHoldDelayMsChanged(int value) => _config.PressAndHoldDelayMs = value;
```

And initialize them in the constructor alongside the existing property initializations:

```csharp
        SoundOnPasteEnabled = config.SoundOnPasteEnabled;
        IncognitoDictationEnabled = config.IncognitoDictationEnabled;
        PressAndHoldDelayMs = config.PressAndHoldDelayMs;
```

Note: unlike onboarding's `ConfigPage`, `SettingsPage` (this task) should persist immediately on every change rather than waiting for a terminal "Done" button, since Settings is meant to be always-editable, not a wizard step. Add a persist call to each of the three new change handlers above (and consider whether the *existing* handlers — `OnOrbAccentColorChanged` etc. — should also persist immediately when reached via Settings rather than only via onboarding's `ConfigPage.OnDone`/`Persist()`. This is a real design decision: add `_store.Save(_config);` to the end of every `On*Changed` partial method in `ConfigViewModel` so both onboarding and Settings-page usage persist correctly, since a shared view model used from two different hosting pages should not have close-only-persists semantics for one of its two hosts.)

- [ ] **Step 6: Create `SettingsPage.xaml` and `SettingsPage.xaml.cs`**

Match the mockup's Settings layout: hotkeys card (dictate hold/lock label, hey-kivi label, press-and-hold-delay value), languages card (chip multi-display, reusing `ConfigPage`'s chip-building code pattern), behavior card (launch at login, screen context, incognito dictation, sound on paste toggles), privacy card ("clear all history" button wired to `ITranscriptStore.Clear()`).

```csharp
// Kivi.App/Views/MainApp/SettingsPage.xaml.cs
using Kivi.App.ViewModels;
using Kivi.Core.History;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kivi.App.Views.MainApp;

public sealed partial class SettingsPage : Page
{
    public ConfigViewModel ViewModel { get; }

    public SettingsPage()
    {
        ViewModel = Kivi.App.App.Services.GetRequiredService<ConfigViewModel>();
        InitializeComponent();

        HotkeyBox.SetInitial(ViewModel.HotkeyVk);
        HotkeyBox.HotkeyChanged += vk => ViewModel.HotkeyVk = vk;
        RewriteHotkeyBox.SetInitial(ViewModel.RewriteHotkeyVk);
        RewriteHotkeyBox.HotkeyChanged += vk => ViewModel.RewriteHotkeyVk = vk;

        LaunchAtLoginToggle.IsOn = ViewModel.LaunchAtLogin;
        ScreenContextToggle.IsOn = ViewModel.ScreenContextEnabled;
        IncognitoToggle.IsOn = ViewModel.IncognitoDictationEnabled;
        SoundOnPasteToggle.IsOn = ViewModel.SoundOnPasteEnabled;
    }

    private void OnLaunchAtLoginToggled(object sender, RoutedEventArgs e) => ViewModel.LaunchAtLogin = LaunchAtLoginToggle.IsOn;
    private void OnScreenContextToggled(object sender, RoutedEventArgs e) => ViewModel.ScreenContextEnabled = ScreenContextToggle.IsOn;
    private void OnIncognitoToggled(object sender, RoutedEventArgs e) => ViewModel.IncognitoDictationEnabled = IncognitoToggle.IsOn;
    private void OnSoundOnPasteToggled(object sender, RoutedEventArgs e) => ViewModel.SoundOnPasteEnabled = SoundOnPasteToggle.IsOn;

    private async void OnClearHistory(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Clear all history?",
            Content = "This can't be undone.",
            PrimaryButtonText = "Clear all",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot,
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            Kivi.App.App.Services.GetRequiredService<ITranscriptStore>().Clear();
        }
    }
}
```

Write `SettingsPage.xaml` following `ConfigPage.xaml`'s existing structural conventions (read it first) for the hotkey-capture controls (`HotkeyBox`/`RewriteHotkeyBox` are the existing `HotkeyCaptureBox` custom control already used in `ConfigPage.xaml`) plus new `Toggle`/`Button` elements for the behavior/privacy sections named to match the code-behind above (`IncognitoToggle`, `SoundOnPasteToggle`, plus a "Clear all history" `Button` wired to `Click="OnClearHistory"`).

- [ ] **Step 7: Add the Settings sidebar nav item**

In `Kivi.App/Views/MainApp/MainAppWindow.xaml`, add a settings row near the bottom of the sidebar (below the usage meter, above or alongside the account footer — check the mockup's exact placement in `04 - mockups.png`'s settings screen, which shows "settings" pinned at the very bottom of the nav list, below "analytics"/"leaderboard"). Add it as its own `Grid.Row` or fold it into the existing account-footer row area — since the account footer (`Grid.Row="4"`) is the last row today, add a new row for Settings between the usage meter and the account footer, or below the account footer, matching the mockup: place it as a `SidebarNavItem` directly below the account footer.

```xml
                <local:SidebarNavItem x:Name="NavSettings" Grid.Row="5" Glyph="&#xE713;" Label="settings" Click="OnNavSettings" Margin="0,8,0,0"/>
```

This requires adding a sixth `RowDefinition Height="Auto"` to the sidebar's `Grid.RowDefinitions` (currently 5 rows, indices 0-4) and moving the account footer or settings item accordingly — read the current row structure carefully before editing so existing rows (wordmark, primary nav, workspace stubs, usage meter, account footer) keep their intended positions.

In `Kivi.App/Views/MainApp/MainAppWindow.xaml.cs`, add:

```csharp
    private void OnNavSettings(object sender, RoutedEventArgs e)
    {
        NavRecord.IsActive = false;
        NavHistory.IsActive = false;
        NavAnalytics.IsActive = false;
        ContentFrame.Navigate(typeof(SettingsPage));
    }
```

(Extend this same deactivation list into `OnNavRecord`/`OnNavHistory`/`OnNavAnalytics` too, so all four real nav items correctly clear each other's active state — there is no `NavSettings.IsActive` toggle needed unless `SidebarNavItem`'s `IsActive` dependency property is also set on it in each handler; add `NavSettings.IsActive = false;` to the other three handlers and `= true` here, mirroring the existing pattern exactly.)

- [ ] **Step 8: Register the new page in the csproj**

Same pattern as prior tasks.

- [ ] **Step 9: Build**

Run: `dotnet build Kivi.App`
Expected: builds clean.

- [ ] **Step 10: Manual smoke test**

Open Settings from the sidebar, toggle each behavior switch, confirm values persist after closing/reopening the app (re-launch and check Settings reflects the same toggle states). Confirm "Clear all history" empties the History page after confirming the dialog.

- [ ] **Step 11: Commit**

```bash
git add Kivi.App/Views/MainApp/SettingsPage.xaml Kivi.App/Views/MainApp/SettingsPage.xaml.cs Kivi.App/ViewModels/ConfigViewModel.cs Kivi.Core/Config/AppConfig.cs Kivi.Core.Tests/AppConfigTests.cs Kivi.App/Views/MainApp/MainAppWindow.xaml Kivi.App/Views/MainApp/MainAppWindow.xaml.cs Kivi.App/Kivi.App.csproj
git commit -m "feat(app): add real SettingsPage (hotkeys, languages, behavior, privacy)"
```

---

### Task 7: In-memory mock model for Personas/Presets/Memory

A single shared static seed-data class, kept intentionally simple (no persistence, no DI registration as a singleton service — just process-lifetime static state) since the spec explicitly scopes these three pages to UI-only with mock data.

**Files:**
- Create: `Kivi.App/ViewModels/WorkspaceMockData.cs`

**Interfaces:**
- Produces:
  - `sealed class PersonaModel { string Name; List<string> AssignedApps; List<string> ToneRules; List<string> AttachedPresetNames; }` (mutable, plain class — not `ObservableObject`, since these pages re-render their full list on every mutation rather than using granular bindings, matching `HistoryPage`'s existing rebuild-the-panel style)
  - `sealed class PresetModel { string Name; string Instruction; }`
  - `sealed class MemoryEntryModel { string Original; string Corrected; DateTimeOffset AddedAt; }`
  - `static class WorkspaceMockData { static List<PersonaModel> Personas; static List<PresetModel> Presets; static List<MemoryEntryModel> MemoryEntries; }` — static lists seeded once at class-load time with the exact sample content shown in the mockups ("work messaging", "email", "developer", "casual" personas; a "standup summariser" preset; etc.), so relaunching the app resets to the same seed (in-memory only, per spec).

- [ ] **Step 1: Implement `WorkspaceMockData`**

```csharp
// Kivi.App/ViewModels/WorkspaceMockData.cs
namespace Kivi.App.ViewModels;

public sealed class PersonaModel
{
    public required string Name { get; set; }
    public List<string> AssignedApps { get; set; } = new();
    public List<string> ToneRules { get; set; } = new();
    public List<string> AttachedPresetNames { get; set; } = new();
}

public sealed class PresetModel
{
    public required string Name { get; set; }
    public required string Instruction { get; set; }
}

public sealed class MemoryEntryModel
{
    public required string Original { get; set; }
    public required string Corrected { get; set; }
    public DateTimeOffset AddedAt { get; set; }
}

/// <summary>
/// In-memory-only mock data for Personas/Presets/Memory, per the design spec: these three
/// pages get a complete, demoable UI now, but no real persistence or backend wiring (no
/// per-app auto-detection, no prompt injection, no disk storage). Resets to this seed on
/// every app restart. Kept as static state (not a DI-registered service) since nothing
/// else in the app depends on it and it isn't meant to model real, durable behavior yet.
/// </summary>
public static class WorkspaceMockData
{
    public static List<PersonaModel> Personas { get; } = new()
    {
        new PersonaModel
        {
            Name = "work messaging",
            AssignedApps = new() { "Slack", "Teams", "Discord" },
            ToneRules = new() { "Keep messages under three sentences", "Address seniors by first name, no \"sir\"", "Never use em dashes" },
            AttachedPresetNames = new() { "standup summariser" },
        },
        new PersonaModel { Name = "email", AssignedApps = new() { "Mail", "Outlook" }, ToneRules = new() { "Formal greeting and sign-off" }, AttachedPresetNames = new() },
        new PersonaModel { Name = "developer", AssignedApps = new() { "Cursor", "VS Code" }, ToneRules = new() { "Keep code comments terse" }, AttachedPresetNames = new() },
        new PersonaModel { Name = "casual", AssignedApps = new() { "WhatsApp", "Notes" }, ToneRules = new(), AttachedPresetNames = new() },
    };

    public static List<PresetModel> Presets { get; } = new()
    {
        new PresetModel { Name = "standup summariser", Instruction = "Summarize into: what I did yesterday, what I'm doing today, blockers." },
        new PresetModel { Name = "make it formal", Instruction = "Rewrite in a more formal, professional tone." },
        new PresetModel { Name = "make it concise", Instruction = "Cut to the essential point, remove filler words." },
    };

    public static List<MemoryEntryModel> MemoryEntries { get; } = new()
    {
        new MemoryEntryModel { Original = "sarvam", Corrected = "Sarvam", AddedAt = DateTimeOffset.UtcNow.AddDays(-3) },
        new MemoryEntryModel { Original = "kivi", Corrected = "Kivi", AddedAt = DateTimeOffset.UtcNow.AddDays(-2) },
        new MemoryEntryModel { Original = "priyank", Corrected = "Priyank", AddedAt = DateTimeOffset.UtcNow.AddDays(-1) },
    };
}
```

- [ ] **Step 2: Build**

Run: `dotnet build Kivi.App`
Expected: builds clean (this file has no consumers yet — Tasks 8-10 add them).

- [ ] **Step 3: Commit**

```bash
git add Kivi.App/ViewModels/WorkspaceMockData.cs
git commit -m "feat(app): add in-memory mock data model for Personas/Presets/Memory"
```

---

### Task 8: `PersonasPage` (UI-only, mock data)

**Files:**
- Create: `Kivi.App/Views/MainApp/PersonasPage.xaml`
- Create: `Kivi.App/Views/MainApp/PersonasPage.xaml.cs`
- Modify: `Kivi.App/Kivi.App.csproj`
- Modify: `Kivi.App/Views/MainApp/MainAppWindow.xaml` (turn `personas` `SidebarNavItem` from `IsStub="True"` to real, `Click="OnNavPersonas"`)
- Modify: `Kivi.App/Views/MainApp/MainAppWindow.xaml.cs` (add `OnNavPersonas`, extend all other nav handlers' deactivation lists to include `NavPersonas.IsActive = false;`)

**Interfaces:**
- Consumes: `WorkspaceMockData.Personas` (Task 7).
- Produces: no new public interface.

- [ ] **Step 1: Create `PersonasPage.xaml`**

Match the mockup's two-column layout: left column lists personas as selectable pills (work messaging / email / developer / casual + "new persona"), right side shows the selected persona's assigned apps, tone rules list, and attached presets — per `03 - components.png`'s "personas" screen. Follow `HistoryPage.xaml`'s card/border conventions.

```xml
<!-- Kivi.App/Views/MainApp/PersonasPage.xaml -->
<Page
    x:Class="Kivi.App.Views.MainApp.PersonasPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Background="{ThemeResource KiviSurfaceAltBrush}">
    <Grid Padding="32,28,32,28" ColumnSpacing="20">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="220"/>
            <ColumnDefinition Width="*"/>
        </Grid.ColumnDefinitions>

        <StackPanel Grid.Column="0" Spacing="8">
            <TextBlock Text="personas" FontFamily="{ThemeResource KiviFontFamily}"
                       FontSize="{ThemeResource KiviFontSizeTitle}" Foreground="{ThemeResource KiviTextPrimaryBrush}" Margin="0,0,0,8"/>
            <StackPanel x:Name="PersonaListPanel" Spacing="4"/>
            <Button x:Name="NewPersonaButton" Content="+ new persona" Click="OnNewPersona" HorizontalAlignment="Stretch" Margin="0,8,0,0"/>
        </StackPanel>

        <Border Grid.Column="1" Style="{StaticResource KiviCardStyle}" Padding="24" VerticalAlignment="Top">
            <StackPanel Spacing="16">
                <Grid>
                    <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                    <TextBlock x:Name="DetailNameText" Grid.Column="0" FontSize="18" Foreground="{ThemeResource KiviTextPrimaryBrush}"/>
                    <Button x:Name="DeleteButton" Grid.Column="1" Content="delete persona" Click="OnDeletePersona"/>
                </Grid>

                <TextBlock Text="apps in this persona" FontFamily="{ThemeResource KiviMonoFontFamily}" FontSize="10.5"
                           Foreground="{ThemeResource KiviTextTertiaryBrush}"/>
                <StackPanel x:Name="AssignedAppsPanel" Spacing="4"/>

                <TextBlock Text="rules" FontFamily="{ThemeResource KiviMonoFontFamily}" FontSize="10.5"
                           Foreground="{ThemeResource KiviTextTertiaryBrush}"/>
                <StackPanel x:Name="ToneRulesPanel" Spacing="4"/>

                <TextBlock Text="attached presets" FontFamily="{ThemeResource KiviMonoFontFamily}" FontSize="10.5"
                           Foreground="{ThemeResource KiviTextTertiaryBrush}"/>
                <StackPanel x:Name="AttachedPresetsPanel" Spacing="4"/>
            </StackPanel>
        </Border>
    </Grid>
</Page>
```

- [ ] **Step 2: Create `PersonasPage.xaml.cs`**

```csharp
// Kivi.App/Views/MainApp/PersonasPage.xaml.cs
using Kivi.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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

    public PersonasPage()
    {
        InitializeComponent();
        RenderList();
        if (WorkspaceMockData.Personas.Count > 0) RenderDetail(0);
    }

    private void RenderList()
    {
        PersonaListPanel.Children.Clear();
        for (int i = 0; i < WorkspaceMockData.Personas.Count; i++)
        {
            int index = i;
            var persona = WorkspaceMockData.Personas[i];
            var button = new Button
            {
                Content = persona.Name,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
            };
            button.Click += (_, _) => RenderDetail(index);
            PersonaListPanel.Children.Add(button);
        }
    }

    private void RenderDetail(int index)
    {
        _selectedIndex = index;
        var persona = WorkspaceMockData.Personas[index];
        DetailNameText.Text = persona.Name;

        AssignedAppsPanel.Children.Clear();
        foreach (var app in persona.AssignedApps)
            AssignedAppsPanel.Children.Add(new TextBlock { Text = app, FontSize = 13 });

        ToneRulesPanel.Children.Clear();
        foreach (var rule in persona.ToneRules)
            ToneRulesPanel.Children.Add(new TextBlock { Text = "• " + rule, FontSize = 13 });

        AttachedPresetsPanel.Children.Clear();
        foreach (var preset in persona.AttachedPresetNames)
            AttachedPresetsPanel.Children.Add(new TextBlock { Text = preset, FontSize = 13 });
    }

    private async void OnNewPersona(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "New persona",
            Content = new TextBox { PlaceholderText = "Persona name" },
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && dialog.Content is TextBox tb && !string.IsNullOrWhiteSpace(tb.Text))
        {
            WorkspaceMockData.Personas.Add(new PersonaModel { Name = tb.Text.Trim() });
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
            Title = $"delete \"{persona.Name}\"?",
            Content = "The persona and its rules will be removed. Apps assigned to it fall back to casual. This can't be undone.",
            PrimaryButtonText = "Delete persona",
            CloseButtonText = "cancel",
            XamlRoot = this.XamlRoot,
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            WorkspaceMockData.Personas.RemoveAt(_selectedIndex);
            RenderList();
            if (WorkspaceMockData.Personas.Count > 0) RenderDetail(0);
            else { DetailNameText.Text = ""; AssignedAppsPanel.Children.Clear(); ToneRulesPanel.Children.Clear(); AttachedPresetsPanel.Children.Clear(); }
        }
    }
}
```

- [ ] **Step 3: Turn on the Personas sidebar nav item**

In `Kivi.App/Views/MainApp/MainAppWindow.xaml`, change the `personas` row to:

```xml
                    <local:SidebarNavItem x:Name="NavPersonas" Glyph="&#xE70F;" Label="personas" Click="OnNavPersonas"/>
```

In `Kivi.App/Views/MainApp/MainAppWindow.xaml.cs`, add the handler and extend every other nav handler's deactivation list with `NavPersonas.IsActive = false;` (and this handler sets `NavPersonas.IsActive = true;` while deactivating the rest):

```csharp
    private void OnNavPersonas(object sender, RoutedEventArgs e)
    {
        NavRecord.IsActive = false;
        NavHistory.IsActive = false;
        NavAnalytics.IsActive = false;
        NavSettings.IsActive = false;
        NavPersonas.IsActive = true;
        ContentFrame.Navigate(typeof(PersonasPage));
    }
```

- [ ] **Step 4: Register the page in the csproj**

Same pattern as prior tasks.

- [ ] **Step 5: Build**

Run: `dotnet build Kivi.App`
Expected: builds clean.

- [ ] **Step 6: Manual smoke test**

Open Personas from the sidebar, confirm the four seeded personas list, clicking one shows its apps/rules/presets, "new persona" creates one via the dialog, "delete persona" removes it after confirming. Confirm relaunching the app resets back to the four seeded personas (proving it's genuinely in-memory-only, not accidentally persisted).

- [ ] **Step 7: Commit**

```bash
git add Kivi.App/Views/MainApp/PersonasPage.xaml Kivi.App/Views/MainApp/PersonasPage.xaml.cs Kivi.App/Views/MainApp/MainAppWindow.xaml Kivi.App/Views/MainApp/MainAppWindow.xaml.cs Kivi.App/Kivi.App.csproj
git commit -m "feat(app): add Personas page (UI-only, in-memory mock data)"
```

---

### Task 9: `PresetsPage` (UI-only, mock data)

**Files:**
- Create: `Kivi.App/Views/MainApp/PresetsPage.xaml`
- Create: `Kivi.App/Views/MainApp/PresetsPage.xaml.cs`
- Modify: `Kivi.App/Kivi.App.csproj`
- Modify: `Kivi.App/Views/MainApp/MainAppWindow.xaml`/`.xaml.cs` (same pattern as Task 8 — turn `presets` stub real, add `OnNavPresets`, extend all nav handlers' deactivation lists with `NavPresets.IsActive = false;`)

**Interfaces:**
- Consumes: `WorkspaceMockData.Presets` (Task 7).
- Produces: no new public interface.

- [ ] **Step 1: Create `PresetsPage.xaml`**

A simple list of preset name + instruction cards, with add/edit/delete, following the same structural conventions as `PersonasPage.xaml`/`HistoryPage.xaml`.

```xml
<!-- Kivi.App/Views/MainApp/PresetsPage.xaml -->
<Page
    x:Class="Kivi.App.Views.MainApp.PresetsPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Background="{ThemeResource KiviSurfaceAltBrush}">
    <ScrollViewer Padding="32,28,32,28">
        <StackPanel Spacing="16" MaxWidth="720">
            <Grid>
                <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                <TextBlock Grid.Column="0" Text="presets" FontFamily="{ThemeResource KiviFontFamily}"
                           FontSize="{ThemeResource KiviFontSizeTitle}" Foreground="{ThemeResource KiviTextPrimaryBrush}"/>
                <Button Grid.Column="1" Content="+ new preset" Click="OnNewPreset"/>
            </Grid>
            <StackPanel x:Name="PresetsPanel" Spacing="10"/>
        </StackPanel>
    </ScrollViewer>
</Page>
```

- [ ] **Step 2: Create `PresetsPage.xaml.cs`**

```csharp
// Kivi.App/Views/MainApp/PresetsPage.xaml.cs
using Kivi.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kivi.App.Views.MainApp;

/// <summary>
/// UI-only per the design spec: backed by WorkspaceMockData.Presets (in-memory, resets on
/// restart). See PersonasPage's doc comment for the same real-backend-deferred rationale.
/// </summary>
public sealed partial class PresetsPage : Page
{
    public PresetsPage()
    {
        InitializeComponent();
        Render();
    }

    private void Render()
    {
        PresetsPanel.Children.Clear();
        for (int i = 0; i < WorkspaceMockData.Presets.Count; i++)
        {
            int index = i;
            var preset = WorkspaceMockData.Presets[i];

            var card = new Border { Style = (Style)Application.Current.Resources["KiviCardStyle"], Padding = new Thickness(16) };
            var stack = new StackPanel { Spacing = 6 };
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var nameText = new TextBlock { Text = preset.Name, FontSize = 15, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["KiviTextPrimaryBrush"] };
            Grid.SetColumn(nameText, 0);

            var deleteButton = new Button { Content = "delete" };
            deleteButton.Click += (_, _) => { WorkspaceMockData.Presets.RemoveAt(index); Render(); };
            Grid.SetColumn(deleteButton, 1);

            header.Children.Add(nameText);
            header.Children.Add(deleteButton);

            var instructionText = new TextBlock
            {
                Text = preset.Instruction,
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                FontSize = 13,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["KiviTextSecondaryBrush"],
            };

            stack.Children.Add(header);
            stack.Children.Add(instructionText);
            card.Child = stack;
            PresetsPanel.Children.Add(card);
        }
    }

    private async void OnNewPreset(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox { PlaceholderText = "Preset name" };
        var instructionBox = new TextBox { PlaceholderText = "Instruction", AcceptsReturn = true, Height = 80 };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(nameBox);
        panel.Children.Add(instructionBox);

        var dialog = new ContentDialog
        {
            Title = "New preset",
            Content = panel,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(nameBox.Text))
        {
            WorkspaceMockData.Presets.Add(new PresetModel { Name = nameBox.Text.Trim(), Instruction = instructionBox.Text.Trim() });
            Render();
        }
    }
}
```

- [ ] **Step 3: Turn on the Presets sidebar nav item**

Same pattern as Task 8 Step 3, substituting `presets`/`OnNavPresets`/`NavPresets`/`PresetsPage`, and extending every other nav handler's deactivation list to include `NavPresets.IsActive = false;` (now five real nav items total: Record, History, Analytics, Settings, Personas, Presets — six, update accordingly).

- [ ] **Step 4: Register the page in the csproj**

Same pattern as prior tasks.

- [ ] **Step 5: Build**

Run: `dotnet build Kivi.App`
Expected: builds clean.

- [ ] **Step 6: Manual smoke test**

Open Presets, confirm the three seeded presets show, "new preset" creates one, "delete" removes it.

- [ ] **Step 7: Commit**

```bash
git add Kivi.App/Views/MainApp/PresetsPage.xaml Kivi.App/Views/MainApp/PresetsPage.xaml.cs Kivi.App/Views/MainApp/MainAppWindow.xaml Kivi.App/Views/MainApp/MainAppWindow.xaml.cs Kivi.App/Kivi.App.csproj
git commit -m "feat(app): add Presets page (UI-only, in-memory mock data)"
```

---

### Task 10: `MemoryPage` (UI-only, mock data)

**Files:**
- Create: `Kivi.App/Views/MainApp/MemoryPage.xaml`
- Create: `Kivi.App/Views/MainApp/MemoryPage.xaml.cs`
- Modify: `Kivi.App/Kivi.App.csproj`
- Modify: `Kivi.App/Views/MainApp/MainAppWindow.xaml`/`.xaml.cs` (same pattern as Tasks 8-9 — turn `memory` stub real, add `OnNavMemory`, extend all nav handlers' deactivation lists with `NavMemory.IsActive = false;`)

**Interfaces:**
- Consumes: `WorkspaceMockData.MemoryEntries` (Task 7).
- Produces: no new public interface.

- [ ] **Step 1: Create `MemoryPage.xaml`**

A simple two-column list (original → corrected) with delete, matching the mockup's Settings-page "Memory" toggle description ("names, spellings, phrasing you correct").

```xml
<!-- Kivi.App/Views/MainApp/MemoryPage.xaml -->
<Page
    x:Class="Kivi.App.Views.MainApp.MemoryPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Background="{ThemeResource KiviSurfaceAltBrush}">
    <ScrollViewer Padding="32,28,32,28">
        <StackPanel Spacing="16" MaxWidth="720">
            <TextBlock Text="memory" FontFamily="{ThemeResource KiviFontFamily}"
                       FontSize="{ThemeResource KiviFontSizeTitle}" Foreground="{ThemeResource KiviTextPrimaryBrush}"/>
            <TextBlock Text="names, spellings, and phrasing Kivi has learned from your corrections."
                       FontSize="13" Foreground="{ThemeResource KiviTextSecondaryBrush}"/>
            <StackPanel x:Name="MemoryPanel" Spacing="1"/>
        </StackPanel>
    </ScrollViewer>
</Page>
```

- [ ] **Step 2: Create `MemoryPage.xaml.cs`**

```csharp
// Kivi.App/Views/MainApp/MemoryPage.xaml.cs
using Kivi.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kivi.App.Views.MainApp;

/// <summary>
/// UI-only per the design spec: backed by WorkspaceMockData.MemoryEntries (in-memory,
/// resets on restart). See PersonasPage's doc comment for the same real-backend-deferred
/// rationale -- no real correction-learning pipeline or prompt injection exists yet.
/// </summary>
public sealed partial class MemoryPage : Page
{
    public MemoryPage()
    {
        InitializeComponent();
        Render();
    }

    private void Render()
    {
        MemoryPanel.Children.Clear();
        for (int i = 0; i < WorkspaceMockData.MemoryEntries.Count; i++)
        {
            int index = i;
            var entry = WorkspaceMockData.MemoryEntries[i];

            var row = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 6, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var text = new TextBlock
            {
                Text = $"{entry.Original} → {entry.Corrected}",
                FontSize = 13,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["KiviTextPrimaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(text, 0);

            var dateText = new TextBlock
            {
                Text = entry.AddedAt.LocalDateTime.ToString("MMM d"),
                FontSize = 12,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["KiviTextTertiaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(dateText, 1);

            var deleteButton = new Button { Content = "remove" };
            deleteButton.Click += (_, _) => { WorkspaceMockData.MemoryEntries.RemoveAt(index); Render(); };
            Grid.SetColumn(deleteButton, 2);

            row.Children.Add(text);
            row.Children.Add(dateText);
            row.Children.Add(deleteButton);
            MemoryPanel.Children.Add(row);
        }
    }
}
```

- [ ] **Step 3: Turn on the Memory sidebar nav item**

Same pattern as Tasks 8-9, substituting `memory`/`OnNavMemory`/`NavMemory`/`MemoryPage`, and extending every other nav handler's deactivation list to include `NavMemory.IsActive = false;` (now six real nav items: Record, History, Analytics, Settings, Personas, Presets, Memory — seven; update accordingly).

- [ ] **Step 4: Register the page in the csproj**

Same pattern as prior tasks.

- [ ] **Step 5: Build**

Run: `dotnet build Kivi.App`
Expected: builds clean.

- [ ] **Step 6: Manual smoke test**

Open Memory, confirm the three seeded entries show with dates, "remove" deletes an entry.

- [ ] **Step 7: Commit**

```bash
git add Kivi.App/Views/MainApp/MemoryPage.xaml Kivi.App/Views/MainApp/MemoryPage.xaml.cs Kivi.App/Views/MainApp/MainAppWindow.xaml Kivi.App/Views/MainApp/MainAppWindow.xaml.cs Kivi.App/Kivi.App.csproj
git commit -m "feat(app): add Memory page (UI-only, in-memory mock data)"
```

---

### Task 11: Full solution build and regression check

**Files:** none (verification-only task).

- [ ] **Step 1: Run the full `Kivi.Core.Tests` suite**

Run: `dotnet test`
Expected: all tests pass, including the new `JsonTranscriptStoreTests`, the extended `OrchestratorTests`, and the extended `AppConfigTests`.

- [ ] **Step 2: Build the full solution**

Run: `dotnet build Kivi.sln`
Expected: builds clean, 0 errors.

- [ ] **Step 3: Full manual walkthrough**

Run the app end-to-end: complete onboarding (or reuse an existing profile), open `MainAppWindow`, click through all seven sidebar items (Record, History, Personas, Presets, Memory, Analytics, Settings) confirming each renders without crashing and shows either real data (Record/History/Analytics/Settings) or seeded mock data (Personas/Presets/Memory). Confirm the pre-existing "leaderboard" stub still renders as a dimmed, non-interactive stub (unchanged, out of scope).

- [ ] **Step 4: Commit any final fixups discovered during the walkthrough**

If Step 3 surfaces small issues (e.g. a missed `NavX.IsActive = false` in one handler, a missing csproj registration), fix them and commit:

```bash
git add -A
git commit -m "fix(app): correct sidebar nav active-state handling across all seven pages"
```

(Skip this commit if Step 3 found nothing to fix.)

---

## Self-Review Notes

- **Spec coverage:** Part 4 of the spec is fully covered — Record (Task 3), History (Task 4), Analytics (Task 5), Settings (Task 6) are real end-to-end; Personas (Task 8), Presets (Task 9), Memory (Task 10) are UI-only with in-memory mock data (Task 7 provides the shared seed). Task 1-2 build the persistence foundation Record/History/Analytics all depend on.
- **Placeholder scan:** no TBDs remain. Where a genuine open question existed (e.g. exactly how `DictationOrchestrator` captures an app name today), Task 2 Step 1 explicitly instructs reading the file first rather than guessing, and Step 4's snippet says to use `""` rather than fabricate a signal that may not exist — this is a deliberate "investigate before assuming" instruction, not an unresolved gap in the plan's logic.
- **Type consistency check:** `TranscriptEntry`/`ITranscriptStore` (Task 1) are referenced identically in Tasks 2, 4, 5, 6. `PersonaModel`/`PresetModel`/`MemoryEntryModel`/`WorkspaceMockData` (Task 7) are referenced identically in Tasks 8-10. Every new sidebar nav handler (`OnNavAnalytics`, `OnNavSettings`, `OnNavPersonas`, `OnNavPresets`, `OnNavMemory`) follows the same deactivate-all-others-then-activate-self pattern as the pre-existing `OnNavRecord`/`OnNavHistory`, and each task's instructions explicitly say to extend prior handlers' deactivation lists so all seven nav items stay mutually exclusive as they're added incrementally across tasks.
- **Known scope boundary, not a gap:** the "leaderboard" sidebar stub (visible in `MainAppWindow.xaml` today) is not one of the spec's six sections and is deliberately left untouched/still a stub — this plan does not silently drop it, it's explicitly called out in Task 11 Step 3's verification pass.
