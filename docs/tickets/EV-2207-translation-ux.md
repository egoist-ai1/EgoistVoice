# EV-2207 — trustworthy translation UX and recovery

- Status: `PROPOSED — requires ticket approval`
- Specification: [`Voice 2.2`](../specs/001-voice-2.2-accuracy-offline-translation.md)
- Depends on: `EV-2206`

## Observable result

Capsule and settings show exactly whether the offline engine is missing,
verifying, loading, ready, translating, sleeping or failed. A failed translation
offers explicit retry/original recovery instead of pretending source text is a
successful translation.

## Scope

- Extend existing visual/state models, not the overall EGOIST design language.
- Add engine/model/version/backend/size/readiness details and bounded recovery
  actions. Destructive repair/uninstall remains outside normal dictation.
- Retain recognized payload only in memory for a short bounded retry window;
  sensitive targets receive no retention or clipboard fallback.
- Add microphone calibration/level hint without recording or displaying text.
- Ensure keyboard navigation, UI Automation names, visible focus, high
  contrast, 100/150/200% DPI and reduced-motion behavior.

## Expected files

- `MainWindow.xaml`, `MainWindow.xaml.cs`
- `MainWindow.Models.cs`, `MainWindow.Visuals.cs`
- `Core/CapsuleVisualState.cs`, `Services/DictationSettingsService.cs`
- `App.xaml` only for existing-token-compatible styles
- `VisualBehaviorTests.cs`, `CapsuleRenderingTests.cs` and focused state tests
- rendered evidence under the existing screenshot/visual workflow

## Blockers and human input

- Stable EV-2206 statuses/errors. UI may not invent states absent from protocol.
- No full redesign, new design dependency or telemetry.

## Verification

- Render every engine and error state at 100/150/200%, long Russian labels and
  high contrast; inspect screenshots, clipping and focus order.
- Keyboard-only primary flow and recovery pass; state is never color-only.
- Failure tests prove source is not silently inserted and sensitive-target
  protections remain fail closed.
- UI thread heartbeat p95 ≤50 ms during loading/translation.

## Done when

The user can understand readiness, progress and recovery without opening logs,
and existing dictation interactions remain visually and functionally intact.
