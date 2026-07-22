# Onboarding Rebuild + Tray-Resident App Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild Kivi's onboarding flow with real Google identity capture (client-side only, no backend), a preferences step, and an interactive hotkey walkthrough; then wire a system tray icon so Kivi keeps running (orb + hotkeys active) after the main app window is closed.

**Architecture:** Onboarding continues to be a Frame-navigated flow inside `OnboardingWindow` (`Kivi.App/Views/Onboarding/`), gated by `AppConfig.OnboardingCompleted` in `App.xaml.cs`'s existing `RunStartupGateAsync`. The tray icon is a new `TaskbarIcon` (from the already-referenced-but-unused `H.NotifyIcon.WinUI` package) composed alongside the existing lifetime-anchor `OverlayWindow`, since that's the window that already keeps the WinUI app alive independent of `MainAppWindow`. `MainAppWindow`'s close is intercepted to hide instead of destroy, mirroring the existing show/refocus pattern already used by `OverlayWindow.OnMainAppRequested`.

**Tech Stack:** C#/.NET, WinUI3, `H.NotifyIcon.WinUI` (tray icon), `CommunityToolkit.Mvvm` (ObservableObject), a local HTTP loopback listener (`System.Net.HttpListener`) for OAuth redirect capture, system default browser launch (`Process.Start` with `UseShellExecute = true`).

## Global Constraints

- Hotkey stays **Right Ctrl** (hold-to-talk) + existing rewrite hotkey — the mockups' "fn" hotkey is an iOS convention, not adopted here (per spec's standing note).
- Google auth is identity capture only — no backend, no account creation, no token verification against a server. Launches the **system default browser** (not embedded WebView2), per spec Part 2.
- The interactive walkthrough must use the real orb/hotkey/STT pipeline, not a scripted animation — advances on real user action, with a Skip escape hatch.
- "What do you primarily use typing for" is stored for display only — never wired into the polish prompt (per spec Part 2 decision).
- Tray: closing `MainAppWindow` hides it; only the tray's "Quit Kivi" command does a real exit (per spec Part 3).
- Never log OAuth tokens, transcripts, or API keys (existing project-wide rule).

---

### Task 1: Add a local profile section to `AppConfig`

Google identity capture and the "primary use case" preference both need somewhere to live. Add plain fields — no new abstraction needed, matching how `OrbAccentColor`/`ScreenContextEnabled` etc. are already flat fields on `AppConfig`.

**Files:**
- Modify: `Kivi.Core/Config/AppConfig.cs`
- Test: `Kivi.Core.Tests/AppConfigTests.cs`

**Interfaces:**
- Produces: `AppConfig.ProfileName` (`string?`), `AppConfig.ProfileEmail` (`string?`), `AppConfig.ProfileAvatarUrl` (`string?`), `AppConfig.PrimaryUseCase` (`string?`, one of `"Emails"`, `"Messaging"`, `"Notes"`, `"Code"`, `"Social"`, `"Other"`, or `null` if unset).

- [ ] **Step 1: Write the failing test**

Add to `Kivi.Core.Tests/AppConfigTests.cs`:

```csharp
    [Fact]
    public void Default_HasNullProfileAndUseCaseFields()
    {
        var c = AppConfig.Default();
        Assert.Null(c.ProfileName);
        Assert.Null(c.ProfileEmail);
        Assert.Null(c.ProfileAvatarUrl);
        Assert.Null(c.PrimaryUseCase);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Kivi.Core.Tests --filter "FullyQualifiedName~AppConfigTests.Default_HasNullProfileAndUseCaseFields"`
Expected: FAIL — compile error, properties don't exist.

- [ ] **Step 3: Add the fields**

In `Kivi.Core/Config/AppConfig.cs`, add after the `RewriteHotkeyVirtualKeyCode` line (currently line 22):

```csharp
    public string? ProfileName { get; set; }
    public string? ProfileEmail { get; set; }
    public string? ProfileAvatarUrl { get; set; }
    public string? PrimaryUseCase { get; set; }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Kivi.Core.Tests --filter "FullyQualifiedName~AppConfigTests"`
Expected: PASS — all tests in the file.

- [ ] **Step 5: Commit**

```bash
git add Kivi.Core/Config/AppConfig.cs Kivi.Core.Tests/AppConfigTests.cs
git commit -m "feat(core): add profile identity and primary-use-case fields to AppConfig"
```

---

### Task 2: Loopback OAuth helper for Google sign-in

A small, testable class that owns the loopback listener + browser launch + Google token exchange, kept separate from the `LoginPage` XAML code-behind so it can be unit tested without WinUI. This lives in `Kivi.App` (not `Kivi.Core`) since it depends on `System.Diagnostics.Process` / `System.Net.HttpListener`, which are fine in the app project but keep `Kivi.Core` portable.

**Files:**
- Create: `Kivi.App/Services/GoogleSignIn.cs`
- Test: `Kivi.App.Tests` does not exist yet as a project — this task adds no automated test project for `Kivi.App` (none currently exists; `Kivi.Core.Tests` only covers `Kivi.Core`). Instead, correctness is verified via manual testing in Task 4's walkthrough step, and this class is kept small and side-effect-isolated (pure request/URL building extracted into testable static methods) so the risky parts are at least reviewable.

**Interfaces:**
- Produces:
  - `static string GoogleSignIn.BuildAuthUrl(string clientId, string redirectUri, string state)` — pure function, builds the Google OAuth consent URL.
  - `sealed record GoogleProfile(string Name, string Email, string? AvatarUrl)`
  - `static Task<GoogleProfile?> GoogleSignIn.SignInAsync(string clientId, CancellationToken ct)` — starts a loopback `HttpListener` on an available port, launches the system browser to the consent URL, waits for the redirect, extracts the id_token, decodes the JWT payload (base64url, no signature verification needed since this is identity display only, not an auth boundary), and returns a `GoogleProfile`. Returns `null` on user cancellation/timeout (5 minutes).

