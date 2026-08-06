# Proposed the Voice 2.2 tracer-bullet breakdown

- Date: `2026-08-01T19:52:45Z`
- Type: `docs`
- Status: `complete`
- Related: [`docs/tickets/README.md`](../tickets/README.md)

## What changed

- Recorded the user's explicit approval of the Voice 2.2 specification.
- Split the approved scope into ten dependency-ordered proposed tickets from
  private corpus/baseline through capture, ASR/entity accuracy, commands,
  shared translation, UX/resources, packaging and independent release gate.
- Required every ticket to declare one observable result, expected files,
  dependencies/human input, verification and a hard done condition.
- Updated the specification/ticket indexes, roadmap and current status without
  changing product source, model state, installer, version or release artifact.

## Why

Capture, ASR model choice, translation-host integration and release packaging
have different evidence and failure boundaries. Separate context-sized tickets
prevent an unmeasured model or failed installer gate from being hidden inside a
large rewrite and make each change independently reviewable.

## How

- EV-2201 freezes the current candidate before product changes.
- Decision tickets choose ASR/models from locked evidence; integration tickets
  consume those decisions and cannot silently substitute another engine.
- The shared-host client is blocked on a real Translator conformance artifact,
  while Full Offline packaging is blocked on a pinned engine/model pack.
- Immediately before this checkpoint, `STATUS.md` and
  `docs/changes/INDEX.md` were re-read; neither had advanced.

## Verification

| Check | Result | Evidence |
| --- | --- | --- |
| Ticket count and order | pass | 10 proposed tickets indexed EV-2201 through EV-2210 with explicit dependencies. |
| Required ticket fields | pass | 0 missing Observable result, Scope, Expected files, Blockers/human input, Verification or Done when sections. |
| Local link audit | pass | 0 broken local links across the approved spec, roadmap, indexes and all tickets. |
| Implementation gate | pass | Every ticket is `PROPOSED`; no active implementation ticket exists. |
| Product/release mutation | pass | Documentation/continuity only; no product code, model/download, version, installer or release artifact changed. |

## Contract impact

- Architecture: unchanged in product; approved future architecture remains in the specification.
- App map: unchanged; current 2.1.x executable flow remains the baseline.
- Roadmap: updated — exact EV-2201…EV-2210 dependency sequence.
- Specs/tickets: updated — specification approved and full proposed breakdown added.

## Risks and next action

- Tickets still need explicit user approval before EV-2201 implementation.
- Private corpus remains absent and candidate models remain undownloaded.
- Next safe action: obtain approval or corrections for the entire ticket order;
  then implement only EV-2201 in a clean context.

## Files

- `STATUS.md`
- `docs/ROADMAP.md`
- `docs/specs/README.md`
- `docs/specs/001-voice-2.2-accuracy-offline-translation.md`
- `docs/tickets/README.md`
- `docs/tickets/EV-2201-corpus-baseline.md` through `EV-2210-release-gate.md`
- `docs/changes/2026-08-01T195245Z-voice-22-ticket-breakdown.md`
