# EV-2209 — package Voice 2.2 Full Offline

- Status: `PROPOSED — requires ticket approval`
- Specification: [`Voice 2.2`](../specs/001-voice-2.2-accuracy-offline-translation.md)
- Depends on: `EV-2207`, `EV-2208`; pinned Translator `T010` owner-safe
  engine/model pack

## Observable result

One local Full Offline installer places Voice, ASR artifacts and the verified
shared translation engine/model pack on a clean machine. Dictation and
translation work with networking disabled, and uninstalling either product
cannot break the other owner.

## Scope

- Consume an exact Translator-produced engine/model artifact by manifest and
  SHA-256; do not build from a sibling working tree or download `latest`.
- Install side-by-side shared versions, atomic current pointer and per-product
  owner marker under the approved per-user location.
- Support clean install, repair, upgrade from supported Voice/Translator
  versions, rollback and owner-safe uninstall.
- Report exact installed/download size and free-space requirement. Full Offline
  is expected to exceed 3 GB; artifact output determines the final number.
- Preserve settings, user dictionary and shared model on app upgrade; final
  engine cleanup is explicit and refuses while an owner remains.

## Expected files

- `Egoist.Voice.csproj`, `installer/EgoistVoice.iss`
- `scripts/build-installer.ps1`, `scripts/verify-staging.ps1`
- `scripts/test-installer.ps1`, `scripts/full-release-smoke.ps1`
- pinned engine/model manifest and notices in installer/staging inputs
- `ReleaseContractTests.cs`, README/changelog only for verified behavior

## Blockers and human input

- Pinned host/model pack from Translator and enough approved network/disk space.
- Public signing/publishing remains separate; private unsigned status is
  reported honestly if no certificate is available.

## Verification

- Reproducible staging hashes and offline install/start on clean Windows 10/11.
- Voice + Translator concurrent translation, upgrade, uninstall first owner,
  second owner still works, final explicit engine uninstall leaves no process.
- Corrupt/partial pack, low disk, interrupted repair and rollback fail safely.
- Network-denied trace proves normal post-install ASR/translation makes no
  external request.

## Done when

A version-candidate Full Offline installer passes the complete ownership and
offline lifecycle without publishing or declaring 2.2.0 final.
