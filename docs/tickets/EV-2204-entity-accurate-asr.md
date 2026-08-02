# EV-2204 — integrate entity-accurate RU/EN recognition

- Status: `IMPLEMENTED — private corpus accuracy gate pending`
- Specification: [`Voice 2.2`](../specs/001-voice-2.2-accuracy-offline-translation.md)
- Depends on: `EV-2203`

## Observable result

Dictation writes canonical `Claude Code`, `Anthropic` and the approved
AI/dev/game/company catalog while preserving ordinary Russian words and already
correct English. The measured EV-2203 ASR strategy is the only production path.

## Scope

- Integrate the EV-2203 winner behind existing transcription abstractions; if
  baseline won, retain it and change only measured deterministic stages.
- Expand/version the built-in catalog and technical prompt with corpus-backed
  spoken variants, including safe whitespace/hyphen split aliases.
- Add bounded catalog-backed split repair with whole-token/context rules.
  Arbitrary edit-distance correction and unaligned ASR span splicing are out.
- Make candidate scoring entity-aware without allowing one recognized brand to
  replace an otherwise substantially worse transcript.
- Preserve user dictionary precedence, timeout-safe regex and lazy fallback
  loading/unloading.

## Expected files

- `Services/HybridTranscriptionService.cs`
- selected `GigaAmTranscriptionService.cs` / `WhisperTranscriptionService.cs`
- `Services/ModelManager.cs`, `Core/ModelCatalog.cs`
- `Core/BuiltInVocabulary.cs`, `Core/UserDictionary.cs`
- `Core/TechnicalTermCatalog.cs`, `Core/MixedSpeechDetector.cs`
- `Core/RecognitionScorer.cs`
- vocabulary/mixed/hybrid/model tests under `tests/Egoist.Voice.Tests/`

## Blockers and human input

- EV-2203 decision and pinned production-compatible artifact.
- New catalog entries require positive utterances and negative controls; an
  untested popularity list is not accepted.

## Implementation evidence

- Versioned catalogue v2 covers AI/dev, collaboration/apps, large companies,
  Windows/platforms, gaming/studios and creative tools. Exact split/join and
  observed phonetic aliases are whole-token bounded; no fuzzy correction runs.
- Ambiguous `клауд/Cloud Code`, `кодекс`, `курсор`, `мета` and `стим` require a
  local target/utterance profile and retain explicit ordinary-language negative
  controls. User aliases compile last and still override shipped rules.
- The normal window path and corpus runner use the same profile → dictionary →
  command → normalization order. Benchmark reports pin catalogue/policy version.
- Candidate scoring derives canonical evidence from the catalogue but still
  rejects a brand-only Whisper result when transcript length is not comparable.
- Fresh Release suite passes `458/458`. A repeated local Windows-TTS integration
  WAV produced exact `Anthropic`, `Claude Code`, `DeepSeek`, `GitHub`,
  `Android Studio`, `Counter-Strike` (6/6); a separate WAV preserved four
  ordinary-language negative controls (4/4). These are integration smokes, not
  substitutes for the user's 60-utterance entity corpus.

## Verification

- Covered entity exact accuracy ≥90%, 0 false replacements across locked
  negative controls and at least 50% fewer tagged split-word errors.
- Pure-RU WER regression ≤0.5 pp and latency ≤1.15× baseline.
- Unit/property tests cover casing, inflections, whitespace/hyphens, Unicode,
  already-correct English, user overrides and ordinary-word collisions.
- Missing/corrupt optional fallback returns the measured safe behavior without
  crashing or sending text elsewhere.

## Done when

The deterministic normal-path implementation and contract tests are complete.
Final acceptance remains blocked until the private entity/split corpus proves
≥90% exact accuracy, zero negative-control replacements and ≥50% fewer tagged
split errors on the same baseline recordings.
