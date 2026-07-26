# MAP: menubar-onboarding-auth

Windows-only .NET port of `_reference/sarvam-kivi-electron/docs/maps/menubar-onboarding-auth.md`.
Covers the tray (menu-bar), onboarding, and auth. Auth logic is pure HTTP (ports 1:1 via
`HttpClient`); the native cost is the OAuth callback delivery, secret storage (DPAPI), permission
preflight, and the tray. Target: `Kivi.Platform.Tray`, `Kivi.Platform.Auth`, `Kivi.Platform.Secrets`,
`Kivi.App/Views` (onboarding/auth UI).

---

## 0. Architecture at a glance

- **Entry point** `Kivi.App/App.xaml.cs` — the WinUI host + DI composition root. Deep-link handling: register the `kivi` protocol + single-instance argv (loopback callback preferred, see §3.4).
- The app owns three **independent** surfaces, each with its own `FlowRuntime` sharing only the mic: floating orb, **tray**, main window.
- **Resident-agent mode:** the process stays alive with all windows hidden; closing the main window does not terminate it (orb + tray stay resident). On Windows there is no Dock/`.accessory` concept — just keep the process alive and give the orb window `skipTaskbar`/tool-window style. (Reference `setMainWindowActive` policy trick is a macOS-only Dock detail — dropped.)

---

## 1. TRAY (menu-bar)

### 1.1 Why a custom popover (not a stock menu)
The reference dropped down to AppKit because SwiftUI's `MenuBarExtra` swallowed clicks and couldn't
animate its label. **On Windows this dilemma doesn't exist** — you build the tray + a custom
popover window explicitly anyway: a **notification-area icon** (`NotifyIcon` / the Windows App SDK
`AppNotificationManager`-adjacent tray API, or a thin Shell_NotifyIcon interop) + a frameless
always-on-top popover window.

