# Voice Full Offline field candidate

- UTC: `2026-08-07T21:58:32Z`
- Scope: EV-2209 local package; EV-2213 source readiness; focused EV-2205 repair

## What changed and why

Voice now consumes the exact Translator-produced Engine bundle instead of
requiring the user to install translation separately. Setup verifies/installs
the Host, exact Hy-MT2 Q8_0 and llama b10219 Vulkan pack, then writes only the
Voice owner. Uninstall removes only that owner and preserves the shared tree
while another owner remains.

The complete Voice + GigaAM + Whisper + CUDA/Vulkan/CPU + MT payload exceeds
Inno Setup's 2.1 GB single-file ceiling. Native disk spanning retains every
component and emits one launch EXE with two required adjacent BIN slices. The
receipt and checksum sidecar bind all three files.

Focused post-processing repairs canonicalize observed variants to `Egoist
Voice` and `EGOIST Translator`, interpret strict inline punctuation, and avoid
changing dotted identifiers such as `Vue.js`, `example.com` and `config.json`.

## Verification

- Voice Release tests: `478/478` pass.
- Package total: `3,191,699,773` bytes.
- Launch EXE SHA-256:
  `35cff1f5604780cf805bbdc32108b243198a4a09ffeaf71fb1a3b3c72e013312`.
- BIN1 SHA-256:
  `a4c71fe2438d544c8abddc3a2861daf49dbd3df52e073e184e86c72e71392eaa`.
- BIN2 SHA-256:
  `1b6d4d2c747dc5db6c3d12c3353e16fd57841bcc7aa7445c046001460a0ac47e`.
- Receipt/package verification and independent artifact review: `pass` for
  unsigned local field testing.
- Installer execution on the development host: `not-run` by policy.

## Contracts and durable context

- EV-2209 is locally implemented at source/package level; CTX-013 records the
  durable package fact.
- EV-2213 owner-safe source/fixture work is active, but C-02/C-03 and exact
  Sandbox/Hyper-V rows remain pending EV-2214.
- No public release, signing, tag, push or upload occurred.

## Risk and next safe action

The user must keep EXE, BIN1 and BIN2 together and launch only the EXE. The
exact clean install/start/repair/coexistence/uninstall behavior has not yet been
observed on guest Windows; EV-2214/EV-2213 own that evidence before SHIP.
