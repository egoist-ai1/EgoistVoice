# Official no-TTS native dependency for Russian 2.4

The existing NuGet CAPI contains unused eSpeak/piper TTS code. Before public redistribution, choose the official upstream v1.13.4 no-TTS shared MT Windows x64 binary. The application uses offline ASR only. This changes native packaging, not the selected GigaAM feature extraction or model weights.

Official archive SHA9fd2bdb7fca85120e1e2099acb775c0079bdc1cd4c8d3de9a9eaee86394eb83e; CAPI dcfc89cf50fbd0fb77c115a6b53ee9b2739e57d6d1f4158a3aeab31dd7139676. All176 Sherpa exports retained;65 unrelated exports absent; only ONNX Runtime/KERNEL32 imports. ORT remains daa77083a45bf525da0dde9e87f85d8eb146f58f9c9aa7124ca84545e1c0f148. Source pin142807252687d81b40d6315f23470a1512a00de3; native manifest and notices are vendored for offline builds. MSBuild checks both DLL hashes and copies only the accepted native assets; stock NuGet native assets excluded.

Actual complete quality outputs on80 public clips are exact equal, lexical26/1169/punctuation130/rawchar241 unchanged; three silence fixtures, file parity, cancellation/recovery and111.15s audio pass. Independent plain/E2E raw-text equality checks are accepted separately. New native appears in app and test output directories. Full1037pass;Compact1030pass/7Full-onlyskips. Packaging scripts unchanged from Pester34/Analyzer0 accepted check.

Personal audio, DNS and installed2.3.0 were untouched. Earlier43374 installer remains local historical evidence and is not the release asset. New source freeze, Windows CI, final offline payload, independent hashes, publication and transactional workstation replacement follow this note. Clean VM installer lifecycle remains unavailable; host installer is not executed.
