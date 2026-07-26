# Running the Electron reference app

How to run the Electron implementation of Kivi (our visual + behavioral source of truth) locally, to eyeball what we're porting to .NET.

> The Electron app lives at `_reference/sarvam-kivi-electron/` and is **immutable** — we never modify it (see `CLAUDE.md` RULE 1). Running it (installing deps, launching) does not change its source.

## Prerequisites (already present on this machine)
- **Node** ≥ 22 and **npm** (verified: Node 22.15, npm 10.9)
- **Python 3** (only needed for the `icons` script, not for running)

## First-time setup

Open a terminal and run **from the Electron folder**:

```powershell
cd C:\Users\AGRIM\Desktop\Agrim\sarvam\Kivi\_reference\sarvam-kivi-electron

# Install dependencies (one-time, ~1–3 min). Zero native modules, so it installs clean.
npm install
```

## Run it (dev mode)

```powershell
npm run dev
```

`electron-vite dev` launches with hot-reload. You'll see:
- the **main window** — the forest-green "kivi" shell
- the **transparent orb overlay** — bottom-center

## See the orb animations (no backend needed) — this is the visual reference

The orb is the primary parity target. **Demo mode** cycles it through its states without any backend:

```powershell
$env:KIVI_ORB_DEMO=1; npm run dev
```

Drive it to a specific frozen pose (useful when matching one screen):

```powershell
$env:KIVI_ORB_DEMO=1; $env:KIVI_ORB_POSE="box_with_tooltip"; npm run dev
```

Known poses: `box_with_tooltip`, `bottom_expanded`, `mist_cancel_hover`.

To clear the env vars afterward in the same terminal:

```powershell
Remove-Item Env:KIVI_ORB_DEMO -ErrorAction SilentlyContinue
Remove-Item Env:KIVI_ORB_POSE -ErrorAction SilentlyContinue
```

## What works with vs. without the backend

Live dictation (hold hotkey → speak → paste) is a **client of `kivi-service`** (`ws://127.0.0.1:8788`). It needs that backend running.

| | Without backend | With `kivi-service` running |
|---|---|---|
| Main window + all pages | ✅ renders | ✅ |
| Orb visuals + animations (demo mode) | ✅ full showcase | ✅ |
| Live dictation (hotkey → speak → paste) | ❌ | ✅ |

For visual reference right now, `KIVI_ORB_DEMO=1` mode is all you need. Live dictation waits until the backend is reachable.

## Other useful scripts

```powershell
npm run build       # production build into out/
npm run start       # electron-vite preview (run the built app)
npm test            # vitest unit + integration tests (some need the local service)
npm run typecheck   # TS typecheck
```

## Troubleshooting

- **Orb shows a black box instead of transparency:** a known Windows-compositor quirk (the app was primarily verified on macOS). The animations still play; the transparency issue is a rendering artifact on Windows, not a bug you introduced. This is exactly the kind of thing our .NET layered/Composition overlay is meant to do correctly.
- **`npm install` warns about optional/native deps:** the core app declares `npmRebuild:false` and has no native modules, so it should install clean. If it hard-fails, capture the error.
- **Nothing appears:** make sure you're in `_reference/sarvam-kivi-electron/` (not the repo root) when running `npm run dev`.

## Reference docs inside the Electron app (read, don't edit)
- `_reference/sarvam-kivi-electron/docs/GOAL.md` — mission
- `_reference/sarvam-kivi-electron/docs/MASTER-PLAN.md` — authoritative architecture + M0–M9 roadmap
- `_reference/sarvam-kivi-electron/docs/FEATURE-PARITY.md` — 131-feature matrix + P0 shortlist
- `_reference/sarvam-kivi-electron/docs/PROGRESS.md` — what's already built in Electron
- `_reference/sarvam-kivi-electron/docs/maps/` — the 12 architecture/behavior maps
