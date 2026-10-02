# EV-2232 — measured ≥50% quality improvement

Status: active,2026-10-02. Continuation of the original quality objective;2.4 is a verified intermediate deployment. No next final build until this objective is verified. Do not redefine successful formatting as successful lexical ASR.

## Full acceptance

1. Improve intelligibility and correct Russian words, spelling, punctuation/case, difficult articulation and secondary English names through measured local mechanics. Since word accuracy is already above97%, interpret requested50% improvement as at least50% relative reduction of errors; no impossible absolute percentage claim.
2. Against the fixed original plain2.3 public80 baseline: lexical errors26→≤13, lexical characters38→≤19, punctuation277→≤138, raw surface characters573→≤286. Keep scorer/PCM/normalization fixed. This set is development evidence after extensive experiments, not an untouched test.
3. Prove lexical/spelling gain on a newly predetermined human control set with an independent candidate lock before scoring; target≥50% relative reduction against the exact plain baseline on each declared aggregate. New SOVA/Golos sets contain live/far-field speech and unpunctuated references: evaluate words/characters there, not fabricated punctuation gold. Report possible model-training overlap and unknown speaker dependence. Extend human punctuation and English-name acoustic gold independently; scenario text alone does not establish prosody.
4. Personal soft/hissing/difficult articulation remains a distinct requirement. Public speech and synthetic perturbations may find bugs and improve robustness but do not prove personal accuracy. Request optional concrete wrong/expected phrases without blocking independent work. Never access private recordings/history or collect microphone data silently.
5. Correction must preserve intended content, negation, numbers, unknown names, technical strings and silence. No reference-derived phrase replacement, memorized benchmark dictionary or unconstrained semantic rewriting. Any lexical change needs independent acoustic/context evidence and negative tests.
6. After quality gates: measure actual cold process launch to capture-ready and both-engine-ready, repeated cancellation/recovery, long speech, device/capture edges and UI responsiveness. Record startup median/p95 and memory/CPU/VRAM/idle scope; optimize on-device resources without losing the verified quality gain.2.4 service startup2008.8457ms and WPF readyPrivate833245184B are scoped reference observations, not full cold-OS measurements.
7. Rebuild and independently review source-bound final payload, then use already authorized publication/replacement while preserving user settings/Data/history. Existing2.3/2.4 tag/assets immutable. No next final release solely because tests are green or punctuation alone passes.

## Experiments and ownership

- Root: acceptance, fixed development/control corpus selection, native baseline, integration and independent validation.
- asr_models: supported modified beam search2/4/8 without gold-derived vocabulary; exact model/native pins and latency/memory.
- voice_audit: saved hypotheses, error overlap, oracle upper bounds and deployable reference-free ROVER/consensus; exact scorer and negative cases.
- Accepted evidence: artifacts/quality-next. Each experimental subdirectory has one writer. Preserve2.4 source/receipts/reports. No user audio/text/cloud inference; DNS and unrelated apps out of scope.

## Current evidence

2.4 public80: lexical26/1169 unchanged; punctuation277→130 (−53.07%), raw surface573→241 (−57.94%). Larger models were worse individually; that does not prove that decoding/rescoring/complementary hypotheses cannot improve words. The next experiment will quantify that possibility rather than assuming the current model is the limit.

HF MCP dataset search is disabled by server configuration; use the read-only Hub/Viewer API and pinned primary cards without changing global connector setup. New corpus selection must precede candidate output inspection, include audio/reference hashes and explicit dev/control split, and keep audio/hypotheses out of Git/releases.

## Measured progress — 2026-10-02

Fresh corpus320 received Viewer PCM clips/1082.91seconds:128 validation development and192 test control, selected without hypotheses/errors; pins and all audio/reference hashes verified. No resampling or input mutation in the pipeline. Model-training overlap and speaker dependence remain unknown. Viewer-transcoded WAV was not independently compared to source-parquet audio bytes. Selection lockcddb233a4de8eb39e4b66f64642947d1535dda75098dedb7dbcb08330585307d.

Exact released2.4 baseline: development68/916 worderrors and179/5392 charerrors; control baseline-only114/1204 and220/6979. Quality formatting does not change lexical counts. Control candidate outputs have not been generated or used for selection.

