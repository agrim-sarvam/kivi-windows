# Questions for Sarvam Kivi (iOS → Windows port)

Context: Sarvam has a Kivi dictation app that already exists on iOS. Before
receiving that codebase, these are the questions to ask before deciding
whether the existing Windows engine (built as a FreeFlow port using Groq)
can absorb Sarvam's backend, or whether the Windows app needs to be
re-architected around Sarvam's actual model shape.

## 1. Context capture

- Do we capture the entire screenshot for context, or just UIA-style text
  (focused app name, window title, selected text)? This is the single
  biggest fork — screenshot implies a vision-capable model in the loop for
  context synthesis (extra API call, cost, latency); text-only is cheap but
  weaker. What does iOS Kivi actually do?
- If screenshot: how is it downscaled/cropped before sending, and is there
  a redaction/blur step for anything sensitive on screen?
- Does context capture run on every dictation, or only when nothing better
  is available?

## 2. Privacy / sensitive-data boundary

- Password fields — does iOS Kivi have a fail-closed guard, and does it
  extend to screenshots (a password field can be visible in a screenshot
  even if its text value is never read)?
- Is there a deny-list of apps (banking, password managers) where context
  capture is skipped entirely regardless of field type?
- Where does captured context live in memory — discarded immediately after
  the LLM call, ever written to disk or logs?

## 3. Backend / model shape

- Is Sarvam's STT + cleanup one model call or two separate calls (like
  Groq's Whisper + LLM split)? Determines whether the existing
  `ISttEngine`/`IPolishClient` two-interface split holds or needs
  collapsing.
- Streaming or request/response? Streaming transcription would mean a
  materially different orchestrator than today's record-then-send.
- Hosted API or on-device model? Changes secret storage, network error
  handling, and the offline story.
- Does Sarvam's model accept vision input directly (multimodal STT +
  context in one call), or is context synthesis a separate text-only LLM
  call?

## 4. Product scope

- Is the goal feature parity with iOS Kivi, or "iOS Kivi's backend +
  the existing Windows-native shell" (i.e. keep the engine already built,
  swap Groq → Sarvam)? This determines whether the current Windows engine
  survives largely as-is or gets rebuilt around Sarvam's actual API shape.
