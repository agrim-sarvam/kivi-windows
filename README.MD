# Migration scaffolding — drop into the new repo

This folder holds the Claude scaffolding for the Electron → .NET/Windows migration. It is staged here so you can move it into the freshly-branched, cleaned repo.

## What's here
- `CLAUDE.md` — the repo-root instructions every session reads. Encodes the two hard rules (translate every macOS/Linux/Electron primitive to Windows/.NET; UI pixel-perfect from Electron rebuilt in .NET), the source-of-truth rule, the four divergence tripwires, the parity constants, the mapping table, the UI-fidelity gate, and workflow discipline.
- `.claude/agents/` — five discipline agents:
  - `docs-porter.md` — Electron docs/maps → Windows-only .NET docs (runs first).
  - `core-porter.md` — pure logic (engine, frame, transcript, tokens, mask, models) → C#.
  - `wire-backend.md` — STT `ClientWebSocket` client, REST, auth/JWT, budgets (byte-exact).
  - `platform-native.md` — Windows seams from scratch (hotkey, paste, frontmost, overlay, WASAPI, secrets, tray).
  - `ui-fidelity.md` — XAML/Composition views + the side-by-side pixel/motion gate.

## How to deploy
From the new repo root (after you've created the branch and cleaned the tree, with `_reference/sarvam-kivi-electron` present):

```
# copy CLAUDE.md and the agents into place at the repo root
cp migration-scaffolding/CLAUDE.md ./CLAUDE.md
cp -r migration-scaffolding/.claude/agents ./.claude/agents
```

Then delete `migration-scaffolding/`. Adjust the assumed paths in `CLAUDE.md` if your layout differs (it assumes `_reference/sarvam-kivi-electron/`, our `docs/`, the `Kivi.*` solution, and an optional read-only `legacy/`).

## Assumptions baked into the files
- The Electron reference is at `_reference/sarvam-kivi-electron/` and is immutable.
- Our ported docs go under `docs/` (`architecture/`, `maps/`, `parity/`, `roadmap/`).
- Agents are `.claude/agents/*.md` (Claude Code convention). If you use SDK agent definitions instead, move the frontmatter+body accordingly.
- The design rationale behind all of this is in `docs/superpowers/specs/2026-07-26-electron-to-dotnet-migration-scaffolding-design.md`.
