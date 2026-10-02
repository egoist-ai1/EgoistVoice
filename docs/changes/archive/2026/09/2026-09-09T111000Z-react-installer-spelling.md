# React installer and precise product spelling

The user's screenshot showed invisible native checkbox labels. Replaced the visible
wizard with a local React/Electron shell: explicit colors, accessible switches,
directory chooser, real Inno progress, success/error states and restrained motion.
The existing Inno engine preserves application identity, settings and uninstall.
Its new bridge parameters carry exact folder/autostart/shortcut choices. Electron
is temporary installer infrastructure and is absent from installed Voice.

The subsequent spelling report adds a small path-guarded literal correction pass
for the confirmed Egoist product aliases and `репозиторй`. It does not infer changes
to Conor of Kings, Rostov On Don, unknown words or the rest of the phrase.

Code: `3578663`, `997e134`, `6e01ecc`, `4bd7cae`, `201ed8c`.
Owners: `installer/react`, `Build-ReactInstaller.ps1`, Inno bridge,
`TrustedSpellingCorrections`, postprocessor and focused tests. App version RC3.
Checks: 23 focused tests; native publish; Inno compile; React/Vite build;
four rendered states plus 500px layout, checkbox interaction and mocked transition.
PSScriptAnalyzer clean. Host installation not performed; guest lifecycle remains
unverified. No additional full ASR run or model download. Cached Electron reused.
Evidence: `artifacts/validation/react-2.2.1-rc.3` and final package receipt.
Next: guest installation/upgrade when an isolated Windows guest is available.
