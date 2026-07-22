# Kivi Overlay: Four Postures + "Hey Kivi" Voice Rewrite

> **Scope.** This supersedes the overlay behavior described in
> `docs/superpowers/specs/2026-07-21-kivi-onboarding-and-orb-design.md` §3 (which itself
> already evolved past its own "bare bird, no container" text via the `9ec72a5`/`5e147f4`
> commits — `LayeredOrb.cs` today draws an orb + growing text/graphic box). This spec
> replaces that 2-posture system (orb / box) with the real 4-posture design and adds the
> "hey kivi" voice-rewrite feature shown in the approved mockup.
>
> **Reference inputs:** the "kivi on the desktop" mockup frame (rest pill / woken orb /
> dictating box / hey-kivi box, four numbered panels with a rules list underneath),
> `ui/components/TranscriptBoxListening.jsx` and `ui/components/TranscriptBoxHeyKivi.jsx`
> (exact Figma-exported typography/color/padding for the box content),
> `ui/components/fig-tokens.css` (color/radius/spacing tokens), the existing
> `Kivi.Core/Prompts/Prompts.cs` `CommandModeSystem` prompt (already ported from FreeFlow,
> never wired up — built exactly for "transform SELECTED_TEXT per VOICE_COMMAND").

---

## 1. The four postures

One object, growing from the fixed bottom-center anchor, same as before — but now four
distinct shapes instead of two:

1. **Rest** (`Idle`) — a small pill, 39×15px, forest fill (`--brand-orbforest` /
   `OverlayIdleBrush` when not overridden by accent), with a slow breathing glow. This
   replaces the current implementation, which shows a full round orb even at rest.
2. **Woken** — a 61×61 circle containing the dot-matrix kiwi mark, with two satellite dots
   23px out from the orb's edge. Shown for a brief transition (~250ms) whenever the overlay
   leaves `Idle` and enters `Listening` — for both the normal dictation hotkey and the
   rewrite hotkey (§3), since both reuse `Listening`, distinguished only by the
   `IsRewriteCapture` flag (§6) — before growing into a box. This is a **timing-only**
   change to the existing orb-drawing code (`DrawOrb` in `LayeredOrb.cs` already draws
   essentially this shape); no new `RecordingState` is introduced for it.
3. **Dictating** — a 322×108 box, radius 20, replacing the box for the remainder of
   `Listening` and for `Processing`/`Speaking`/`Waiting`/`Done`/`Error`. Layout matches
   `TranscriptBoxListening.jsx` structurally:
   - Header row: state label top-left (Space Mono, 11px, uppercase, letter-spacing
     0.08em — e.g. "LIVE", "POLISHING", "INSERTING", "DONE", "ERROR" depending on state),
     language chip top-right (Space Mono, 12px, `--color-fg2`, e.g. "hi-IN · auto").
   - Body: Inter 15px, line-height 1.65. While `Listening`, shows the live partial
     transcript (§2); other states show short status copy ("cleaning up your text…",
     "pasting…", the final result on `Done`, or the error message).
   - Footer: Space Mono 12px hint line ("right ctrl to stop · esc to discard" while
     listening; blank/omitted for the non-interactive states).
   - Card chrome: `--color-paper2` fill, `inset 0 0 0 1px --color-border1` +
     `0 0 64px rgba(20,20,20,0.16)` shadow — the same "paper-2, radius 20, 12 shadow"
     treatment already used by in-app cards elsewhere in the design system.
   - This **replaces** the current graphic-based rendering (animated waveform bars,
     pulsing dots, checkmark, bang icon, bottom-left chip) entirely. No graphic-based
     fallback is kept.