Fixed development experiments: greedy BlankPenalty0.5 yields65words/169chars; beam4 variants70–77words, worse than baseline. RussianWhisperTurbo fixed CUDA beam5 yields87/916, worse than68. Eight signal transforms: best adaptive RMS0.07/cappedgain8 yields62words/127chars (−8.82%worderrors/−29.05%charerrors), end-padding400ms gives64words, speed0.95 gives65. All24 fixed zero/quiet-noise/tone tests remained empty. Identity exactly matched every128 baseline output. These are development findings; no product promotion or≥50% claim.

Saved-model consensus public80: declared plainINT8/plainFP32/E2E ROVER23 vs26; exploratory best among120 triples21, gold-selected and not independent evidence. Oracle wholeclip12/token9 is reference-aware coverage, not a deployable result. Canonical C# scoring3160pairs matched; report/source hashes verified. GigaAM Multilingual600M INT8 and bounded local4B hypothesis ranking have completed; results below. Next final packaging remains gated.

Evidence: artifacts/quality-next/progress-aggregate.json, baseline, corpus, waveform-development, decoder-search and error-analysis. Prior2.4 release remains installed and immutable.

## Completed measured stage — 2026-10-02T054800Z

Fixed signal/decoder interaction: adaptive RMS0.07/cap8 +blank0.5 gave61/916 worderrors and133/5392 normalizedcharerrors (−10.29%words); original public80 remains26words/37chars vs26/38. No gain on the old words and no held-out proof. Identity208 outputs exact,180 repeated synthetic negatives empty; original PCM copies preserved.

Same scorer: GigaAM multilingual600M INT8 gave public80 28words/38chars and development82/142; root independently reconciled208 exact C# scores,157 canonical artifact hashes. Acoustic CTC rescoring289 saved alternatives left guarded words26 and chars33 on the old80; it removed7 worderrors and introduced7.505 forward-DP tests and root240 C# score rows passed. Qwen3.5-4B existing-hypothesis ranking gave47raw/27guard vs26, despite conservative constraints. Neither is a lexical upgrade.

T-one pinned106f3b0b32a9e107eb613312e4ebc61ff3d53926, official source3c5b6c015038173840e62cea99e10cdb1c759116, actual144193371B ONNX hash matched HF LFS. CPU greedy with official streaming/padding and documented derived8k PCM gave public80 66words/81chars and development65/104. Sources differ: SOVA53→41words, Golos15→24.53 accepted files and original208 PCM hashes checked. Full official beam200+KenLM5.46GB is untested; greedy does not reject that pipeline.

Consumer measured-stage report and count plots are frozen at artifacts/quality-next/progress-report; reportSHA156b2d31b2ed1fc1a82856c3ee9bfad93db79a2771712cc4bfac02f59f9f4992. Companion board16 DOM/storage contract scenarios passed; no native-bridge/browser-pixel claim. Root verified18 source hashes, key outputs and actual chart legibility. Original aggregate byte snapshot is retained so the report does not silently change when current progress-aggregate is updated. New corpus has no punctuation/intonation gold; normalized CER is not orthographic proof.

Capture source audit reproduced a blocking join under callback lock using exact source/NAudio2.3.0 copies and synthetic callback threads. A focused service patch/regression is being implemented; microphone/private audio is not used. Capture DSP and cue-suppression findings remain separate investigations and are not bundled into that fix.

GigaChat3.1-Audio10B-A1.8B feasibility pinned expected23.71GB weights but downloaded none.17 static/mock tests and42-file manifest verify contracts only. Outer quantization kwargs are swallowed by custom loader: decoder NF4 must be applied directly while audio keeps its actual processor/encoder path. Real CUDA/NF4 smoke and sequential mmap-to-GPU loader are next; full CPU BF16/offload and stopping unrelated apps are forbidden. GigaAM600M paired FP32 export/lineage and a small predeclared saved128 consensus are also underway. No checkpoint/user approval is substituted for actual kernel or resource verification.

[Report](../../artifacts/quality-next/progress-report/ПРОМЕЖУТОЧНЫЙ-ОТЧЁТ.md), [review board](../../artifacts/quality-next/progress-report/review.html), [root verification](../../artifacts/quality-next/progress-and-feasibility-root-verification.json). Full goal remains active; installed2.4.0 and released tags/assets are unchanged.

## Capture fix and completed full-model comparisons — 2026-10-02T06:56:25Z