### 1.2 The tray icon — live, state-tinted, "breathing" pill
Rasterized per state. **Exact values (carried over):**
- Shape: rounded squircle, `pillW = round(height * 1.12)`, `radius = rect.height * 0.28`, 0.5px inset.
- Fill: vertical gradient `[top, base, bottom]` at `[0, 0.52, 1]`, angle −90. `top = base.blend(0.26 white)`, `bottom = base.blend(0.18 black)`.
- Sheen: white ellipse @ alpha 0.14, height `rect.height*0.55`, x 10%→90% width.
- Glyph: the pixel **`KiwiData` silhouette mask** filled white→`rgb(0.93,0.94,0.92)`, ~86% of pill height (`vpad = pillH*0.07`).
- **Per-state background**: idle = `rgb(104,106,100)` calm grey; all other states = `KiwiMarkEngine.StateColor` (orb's exact per-state colour — listening orange, processing indigo, editing yellow, speaking/done green).
- **Breathing**: `breathingAlpha = 0.55 + 0.45*(sin(2π·elapsed/period)+1)/2`. Period 1.1 s for processing/editing/acting, else 1.6 s. Steady 1.0 when reduced-motion OR a non-motion state.
- Icon reflects **whichever surface is live** (prefers orb when non-idle).

**Windows:** **pre-render a small set of discrete per-state icon frames** (Win2D → `nativeImage`
equivalent → `tray.SetImage` on an interval). **Avoid high-frequency tray updates** — the notification
area throttles; a handful of discrete frames is fine. Windows tray popovers render regardless of a
fullscreen foreground app; set the popover window always-on-top with a high level.

### 1.3 Popover
- A frameless, always-on-top, `skipTaskbar` popover window positioned near the tray-icon bounds; **hide on deactivate** (click-away dismiss).
- A menu-initiated live take aborts on popover close (`menuCloseStopsTake = phase.IsRecording`); a settled `.done` is kept.
- Onboarding hooks hide/show it.

### 1.4 Dropdown content — `MenuBarContent`
Container: vertical stack, padding s6, **width 320**, **max height 640**, background canon canvas
(opaque). Structure:
1. **header**: open-kivi button (dotted kiwi mark 22 + "kivi" wordmark 18) + trailing state chip.
2. **talkPane**: "dictate"/"stop" mode button (mic glyph, bound-hotkey label, accent fill `rgb(20,42,13)`) + cancel (red ✕) or collapse + "hey kivi" sparkle voice-edit. Below: the **same** orb transcript box (min 96 / max 240, resizable), edit-preset pills, and a "sign in / grant access to dictate →" fallback when `!voiceReady`.
3. hairline divider.
4. **historySection**: "history" eyebrow + clipboard opt-in toggle + search field + scrollable rows (max height 252, last-100 + local keyword search), "open history →".
5. hairline.
6. **footer**: "settings" (⚙) · spacer · "quit kivi" (destructive → app terminate).

Row/button radii: mode/sparkle/collapse/cancel `11`; chips/rows/search `sm`(8). The menu runs its
**own** runtime (`workspaceSink = true` — keep-in-box, never pastes elsewhere).

---

## 2. ONBOARDING

### 2.1 Gate + flow driver
- **Gate:** an `OnboardingState` bool key **`"kiviOnboarded"`** (settings store).
- **Phases** (ordered): `.permissions → .playground → .personalization → .handoff`. Linear next/prev.
- **View**: paper canvas, content clamped `maxWidth 820`, padding s24.
- **Resident-Kivi suppression** (critical): during onboarding, isolate the orb input, hide the real orb, and **suppress resident Kivi** — tear down the tray item AND uninstall the global hotkey hook so only the on-screen demo orb responds. `Finish()` sets `kiviOnboarded`, restores Kivi, reveals the real orb.
- **Reveal cadence** `OnboardingReveal`: `pop` = a spring-like overshoot (response 0.55, damping 0.8 — reproduce with a Composition spring or a `KeySpline` overshoot), `fade = easeOut(0.45)`, `leadIn 350 ms`, `step 450 ms`.

### 2.2 Phase A1 — Permissions & setup
- Responsive header (3 layouts by width) with a shiny gold kiwi badge.
- Card rows (status dot 8px, title, hover info dot, pill):
  - **microphone** — request via `getUserMedia`-equivalent (WASAPI open / `AppCapability` mic prompt); denied ⇒ deep-link `ms-settings:privacy-microphone`.
  - **accessibility** — **NOT NEEDED ON WINDOWS.** Windows has no Accessibility trust gate; `SetWindowsHookEx`/`SendInput`/UI Automation work without a per-app permission. **Drop this row** (or replace with a one-line "no extra permission needed on Windows" note). The macOS `AXIsProcessTrusted` prompt + recovery UX collapses entirely.
  - **screen context** — a Kivi setting backed by UI Automation text capture (deferred to M9), **not** a screen-recording prompt.
  - **launch at login** — registry `Run` key / Startup shortcut (see `platform-coupling-audit.md §14`).
  - **app theme** — day / night / system pills, writes `kiviAppearance` live.
- **Gate `allGranted`**: on Windows, effectively **mic-only** (mic required; the AX gate is gone).
- **Live state:** ~1.2 s poll + refresh when the window regains activation (returning from Settings). Mic status via the media-capability API or a `getUserMedia`-equivalent probe.

### 2.3 Phase A2 — Orb playground
A **video-style scrubbable tour**: pixel timeline, transport ▶/■, 3 chapters (`dictate 7600 ms`,
`dictate + edit 7200 ms`, `only edit 9600 ms`), intro beat 1750 ms. Nav truth is a UI-free
`TourTimeline` struct (state machine preroll→playing→paused→finale, unit-tested — **port verbatim to
C#**). Runs the real orb display-only until the finale ("it's alive, try it now!").

### 2.4 Phase A3 — Personalization ("make it yours")
One-item-at-a-time with a **live, fully interactive orb preview**. Items: `hotkey → theme → type →
state → positionType → position → ready`. Choices persisted to `OrbDefaultsKeys` (`kiviOrbVisibility`
show/pill/hide, `OrbStyle`, dock, movable). "launch kivi" finishes.

### 2.5 Phase A4 — Handoff
Green wave badge, "you're all set", copy "Press `<hotkey>` anywhere and talk…", back / "open kivi" →
`Finish()`.

**Windows/.NET:** the tour + personalization are **pure UI** (portable). The permission model is the
one place that *simplifies* — mic only, no Accessibility trust gate (see §5).

---

## 3. AUTH

### 3.1 Routing gate
Pure `authGateDestination(route, onboarded, bypass, forceOnboarding)`:
- `auth == null` (no backend) ⇒ **`.shell`** (anonymous local dev — never a wall).
- `bypass` (debug) ⇒ `.shell`.
- else by route: `.restoring → .splash`, `.signedOut → .signIn`, `.signedIn → onboarded ? .shell : .onboarding`.
- **Only the main window is gated**; orb + tray run signed-out. `.shell` additionally shows the permissions gate until mic is granted.
- Min window sizes per destination: shell 980×640, onboarding 360×480, splash/signIn 480×560.

### 3.2 Screens
- `SplashView` — "waking kivi…" while session restores; after **4 s** shows a "taking long — sign in again" escape hatch.
- `SignInScreen` — 400px card over a constellation field. State-driven set: sign-in / create-account, 6-digit email **verification** (paste-aware OTP field), password **recovery**, Google↔password **account-linking**. Google button, "or" divider, pill fields, requirements checklist (**≥8 chars, ≥1 digit, mixed case**).
- `AuthChrome` — mark badge (reuses the KiwiMark engine) + wordmark (green "i"s).

### 3.3 Controller + backends
An `AuthController` facade over **two backends**, built from config:
- **Kratos** (preferred) — Ory Kratos identity + **org-service v2 JWT mint**.
- **Supabase** (fallback) — GoTrue REST.
- `null` if neither configured ⇒ anonymous.
- Exposes observable `route`, `isSignedIn`, `displayName/Email`, `userID`, tenant scope, `sessionEndedBecauseExpired`, and `tokenProvider()`/`userIDProvider()` seams for the service clients.

### 3.4 Google OAuth — **loopback redirect** (the Windows/.NET decision)
The reference used a default-browser hop + a `kivi://` custom-scheme deep link. **For Windows/.NET,
switch to a loopback redirect** (`http://127.0.0.1:<port>/callback` via `HttpListener`):
- Open the auth URL in the user's **default browser** (`Process.Start` with the URL / `Launcher.LaunchUriAsync`) so their live Google session is used; park a `TaskCompletionSource`; resume when the loopback callback fires. A re-tap or cancel supersedes a stale hop.
- **Kratos flow**: `callbackURL = http://127.0.0.1:<port>/callback` → GET `self-service/login/api?return_to=…&return_session_token_exchange_code=true` → POST action `{method:"oidc", provider:"google"}` → **HTTP 422 `redirect_browser_to`** + append `prompt=select_account` → browser → callback `?code=` → GET `sessions/token-exchange?init_code&return_to_code` → `session_token`. Missing `code` ⇒ email collision → `accountLinkingRequired`.
- **Supabase**: `redirect = http://127.0.0.1:<port>/callback`, GET `/auth/v1/authorize?provider=google&redirect_to=…`, callback carries tokens in the **URL hash fragment** — the loopback listener serves a tiny HTML page that reads `location.hash` and posts it back (the fragment isn't sent to the server otherwise). The loopback callback **handles both Kratos `?code=` and Supabase `#fragment` uniformly** — the reason to prefer it over a custom scheme.
- **Custom-scheme fallback** (if ever needed): register the `kivi` protocol (registry `HKCU\Software\Classes\kivi`) + single-instance argv parsing. The loopback callback is preferred and more robust on desktop.

### 3.5 Org-JWT mint + session validation
- **Mint**: `POST <OrgServiceURL>/api/v2/auth/jwt` with header **`X-Session-Token: <kratos session>`**, body optional `org_id`/`workspace_id` → **15-minute** org JWT. `RefreshIfNeeded()` re-mints when within the remint margin — **clock-driven, on demand, no background timer**. Retry a 403 twice (0.3 s, 0.7 s backoff). Pure HTTP (`HttpClient`).
- **`whoami` arbiter**: `GET <KratosURL>/sessions/whoami` with `X-Session-Token`. Only a **401 = dead** destroys the durable session; network/5xx/403 = degraded-but-signed-in. Forced re-validation on app activation / wake (throttled).
- **Honest expiry**: `sessionEndedBecauseExpired` drives the "your session expired" caption instead of a silent sign-out.

### 3.6 Token storage — DPAPI (`Kivi.Platform.Secrets`)
- The macOS data-protection keychain (service `ai.sarvam.kivi`, access group `ai.sarvam.flow.shared`) / Electron `safeStorage` → **DPAPI** (`System.Security.Cryptography.ProtectedData`, `DataProtectionScope.CurrentUser`) persisted to a file under `%APPDATA%\Kivi`, **or** Windows Credential Manager.
- Keys: `kratosSessionToken`, `orgServiceJWT`, `kratosUserID`, `kratosEmail`, `kratosDisplayName`, `supabaseAccessToken/RefreshToken/UserID`, `retainedAudioEncryptionKey`.
- **Drop the access-group + legacy-carryover logic** (macOS-only, unnecessary for a standalone Windows app). The macOS "SecurityAgent focus-stealing prompt" mitigations are N/A. Keep the AES-GCM per-install key pattern for retained audio (the key itself DPAPI-protected).
- Session restore kicked off at app launch, skipped under bypass.

### 3.7 Voice-feature gate (why it matters to all three surfaces)
`canUseVoiceFeatures` / `canStartVoiceFeature()`: a take requires **isAuthed && permissions
(mic) granted && tenant context**. Signed-out ⇒ `presentSignIn()` (opens main window → auth gate).
This is what the tray/orb "sign in to dictate →" fallbacks call. (On Windows the "permissions"
factor is mic-only.)

---

## 4. Config an installer must reproduce
- Register the `kivi` URL scheme (registry) — for the OAuth deep-link fallback; the primary callback is the loopback `HttpListener`.
- Config values (was `Info.plist`): `KratosURL = https://login.sarvam.ai/identity`; `OrgServiceURL = https://auth.sarvam.ai`; `SupabaseURL = https://bjutljpmfhogrbdofplf.supabase.co`; `SupabaseAnonKey` (blank, filled at build).
- Mic usage — Windows surfaces the mic prompt via the OS privacy model (no usage-description string needed like macOS `NSMicrophoneUsageDescription`; declare mic capability in the MSIX manifest if packaged).
- **Dropped (macOS notarization / hardened runtime):** `com.apple.security.device.audio-input`, `automation.apple-events` entitlements, Sparkle `SUFeedURL`/`SUPublicEDKey` — no Windows analog. Auto-update = MSIX/Squirrel (see `RELEASE.md`). Not-sandboxed is the default for a Win32/full-trust app.

---

## 5. Windows/.NET notes (macOS/Electron → Windows/.NET)

**Tray / menu-bar**
- `NSStatusItem` + `NSPopover` / Electron `Tray` → a Windows **notification-area icon** + a frameless, always-on-top, `skipTaskbar` popover window (hide-on-deactivate). Windows tray works directly.
- Live **state-tinted breathing pill** → **pre-render a small set of discrete per-state icon frames** (Win2D → tray icon on an interval). **Avoid high-frequency tray updates** (throttling/flicker).
- **Resident with no window** → keep the process alive with all windows hidden + the orb as a tool-window (no taskbar button). No Dock concept.

**Onboarding permissions (the one place Windows SIMPLIFIES)**
- **Microphone**: macOS TCC prompt / Electron `systemPreferences` → on Windows detect via a capture-open failure and deep-link `ms-settings:privacy-microphone` (or the media-capability API). No in-app grant API needed.
- **Accessibility trust** (`AXIsProcessTrusted`) — **does not exist on Windows.** Global hooks + `SendInput` + UI Automation work without any trust gate. **Drop the whole accessibility permission row + its self-heal recovery UX.**
- **Launch-at-login** (`SMAppService`) → registry `Run` key / Startup shortcut / MSIX `StartupTask`.

**Auth**
- **Custom URL scheme** `kivi://` → prefer a **loopback redirect** (`http://127.0.0.1:<port>/callback` via `HttpListener`) — handles Kratos `?code=` + Supabase `#fragment` uniformly and avoids custom-scheme delivery pitfalls. If a scheme is still wanted, register `kivi` (registry) + single-instance argv.
- **Default-browser OAuth hop** (`NSWorkspace.open`) → `Process.Start(url)` / `Launcher.LaunchUriAsync`. The parked-continuation/supersede logic ports directly to `TaskCompletionSource`.
- **Token storage** (Keychain / `safeStorage`) → **DPAPI** (`ProtectedData`) or Credential Manager. The access-group + legacy carryover is macOS-only — dropped. The `X-Session-Token` mint, whoami arbiter, and 15-min JWT logic are pure HTTP — fully portable.

**Packaging**
- Sparkle → MSIX/Squirrel auto-update (see `RELEASE.md`). Hardened-runtime entitlements have no Windows analog — dropped.

**Deferred / v1 non-goals:** the whole auth+onboarding+tray tier is **M7**. The MVP runs anonymous
(local `DICTATE_AUTH_MODE=none`) and renders the shell directly.

> **Not applicable — Windows-only.** Every Linux/Wayland row from the reference (AppIndicator/
> StatusNotifierItem, GNOME-hides-tray, `.desktop` autostart/MimeType, `second-instance` argv on
> Linux, libsecret/kwallet, XDG portals) is dropped. There is no Linux target.