4. **Hey Kivi** — the same box shell, widened dynamically to fit its content (up to a
   cap of 480px — wider than the 322px dictating box per the mockup's "same box, wider,"
   short of the Figma component's own 560px default artboard which isn't a literal target
   size for this small on-desktop overlay). Header label reads
   `HEY KIVI · "<spoken instruction>"` in `--color-stateprocessing` blue, matching
   `TranscriptBoxHeyKivi.jsx`. Body renders the word-level diff (§4): unchanged words in
   normal `--color-fg1`, deleted words struck through in muted `--color-fg2`, inserted
   words highlected with `--color-positivebg` background / `--color-positive` text. Footer
   reads "⏎ paste · esc keep original" once the diff is ready (`RewriteReview` state); while
   still computing (`RewritePending`) it shows a "rewriting…" status line instead, same
   visual family as the dictating box's processing copy.

All colors, radii, spacing, and type come from `ui/components/fig-tokens.css` (already
transcribed into `Kivi.App/Themes/Tokens.xaml`) — no hand-picked literals. Idle/Processing/
Waiting/Error keep their fixed semantic colors; Listening/Speaking/Done/the hey-kivi flow
use the user's `OrbAccentColor` where the existing code already does so (unchanged from
today's behavior).

---

## 2. Live partial transcript

Groq's Whisper endpoint is request/response only (no streaming API). To make the
"dictating" box feel live without switching STT providers:

- `IAudioCaptureService` gains `byte[] SnapshotRecording()`. In
  `WasapiAudioCaptureService`, this flushes the existing `WaveFileWriter` (which patches
  the RIFF header in place, same as on `Dispose()`) and returns a copy of the
  accumulated-so-far stream bytes — a valid, decodable WAV — without stopping capture.
- While `Listening`, `DictationOrchestrator` runs a **1.0s interval** loop: snapshot the
  buffer, send it through the existing `ISttEngine.TranscribeAsync`, and raise a new
  `PartialTranscriptChanged(string)` event that `OverlayViewModel` surfaces as the box's
  body text. Skip the first snapshot until at least 0.5s of audio has been captured (avoids
  transcribing near-silence).
- Only one partial call is in flight at a time (skip a tick if the previous one hasn't
  returned yet). The loop stops the instant `HoldEnded` fires; the final transcript from
  the normal (existing) `RunPipelineAsync` path always overwrites whatever partial text was
  last shown.
- Trade-off, accepted: extra Whisper API calls during every recording (roughly one per
  second of hold time), and partial text can occasionally revise/flicker as more audio
  arrives — there is no true incremental streaming API on Groq to avoid this.

---

## 3. "Hey kivi" trigger and target

- **Trigger**: a **second, separate hotkey** (not a spoken "hey kivi" prefix within the
  normal dictation hotkey). New `AppConfig.RewriteHotkeyVirtualKeyCode` (uint), default
  **Right Alt** (`0xA5`), mirroring Right Ctrl's placement for the primary hotkey.
  `IHotkeyService` is extended with a second held-key channel:
  ```csharp
  public interface IHotkeyService
  {
      event Action? HoldStarted;      // existing: primary/dictation hotkey
      event Action? HoldEnded;
      event Action? RewriteHoldStarted;  // NEW: rewrite hotkey
      event Action? RewriteHoldEnded;    // NEW
      void Start();
      void Stop();
      void SetHotkey(uint virtualKeyCode);         // existing: primary
      void SetRewriteHotkey(uint virtualKeyCode);   // NEW
  }
  ```
  `LowLevelKeyboardHookService`'s single hook callback checks the pressed key against both
  bound VK codes independently and raises the matching pair of events — no second OS-level
  hook needed.
- **Target text**: `DictationOrchestrator` already knows the exact string it last pasted
  (`textToPaste` in `RunPipelineAsync`, today discarded after use). It's retained as
  `_lastDictatedText`. The rewrite flow always targets this value — not an arbitrary OS
  text selection, which would require new UI-Automation read capability that's out of
  scope here.
  - If `_lastDictatedText` is empty when the rewrite hotkey is released (nothing dictated
    yet this session), skip transcription/rewrite entirely and show the box briefly in an
    error-flavored state ("nothing to rewrite yet") before returning to `Idle`.

