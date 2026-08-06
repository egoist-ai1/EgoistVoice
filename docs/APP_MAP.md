# Egoist Voice — application map

## Actors and flows

- Warm WASAPI pre-roll → user presses configured trigger → in-memory session +
  release tail → one 16 kHz mono conversion → local GigaAM/conditional Whisper
  ASR → target/utterance entity profile → deterministic canonicalization →
  normalization → safe clipboard/keystroke delivery.
- Esc cancels; rejected/quiet/short audio receives explicit state; sensitive password targets suppress copy/insert.
- Tray settings control triggers, sound and dictionaries; an explicit voice
  translation command routes text through the pinned current-user shared Engine
  client and fails closed before delivery on any translation error.

## Implementation map

- `Services/AudioCaptureService.cs` owns warm WASAPI, bounded pre-roll/session,
  release tail, level measurement, adaptive noise calibration and the explicit
  corpus-only WAV boundary. Root WPF files and the remaining `Services/`,
  `Core/` paths own UI, hooks, inference and delivery.
- `Core/BuiltInVocabulary.cs` owns canonical names and exact observed aliases;
  `Services/EntityProfilePolicy.cs` enables only justified technology/gaming
  ambiguities. `UserDictionary` preserves user-last precedence and regex limits.
- `Services/TranslatorClient.cs` owns the pinned named-pipe adapter, shared Host
  discovery/start without ownership, target-tier mapping and typed failures.
- `installer/EgoistVoice.iss` owns only `egoist-voice.owner.json`; shared Host,
  model and other product owners are outside Voice uninstall scope.
- `tests/Egoist.Voice.Tests/` owns source contracts; `tests/corpus/` describes private benchmark workflow.
- `scripts/` owns installer, release smoke, visual capture and operational helpers.
- `docs/HANDOFF-2.1.1.md` is the current predecessor to `STATUS.md`; historical detail remains linked, not copied.

## States to preserve

- idle/listening/processing/success/error/cancelled, model cold/ready/unloaded, translator verified/unavailable, delivery inserted/suppressed.
- Single-instance mutex, hook watchdog, GPU/CPU fallback and installer upgrade/uninstall lifecycle are durable reliability contracts.
