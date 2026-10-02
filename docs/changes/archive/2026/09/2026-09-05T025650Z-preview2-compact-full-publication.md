# Preview 2: Compact RU and Full + Qwen

Date: 2026-09-05T02:56:50.837059+00:00

## Change and reason

Prepared the requested public GitHub preview of the current EV-2224 application in
an isolated checkout. Added a Compact installer and portable ZIP; rebuilt Full with
the exact installed application, Qwen and the pinned shared translation runtime.
Full offers a small SHA-256 checking EXE and a three-part offline archive with licenses.
README, installation/build/user guides and 11 actual UI screenshots describe both editions.

## Verification

634/634 Release tests passed. All 129 source files match EV-2224. Compact's exact
installer passed clean Sandbox installation, native ASR, AAC recovery, repair and
uninstall preserving Data. All 506 portable entries and 22 full archive files match
their manifests. Full bootstrap verification and HTTP resume/hash fixture passed.
Release checksums and source commit are recorded in the published release manifest.

## Contracts, risks and next step

Full preserves app data and model caches on uninstall and respects other Engine owners.
Preview is unsigned. The interrupted Full Sandbox lifecycle was not completed; the
owner explicitly requested publication without another Sandbox run. This does not
promote the whole product to stable or prove acoustic quality gains. Next: collect
field reports and finish Full/device/coexistence gates before stable delivery.

Durable context: accepted two-edition packaging and explicit preview test scope are
owned by STATUS.md, docs/INSTALL.md and docs/releases/2.2.0-preview.2.md. No new candidates.
