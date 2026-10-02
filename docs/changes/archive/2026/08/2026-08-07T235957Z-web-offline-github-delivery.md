# Web/offline GitHub delivery and public repository presentation

- UTC: `2026-08-07T23:59:57Z`
- Scope: `v2.2.0-preview.1` public unsigned field-test delivery and repository presentation.

## What changed and why

The verified Full Offline EXE is `3,191,770,964` bytes, above GitHub's 2 GiB
per-asset limit. Its three embedded Inno files are already individually below
that limit, so the release now exposes those exact bytes rather than recompressing
models or changing the product payload.

A 75 KB `EgoistVoice-Web-Setup-2.2.0.exe` prefers a complete local set for
offline installation. Otherwise it downloads from the immutable
`v2.2.0-preview.1` release, resumes partial files with HTTP Range, restricts
redirects to GitHub HTTPS asset hosts, verifies declared size and SHA-256 and
only then launches the inner installer. Download failure, truncation or hash
mismatch returns `1603` before installation. Successful online installation
removes its versioned cache; failed installation preserves it for retry.

The public README now follows the information architecture of Egoist Account
Manager while retaining Voice's own black/red capsule and faceted-microphone
identity: clear promise, real screenshots, installation paths, privacy,
verification, limitations, rollback and creator-brand support. A `1280x640`
social preview, Windows CI and GitHub funding link were added. `LICENSE` is the
canonical MIT text; third-party model/runtime terms remain in
`THIRD-PARTY-NOTICES.md`.

## Exact payload lineage

- Source Full Offline package SHA-256:
  `eb2b2323ee8f77e234956711ab7b8eaf20fc5c2cd318e6380e87cb59eac14b9c`.
- Embedded manifest SHA-256:
  `8bff178ff1993810e5645a9322c198495885f9162dd54ada312d2fc3c9f96a9c`.
- Inner EXE SHA-256:
  `0fd1706666f1411404799308f70c4bd6f82d9e801c07177f24f8bca49399175a`.
- BIN1 SHA-256:
  `691f5e60b76b7595dc8752a2264bccda04ebb9d9dbc5e1257eb9a300846b2a02`.
- BIN2 SHA-256:
  `6d7cada0e16fcec94899811d30fd386d816945f8185e62a3c20aab05b468c74c`.

## Verification

- Voice Release suite: `480/480` pass.
- Existing one-file footer/manifest and all embedded payload hashes: pass.
- Web bootstrap compilation, offline discovery and exact hash verification: pass.
- Local HTTP fixture: initial download, forced half `.part`, Range resume and
  final SHA-256: pass.
- PowerShell AST, GitHub workflow/FUNDING YAML, README local links and
  `git diff --check`: pass.
- Social preview: rendered and visually inspected at `1280x640`.
- Installer execution on the development host: `not-run` by policy.

## Contracts and durable context

- `CTX-015` records the public delivery composition and explicitly does not
  supersede the verified CTX-014 one-file artifact.
- EV-2209 gains a distribution adapter only; ASR, translation, ownership and
  installed-byte contracts are unchanged.
- User-facing evidence and known limitations live in
  `docs/releases/2.2.0-preview.1.md`.

## Risks and next safe action

The candidate remains unsigned and has no clean Sandbox/Hyper-V
install/repair/upgrade/coexistence/uninstall evidence. `EV-2210` has not issued
SHIP. Complete the guarded GitHub tag/upload transaction, verify public download
hashes, then run EV-2214/EV-2213 against those exact assets before promoting a
stable `2.2.0`.
