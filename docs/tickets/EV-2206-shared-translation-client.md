# EV-2206 — replace fixed-port translation with the shared host client

- Status: `IMPLEMENTED LOCALLY — independent pass; installed/coexistence VM gate pending`
- Specification: [`Voice 2.2`](../specs/001-voice-2.2-accuracy-offline-translation.md)
- Depends on: `EV-2205`; Translator `T002` Contracts artifact and `T008` real
  Engine Host translation/conformance artifact

## Observable result

An explicit spoken translation command uses the approved per-user Engine Host
over a current-user named pipe. Voice never sends text to an arbitrary process
that merely owns port 47821, and it returns typed recoverable failures.

## Scope

- Consume the pinned `Egoist.Translation.Contracts` assembly plus canonical
  protocol-v1 schema/manifest from the Translator artifact; keep Voice build
  independent of a sibling source-tree reference or floating package feed.
- Implement async `Handshake`, `GetStatus`, `Translate` and explicit
  `Cancel(targetRequestId)` with bounded length-prefixed JSON, deadlines, frame
  limits and the canonical idempotent cancellation dispositions.
- Verify protocol version, pipe ownership, host/model/runtime identity and
  capabilities before source text is framed.
- Map Voice requests to `Interactive`, explicit RU/EN direction and the approved
  profile/format; remove direct `/health`/chat completion trust and sidecar
  ownership from Voice.
- Never log source/result, pipe secret or model response excerpts.

## Expected files

- `Services/TranslatorClient.cs`
- likely new internal protocol/pipe transport files under `Services/`
- `Egoist.Voice.csproj` plus pinned contract assembly/schema/manifest under a
  project-local verified artifact path
- `MainWindow.xaml.cs`, `Services/AppLog.cs`
- `tests/Egoist.Voice.Tests/TranslatorClientTests.cs`
- `tests/Egoist.Voice.Tests/ReleaseContractTests.cs`

## Blockers and human input

- Pinned Contracts/schema from Translator T002 and a versioned real-host
  artifact from T008. A fake server alone is not release evidence.
- No fallback to fixed unauthenticated TCP is allowed.

## Verification

- Contract tests cover every typed error, minor/major compatibility, max frame,
  timeout, cancellation, disconnect and malformed response.
- Integration uses real host plus hostile wrong pipe/old port/wrong model cases;
  canary source/result are absent from all logs.
- Concurrent Translator + Voice requests share one host/model; closing Voice
  does not terminate another client's request.
- On translation failure Voice does not silently insert untranslated payload.

## Done when

The normal spoken-command path translates through the verified host and all
legacy fixed-port/source-text logging paths are unreachable.

## Local checkpoint — 2026-08-06

- Voice stays on `net8.0-windows` and consumes exact project-local
  Contracts/Client `1.0.0` DLLs under a hash-bound manifest.
- The current-user named-pipe client starts an installed shared Host but never
  owns/kills it. Port `47821`, `/health` and chat-completions are absent from
  reachable Voice translation source.
- Typed failures return before delivery; untranslated payload is never inserted
  as a successful translation. The offline language tier is bounded before
  framing.
- Release build `0/0`, tests `466/466`, changed-file format pass, independent
  review `pass`.
- The Voice installer registers/removes only `egoist-voice.owner.json`; actual
  install/uninstall coexistence remains the EV-2213/EV-2214 VM gate.
