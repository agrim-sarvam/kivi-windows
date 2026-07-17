# Impl 01 — Screen Context Capture via Windows UI Automation (UIA)

> Requirement 1 (POA #2) of the Kivi-for-Windows port. Reads the currently-focused
> UI element and its surrounding/selected text from **other** applications so the LLM
> cleanup step has context (format an email reply correctly, spell names from the visible
> thread, etc.). Mirrors Kivi's macOS Accessibility (AX) behaviour, and **must never read
> password/secure fields.**
>
> Prereqs: read [`freeflow-research.md`](./freeflow-research.md) and [`overview.md`](./overview.md) first.
> Stack is fixed: **WinUI 3 / Windows App SDK, .NET 8/9, C#**, Groq backend (OpenAI-compatible REST).

---

## 0. API surface — verified against Microsoft Learn

Every native call below was confirmed against official docs before writing this doc.
We use the **modern COM UIA3 client** (`IUIAutomation*`), *not* the legacy WPF-era managed
`System.Windows.Automation` wrapper.

| Purpose | API | Verified doc |
|---|---|---|
| Create the UIA client object | `CoCreateInstance(CLSID_CUIAutomation, …, IID_IUIAutomation)` | [Creating the CUIAutomation Object](https://learn.microsoft.com/windows/win32/winauto/uiauto-creatingcuiautomation#creating-the-object) · [CoCreateInstance](https://learn.microsoft.com/windows/win32/api/combaseapi/nf-combaseapi-cocreateinstance) |
| Get focused element | `IUIAutomation::GetFocusedElement` | [GetFocusedElement](https://learn.microsoft.com/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomation-getfocusedelement) |
| Password / secure field check | `IUIAutomationElement::get_CurrentIsPassword` → `UIA_IsPasswordPropertyId = 30019` | [get_CurrentIsPassword](https://learn.microsoft.com/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationelement-get_currentispassword) · [Property IDs](https://learn.microsoft.com/windows/win32/winauto/uiauto-automation-element-propids) |
| Selected / surrounding text | `IUIAutomationTextPattern::GetSelection` + `IUIAutomationTextRange::GetText(maxLength)` | [TextRange::GetText](https://learn.microsoft.com/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationtextrange-gettext) · [Text pattern perf](https://learn.microsoft.com/windows/win32/winauto/uiauto-understandingperformanceissues) |
| Simple field value | `IUIAutomationValuePattern::get_CurrentValue` | [ValuePattern::get_CurrentValue](https://learn.microsoft.com/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationvaluepattern-get_currentvalue) |
| Win32 fallback — window handle | `GetForegroundWindow` | winuser.h |
| Win32 fallback — window title | `GetWindowTextW` | winuser.h |
| Win32 fallback — owning PID | `GetWindowThreadProcessId` | [GetWindowThreadProcessId](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getwindowthreadprocessid) |
| Win32 fallback — exe path from PID | `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` + `QueryFullProcessImageNameW` | [QueryFullProcessImageNameW](https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-queryfullprocessimagenamew) |
| COM init on the worker thread | `CoInitializeEx` / `CoUninitialize` | [The COM Library](https://learn.microsoft.com/windows/win32/com/the-com-library) |

**Two load-bearing facts from the docs that shape the whole design:**

1. **TextPattern/TextRange are cross-process and have no caching mechanism.** The docs are
   explicit: retrieve *moderately sized* blocks in **one** `GetText` call. Never walk char-by-char —
   each call is a cross-process round-trip. We call `GetText(maxLength)` once with a bounded length.
   ([Text pattern perf](https://learn.microsoft.com/windows/win32/winauto/uiauto-understandingperformanceissues))
2. `GetFocusedElement` can return `UIA_E_ELEMENTNOTAVAILABLE` if focus moved by the time the call
   returns — docs say clients "should handle errors gracefully." We swallow all UIA errors to an empty
   context (see §10).

---

## 1. Purpose & how it fits the pipeline

The dictation pipeline is: **hotkey → mic capture → STT (Groq) → LLM cleanup (Groq) → paste.**
The cleanup LLM call takes a system prompt plus a `CONTEXT:` block. Screen context fills that block.

```
Hold START ─┬─► begin mic capture ─────────────────────────┐
            └─► SNAPSHOT screen context (this service) ──┐  │   (concurrent)
                                                         ▼  ▼
Hold END  ─────► STT transcribe  ─────────────────────► LLM cleanup(system, CONTEXT + transcript)
                                                         ▲
                          context string injected here ──┘
```

**Snapshot timing — at hold-START, not hold-END.** This mirrors FreeFlow/Kivi macOS: capture the
context of the app the user is looking at *when they start dictating*, before our own overlay or paste
target steals focus. The capture runs **concurrently with mic capture and transcription** so it never
adds to end-to-end latency — by the time STT returns, `Task<AppContext>` is already complete.

The result feeds the `CONTEXT:` field of the cleanup prompt (§7). If capture fails or the field is a
password, context is simply empty and cleanup proceeds text-only — context is an *enhancement*, never a
hard dependency.

**macOS parity (from `zachlatta/freeflow` `AppContextService.swift`):** the mac app reads
`kAXFocusedUIElementAttribute` + `kAXSelectedTextAttribute` + window `kAXTitleAttribute` + frontmost app
name, assembles an `App:/Window:/Selected text:` string, and caps screenshot data-URIs at 500 000 chars.
The Windows UIA equivalents map 1:1: focused element → `GetFocusedElement`, selected text → TextPattern
selection, window title/app → Win32 fallback. **One parity gap we deliberately fix:** neither the macOS
original nor the Python Windows port explicitly skips password fields — Kivi's internal Data Flow doc
requires it, so we add the `IsPassword` guard as a first-class step (§3).

---

## 2. Tiered capture strategy

Capture is best-effort and tiered. Each tier is attempted only if the previous produced nothing
useful; every tier is independently wrapped so one failure never aborts the others.

**Tier A — UIA3 focused element (primary).**
1. `GetFocusedElement()` → `IUIAutomationElement`.
2. **Password guard first** (§3). If `CurrentIsPassword == TRUE`, abandon *all* text extraction for this
   element and return identity-only context. Do this *before* touching TextPattern/ValuePattern.
3. Try **TextPattern**: `GetCurrentPattern(UIA_TextPatternId)` → `GetSelection()`; if a non-empty
   selection exists, `range.GetText(MaxSelectedChars)`. If the selection is empty (degenerate range),
   optionally fall back to `DocumentRange.GetText(MaxSurroundingChars)` for surrounding context.
4. If TextPattern is unsupported (many single-line edits don't implement it), try **ValuePattern**:
   `GetCurrentPattern(UIA_ValuePatternId)` → `get_CurrentValue`. (Docs: single-line edits expose
   contents via ValuePattern; multi-line edits use TextPattern instead.)
5. Element identity: `CurrentName`, `CurrentClassName`, `CurrentProcessId` for app naming.

**Tier B — Win32 window identity (always runs).**
Independent of UIA, we always grab foreground-window identity because it's cheap, reliable, and works
even when UIA returns nothing:
`GetForegroundWindow` → `GetWindowTextW` (window title) → `GetWindowThreadProcessId` (PID) →
`OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` + `QueryFullProcessImageNameW` → strip to
`chrome`, `outlook`, `notepad`, etc. (basename without `.exe`). This is the `App:` / `Window:` line of
the context even when no text could be read.

**Tier C — Ctrl+C clipboard trick (optional, opt-in, default OFF).**
Last resort when UIA yields no selected text but the user clearly has a selection (e.g. a stubborn
Electron/Chromium surface). Save clipboard → synthesise Ctrl+C → wait ~120 ms → read clipboard →
**restore original clipboard**. This is intrusive (mutates a shared resource, can race the app) and is
the same fallback `stha-hardik/freeflow-windows` used. Gate it behind a setting
(`EnableClipboardFallback`, default `false`) and **never** run it when the focused element is a password
field. Because paste (POA) already owns clipboard-safety logic, reuse that module's save/restore rather
than duplicating it.

---

## 3. Password / secure-field skip logic (correctness & privacy — non-negotiable)

**Rule:** if the focused element reports itself as a password/protected field, we read **no text** from
it — not via TextPattern, not via ValuePattern, not via the Ctrl+C trick. We may still report app/window
identity (Tier B), which is not sensitive.

The check is a single UIA property: **`UIA_IsPasswordPropertyId` (numeric id `30019`)**, exposed on
every element as `IUIAutomationElement::get_CurrentIsPassword`. Per the property-ID docs: *"When the
IsPassword property is TRUE … a client application should disable … feedback that may expose the user's
protected information. Attempting to access the Value property of the protected element may cause an
error."* So checking it is both the privacy-correct and the crash-safe thing to do.

```csharp
// element is IUIAutomationElement (CsWin32-generated COM interface)
static bool IsSecureField(IUIAutomationElement element)
{
    try
    {
        // get_CurrentIsPassword -> Windows BOOL (int). Non-zero == protected.
        element.get_CurrentIsPassword(out BOOL isPassword);
        return isPassword;
    }
    catch
    {
        // If we can't even determine it, treat as secure (fail closed).
        return true;
    }
}
```

**Fail-closed policy.** Any uncertainty → treat as secure and skip text. This covers:
- The element throws on the property read.
- A UAC / secure-desktop prompt is up (UIA generally can't reach it anyway).
- Elevated target process our medium-IL app can't inspect (UIA returns error).

**Belt-and-suspenders (recommended, cheap):** in addition to `IsPassword`, also skip when the control
type is a known-sensitive one and the app is a known credential surface. But `IsPassword` is the
authoritative signal — standard Win32 `Edit` controls with `ES_PASSWORD`, WinForms/WPF/WinUI
`PasswordBox`, and Chromium `<input type=password>` all set it. Do **not** rely on window-title keyword
sniffing as the primary mechanism; it's a weak heuristic, `IsPassword` is the contract.

---

## 4. CsWin32 project setup

We generate the interop from metadata at build time with **CsWin32** — no hand-written
`[DllImport]`/`[ComImport]`, no legacy `Interop.UIAutomationClient` reference.

### NuGet

```xml
<ItemGroup>
  <!-- Source generator: emits P/Invokes + COM interop from Windows metadata -->
  <PackageReference Include="Microsoft.Windows.CsWin32" Version="0.3.*" PrivateAssets="all" />
</ItemGroup>
```

`PrivateAssets="all"` keeps the generator out of downstream references. CsWin32 also pulls
`Microsoft.Windows.SDK.Win32Metadata` transitively.

> Note on package name: the current published generator package is **`Microsoft.Windows.CsWin32`**
> (the research doc's "Microsoft.Windows.CSharpWin32" refers to the same CsWin32 generator — use the
> `Microsoft.Windows.CsWin32` id, which is what Microsoft Learn's own samples install).

### `NativeMethods.txt`

CsWin32 reads a plain text file named `NativeMethods.txt` at project root; each line is an API, type,
or constant name to generate. For this feature:

```text
; --- COM: UIA3 client ---
CUIAutomation                 ; CLSID/coclass -> lets CsWin32 emit CoCreateInstance-able type + IID
IUIAutomation                 ; root client interface (GetFocusedElement, ElementFromHandle, …)
IUIAutomationElement          ; focused element; get_CurrentIsPassword, CurrentName, CurrentProcessId
IUIAutomationTextPattern      ; GetSelection, DocumentRange
IUIAutomationTextRange        ; GetText(maxLength)
IUIAutomationValuePattern     ; get_CurrentValue
UIA_TextPatternId             ; = 10014 (pattern id constant)
UIA_ValuePatternId            ; = 10002
UIA_IsPasswordPropertyId      ; = 30019

; --- COM plumbing ---
CoCreateInstance
CLSCTX

; --- Win32 fallback: foreground window + process identity ---
GetForegroundWindow
GetWindowTextW
GetWindowTextLengthW
GetWindowThreadProcessId
OpenProcess
QueryFullProcessImageNameW
CloseHandle
PROCESS_ACCESS_RIGHTS         ; provides PROCESS_QUERY_LIMITED_INFORMATION
```

### CsWin32 options (optional `NativeMethods.json`)

```json
{
  "$schema": "https://aka.ms/CsWin32.schema.json",
  "comInterop": { "preserveSigMethodsAndInterfaces": [ "IUIAutomation*" ] },
  "allowMarshaling": true
}
```

`allowMarshaling: true` gives friendlier COM interfaces (BSTR→`string`, HRESULT→exceptions where
appropriate). CsWin32 emits the UIA interfaces as .NET COM interfaces, the CLSID/IID GUIDs for
`CUIAutomation`, and the `UIA_*Id` constants.

### Getting the `IUIAutomation` instance

Per [Creating the CUIAutomation Object](https://learn.microsoft.com/windows/win32/winauto/uiauto-creatingcuiautomation#creating-the-object),
`CoInitialize` must run first, then `CoCreateInstance(CLSID_CUIAutomation, …, IID_IUIAutomation)`.
The generated coclass makes this a one-liner; equivalently, use the classic activation path:

```csharp
using Windows.Win32;                 // CsWin32 root
using Windows.Win32.UI.Accessibility; // IUIAutomation, CUIAutomation
using Windows.Win32.System.Com;       // CLSCTX

// Simplest: activate the coclass directly (COM must already be initialized on this thread).
IUIAutomation automation = (IUIAutomation)new CUIAutomation();

// Explicit CoCreateInstance form (equivalent), if you prefer no coclass 'new':
// PInvoke.CoCreateInstance(typeof(CUIAutomation).GUID, null,
//     CLSCTX.CLSCTX_INPROC_SERVER, typeof(IUIAutomation).GUID, out object obj);
// var automation = (IUIAutomation)obj;
```

The instance is created **once per worker thread** and reused across snapshots (see §8) — creating it
per-call is wasteful and each thread needs its own COM apartment anyway.

---

## 5. Proposed C# interface + concrete skeleton

### Interface (lives in `Kivi.Core`, so the orchestrator depends only on the seam)

```csharp
namespace Kivi.Core.Context;

/// <summary>Immutable snapshot of the user's screen context at hold-start.</summary>
public sealed record AppContext(
    string AppName,          // e.g. "outlook"  (never null; "" if unknown)
    string WindowTitle,      // e.g. "Re: Q3 planning - Message (HTML)"
    string SelectedText,     // selected or surrounding text; "" if none/secure
    bool   WasSecureField,   // true => we intentionally skipped text
    string? ScreenshotBase64 // v2/optional (§6); null when disabled
)
{
    public static readonly AppContext Empty =
        new("", "", "", false, null);

    public bool HasText => SelectedText.Length > 0;
    public bool IsEmpty => AppName.Length == 0 && WindowTitle.Length == 0 && !HasText;
}

/// <summary>Captures screen context from OTHER applications. Best-effort, never throws.</summary>
public interface IScreenContextProvider   // a.k.a. IContextService
{
    /// <summary>
    /// Snapshot the currently-focused app/element. Call at hold-START.
    /// Always completes (returns AppContext.Empty on any failure); honours <paramref name="timeout"/>.
    /// </summary>
    Task<AppContext> CaptureAsync(TimeSpan timeout, CancellationToken ct = default);
}
```

### Concrete implementation skeleton (Windows-native, `Kivi.Windows` assembly)

Key design points baked in below: a **dedicated STA worker thread** that owns the COM apartment and the
`IUIAutomation` instance; **all UIA work marshalled onto it**; a **hard timeout**; and **error-swallowing
to `AppContext.Empty`**.

```csharp
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Kivi.Core.Context;

public sealed class UiaScreenContextProvider : IScreenContextProvider, IDisposable
{
    private const int  MaxSelectedChars    = 2_000;  // one GetText call, bounded (perf §8)
    private const int  MaxSurroundingChars = 2_000;
    private const int  ContextCharCap      = 500;    // final string cap (§7)

    private readonly StaTaskScheduler _sta = new(threadCount: 1); // STA, owns COM + IUIAutomation
    private IUIAutomation? _automation;                            // created lazily on the STA thread

    public Task<AppContext> CaptureAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        // Marshal the whole capture onto the STA worker; race it against a timeout so a hung
        // cross-process UIA call can never stall the pipeline.
        var work = Task.Factory.StartNew(
            () => CaptureOnStaThread(ct),
            ct, TaskCreationOptions.None, _sta);

        return WithTimeout(work, timeout);
    }

    // ---- Runs ON the STA worker thread ----
    private AppContext CaptureOnStaThread(CancellationToken ct)
    {
        // Win32 identity first — cheap and always available, so we degrade to it gracefully.
        var (appName, windowTitle) = TryGetForegroundIdentity();

        try
        {
            _automation ??= (IUIAutomation)new CUIAutomation(); // one-time per thread

            _automation.GetFocusedElement(out IUIAutomationElement? focused);
            if (focused is null)
                return new AppContext(appName, windowTitle, "", false, null);

            // ── PASSWORD GUARD — must be before any text read (§3) ──
            if (IsSecureField(focused))
                return new AppContext(appName, windowTitle, "", WasSecureField: true, null);

            string text = TryReadSelectedOrValue(focused);
            return new AppContext(appName, windowTitle, Clamp(text, MaxSelectedChars), false, null);
        }
        catch
        {
            // GetFocusedElement can return UIA_E_ELEMENTNOTAVAILABLE, target may be elevated, etc.
            // Degrade to identity-only; never propagate. (docs: handle UIA errors gracefully)
            return new AppContext(appName, windowTitle, "", false, null);
        }
    }

    private static bool IsSecureField(IUIAutomationElement el)
    {
        try { el.get_CurrentIsPassword(out BOOL p); return p; }
        catch { return true; }   // fail closed
    }

    private string TryReadSelectedOrValue(IUIAutomationElement el)
    {
        // Tier A.3: TextPattern selection (multi-line / documents / browsers).
        try
        {
            if (el.GetCurrentPattern(PInvoke.UIA_TextPatternId) is IUIAutomationTextPattern tp)
            {
                var selection = tp.GetSelection();            // IUIAutomationTextRangeArray
                if (selection is not null && selection.Length > 0)
                {
                    var range = selection.GetElement(0);
                    string sel = range.GetText(MaxSelectedChars); // ONE cross-process call, bounded
                    if (!string.IsNullOrWhiteSpace(sel)) return sel;
                }
                // No selection: optional surrounding context from the document range.
                string doc = tp.DocumentRange.GetText(MaxSurroundingChars);
                if (!string.IsNullOrWhiteSpace(doc)) return doc;
            }
        }
        catch { /* pattern unsupported / cross-proc hiccup -> try ValuePattern */ }

        // Tier A.4: ValuePattern (single-line edit controls).
        try
        {
            if (el.GetCurrentPattern(PInvoke.UIA_ValuePatternId) is IUIAutomationValuePattern vp)
            {
                vp.get_CurrentValue(out string value);
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
        }
        catch { /* swallow */ }

        return "";
    }

    private static (string app, string title) TryGetForegroundIdentity()
    {
        try
        {
            HWND hwnd = PInvoke.GetForegroundWindow();
            if (hwnd == HWND.Null) return ("", "");

            // Window title
            int len = PInvoke.GetWindowTextLength(hwnd);
            string title = "";
            if (len > 0)
            {
                Span<char> buf = stackalloc char[len + 1];
                int n = PInvoke.GetWindowText(hwnd, buf);
                title = new string(buf[..n]);
            }

            // Owning process -> exe basename
            uint pid = 0;
            unsafe { PInvoke.GetWindowThreadProcessId(hwnd, &pid); }
            string app = TryGetProcessName(pid);
            return (app, title);
        }
        catch { return ("", ""); }
    }

    private static string TryGetProcessName(uint pid)
    {
        SafeHandle? h = null;
        try
        {
            h = PInvoke.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h is null || h.IsInvalid) return "";
            Span<char> buf = stackalloc char[260];
            uint size = (uint)buf.Length;
            if (PInvoke.QueryFullProcessImageName(h, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, buf, ref size))
                return Path.GetFileNameWithoutExtension(new string(buf[..(int)size])).ToLowerInvariant();
            return "";
        }
        catch { return ""; }
        finally { h?.Dispose(); }
    }

    private static string Clamp(string s, int max)
        => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max]);

    private static async Task<AppContext> WithTimeout(Task<AppContext> work, TimeSpan timeout)
    {
        var done = await Task.WhenAny(work, Task.Delay(timeout)).ConfigureAwait(false);
        // If UIA hung, we abandon the STA task (it will finish and be GC'd) and return Empty.
        return done == work && work.IsCompletedSuccessfully ? work.Result : AppContext.Empty;
    }

    public void Dispose() => _sta.Dispose();
}
```

> `StaTaskScheduler` is the well-known single-thread STA scheduler (from
> `System.Threading.Tasks.Extensions` samples / ParallelExtensionsExtras) — a `TaskScheduler` backed by
> one dedicated `ApartmentState.STA` thread that calls `CoInitialize` on start. It guarantees every UIA
> call runs on the same STA thread that created `_automation`. A hand-rolled single-thread STA pump is
> equally fine.

> Exact generated member names (`GetSelection`/`get_CurrentValue`/pattern casts) depend on CsWin32's
> `allowMarshaling` setting; if marshaling is off you'll see `PreserveSig` HRESULT-returning signatures
> with `out` params instead. Adjust the call sites to match what CsWin32 emits — the shape above assumes
> marshaled interfaces.

---

## 6. Optional screenshot capture for vision context (v2)

Marked **optional / v2** — only useful once the cleanup path uses a vision-capable model. Mirrors the
macOS app's screenshot data-URI (capped at 500 000 chars) and the Python port's `capture_screenshot`.

```csharp
// v2 only. Requires the cleanup model to accept image_url content parts.
private static string? CaptureScreenshotBase64(int maxDimension = 1024, long maxBytes = 500_000)
{
    try
    {
        var bounds = System.Windows.Forms.Screen.PrimaryScreen!.Bounds; // or per-monitor DPI-aware bounds
        using var full = new Bitmap(bounds.Width, bounds.Height);
        using (var g = Graphics.FromImage(full))
            g.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size);

        using var scaled = Downscale(full, maxDimension);   // longest side -> maxDimension
        long quality = 55;
        byte[] bytes;
        do
        {
            bytes = EncodeJpeg(scaled, quality);
            quality -= 10;                                   // 55 -> 45 -> … -> 30 (match mac/py behaviour)
        } while (bytes.LongLength > maxBytes && quality >= 30);

        return $"data:image/jpeg;base64,{Convert.ToBase64String(bytes)}";
    }
    catch { return null; }   // screenshots are pure bonus; never fail the pipeline
}
```

Notes: `System.Drawing.Common` is Windows-only from .NET 6+ (fine here). Prefer `Windows.Graphics.Capture`
(WinRT) later if we want per-window capture without grabbing the whole desktop. Keep this OFF by default;
**do not** screenshot when the foreground field is a password (same privacy rule as text).

---

## 7. Context string format injected into the prompt

The `AppContext` record is rendered to a compact string and placed in the cleanup prompt's `CONTEXT:`
block. Format mirrors the macOS app's `App: / Window: / Selected text:` layout and the Python port's
500-char cap.

```csharp
public static string ToPromptContext(this AppContext c)
{
    if (c.IsEmpty) return "";

    var sb = new StringBuilder();
    if (c.AppName.Length     > 0) sb.Append("App: ").Append(Sanitize(c.AppName)).Append('\n');
    if (c.WindowTitle.Length > 0) sb.Append("Window: ").Append(Sanitize(c.WindowTitle)).Append('\n');
    if (c.HasText)                sb.Append("Selected text: ").Append(Sanitize(c.SelectedText)).Append('\n');

    var s = sb.ToString().TrimEnd('\n');
    return s.Length <= 500 ? s : s[..500];   // hard cap — matches macOS/Python behaviour
}
```

Produces, e.g.:

```
App: outlook
Window: Re: Q3 planning - Message (HTML)
Selected text: Hi Priya, thanks for the numbers. A couple of follow-ups:
```

**`Sanitize` is mandatory, not cosmetic.** Window titles and selected text are attacker-influenced input
flowing straight into an LLM prompt. Reuse the core `sanitizeContextField` logic ported from
`mrinalwadhwa/freeflow` (POA reuse boundary): strip ChatML/role delimiters (`<|im_start|>`,
`system:` / `assistant:` prefixes, etc.), collapse whitespace/newlines, and drop control chars. This is
the prompt-injection defence for the context field. The whole block is capped at **500 chars**.

The orchestrator injects it as: `CONTEXT:\n{block}\n\nTRANSCRIPT:\n{transcript}` (exact template lives
with the ported prompts in `Kivi.Core`).

---

## 8. Threading & performance notes

- **Cross-process cost.** UIA element/pattern calls marshal across process boundaries; TextPattern
  explicitly has **no caching** ([docs](https://learn.microsoft.com/windows/win32/winauto/uiauto-understandingperformanceissues)).
  Mitigations we apply: (a) one bounded `GetText(maxLength)` call, never per-char; (b) read at most
  `MaxSelectedChars` (2 000) — far more than the 500-char final cap, so truncation is safe;
  (c) prefer the cheap selection over the full document range.
- **STA apartment.** Create `IUIAutomation` and make **all** its calls from a single STA thread
  (`CoInitializeEx(COINIT_APARTMENTTHREADED)`). The `StaTaskScheduler` owns that thread for the app's
  lifetime; the instance is created once and reused. Never touch a UIA COM pointer from the WinUI UI
  thread or a random threadpool thread.
- **Timeout.** `CaptureAsync` races the STA task against `Task.Delay(timeout)`. Recommended default
  **250–400 ms**. If UIA hangs on a slow/hostile app, we abandon and return `AppContext.Empty`; the
  orphaned STA task completes later harmlessly.
- **Concurrency with STT.** Capture is kicked off at hold-START and awaited in parallel with the STT
  request at hold-END:

  ```csharp
  // At hold-start:
  Task<AppContext> ctxTask = _context.CaptureAsync(TimeSpan.FromMilliseconds(350));
  // ... user speaks; at hold-end:
  Task<string> sttTask = _stt.TranscribeAsync(wav);
  await Task.WhenAll(ctxTask, sttTask);
  var cleaned = await _polish.CompleteAsync(system, ctxTask.Result.ToPromptContext(), sttTask.Result);
  ```

  Because context is snapshotted at hold-start and usually completes in well under 50 ms, it is
  effectively free — it is already done before transcription returns.
- **Idle cost.** Zero work when not dictating (aligns with the <100 MB / near-zero-idle budget in
  overview.md). The STA thread parks; the single COM object is tiny.

---

## 9. Testing approach (no special hardware needed)

UIA reads *other* apps, so tests just launch stock apps and assert on the captured string. All of this
runs on a normal dev box / CI Windows runner.

1. **Notepad (ValuePattern / TextPattern edit).** Launch `notepad.exe`, type known text, select it
   (`Ctrl+A`), `CaptureAsync` → assert `AppName == "notepad"` and `SelectedText` contains the typed text.
   Automate the keystrokes with `SendInput` (the paste module already wraps it) so it's hands-free/CI-able.
2. **Browser (TextPattern, cross-process, Chromium).** Open a local HTML file in Edge/Chrome with a
   `<textarea>` of known content, select all, capture → assert text round-trips. Validates the
   no-caching bounded-`GetText` path against a real Chromium tree.
3. **Password field — the critical privacy test.** A tiny WinForms/WPF harness with a `PasswordBox`
   (or a Win32 `Edit` with `ES_PASSWORD`, or a browser `<input type=password>`). Type a secret, focus it,
   capture → **assert `WasSecureField == true` AND `SelectedText == ""`**. Add the same assertion for the
   Ctrl+C fallback path (must not fire on secure fields). This test is a release gate.
4. **No focus / desktop.** Focus the desktop, capture → assert `AppContext.Empty` (or identity-only), no
   throw.
5. **Timeout.** Inject a fake `IScreenContextProvider` whose STA work sleeps > timeout → assert
   `CaptureAsync` returns `Empty` within ~timeout.
6. **Sanitizer unit tests.** Feed window titles containing `<|im_start|>system`, `assistant:` prefixes,
   newlines, 10 000-char blobs → assert delimiters stripped and output ≤ 500 chars. Pure string tests,
   no UIA.
7. **`ToPromptContext` formatting.** Table-driven: empty context → `""`; identity-only → 2 lines;
   full → 3 lines; over-long → truncated at 500.

CI note: UIA needs an interactive desktop session; run tiers 1–4 on a self-hosted/interactive Windows
runner. Tiers 5–7 are pure logic and run anywhere.

---

## 10. Failure modes & handling

| # | Failure mode | Detection | Handling |
|---|---|---|---|
| 1 | Focus moved before capture (`UIA_E_ELEMENTNOTAVAILABLE`) | HRESULT / exception from `GetFocusedElement` | Swallow → return identity-only or `Empty`. Docs explicitly say handle gracefully; optionally retry once. |
| 2 | **Focused field is a password** | `get_CurrentIsPassword == TRUE` | Return `WasSecureField=true`, `SelectedText=""`. No TextPattern/ValuePattern/Ctrl+C. |
| 3 | Can't read `IsPassword` (throws) | exception | **Fail closed** — treat as secure, skip text. |
| 4 | Element supports neither TextPattern nor ValuePattern | `GetCurrentPattern` returns null / cast fails | Fall through tiers; end with identity-only context. |
| 5 | Elevated target process (our app is medium-IL) | UIA error / empty result | Degrade to Win32 identity (title may still be readable); text = "". |
| 6 | UIA call hangs on a slow/hostile app | timeout in `CaptureAsync` | Abandon STA task, return `Empty`. Pipeline proceeds text-only. |
| 7 | COM not initialised on the calling thread (`CO_E_NOTINITIALIZED` 0x800401F0) | exception on first UIA call | Prevented by design: STA scheduler calls `CoInitializeEx` on its thread before any UIA use. |
| 8 | `GetForegroundWindow` returns null (no foreground) | `HWND.Null` | `("", "")` identity; capture continues / returns `Empty`. |
| 9 | `OpenProcess`/`QueryFullProcessImageName` denied | BOOL false / exception | App name = `""`; window title may still populate. |
| 10 | Selected text huge (whole document) | bounded `GetText(MaxSelectedChars)` + final 500-char clamp | Never unbounded; single cross-process call. |
| 11 | Prompt-injection payload in window title / selection | always | `Sanitize()` strips ChatML/role delimiters before it reaches the LLM (§7). |
| 12 | Ctrl+C fallback clobbers clipboard | always | Save→copy→read→restore via the paste module's clipboard-safety logic; disabled by default; never on secure fields. |

**Golden rule:** this service **never throws** across its public surface and context is always optional.
Any failure degrades to a less-rich (or empty) context; the dictation pipeline continues and pastes
cleaned text regardless.