### Flow

1. User holds the rewrite hotkey and speaks an instruction (e.g. "make it formal and add
   the doc link"). Recording behaves exactly like normal dictation (same
   `IAudioCaptureService` calls); the box shows the **Woken → dictating** posture sequence
   with header label `HEY KIVI · "…"` filled in live as partial transcription (§2) resolves
   the instruction text itself.
2. On release: transcribe the instruction (final, via the existing `ISttEngine` call).
   State → `RewritePending`.
3. Call `IPolishClient.RewriteAsync(_lastDictatedText, instructionTranscript, ct)` (§5).
4. Compute a word-level diff (§4) between `_lastDictatedText` and the rewritten result.
   State → `RewriteReview`. The box shows the diff and the "⏎ paste · esc keep original"
   footer.
5. While `RewriteReview` is active, the keyboard hook additionally arms a scoped watch for
   Enter/Esc (only while this state is active — Enter/Esc are otherwise left completely
   untouched everywhere else in the system):
   - **Enter** → send Ctrl+Z (undoes the prior dictation's single paste — `SendInputPasteService`
     injects text via one clipboard-paste keystroke, which is one atomic undo step in
     essentially all target apps), then `IPasteService.InjectTextAsync(rewrittenText, ...)`
     to paste the replacement. `_lastDictatedText` updates to the new value (so a further
     hey-kivi rewrite chains correctly). State → `Done` → `Idle`.
   - **Esc** → discard the rewrite. Nothing is touched in the target app (the document was
     never modified during review). State → `Idle`.
   - If the rewrite result is identical to the original (model judged "no change needed" —
     `CommandModeSystem`'s own contract already asks it to return the original text
     unmodified when appropriate), the diff renders as plain unstyled text; Enter/Esc still
     both function (Enter effectively re-pastes the same text).

---

## 4. Word-level diff

A small, dependency-free word-level LCS diff — no NuGet package added, consistent with
the project's existing minimal-dependency approach.

```
Kivi.Core/Text/WordDiff.cs
  public enum DiffOp { Equal, Delete, Insert }
  public readonly record struct DiffToken(DiffOp Op, string Text);
  public static class WordDiff
  {
      public static IReadOnlyList<DiffToken> Compute(string original, string rewritten);
  }
```

Tokenizes on whitespace (preserving the separator as part of each token's trailing
whitespace, or emitted as its own `Equal` token — implementation detail for the plan),
runs a standard LCS backtrack to produce the `Equal`/`Delete`/`Insert` sequence. Unit-tested
directly (no UI dependency) alongside the existing `Kivi.Core.Tests` suite.

---

## 5. Rewrite call (`IPolishClient`)

Reuses the already-ported, currently-unused `Prompts.CommandModeSystem` prompt (see
`Kivi.Core/Prompts/Prompts.cs` — it was ported from FreeFlow's `commandModeSystemPrompt`
specifically for "transform SELECTED_TEXT per VOICE_COMMAND", but nothing has called it
until now).

```csharp
public interface IPolishClient
{
    event Action<string>? EnteringCooldown;
    Task<string> CleanupAsync(string transcript, string context, CancellationToken ct);
    Task<string> RewriteAsync(string selectedText, string voiceCommand, CancellationToken ct); // NEW
}
```

`GroqPolishClient.RewriteAsync` mirrors `CleanupAsync`'s structure exactly: same
model/fallback list (`_config.CleanupModel` then `_config.FallbackModel`), same 429
cooldown tracking via the existing `EnteringCooldown` event, same think-tag stripping for
the fallback model. Only the prompts differ:
- System prompt: `Prompts.CommandModeSystem` (plus the same custom-vocabulary append used
  by cleanup, since vocabulary terms are just as relevant to a rewrite).
