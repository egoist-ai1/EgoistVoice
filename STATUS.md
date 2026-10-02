# Egoist Voice — current status

Updated: 2026-10-02. Active: EV-2230 / **2.3.0 Russian RNNT**.

The owner authorized new model/settings/UI changes, publication and replacement of the installed Compact. Canonical source: egoist-ai1/EgoistVoice; branch codex/russian-rnnt-2.3. Existing installation remains 2.2.1-rc.3 / 997e134 until verified replacement.

Source ready for packaging. Full920passed0skipped; Compact913passed7Full-onlyskipped. Quietgate2cases fail-to-pass; updater12scenarios with actual isolatedrollback. Paired public40clips/572words: plainINT8greedy16errors vsE2E25; warmdecodep50/p9591.9/152.4ms. 100repeatnativehashesidentical, silenceempty. Nativevisualstates tested; finalpayloadprofile QA pending.

Next: build exact offline self-contained release, verify final bytes/profile, update local installation and readback GitHub release. Personal voice/clean-VM lifecycle unverified; no zero-error claim. [EV-2230](docs/tickets/EV-2230-russian-rnnt.md).