# EV-2205 — precise commands and human-like formatting

- Status: `APPROVED — command parser gate complete; formatting implementation active`
- Specification: [`Voice 2.2`](../specs/001-voice-2.2-accuracy-offline-translation.md)
- Depends on: `EV-2202`, `EV-2204`

## Observable result

Strict prefix/suffix translation commands execute reliably, content that merely
mentions «переведи» remains text, punctuation stays conservative, and long
dictation gains paragraphs only at explicit commands or confirmed pauses.

## Scope

- Introduce one typed parse result for translation intent: payload, direction,
  command position and match class; keep supported language aliases bounded.
- Separate command parsing from final typography and preserve literal payload
  punctuation/code/URLs.
- Make voice punctuation/newline/paragraph commands work at strict utterance or
  clause boundaries without substring side effects.
- Feed EV-2202 pause metadata into the formatter; add paragraph breaks only at
  approved sentence boundaries. No semantic rewriting or guessed lists.
- Normalize casing, whitespace and duplicate punctuation with idempotent rules.

## Expected files

- `Core/TranslateCommandParser.cs`, `Core/VoiceCommandProcessor.cs`
- `Core/TranscriptPostProcessor.cs`, `Core/TranscriptFormatter.cs`
- `Core/TranscriptNormalizer.cs`, `Services/Contracts.cs`
- relevant transcription/coordinator glue only for pause metadata
- parser/post-processing/formatter tests under `tests/Egoist.Voice.Tests/`

## Blockers and human input

- Stable transcript and pause contracts from EV-2202/EV-2204.
- Any new grammar form needs paired positive and adversarial negative fixtures.

## Verification

- Locked translation-command set: recall ≥98%, precision 100% on negatives.
- Formatter is idempotent, preserves content tokens and does not alter
  Markdown/code/URL/placeholders.
- Long-form punctuation metrics and manual correction count are not worse than
  baseline; paragraph placement passes the five recorded long-form cases.
- Existing parser/post-processing regression suite remains green.

## Done when

Normal dictation, voice punctuation and translation intent all pass through one
deterministic documented order and produce a reviewable final string.
