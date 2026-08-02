# Voice 2.2 continuation handoff

- Date: `2026-08-02T16:11:58Z`
- Type: `docs`
- Status: `complete` for handoff; product implementation remains `partial`
- Related: [`Voice 2.2 spec`](../specs/001-voice-2.2-accuracy-offline-translation.md), [`EV-2205`](../tickets/EV-2205-commands-and-formatting.md)
- Paired project: [`EGOIST Translator handoff`](../../../egoist-translator/docs/changes/2026-08-02T161158Z-resume-handoff.md)

## Start here in a new chat

1. Work from `projects/egoist-voice/`; read `AGENTS.md`,
   `project.profile.json`, `STATUS.md`, this note, then the active EV-2205
   ticket. Do not preload another project's status.
2. Preserve the dirty working tree. Git `main` is still `f1f5997`; published
   and source version remain `2.1.0`. The accumulated 2.1.1/2.2 candidate is
   neither committed nor released.
3. Continue EV-2205 at the protected-normalizer/formatter step below. Do not
   restart ASR research or replace the approved models.
4. Before EV-2206, read the paired Translator handoff and resolve the framework
   contract mismatch through a compatible contract/client build, not by copying
   its current `net10.0` DLLs into Voice.

## User-approved product direction

- Keep GigaAM v3 as the Russian primary recognizer and the existing Whisper
  Large v3 Turbo path as a conditional mixed-language fallback. No alternative
  ASR download/model migration belongs to Voice 2.2.
- Improve quiet speech, soft/sibilant consonants, first/last phonemes, Russian
  punctuation and English proper names through capture, deterministic context,
  exact repair and corpus evidence—not semantic rewriting.
- Add local translation through the shared EGOIST Translation Engine. The
  Translator lane currently pins Hy-MT2-1.8B Q8_0 as safe default and Q6_K as a
  benchmark candidate; no multi-GB acquisition was authorized or performed.
- Output should read like careful human text, but code, URLs, placeholders,
  Markdown and proper names must remain literal and reviewable.

## Work completed before EV-2205

### EV-2201 — corpus and baseline harness

- Added the versioned 350-line private corpus script, JSON schemas, benchmark
  runner, scorer, privacy checks and deterministic report contracts.
- The harness is complete; the user's 350 WAV recordings and reference/baseline
  artifacts do not exist. This blocks measured accuracy claims, not safe
  deterministic implementation.

### EV-2202 — boundary-safe quiet capture

- Replaced per-press MME/WAV capture with warm WASAPI shared mode, bounded
  200 ms pre-roll and 350 ms release tail.
- Normal dictation remains in memory, is converted once to 16 kHz mono, and the
  same samples reach GigaAM and conditional Whisper.
- Added adaptive quiet-speech/low-energy consonant gates, cancellation/session
  isolation and 300-cycle resource coverage. A real default-device smoke
  returned 12,960 in-memory samples; user-voice, device-removal and 30-minute
  endurance gates remain pending.

### EV-2203 — contextual-bias candidate

- Pinned the optional official GigaAM tokenizer and generated deterministic
  Sherpa BPE/hotword resources for a 403-phrase catalogue at score `1.15`.
- Baseline and candidate recognizers both initialize. Production remains
  baseline under [`Decision 001`](../decisions/001-gigaam-contextual-bias-hold.md)
  until identical private WAVs pass WER/entity/latency gates.

### EV-2204 — English proper names in Russian dictation

- Added versioned AI/dev/company/app/game terminology, technology/gaming
  profiles, exact split/join aliases, canonical Latin casing and ordinary-word
  blockers. User dictionary rules still apply last.
- Normal UI and corpus paths share the same profile policy. Windows TTS smoke
  produced 6/6 target entities and preserved 4/4 negative ordinary-language
  controls. This synthetic evidence does not replace private user speech.

## EV-2205 work completed in the current uncommitted slice

- `TranslateDirective` is now typed: payload, `TranslationDirection`,
  `TranslateCommandPosition` and `TranslateCommandMatchClass`; the compatibility
  `TargetLanguage` property remains for the current caller.
- Prefix commands without an explicit language require an explicit punctuation
  boundary (`Переведи: …`). Suffix commands require a completed clause boundary.
  This prevents `переведи стрелки/деньги/курсор/разговор` from invoking MT.
- Explicit-language forms, filler sequences, `то/его/её/их`, prefix/suffix
  positions and terminal sentence punctuation are handled without dropping
  payload nouns such as `сообщение`.
- Conservative mention guards preserve examples, labels and quoted command
  descriptions as ordinary text.
