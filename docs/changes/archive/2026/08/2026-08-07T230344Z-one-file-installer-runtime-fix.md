# One-file installer and runtime correction

- UTC: `2026-08-07T23:03:44Z`
- Scope: Egoist Voice 2.2.0 unsigned Full Offline field-test candidate.

## Changed and why

The prior installer set required adjacent BIN files, contrary to the requested
single-file experience. A custom x64 .NET Framework bootstrap now embeds the
inner Inno EXE and all slices into one outer EXE. Its fixed `460x142` dialog
cannot maximize, so preparation does not cover one or multiple monitors.

The bootstrap validates a SHA-bound footer/manifest and every embedded segment
before launching Inno, preflights temporary-drive space, forwards command-line
arguments, waits for the inner exit code and removes its task-owned temporary
directory. A corrupt synthetic wrapper returns `1603` before launch.

The real install failure was also corrected in Voice and Translator: Inno had
passed doubled quotes to `powershell.exe -File`, splitting paths at `EGOIST
Translator` and producing `-196608`. All bootstrap paths now use one quoted
argument. Multi-gigabyte Inno progress is scaled before multiplication, fixing
the observed `Out Of Range` overflow.

The first outer bootstrap build exposed PE version `0.0.0.0` during independent
review and was rejected. The guarded rebuild generates and validates exact
metadata before packaging.

## Exact artifact

- `artifacts/release/EgoistVoice-Setup-2.2.0-win-x64.exe`
- Bytes: `3,191,770,964`
- SHA-256:
  `eb2b2323ee8f77e234956711ab7b8eaf20fc5c2cd318e6380e87cb59eac14b9c`
- PE: `FileVersion 2.2.0.0`, `ProductVersion 2.2.0`, `ProductName Egoist Voice`
- Receipt: schema 3, `deliveryMode=embedded-inno-bootstrap`, three verified
  embedded files totalling `3,191,699,944` bytes.
- Adjacent delivery BIN files: `0`; remaining build staging directories: `0`.

## Verification

- Voice Release tests: `479/479` pass.
- C# bootstrap compilation and PowerShell AST parse: pass.
- Synthetic valid/corrupt wrapper: exit `0` / `1603`.
- Independent final verifier recomputed the manifest and all three segment
  hashes; checksum and receipt match the outer EXE.
- Installer execution on the development host: `not-run` by policy.

## Contracts, context and risk

The shared Engine remains the same exact 1.0.0/Q8/b10219 bundle. Install order
is still Host -> pack -> owner, and exact installed assets are reused across
Voice and Translator. `CTX-013` is superseded by `CTX-014` for delivery shape
and corrected artifact identity. Clean Windows installation, C-02/C-03
coexistence and uninstall evidence remain with EV-2213/EV-2214/T020/T021.

Next safe action: launch the single EXE in the authorized field/guest
environment and test dictation plus built-in translation; no separate BIN or
Engine installation is required.
