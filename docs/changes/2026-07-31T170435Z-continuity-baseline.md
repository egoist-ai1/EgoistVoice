# Project-local continuity baseline

- Date: `2026-07-31T17:04:35Z`
- Type: `maintenance`
- Status: `complete`
- Related: `none`

## What changed

- Added a concise project-local `STATUS.md`, self-contained agent entry points,
  canonical stable-map paths, lazy specs/tickets/decisions indexes and the
  append-only change/release structure.
- Linked the project README to its own status; no workspace/root status was
  created and no unrelated project status is required for entry.
- Added the deterministic history manager with a ten-note hot set and verbatim
  archive movement.

## Why

A new Codex, Claude or other coding-agent session must be able to resume this
project from files after chat loss without preloading global or unrelated
project history.

## How

The baseline was reconstructed from this project's README, manifests, source,
existing specs/progress/handoff documents and Git metadata where available.
Unknown or stale facts remain explicit; product behavior and generated release
artifacts were not changed.

## Verification

| Check | Result | Evidence |
| --- | --- | --- |
| Project continuity audit | pass | `doctor.ps1 -ProjectAudit -ProjectPath projects/egoist-voice -Strict` |
| Product build/tests | not-run | Documentation/continuity migration only; prior product evidence remains linked from `STATUS.md`. |

## Contract impact

- Architecture: updated — indexed the WPF/audio/local-ASR/installer boundaries from current sources and handoff.
- App map: updated — added a source-linked operational map.
- Roadmap: updated — recorded current, next and later outcomes without copying history.
- Specs/tickets: updated — added indexes pointing at existing sources; no requirements were re-approved.

## Risks and next action

- Historical product verification was not rerun; use the dated evidence and
  blockers in `STATUS.md` before product or release work.
- Continue with the single `Next safe action` in `STATUS.md`.

## Files

- `README.md`
- `AGENTS.md`
- `CLAUDE.md`
- `STATUS.md`
- `docs/ARCHITECTURE.md`
- `docs/APP_MAP.md`
- `docs/ROADMAP.md`
- `docs/specs/README.md`
- `docs/tickets/README.md`
- `docs/decisions/README.md`
- `docs/changes/`
- `docs/releases/README.md`
- `scripts/manage-project-history.ps1`
