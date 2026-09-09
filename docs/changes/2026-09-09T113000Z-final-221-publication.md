# Final 2.2.1 publication

- Why: the owner explicitly requested promoting RC3 to final and updating GitHub.
- What: version 2.2.1, monotonically increasing app FileVersion 2.2.1.4; rebuilt
  offline React installer from source 05d9f4b. No dictation behavior changed.
- Artifact: 353,118,713 bytes; SHA-256
  `26da4171d10a5e93a6dd429f79fbeb1388f67cd235b6bf044baf57847155fe12`.
- Validation: 30 focused tests passed; app/Inno/React/Electron builds passed;
  507 payload files and two Inno segments hashed; metadata read back; four UI
  states, toggle, mocked install, 620/500px checked without page errors.
- Contracts: same Compact AppId/path and data policy; Electron installer only.
  The previous RC3 screenshot is replaced by an actual final-version browser
  preview. Source, README, install/build docs, changelog and release notes align.
- Publication plan: fast-forward the corresponding GitHub repository, tag v2.2.1,
  upload EXE/checksum/public manifest into a draft, verify remote asset hashes,
  then make it the latest non-prerelease. No tag or existing asset overwrite.
- Limits: unsigned, clean Windows lifecycle still unverified. The owner's final
  publication decision does not imply Full EV-2210 SHIP or new acoustic evidence.
- Files: csproj, React metadata/preview version, README, CHANGELOG, STATUS,
  IMPLEMENTATION, THIRD-PARTY-NOTICES, docs install/build/guide/roadmap/releases.
- Next: verify GitHub readback and CI; retain guest lifecycle as an open check.
