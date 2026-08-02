# EV-2210 — independent Voice 2.2 release gate

- Status: `PROPOSED — requires ticket approval`
- Specification: [`Voice 2.2`](../specs/001-voice-2.2-accuracy-offline-translation.md)
- Depends on: `EV-2201` through `EV-2209`

## Observable result

An independent verifier issues `SHIP` or `NO-SHIP` from fresh raw evidence. A
`SHIP` verdict produces a local versioned 2.2.0 artifact, checksums, release
notes and rollback instructions; it does not imply external publication.

## Scope

- Freeze implementation and review it against the approved specification and
  every ticket, including preservation of the pre-existing dirty candidate.
- Run full unit/property/contract/corpus/performance/privacy/fault/stress and
  clean installer matrices from clean staging.
- Verify real microphone quiet/boundary/mixed/long-form behavior and concurrent
  Voice + Translate ownership on Windows 10/11.
- Reconcile version across project, installer, manifest and docs only after all
  gates pass. Any behavioral fix discovered here returns to a scoped ticket and
  reruns affected gates.
- Produce release note only for an actual RC/final artifact; tag/push/upload and
  signing with real credentials remain separately authorized actions.

## Expected files

- tests and existing verification/release scripts
- `Egoist.Voice.csproj`, `installer/EgoistVoice.iss`
- `README.md`, `CHANGELOG.md`, `docs/releases/2.2.0.md`
- immutable change note, `STATUS.md`, stable maps only if facts changed
- final artifacts/checksums under the established artifact/output location

## Blockers and human input

- Complete private user-voice corpus and clean Windows environments.
- Independent verifier must not have authored EV-2201…EV-2209.
- Public signing/distribution credentials require separate user authorization.

## Verification

- Existing and new tests pass from clean state; corpus meets every hard metric
  in the approved spec with no missing sample.
- Privacy canary absent from logs/crash/installer evidence; no network after
  Full Offline install.
- Stress/fault/resource budgets and complete install→upgrade→dual-owner
  uninstall matrix pass with raw logs and checksums.
- Final diff/version/artifact hashes are independently reviewed; residual risk
  is explicit and no blocker is relabelled warning.

## Done when

Only a complete `SHIP` record permits local 2.2.0 final status. Otherwise
`STATUS.md` remains RC/blocked with the exact failed gate and next ticket.
