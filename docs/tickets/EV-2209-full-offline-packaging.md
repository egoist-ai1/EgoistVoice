# EV-2209 — package Voice 2.2 Full Offline

- Status: `ACTIVE — local source/package gates pass; clean-VM lifecycle pending EV-2214/EV-2213`
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

## Local acceptance evidence

- Voice consumes the exact Translator-produced Engine 1.0.0 bundle manifest,
  rejects missing/unsafe/undeclared/hash-mismatched files, and installs Host ->
  exact Q8/runtime pack -> `egoist-voice` owner.
- Inno's 2.1 GB ceiling is handled internally with native disk spanning, then
  a versioned .NET Framework bootstrap embeds the inner EXE and every BIN
  slice into one outer EXE. No ASR model or CUDA/Vulkan/CPU payload was
  removed. Footer, manifest and every embedded segment are SHA-256 verified
  before launch; receipt schema 3 binds their bytes, offsets and hashes.
- Final one-file package: `3,191,770,964` bytes, SHA-256
  `eb2b2323ee8f77e234956711ab7b8eaf20fc5c2cd318e6380e87cb59eac14b9c`.
  Voice tests: `479/479` pass. Outer PE identity is `2.2.0.0` / `2.2.0`.
  Independent source and artifact reviews returned `pass` for unsigned local
  field testing.
- Installers were not executed on the development host. Clean Windows 10/11,
  offline start and ownership matrix remain explicitly pending EV-2214/EV-2213.