- [ ] **Step 1: Write a small unit test for the pure URL-building function**

Since `Kivi.App` has no test project yet, and adding one is out of scope for this plan (it's infrastructure, not a spec requirement), verify `BuildAuthUrl` by hand instead: write the implementation, then in Step 3 manually confirm the URL shape against Google's documented OAuth 2.0 for TV and limited-input device / installed-app loopback flow (`https://developers.google.com/identity/protocols/oauth2/native-app`). This is a deliberate deviation from strict TDD because there is no existing harness to add the test to without introducing a whole new test project as an undocumented side effect of this plan — flag this to the user if a `Kivi.App.Tests` project should be added as a fast-follow.

- [ ] **Step 2: Implement `GoogleSignIn`**

Create `Kivi.App/Services/GoogleSignIn.cs`:

```csharp
// Kivi.App/Services/GoogleSignIn.cs
using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;

namespace Kivi.App.Services;

/// <summary>
/// Client-side-only Google identity capture for onboarding personalization (name/email/
/// avatar). Not an auth boundary: the id_token is decoded for display fields only, never
/// verified against a server, and never used to grant access to anything. No backend,
/// no account creation, no token persistence beyond the profile fields on AppConfig.
/// </summary>
public static class GoogleSignIn
{
    public sealed record GoogleProfile(string Name, string Email, string? AvatarUrl);

    public static string BuildAuthUrl(string clientId, string redirectUri, string state)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["client_id"] = clientId;
        query["redirect_uri"] = redirectUri;
        query["response_type"] = "id_token";
        query["scope"] = "openid email profile";
        query["nonce"] = state;
        query["state"] = state;
        return $"https://accounts.google.com/o/oauth2/v2/auth?{query}";
    }

    public static async Task<GoogleProfile?> SignInAsync(string clientId, CancellationToken ct)
    {
        using var listener = new HttpListener();
        // Port 0 is not valid for HttpListener prefixes; pick a fixed high port used only
        // during the sign-in window. If it's in use, the listener throws and sign-in fails
        // gracefully (caller shows "couldn't start sign-in, try again").
        const int port = 51738;
        var redirectUri = $"http://127.0.0.1:{port}/callback";
        listener.Prefixes.Add($"{redirectUri}/");
        listener.Start();

        var state = Guid.NewGuid().ToString("N");
        var authUrl = BuildAuthUrl(clientId, redirectUri, state);

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(authUrl) { UseShellExecute = true });
        }
        catch
        {
            return null; // couldn't launch a browser
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(5));

        HttpListenerContext context;
        try
        {
            var getContextTask = listener.GetContextAsync();
            using var reg = timeoutCts.Token.Register(listener.Stop);
            context = await getContextTask;
        }
        catch (Exception) when (timeoutCts.IsCancellationRequested)
        {
            return null; // timed out or cancelled
        }
        catch
        {
            return null;
        }

        // Google's id_token flow returns the token in the URL *fragment*, which browsers
        // never send to the server. So the redirect page is a tiny script that reads
        // location.hash and re-submits it as a query string to this same endpoint.
        var request = context.Request;
        string responseHtml;
        GoogleProfile? profile = null;

        if (request.QueryString["id_token"] is { } idToken && request.QueryString["state"] == state)
        {
            profile = DecodeProfile(idToken);
            responseHtml = "<html><body>Signed in — you can close this tab and return to Kivi.</body></html>";
        }
        else
        {
            // First hit: browser landed with the token in the fragment. Serve a redirect
            // script that resubmits it as a query string.
            responseHtml = $$"""
                <html><body><script>
                    var params = new URLSearchParams(location.hash.substring(1));
                    location.href = "{{redirectUri}}?" + params.toString() + "&state={{state}}";
                </script></body></html>
                """;
        }

        var buffer = Encoding.UTF8.GetBytes(responseHtml);
        context.Response.ContentType = "text/html";
        context.Response.ContentLength64 = buffer.Length;
        await context.Response.OutputStream.WriteAsync(buffer, ct);
        context.Response.OutputStream.Close();

        if (profile is not null) return profile;

        // Second hit expected next (the resubmitted query-string request) — wait once more.
        try
        {
            var context2 = await listener.GetContextAsync();
            if (context2.Request.QueryString["id_token"] is { } idToken2 && context2.Request.QueryString["state"] == state)
                profile = DecodeProfile(idToken2);

            var buffer2 = Encoding.UTF8.GetBytes("<html><body>Signed in — you can close this tab and return to Kivi.</body></html>");
            context2.Response.ContentType = "text/html";
            context2.Response.ContentLength64 = buffer2.Length;
            await context2.Response.OutputStream.WriteAsync(buffer2, ct);
            context2.Response.OutputStream.Close();
        }
        catch
        {
            return null;
        }

        return profile;
    }

    private static GoogleProfile? DecodeProfile(string idToken)
    {
        try
        {
            var parts = idToken.Split('.');
            if (parts.Length < 2) return null;
            var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;
            var name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
            var email = root.TryGetProperty("email", out var e) ? e.GetString() : null;
            var picture = root.TryGetProperty("picture", out var p) ? p.GetString() : null;
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(email)) return null;
            return new GoogleProfile(name, email, picture);
        }
        catch
        {
            return null;
        }
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }
}
```

- [ ] **Step 3: Manually verify `BuildAuthUrl`'s shape**

Run a quick scratch check (e.g. in `dotnet fsi`/a throwaway `Program.cs`, or by reading the output of a debugger watch) confirming `GoogleSignIn.BuildAuthUrl("test-client-id", "http://127.0.0.1:51738/callback", "abc123")` produces a URL starting with `https://accounts.google.com/o/oauth2/v2/auth?` and containing `response_type=id_token`, `scope=openid+email+profile`, and the redirect_uri URL-encoded correctly. This is a manual sanity check, not an automated test — flag in the plan's self-review that `Kivi.App` has no test project to formalize this in.

- [ ] **Step 4: Build the project**

Run: `dotnet build Kivi.App`
Expected: builds clean. (`System.Web` for `HttpUtility` — confirm it resolves under `net8.0-windows`; if it does not, replace `HttpUtility.ParseQueryString` with manual `Uri.EscapeDataString` concatenation and note the substitution here.)

- [ ] **Step 5: Commit**

```bash
git add Kivi.App/Services/GoogleSignIn.cs
git commit -m "feat(app): add loopback-browser Google identity capture (no backend)"
```

---

### Task 3: Rebuild `LoginPage` to use real Google sign-in

**Files:**
- Modify: `Kivi.App/Views/Onboarding/LoginPage.xaml.cs`
- Modify: `Kivi.App/Views/Onboarding/LoginPage.xaml` (add a loading state + error text; exact XAML left to match existing style conventions in the file, which this plan doesn't reproduce since it's a markup-only tweak — read the current file before editing to match its button/style structure)

**Interfaces:**
- Consumes: `GoogleSignIn.SignInAsync(string clientId, CancellationToken ct)` (Task 2), `AppConfig` (via DI, same pattern as `ConfigViewModel`'s constructor injection — `LoginPage` will need access to the shared `AppConfig` singleton via `Kivi.App.App.Services.GetRequiredService<AppConfig>()`, matching `ConfigPage.xaml.cs:46`'s existing `GetRequiredService` pattern).
- Produces: on successful sign-in, sets `AppConfig.ProfileName`/`ProfileEmail`/`ProfileAvatarUrl` directly (not persisted yet — persistence happens at the same point it already does today, in `ConfigViewModel.Persist()` at the end of onboarding, since `AppConfig` is a shared singleton instance).

- [ ] **Step 1: Read the current `LoginPage.xaml` to match its structure**

Read `Kivi.App/Views/Onboarding/LoginPage.xaml` in full before editing — this task changes only the code-behind's button handlers plus minimal XAML additions (a loading spinner/disabled state on the Google button), not a redesign of the page layout.

- [ ] **Step 2: Replace `LoginPage.xaml.cs`**

```csharp
// Kivi.App/Views/Onboarding/LoginPage.xaml.cs
using Kivi.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Kivi.App.Views.Onboarding;

/// <summary>
/// "Continue with Google" launches the system browser for client-side-only identity
/// capture (name/email/avatar for personalization; no backend, no account creation --
/// see GoogleSignIn). "Use work email instead" skips straight to Permissions without
/// capturing a profile. Windows build intentionally omits the "Continue with Apple"
/// option present in the macOS mockup.
/// </summary>
public sealed partial class LoginPage : Page
{
    // TODO(config): replace with Kivi's real registered OAuth client ID before shipping.
    private const string GoogleClientId = "REPLACE_WITH_REAL_GOOGLE_OAUTH_CLIENT_ID";

    private OnboardingWindow? _host;

    public LoginPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _host = e.Parameter as OnboardingWindow;
    }

    private async void OnGoogle(object sender, RoutedEventArgs e)
    {
        GoogleButton.IsEnabled = false;
        StatusText.Text = "Waiting for sign-in in your browser…";
        StatusText.Visibility = Visibility.Visible;

        var profile = await GoogleSignIn.SignInAsync(GoogleClientId, default);

        if (profile is not null)
        {
            var config = Kivi.App.App.Services.GetRequiredService<Kivi.Core.Config.AppConfig>();
            config.ProfileName = profile.Name;
            config.ProfileEmail = profile.Email;
            config.ProfileAvatarUrl = profile.AvatarUrl;
            _host?.NavigateTo(typeof(PreferencesPage));
            return;
        }

        GoogleButton.IsEnabled = true;
        StatusText.Text = "Sign-in didn't complete. Try again, or use your work email instead.";
    }

    private void OnEmail(object sender, RoutedEventArgs e) => _host?.NavigateTo(typeof(PreferencesPage));
}
```

- [ ] **Step 3: Add the loading-state controls to `LoginPage.xaml`**

Add a `TextBlock x:Name="StatusText"` (initially `Visibility="Collapsed"`) below the existing buttons, and confirm the Google button is named `x:Name="GoogleButton"` (add the name if it isn't already named) — match the existing XAML's brush/font resource references (`KiviTextSecondaryBrush`, `KiviFontFamily`, etc., following the same resource-lookup pattern visible in `ConfigPage.xaml.cs`).

- [ ] **Step 4: Build**

Run: `dotnet build Kivi.App`
Expected: builds clean. `PreferencesPage` doesn't exist yet (Task 4 creates it) — if this task is executed before Task 4, this build will fail on the reference; that's expected and acceptable since Task 4 immediately follows. If executing tasks out of order, stub `PreferencesPage` as an empty `Page` first.

- [ ] **Step 5: Commit**

```bash
git add Kivi.App/Views/Onboarding/LoginPage.xaml Kivi.App/Views/Onboarding/LoginPage.xaml.cs
git commit -m "feat(app): wire LoginPage to real Google sign-in via loopback browser flow"
```

---

### Task 4: New `PreferencesPage` (language + primary use case)

**Files:**
- Create: `Kivi.App/Views/Onboarding/PreferencesPage.xaml`
- Create: `Kivi.App/Views/Onboarding/PreferencesPage.xaml.cs`
- Modify: `Kivi.App/Kivi.App.csproj` (register the new `.xaml` as a `Page` + `.xaml.cs` as `Compile Update`, following the exact pattern already used for every other page at lines 61-114)

**Interfaces:**
- Consumes: `AppConfig` (via `Kivi.App.App.Services.GetRequiredService<AppConfig>()`, same access pattern as `LoginPage`/`ConfigPage`).
- Produces: sets `AppConfig.TranscriptionLanguage` (reusing the existing field — language chips here are the same concept `ConfigPage` already exposes, just surfaced earlier in onboarding) and `AppConfig.PrimaryUseCase` (Task 1's new field). Navigates to `PermissionsPage` on continue.

- [ ] **Step 1: Create `PreferencesPage.xaml`**

Follow `ConfigPage.xaml`'s existing structure/resource-brush conventions (read it first) for a page with:
- A language multi-select chip row (reuse the chip-building pattern from `ConfigPage.xaml.cs:105-143`, but this page only needs single-select since `TranscriptionLanguage` is a single value)
- A "What do you use typing for most?" single-select list of options: Emails, Messaging, Notes, Code/Technical, Social, Other
- A "Continue" button

```xml
<!-- Kivi.App/Views/Onboarding/PreferencesPage.xaml -->
<Page
    x:Class="Kivi.App.Views.Onboarding.PreferencesPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Background="{ThemeResource KiviSurfaceBrush}">
    <StackPanel Padding="48" Spacing="24" MaxWidth="480" HorizontalAlignment="Center" VerticalAlignment="Center">
        <TextBlock Text="A couple of quick preferences"
                   FontFamily="{ThemeResource KiviFontFamily}"
                   FontSize="{ThemeResource KiviFontSizeHeading}"
                   Foreground="{ThemeResource KiviTextPrimaryBrush}" />
        <TextBlock Text="Which languages do you dictate in?"
                   FontFamily="{ThemeResource KiviFontFamily}"
                   Foreground="{ThemeResource KiviTextSecondaryBrush}" />
        <StackPanel x:Name="LanguageChipPanel" Orientation="Horizontal" Spacing="8" />
        <TextBlock Text="What do you use typing for most?"
                   FontFamily="{ThemeResource KiviFontFamily}"
                   Foreground="{ThemeResource KiviTextSecondaryBrush}" />
        <StackPanel x:Name="UseCasePanel" Orientation="Vertical" Spacing="8" />
        <Button x:Name="ContinueButton" Content="Continue" Click="OnContinue" HorizontalAlignment="Right" />
    </StackPanel>
</Page>
```

- [ ] **Step 2: Create `PreferencesPage.xaml.cs`**

```csharp
// Kivi.App/Views/Onboarding/PreferencesPage.xaml.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace Kivi.App.Views.Onboarding;

/// <summary>
/// Second onboarding screen: language preference (reuses AppConfig.TranscriptionLanguage,
/// the same field ConfigPage exposes later in Settings) and "primary use case", stored for
/// display/analytics only -- never wired into the polish prompt.
/// </summary>
public sealed partial class PreferencesPage : Page
{
    private static readonly (string Code, string Label)[] LanguageChoices =
    {
        ("auto", "Auto"),
        ("en", "English"),
        ("hi", "Hindi"),
        ("hi-en", "Hinglish"),
    };

    private static readonly (string Code, string Label)[] UseCaseChoices =
    {
        ("Emails", "Emails"),
        ("Messaging", "Messaging"),
        ("Notes", "Notes"),
        ("Code", "Code / Technical"),
        ("Social", "Social"),
        ("Other", "Other"),
    };

    private OnboardingWindow? _host;
    private Kivi.Core.Config.AppConfig _config = null!;
    private readonly List<Border> _languageChips = new();
    private readonly List<Border> _useCaseChips = new();
    private string _selectedLanguage = "auto";
    private string _selectedUseCase = "Other";

    public PreferencesPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _host = e.Parameter as OnboardingWindow;
        _config = Kivi.App.App.Services.GetRequiredService<Kivi.Core.Config.AppConfig>();
        _selectedLanguage = _config.TranscriptionLanguage ?? "auto";
        _selectedUseCase = _config.PrimaryUseCase ?? "Other";
        BuildLanguageChips();
        BuildUseCaseChips();
    }

    private void BuildLanguageChips()
    {
        foreach (var (code, label) in LanguageChoices)
        {
            var chip = MakeChip(label, code, code == _selectedLanguage, () =>
            {
                _selectedLanguage = code;
                _config.TranscriptionLanguage = code == "auto" ? null : code;
                RefreshChipHighlight(_languageChips, code);
            });
            _languageChips.Add(chip);
            LanguageChipPanel.Children.Add(chip);
        }
    }

    private void BuildUseCaseChips()
    {
        foreach (var (code, label) in UseCaseChoices)
        {
            var chip = MakeChip(label, code, code == _selectedUseCase, () =>
            {
                _selectedUseCase = code;
                _config.PrimaryUseCase = code;
                RefreshChipHighlight(_useCaseChips, code);
            });
            _useCaseChips.Add(chip);
            UseCasePanel.Children.Add(chip);
        }
    }

    private Border MakeChip(string label, string tag, bool selected, Action onSelect)
    {
        var brandInk = (Brush)Application.Current.Resources["KiviBrandInkBrush"];
        var surfaceAlt = (Brush)Application.Current.Resources["KiviSurfaceAltBrush"];
        var textPrimary = (Brush)Application.Current.Resources["KiviTextPrimaryBrush"];
        var surface = (Brush)Application.Current.Resources["KiviSurfaceBrush"];

        var chip = new Border
        {
            Height = 34,
            CornerRadius = new CornerRadius(17),
            Padding = new Thickness(16, 0, 16, 0),
            Background = selected ? brandInk : surfaceAlt,
            Tag = tag,
        };
        chip.Child = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = selected ? surface : textPrimary,
        };
        chip.Tapped += (_, _) => onSelect();
        return chip;
    }

    private void RefreshChipHighlight(List<Border> chips, string selectedTag)
    {
        var brandInk = (Brush)Application.Current.Resources["KiviBrandInkBrush"];
        var surfaceAlt = (Brush)Application.Current.Resources["KiviSurfaceAltBrush"];
        var textPrimary = (Brush)Application.Current.Resources["KiviTextPrimaryBrush"];
        var surface = (Brush)Application.Current.Resources["KiviSurfaceBrush"];

        foreach (var chip in chips)
        {
            bool selected = (string)chip.Tag == selectedTag;
            chip.Background = selected ? brandInk : surfaceAlt;
            if (chip.Child is TextBlock text) text.Foreground = selected ? surface : textPrimary;
        }
    }

    private void OnContinue(object sender, RoutedEventArgs e) => _host?.NavigateTo(typeof(PermissionsPage));
}
```

- [ ] **Step 3: Register the new page in the csproj**

In `Kivi.App/Kivi.App.csproj`, add after the `LoginPage.xaml` entry (after line 66):

```xml
    <Page Include="Views\Onboarding\PreferencesPage.xaml">
      <Generator>MSBuild:Compile</Generator>
    </Page>
```

And after the `LoginPage.xaml.cs` `Compile Update` entry (after line 96):

```xml
    <Compile Update="Views\Onboarding\PreferencesPage.xaml.cs">
      <DependentUpon>PreferencesPage.xaml</DependentUpon>
    </Compile>
```

- [ ] **Step 4: Build**

Run: `dotnet build Kivi.App`
Expected: builds clean.

- [ ] **Step 5: Manual smoke test**

Run the app (`dotnet run --project Kivi.App`), delete `%APPDATA%\Kivi\settings.json` first if it exists (to force first-run onboarding), click "use work email instead" on Login, and confirm PreferencesPage shows, chip selection updates highlighting, and Continue navigates to Permissions.

- [ ] **Step 6: Commit**

```bash
git add Kivi.App/Views/Onboarding/PreferencesPage.xaml Kivi.App/Views/Onboarding/PreferencesPage.xaml.cs Kivi.App/Kivi.App.csproj
git commit -m "feat(app): add onboarding PreferencesPage (language + primary use case)"
```

---

### Task 5: Interactive walkthrough page

Real hands-on steps using the actual dictation pipeline: hold Right Ctrl + speak into a practice field, then double-tap Right Ctrl for hands-free. Inserted between `ConfigPage` (Kivi preferences: color/position) and the end of onboarding — per spec Part 2, order is Login → Preferences → Permissions → Walkthrough → Kivi preferences (Config). This task also means `ConfigPage.OnDone` (currently the terminal step) becomes non-terminal, and the new `WalkthroughPage` sits before it... but the spec lists Config last. Re-reading spec Part 2's ordering: Login → Preferences → Permissions → Walkthrough → Config. So `PermissionsPage.OnContinue` should navigate to `WalkthroughPage`, and `WalkthroughPage`'s continue navigates to `ConfigPage` (already the terminal step, unchanged).

**Files:**
- Create: `Kivi.App/Views/Onboarding/WalkthroughPage.xaml`
- Create: `Kivi.App/Views/Onboarding/WalkthroughPage.xaml.cs`
- Modify: `Kivi.App/Views/Onboarding/PermissionsPage.xaml.cs:52-56` (`OnContinue` navigates to `WalkthroughPage` instead of `ConfigPage`, unless `PermissionsOnly`)
- Modify: `Kivi.App/Kivi.App.csproj` (register new page, same pattern as Task 4 Step 3)

**Interfaces:**
- Consumes: `IDictationOrchestrator` (`Kivi.Core/Orchestration/IDictationOrchestrator.cs`, via `Kivi.App.App.Services.GetRequiredService<IDictationOrchestrator>()`) to observe real `StateChanged` events during the practice steps; `IHotkeyService` is already running globally via `App.xaml.cs`'s startup gate, so the walkthrough doesn't need to re-wire hotkey capture — it just listens to orchestrator state and displays what happens.
- Produces: navigates to `ConfigPage` on completion (or Skip).

- [ ] **Step 1: Read `IDictationOrchestrator` and `RecordingState` to know what to observe**

Read `Kivi.Core/Orchestration/IDictationOrchestrator.cs` and `Kivi.Core/Orchestration/RecordingState.cs` in full before implementing — confirm the exact `StateChanged` event signature and the enum's states (e.g. `Idle`, `Recording`, `Processing`, ...) so the walkthrough's step-advancement logic matches real state names rather than guessed ones.

- [ ] **Step 2: Create `WalkthroughPage.xaml`**

A two-step page: Step 1 shows "Hold Right Ctrl and say something" with a practice `TextBox` and a live status chip; Step 2 shows "Double-tap Right Ctrl for hands-free" with a status chip. Both steps show a "Skip" link. Follow `ConfigPage.xaml`'s existing brush/font resource conventions (read it first, since this plan does not reproduce a full XAML layout — only the page's functional shape is specified here, per the file-structure guidance to follow established patterns in an existing codebase).

- [ ] **Step 3: Create `WalkthroughPage.xaml.cs`**

```csharp
// Kivi.App/Views/Onboarding/WalkthroughPage.xaml.cs
using Kivi.Core.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Kivi.App.Views.Onboarding;

/// <summary>
/// Interactive walkthrough: the user actually holds Right Ctrl and dictates into a
/// practice field (real orchestrator round-trip via the configured STT/polish engines),
/// then double-taps Right Ctrl to see hands-free mode engage. Confirms the real pipeline
/// works end-to-end before the user reaches the main app. "Skip" is always available.
/// </summary>
public sealed partial class WalkthroughPage : Page
{
    private OnboardingWindow? _host;
    private IDictationOrchestrator _orchestrator = null!;
    private DispatcherQueue _dispatcher = null!;
    private bool _step1Completed;

    public WalkthroughPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _host = e.Parameter as OnboardingWindow;
        _orchestrator = Kivi.App.App.Services.GetRequiredService<IDictationOrchestrator>();
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _orchestrator.StateChanged += OnOrchestratorStateChanged;
        ShowStep1();
    }

    private void OnOrchestratorStateChanged(RecordingState state)
    {
        _dispatcher.TryEnqueue(() =>
        {
            if (!_step1Completed && state == RecordingState.Idle && PracticeField.Text.Length > 0)
            {
                _step1Completed = true;
                ShowStep2();
            }
        });
    }

    private void ShowStep1()
    {
        Step1Panel.Visibility = Visibility.Visible;
        Step2Panel.Visibility = Visibility.Collapsed;
        StatusChip.Text = "Hold Right Ctrl and say something";
    }

    private void ShowStep2()
    {
        Step1Panel.Visibility = Visibility.Collapsed;
        Step2Panel.Visibility = Visibility.Visible;
        StatusChip.Text = "Now double-tap Right Ctrl for hands-free mode";
    }

    private void OnSkip(object sender, RoutedEventArgs e) => Finish();

    private void OnContinue(object sender, RoutedEventArgs e) => Finish();

    private void Finish()
    {
        _orchestrator.StateChanged -= OnOrchestratorStateChanged;
        _host?.NavigateTo(typeof(ConfigPage));
    }
}
```

Note: `PracticeField` here relies on Kivi's paste service already targeting whatever control has focus/is the active caret target system-wide (per the existing `IPasteService`/`SendInputPasteService` design, dictation pastes into the OS focus, not a specific WinUI element) — so the practice field just needs focus when the user starts dictating; no special wiring is needed to "catch" the paste beyond it being a normal focused `TextBox`.

- [ ] **Step 4: Update `PermissionsPage.OnContinue`**

In `Kivi.App/Views/Onboarding/PermissionsPage.xaml.cs`, replace lines 52-56:

```csharp
    private void OnContinue(object sender, RoutedEventArgs e)
    {
        if (_host?.PermissionsOnly == true) _host.RaiseCompleted();
        else _host?.NavigateTo(typeof(WalkthroughPage));
    }
```

- [ ] **Step 5: Register the new page in the csproj**

Same pattern as Task 4 Step 3 — add `Page Include="Views\Onboarding\WalkthroughPage.xaml"` and `Compile Update="Views\Onboarding\WalkthroughPage.xaml.cs"` entries to `Kivi.App/Kivi.App.csproj`.

- [ ] **Step 6: Build**

Run: `dotnet build Kivi.App`
Expected: builds clean.

- [ ] **Step 7: Manual smoke test**

Run the app fresh (delete `%APPDATA%\Kivi\settings.json`), walk through Login → Preferences → Permissions → Walkthrough. Confirm holding Right Ctrl and speaking a phrase into the practice field actually transcribes and pastes text (validates the whole STT/polish pipeline from Plan A), and that the page advances to Step 2 afterward. Confirm Skip works from both steps.

- [ ] **Step 8: Commit**

```bash
git add Kivi.App/Views/Onboarding/WalkthroughPage.xaml Kivi.App/Views/Onboarding/WalkthroughPage.xaml.cs Kivi.App/Views/Onboarding/PermissionsPage.xaml.cs Kivi.App/Kivi.App.csproj
git commit -m "feat(app): add interactive hotkey walkthrough between Permissions and Config"
```

---

### Task 6: Tray icon — wire up `H.NotifyIcon.WinUI`

The package is already referenced in `Kivi.App.csproj:30` but unused. Compose a `TaskbarIcon` in `OverlayWindow` (the existing lifetime-anchor window), since it's already the object that owns cross-cutting orb/main-app-window concerns.

**Files:**
- Modify: `Kivi.App/Views/OverlayWindow.xaml` (add a `TaskbarIcon` resource)
- Modify: `Kivi.App/Views/OverlayWindow.xaml.cs`

**Interfaces:**
- Consumes: `IHotkeyService` (for "Pause dictation" — needs a way to disable/re-enable hotkey listening; check `Kivi.Core/Abstractions/IHotkeyService.cs` for whether a pause/resume method already exists, and if not, this task adds one — see Step 1), `IDictationOrchestrator` (already available via constructor/DI in the same window).
- Produces: `OverlayWindow` gains tray icon lifecycle; exposes no new public members (self-contained).

- [ ] **Step 1: Check `IHotkeyService` for pause/resume support**

Read `Kivi.Core/Abstractions/IHotkeyService.cs` and `Kivi.Platform/Hotkey/LowLevelKeyboardHookService.cs` in full. If there is no existing method to temporarily disable hotkey handling (distinct from `SetHotkey`/`SetRewriteHotkey`, which change *which* key is bound, not whether the service is active), add one:

```csharp
// In IHotkeyService, add:
void SetEnabled(bool enabled);
```

Implement in `LowLevelKeyboardHookService`: guard the existing hook callback with an `_enabled` field (default `true`) that, when `false`, causes the hook to pass the keystroke through unhandled instead of acting on it. Read the existing hook callback method fully before adding this guard, to place it at the correct point (before any hold/tap-detection logic runs, so a disabled state truly no-ops rather than partially processing).

- [ ] **Step 2: Add `TaskbarIcon` markup to `OverlayWindow.xaml`**

Read `Kivi.App/Views/OverlayWindow.xaml` first to see its current (likely near-empty, since it's an invisible anchor) content. Add:

```xml
<tb:TaskbarIcon x:Name="TrayIcon"
                 xmlns:tb="using:H.NotifyIcon"
                 ToolTipText="Kivi"
                 IconSource="ms-appx:///Assets/Icons/kivi-mask.png">
    <tb:TaskbarIcon.ContextFlyout>
        <MenuFlyout>
            <MenuFlyoutItem Text="Open Kivi" Click="OnTrayOpenKivi" />
            <MenuFlyoutItem x:Name="TrayPauseItem" Text="Pause dictation" Click="OnTrayPauseToggle" />
            <MenuFlyoutItem Text="Settings" Click="OnTraySettings" />
            <MenuFlyoutSeparator />
            <MenuFlyoutItem Text="Quit Kivi" Click="OnTrayQuit" />
        </MenuFlyout>
    </tb:TaskbarIcon.ContextFlyout>
</tb:TaskbarIcon>
```

Adjust the `xmlns:tb` namespace declaration to sit on the root element if the file already has a root `Window`/`Page` tag with other namespace declarations — follow the file's existing namespace-declaration convention rather than duplicating `xmlns:tb` on the inner element if it's cleaner at the root.

- [ ] **Step 3: Wire tray event handlers in `OverlayWindow.xaml.cs`**

Add fields and handlers, and call the setup from the constructor (after the existing `_orb` wiring, before the final `Activate()` call):

```csharp
    private bool _dictationPaused;

    // ... inside the constructor, after _orb.MainAppRequested += OnMainAppRequested; ...
    TrayIcon.LeftClickCommand = null; // H.NotifyIcon default double/left-click behavior varies by version; explicit handler used instead below
    TrayIcon.TrayLeftMouseUp += (_, _) => OnTrayOpenKivi(this, new RoutedEventArgs());

    // ... new handler methods ...
    private void OnTrayOpenKivi(object sender, RoutedEventArgs e) => OnMainAppRequested();

    private void OnTrayPauseToggle(object sender, RoutedEventArgs e)
    {
        _dictationPaused = !_dictationPaused;
        var hotkey = Kivi.App.App.Services.GetRequiredService<Kivi.Core.Abstractions.IHotkeyService>();
        hotkey.SetEnabled(!_dictationPaused);
        TrayPauseItem.Text = _dictationPaused ? "Resume dictation" : "Pause dictation";
    }

    private void OnTraySettings(object sender, RoutedEventArgs e) => OnSettingsRequested();

    private void OnTrayQuit(object sender, RoutedEventArgs e)
    {
        TrayIcon.Dispose();
        Application.Current.Exit();
    }
```

Add `using Microsoft.Extensions.DependencyInjection;` and `using Microsoft.UI.Xaml;` (for `RoutedEventArgs`) to the top of the file if not already present.

- [ ] **Step 4: Dispose the tray icon on window close**

In the existing `Closed += (_, _) => { ... }` handler (currently lines 48-53), add `TrayIcon.Dispose();` alongside the existing `_orb.Dispose()` call.

- [ ] **Step 5: Build**

Run: `dotnet build Kivi.App`
Expected: builds clean. `H.NotifyIcon.WinUI`'s exact API surface (property/event names like `TrayLeftMouseUp`, `IconSource`) should be double-checked against the installed package version (2.3.2) — if a member name doesn't compile, check the package's actual public API via `dotnet-trace` or by inspecting the installed package's XML docs/IntelliSense, and adjust the handler wiring to match, keeping the same tray menu items and behavior.

- [ ] **Step 6: Manual smoke test**

Run the app, confirm a Kivi tray icon appears in the system tray, right-click shows the four menu items, left-click opens/focuses `MainAppWindow`, "Pause dictation" actually stops Right-Ctrl from triggering dictation (test by holding it and confirming no orb wake), toggling back to "Resume dictation" restores it.

- [ ] **Step 7: Commit**

```bash
git add Kivi.App/Views/OverlayWindow.xaml Kivi.App/Views/OverlayWindow.xaml.cs Kivi.Core/Abstractions/IHotkeyService.cs Kivi.Platform/Hotkey/LowLevelKeyboardHookService.cs
git commit -m "feat(app): wire up system tray icon (open/pause/settings/quit)"
```

---

### Task 7: `MainAppWindow` close-to-hide behavior

**Files:**
- Modify: `Kivi.App/Views/MainApp/MainAppWindow.xaml.cs`
- Modify: `Kivi.App/Views/OverlayWindow.xaml.cs:79-91` (`OnMainAppRequested`)

**Interfaces:**
- Produces: `MainAppWindow.RequestClose()` — public method that performs a real close (used only by the tray's Quit path, which disposes everything and exits the whole process anyway, so `MainAppWindow` doesn't strictly need its own distinct "real quit" path — closing it is moot once `Application.Current.Exit()` is called). Instead, the actual behavior needed is simpler: intercept the window's `Closed`/`Closing` and call `AppWindow.Hide()`-equivalent instead of destroying it.

- [ ] **Step 1: Check whether WinUI3's `Window` exposes a cancelable `Closing` event**

WinUI3's base `Window` class historically did not expose a `Closing` (cancelable) event directly — only `AppWindow.Closing` (via `Microsoft.UI.Windowing.AppWindow`) supports cancellation in most SDK versions. Confirm this against the installed `Microsoft.WindowsAppSDK` version (1.8.260710003, per the csproj) before implementing — read the SDK's `AppWindow.Closing` docs/IntelliSense signature. This task assumes `AppWindow.Closing` is available (it has been since WinAppSDK 1.0), consistent with `OverlayWindow.xaml.cs`'s existing use of `AppWindow`/`OverlappedPresenter` (lines 31-40 of that file) as the established pattern for low-level window control in this codebase.

- [ ] **Step 2: Implement hide-on-close in `MainAppWindow`**

Replace `Kivi.App/Views/MainApp/MainAppWindow.xaml.cs`:

```csharp
// Kivi.App/Views/MainApp/MainAppWindow.xaml.cs
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace Kivi.App.Views.MainApp;

/// <summary>
/// Hosts the sidebar + content Frame. Opened from the orb's hover "expand" icon or the tray
/// icon's "Open Kivi"/"Settings" commands, or re-focused if already open. Closing the window
/// (the titlebar X) hides it instead of destroying it -- Kivi keeps running via the tray icon
/// and orb -- so the window can be reopened without losing its Frame/nav state. Only an
/// explicit Kivi-wide quit (tray "Quit Kivi") actually destroys it, as part of process exit.
/// </summary>
public sealed partial class MainAppWindow : Window
{
    private readonly AppWindow _appWindow;

    public MainAppWindow()
    {
        InitializeComponent();
        Title = "Kivi";
        NavRecord.IsActive = true;
        ContentFrame.Navigate(typeof(RecordPage));

        nint hwnd = WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
        _appWindow.Closing += OnAppWindowClosing;
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        args.Cancel = true;
        _appWindow.Hide();
    }

    /// <summary>Reshows the window if it was hidden via the titlebar close button.</summary>
    public new void Activate()
    {
        _appWindow.Show();
        base.Activate();
    }

    private void OnNavRecord(object sender, RoutedEventArgs e)
    {
        NavRecord.IsActive = true;
        NavHistory.IsActive = false;
        ContentFrame.Navigate(typeof(RecordPage));
    }

    private void OnNavHistory(object sender, RoutedEventArgs e)
    {
        NavRecord.IsActive = false;
        NavHistory.IsActive = true;
        ContentFrame.Navigate(typeof(HistoryPage));
    }
}
```

- [ ] **Step 3: Confirm `OverlayWindow.OnMainAppRequested` still works correctly**

Read `Kivi.App/Views/OverlayWindow.xaml.cs:79-91` again: since `MainAppWindow._mainAppWindow` is only cleared on the window's `Closed` event (line 88: `win.Closed += (_, _) => _mainAppWindow = null;`), and the new `AppWindow.Closing` handler now cancels the close (so `Closed` never fires from the titlebar X), `_mainAppWindow` correctly stays non-null after a hide — meaning `OnMainAppRequested`'s existing `if (_mainAppWindow is not null) { _mainAppWindow.Activate(); return; }` branch will correctly re-show it via the new `Activate()` override in Step 2. No change needed to `OverlayWindow.xaml.cs` for this — confirm this reasoning holds by inspecting the two files side by side rather than assuming.

- [ ] **Step 4: Build**

Run: `dotnet build Kivi.App`
Expected: builds clean.

- [ ] **Step 5: Manual smoke test**

Run the app, open the main app window (orb hover → expand icon, or tray → Open Kivi), click the window's titlebar X. Confirm: the window disappears but the app is still running (orb still visible/responsive, tray icon still present). Click tray "Open Kivi" or the orb's expand icon again — confirm the window reappears with its previous nav state (still on whichever page — Record or History — it was on before closing). Then use tray "Quit Kivi" and confirm the whole app actually exits (orb disappears, tray icon disappears, process ends).

- [ ] **Step 6: Commit**

```bash
git add Kivi.App/Views/MainApp/MainAppWindow.xaml.cs
git commit -m "feat(app): MainAppWindow close (X) hides instead of destroying the window"
```

---

## Self-Review Notes

- **Spec coverage:** Part 2 (Login/Preferences/Permissions/Walkthrough/Config ordering, client-side Google auth via system browser, walkthrough is real/interactive not scripted, primary-use-case stored for display only) is covered by Tasks 1-5. Part 3 (tray icon with Open/Pause/Settings/Quit, close-to-hide) is covered by Tasks 6-7.
- **Ordering correction caught during planning:** the spec's Part 2 lists steps as Login → Preferences → Permissions → Walkthrough → Config, but the existing codebase's `PermissionsPage.OnContinue` originally went straight to `ConfigPage`. Task 5 Step 4 explicitly re-points that navigation through the new `WalkthroughPage` first, and Task 4's `PreferencesPage` is inserted via `LoginPage.OnGoogle`/`OnEmail` (Task 3), preserving the spec's intended order end-to-end.
- **Known plan risk, flagged rather than papered over:** Task 2's `GoogleSignIn` has no automated test coverage since `Kivi.App` has no test project — this is called out explicitly in Task 2 Step 1 rather than silently skipped, and Task 6's exact `H.NotifyIcon.WinUI` API surface (property/event names) is asserted from typical versions of that package but flagged for verification against the actually-installed 2.3.2 version in Step 5, since a deferred-tool schema mismatch would otherwise cause a silent build failure with no fallback described.
- **Type consistency check:** `AppConfig.PrimaryUseCase`/`ProfileName`/`ProfileEmail`/`ProfileAvatarUrl` (Task 1) are referenced with matching names in Task 3 (`LoginPage`) and Task 4 (`PreferencesPage`). `GoogleSignIn.SignInAsync`/`GoogleProfile` (Task 2) match their usage in Task 3. `IHotkeyService.SetEnabled` (Task 6 Step 1) is a new interface member — flagged as an addition, not assumed to pre-exist.