- `tests/corpus/script.jsonl` is now a locked executable command gate:
  40/40 positive commands detected and 80/80 adversarial negatives rejected.
  Focused parser tests pass `82/82`; the full Voice suite passes `466/466`.

## EV-2205 work still required

1. Protect fenced/inline code, URLs, Markdown-sensitive spans and placeholders
   before `TranscriptNormalizer` changes whitespace, casing or punctuation;
   restore them byte-for-byte afterward.
2. Make normalization idempotent and conservatively collapse only duplicate
   punctuation outside protected spans. Add tests proving a second pass is
   identical and literal tokens are unchanged.
3. Keep `VoiceCommandProcessor` exact and segment-boundary based. Add useful
   aliases only with paired positive/negative fixtures; never fuzzy-match
   ordinary phrases such as `точка опоры`.
4. Fix `TranscriptFormatter`: the current `PreferredParagraphCharacters`
   branch may insert a paragraph without a confirmed pause. A paragraph must
   require both a confirmed pause and an already completed sentence, while a
   short phrase with a long pause remains unsplit under the approved threshold.
5. Apply the same paragraph rule in `TranscriptChunkJoiner`; today
   `ParagraphBreakBefore` inserts a break without checking prior sentence
   punctuation/paragraph length.
6. Add long-text, no-pause, no-terminal-punctuation, protected-token and
   idempotence tests. Run the full Release suite and checkpoint EV-2205 before
   activating EV-2206.

## Work after EV-2205

1. `EV-2206`: consume the shared named-pipe contract/client. Current Translator
   assemblies target `net10.0`; Voice targets `net8.0-windows`. Retarget or
   multi-target the narrow Contracts/Client projects and run cross-client
   conformance before referencing them.
2. `EV-2207`: honest translation states and recovery UX—missing pack, loading,
   ready, ambiguous language, timeout, cancellation and integrity failure.
3. `EV-2208`: measure ASR/MT RAM, cold/warm latency, contention and unload
   behavior; do not guess resource budgets.
4. `EV-2209`: build the Full Offline owner-safe installer only after the shared
   pack/install contract exists.
5. `EV-2210`: private corpus, performance/stress, clean Windows 10/11 lifecycle
   and independent outcome review. Only a passing gate can promote 2.2.0.

## Verification

| Check | Result | Evidence |
| --- | --- | --- |
| Full Voice Release test suite | pass | `dotnet test .\tests\Egoist.Voice.Tests\Egoist.Voice.Tests.csproj -c Release --no-restore --nologo`: `466/466`. |
| Focused command parser | pass | `82/82`, including the source JSONL gate. |
| Locked command corpus | pass | 40 positives detected; 80 negatives rejected. |
| Working-tree whitespace | pass | `git diff --check`; LF→CRLF notices only. |
| Private 350-WAV corpus | blocked-human | 0 WAV/reference/baseline artifacts. |
| Real quiet/entity accuracy | not verified | Requires the user's recordings. |
| Translation integration | not run | Framework-compatible shared client and installed model pack are absent. |
| Packaging/release | not run | No version, commit, tag, installer promotion, signing or publication. |

## Contract impact

- Architecture: unchanged; approved GigaAM/Whisper and shared-host boundaries
  remain authoritative.
- App map: unchanged; parser work stays in the existing post-processing path.
- Roadmap: updated to move EV-2202–EV-2204 to deterministic-complete and name
  the remaining EV-2205/framework work.
- Specs/tickets: EV-2205 status updated; acceptance criteria unchanged.

## Durable context

- Candidates: none.
- Promoted: `CTX-001`–`CTX-006` in `docs/CONTEXT.md` from approved specs,
  decisions, source and fresh tests.
- Superseded: none.

## Risks and do-not-do list

- Do not replace GigaAM/Whisper, enable contextual bias by default, claim higher
  accuracy from synthetic TTS, or skip the paired private corpus.
- Do not download Hy-MT2/llama.cpp, publish, sign, tag, commit or overwrite the
  dirty candidate without the corresponding explicit authority.
- Do not use the legacy fixed port `47821` as a trusted translation identity.
- Do not log recognized text, audio, translation payloads, user identity or
  private paths in diagnostic evidence.

## Files

- Added continuity routing: `project.profile.json`, `docs/CONTEXT.md`.
- Updated current routing: `AGENTS.md`, `STATUS.md`, `docs/ROADMAP.md`,
  `docs/tickets/EV-2205-commands-and-formatting.md`.
- Current EV-2205 implementation: `Core/TranslateCommandParser.cs`,
  `tests/Egoist.Voice.Tests/TranslateCommandParserTests.cs`.
- This immutable handoff: `docs/changes/2026-08-02T161158Z-resume-handoff.md`.