Focused capture source now removes the reproduced Dispose.Join / callback lock inversion. It detaches ownership and handlers under state lock, then retires native endpoints outside both locks; callback reentrancy cannot self-join. Generation checks prevent old callbacks, release-tail completion and delayed state notifications from affecting a newer take. Root independently verified both frozen source hashes and actual Full1057passed/0failed/0skipped. Affected Full/Compact102cases each passed;20 new real-service cases are included. No live microphone, private Data, installed product or acoustic DSP was changed. Reproduction/companion board: artifacts/quality-next/capture-audit/lifecycle-fix-v1; root proof: capture-lifecycle-root-verification.json and capture-full-root.trx. Real driver stalls and display-spectrum ordering remain distinct limitations.

Saved128 development consensus uses predeclared rules and outputs locked before gold. Family-balanced rule gives52/916 worderrors and110chars (−23.53%words), removes20 but introduces4 including unknown names. Identical public80 transfer gives33words/45chars vs26/38; it introduces11 and removes4 and changes «зачем не прежде» to «зачем мне прежде». Root720 exact C# transfer scores and all35 files matched. Consensus is not promoted; the gold-aware34-error oracle is nondeployable diagnostic coverage.

Same pinned multilingual600M export:822 original initializers matched,585319495 float values; initial FP32 vsINT8 yields development78vs82 and public80 29vs28, still worse than current68/26. Expanded NumPy-vs-official Torch feature comparisons failed the initial strict numerical tolerance; this finding is retained. Exact official CPU feature extraction was then used for all208 actual FP32 decodes; every raw transcript matched the NumPy route. Root416 C# score rows agreed. This excludes the tested frontend discrepancy as the cause of this result, not all possible export/decoder differences; full PyTorch-to-ONNX encoder numerical parity is not established.

Full Whisper large-v3 convertedFP16 checkpoint pinned edaa852ec7e145841d8ffdb056a99866b5f0a478:3.09GB downloaded and LFS hashes matched. Actual GPU208 audio-only/reference-free decodes scored afterward give public80 67words/95chars and development157/475 vs26/38 and68/179. All three raw silence/quiet-noise/tone probes emitted «Продолжение следует…»; these were raw ASR probes without the product quiet gate. Private memory peak6.206GB; globalGPU9404MiB includes other apps. Latency measurements are isolated warm-file observations, not whole-app or cold-OS comparisons. artifacts/quality-next/whisper-large-v3 contains52 verified files and scoped report.

GigaChatAudio real CUDA/NF4 tiny backend and sequential loader contracts passed; root44 backend file hashes matched. Full pinned weights now downloaded, but guarded load-only and four-clip pilot have no accepted transcription/quality result yet. Prospective resource budgets are estimates until measured; no unrelated app is stopped. The native template/context and UTF-8 prompt errata must be frozen before outputs; backend snapshot stays immutable.

These are development results; new192 control remains candidate-untouched. Normalized CER does not prove spelling, punctuation or intonation. Full≥50% quality goal remains active, and next final package stays gated. Current2.4 remains installed. Detailed measured reports have neutral companion boards; no additional permission is required for the authorized source correction.

## Consumer supplement V2 — 2026-10-02T07:31:38.558446+00:00

Separate immutable report progress-report-v2 includes accepted capture, consensus development/transfer, official-frontend FP32 and full Whisper FP16 results; it excludes ongoing full GigaChat pilot. ReportSHA2b1050cc1738b18779729218b0b76c83f866d718220ebc532e5acb0f6aca45ae; registry6663aa922d367c9b5ee7ee2c945161a4963b842f5851e7bead3e50ebe957f3e9; manifestb64c8818a61c0bab83361b520d846fe9f4cc29d1ff03c8193a68e84c2a271fe7. Root verified18files/512340B and49source/evidence hashes, and visually inspected the count plot. Agent13numeric/16actual JS feedback contracts passed; no native-browser-pixel claim. Exact presentation copy with source ledger is stored in Researchreports/Локальная транскрибация Egoist Voice 2026-10-02/Дополнительные-измерения-v2. Opening report/board returned queued, not confirmed visible.

Full GigaChat resource investigation is ongoing: load-only succeeded, but a later Windows commit guard stopped another load before any transcription. A source audit attributes whole-shard commit to Windows copy-on-write mapping; native safetensors pread is a proposed separately validated path. This is not a speech-quality result and is not included in V2's comparison. T-one official beam/KenLM native Windows feasibility is also being checked separately; no full language-model/audio-quality claim yet. No next release/install; control candidates untouched; full goal active.