- User message: a new `Prompts.CommandModeUserMessage(selectedText, voiceCommand)`
  following `CleanupUserMessage`'s existing shape — a labeled `SELECTED_TEXT` block and a
  labeled `VOICE_COMMAND` block, matching the field names `CommandModeSystem`'s contract
  already references.
- No injection-guard post-check is needed here (unlike `CleanupAsync`) — `RewriteAsync`'s
  entire job is already "transform text per instruction," so there's no "did it execute an
  instruction it should have preserved as text" failure mode to guard against.

No `OutputLanguage` append — a rewrite should stay in whatever language the original
dictation was in, not be forced through the user's configured output-language override.

---

## 6. New `RecordingState` values

```csharp
public enum RecordingState
{
    Idle, Listening, Processing, Speaking, Waiting, Done, Error,
    RewritePending,  // NEW: transcribing instruction / calling RewriteAsync / computing diff
    RewriteReview,   // NEW: diff shown, awaiting Enter (paste) / Esc (discard)
}
```

The **hold-and-speak** phase of the rewrite hotkey reuses `RecordingState.Listening`
itself (same recording/partial-transcription machinery either way) rather than adding a
distinct enum value. `DictationOrchestrator` tracks a separate `IsRewriteCapture` bool
(true from `RewriteHoldStarted` until the instruction is transcribed on release, false
otherwise), exposed on `OverlayViewModel`. `LayeredOrb` reads this flag — orthogonal to
`State` — to pick the box header between "LIVE" (false) and `HEY KIVI · "<partial
instruction>"` (true) while `State == Listening`. Once the rewrite hotkey is released,
`IsRewriteCapture` stays true through `RewritePending`/`RewriteReview` (so the header
keeps showing the instruction) and resets to false when either state resolves back to
`Idle`.

Holding both hotkeys at once is not a supported combination: `DictationOrchestrator`
ignores a second hotkey's `HoldStarted` if a capture is already in progress (mirrors how a
single physical recording session — one `_cts`/`_audio` pair — already can't represent two
concurrent captures).

`OverlayViewModel` gains matching `IsRewritePending`/`IsRewriteReview`/`IsRewriteCapture`
properties and a `Diff`/`InstructionLabel`/`PartialTranscript` surface for `LayeredOrb` to
read when rendering box content.

---

## 7. Onboarding / Config page

The Config page gains a second `HotkeyCaptureBox` (existing control, already reusable
as-is) labeled for the rewrite hotkey, right below the existing dictation-hotkey capture
box, persisted the same way (`AppConfig.RewriteHotkeyVirtualKeyCode` via
`IAppConfigStore.Save`, re-applied via `IHotkeyService.SetRewriteHotkey` on next launch —
same pattern already established for the primary hotkey in the 2026-07-21 spec's §4).

---

## 8. What does NOT change

- `Kivi.Core`'s Groq HTTP client, `PolishPipeline`, `TranscriptCommands`, macro matching,
  the existing `CleanupAsync` path and its injection guard — untouched.
- `Kivi.Platform`'s screen-context provider, DPAPI secret store — untouched.
- The onboarding gate/flow (Login → Permissions → Config) from the 2026-07-21 spec —
  untouched structurally; only Config gains the one new control in §7.
- No selection-based rewrite (only Kivi's own last dictation is ever an editable target),
  no true streaming ASR, no multi-step undo/redo history beyond the single Ctrl+Z used for
  the hey-kivi replace step, no position/other overlay settings.

---

## 9. Open items deliberately deferred

- A rewrite chain longer than "undo once, paste new" (e.g. reviewing multiple past
  dictations, not just the most recent one) — out of scope.
- Selection-based ("rewrite whatever text is highlighted anywhere") rewrite mode — would
  need new UI-Automation text-read capability; deferred.
- Configurable partial-transcription interval (currently a fixed 1.0s constant, not
  exposed in Settings) — future work if it turns out to need tuning per user.
