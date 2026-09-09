# Voice 2.1.1/2.2 work is recoverable on v2.2-wip

- Date: `2026-08-02T19:29:01Z`
- Type: `maintenance`
- Status: `complete`
- Related: `EV-2200`

## What changed

- Created local branch `v2.2-wip` from `main` at `f1f5997` and committed all
  current candidate/spec/harness work as `b7281fd`.
- Extended `.gitignore` so downloaded GGUF/model payloads and WAV audio cannot
  enter future commits.

## Why

The approved program requires preserving the unreleased candidate before any
new code work while keeping the 2.1.0 mainline unchanged.

## How

Audited the staged set for size and private/model extensions. A pre-existing
zero-byte `.git/index.lock` was removed only after confirming that no Git
process was active; staging then completed normally.

## Verification

| Check | Result | Evidence |
| --- | --- | --- |
| Branch and baseline | `pass` | `v2.2-wip` at `b7281fd`; `main` remains `f1f5997`. |
| Preserved scope | `pass` | 99 changed/new files captured in the checkpoint. |
| Large/private payload gate | `pass` | No staged file over 5 MB and no GGUF/WAV/private-key extension. |
| Product behavior | `not-run` | EV-2200 intentionally changes no product source. |

## Contract impact

- Architecture: `unchanged`; no runtime code changed.
- App map: `unchanged`; no surface changed.
- Roadmap: `unchanged`; EV-2200 was already the first approved phase.
- Specs/tickets: `unchanged`; only their approved action was executed.
- Durable context: `none`; Git evidence is recorded in current status and this note.

## Risks and next action

The work remains unreleased and local. Next: finish EV-2205 formatting and
idempotence without claiming private-corpus accuracy.

## Files

- `.gitignore`
- local branch `v2.2-wip`
- `STATUS.md`
- `docs/changes/2026-08-02T192901Z-ev-2200-repository-safety-net.md`
