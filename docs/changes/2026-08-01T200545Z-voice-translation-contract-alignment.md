# Aligned the proposed Voice client with Translator tickets

- Date: `2026-08-01T20:05:45Z`
- Type: `docs`
- Status: `complete`
- Related: [`2026-08-01T195245Z-voice-22-ticket-breakdown.md`](./2026-08-01T195245Z-voice-22-ticket-breakdown.md)

## What changed

- Clarified the approved Voice specification and EV-2206 wire contract to use
  explicit idempotent `Cancel(targetRequestId)`.
- Made Voice consume the pinned `Egoist.Translation.Contracts` assembly plus
  canonical schema/manifest produced by Translator T002, without referencing a
  sibling source tree or floating package feed.
- Bound EV-2206 to Translator T002/T008 and EV-2209 to the owner-safe T010 pack.
- Added an unambiguous second approval statement that authorizes only scoped
  local ticket implementation and keeps downloads/deployment/publishing gated.

## Why

The Translator breakdown was completed after the initial Voice ticket index.
Naming exact artifact-producing dependencies before approval removes the last
opportunity for the two products to implement similar but incompatible local
protocols or lifecycle ownership.

## How

The change is an additive wire-level clarification of already approved
correlated cancellation and shared-host semantics. It does not change product
scope, runtime behavior or the current version. Immediately before this note,
`STATUS.md` and `docs/changes/INDEX.md` were re-read; neither had advanced.

## Verification

| Check | Result | Evidence |
| --- | --- | --- |
| Cross-project operation name | pass | Voice spec and EV-2206 contain explicit `Cancel(targetRequestId)` matching Translator T002/T008. |
| Artifact dependency | pass | Voice pins Contracts/schema and names T002/T008/T010; no sibling source reference is proposed. |
| Local link audit | pass | 0 broken links across Voice spec and ticket set after alignment. |
| Product mutation | pass | Documentation/continuity only; no source, model, installer, version or release artifact changed. |

## Contract impact

- Architecture: unchanged in current product; future IPC detail clarified.
- App map: unchanged.
- Roadmap: unchanged.
- Specs/tickets: updated — exact shared contract/artifact dependencies.

## Risks and next action

- Both breakdowns still require user approval before local product-code work.
- Candidate downloads remain separately cost-gated.
- Next safe action: obtain the combined second approval, then begin only the
  first ticket in each authorized stream.

## Files

- `STATUS.md`
- `docs/specs/001-voice-2.2-accuracy-offline-translation.md`
- `docs/tickets/README.md`
- `docs/tickets/EV-2206-shared-translation-client.md`
- `docs/tickets/EV-2209-full-offline-packaging.md`
- `docs/changes/2026-08-01T200545Z-voice-translation-contract-alignment.md`
