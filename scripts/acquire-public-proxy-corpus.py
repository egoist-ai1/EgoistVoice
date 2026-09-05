#!/usr/bin/env python3
"""Build a deterministic local ASR proxy corpus from pinned public sources.

The generated WAV files, transcripts, row map and reports stay below artifacts/ and are ignored by
Git. The checked-in descriptor contains only provenance, revisions, licenses, counts and digests.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import heapq
import json
import os
import subprocess
import tarfile
import tempfile
import wave
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable, Iterator

import pyarrow.parquet as parquet
from huggingface_hub import hf_hub_download


SCHEMA_VERSION = 2
LOCAL_PRIVACY = "private-local-only"
NEWLINE = "\r\n" if os.name == "nt" else "\n"


@dataclass(frozen=True)
class Candidate:
    source_id: str
    row: int
    text: str
    priority: str


@dataclass(frozen=True)
class Materialized:
    source_id: str
    source_row: int
    set_id: str
    index: int
    text: str
    audio_sha256: str
    audio_bytes: int
    derived_from: str | None = None

    @property
    def corpus_id(self) -> str:
        return f"{self.set_id}/{self.index:03d}"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--descriptor", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--source-root", required=True, type=Path)
    parser.add_argument("--no-download", action="store_true")
    parser.add_argument("--force", action="store_true")
    return parser.parse_args()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(8 * 1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def canonical_json(value: Any) -> str:
    return json.dumps(value, ensure_ascii=False, separators=(",", ":"), sort_keys=True)


def write_utf8_exact(path: Path, content: str) -> None:
    # newline="" prevents Python on Windows from translating our explicit CRLF into CRCRLF.
    with path.open("w", encoding="utf-8", newline="") as stream:
        stream.write(content)


def load_descriptor(path: Path) -> dict[str, Any]:
    descriptor = json.loads(path.read_text(encoding="utf-8"))
    if descriptor.get("schema") != "egoist.voice.public-proxy-sources/v1":
        raise ValueError("Unsupported public proxy descriptor schema.")
    ids = [source["id"] for source in descriptor["sources"]]
    if len(ids) != len(set(ids)):
        raise ValueError("Duplicate public proxy source id.")
    return descriptor


def obtain_sources(
    descriptor: dict[str, Any], source_root: Path, no_download: bool
) -> dict[str, list[Path]]:
    result: dict[str, list[Path]] = {}
    for source in descriptor["sources"]:
        local_root = source_root / source["id"]
        local_root.mkdir(parents=True, exist_ok=True)
        paths: list[Path] = []
        for item in source["files"]:
            expected_path = local_root / item["path"]
            if expected_path.exists():
                path = expected_path
            elif no_download:
                raise FileNotFoundError(f"Pinned source is missing: {expected_path}")
            else:
                print(f"download source={source['id']} file={item['path']}", flush=True)
                path = Path(
                    hf_hub_download(
                        repo_id=source["repository"],
                        filename=item["path"],
                        repo_type="dataset",
                        revision=source["revision"],
                        local_dir=local_root,
                    )
                )
            if path.stat().st_size != item["bytes"]:
                raise ValueError(f"Size mismatch for {source['id']}:{item['path']}")
            actual_hash = sha256_file(path)
            expected_hash = item.get("sha256")
            if expected_hash and actual_hash != expected_hash:
                raise ValueError(f"SHA-256 mismatch for {source['id']}:{item['path']}")
            item["observedSha256"] = actual_hash
            paths.append(path)
            print(
                f"verified source={source['id']} bytes={path.stat().st_size} sha256={actual_hash[:12]}",
                flush=True,
            )
        result[source["id"]] = paths
    return result


def valid_text(value: Any, minimum: int) -> str | None:
    if not isinstance(value, str):
        return None
    text = " ".join(value.replace("\u00a0", " ").split()).strip()
    if len(text) < minimum or not any("а" <= char.lower() <= "я" or char.lower() == "ё" for char in text):
        return None
    return text


def priority(seed: int, source_id: str, row: int) -> str:
    return hashlib.sha256(f"{seed}:{source_id}:{row}".encode("ascii")).hexdigest()


def choose(candidates: Iterable[Candidate], count: int) -> list[Candidate]:
    # Negated integer priorities make heap[0] the current worst retained candidate.
    heap: list[tuple[int, int, Candidate]] = []
    for candidate in candidates:
        score = int(candidate.priority, 16)
        item = (-score, -candidate.row, candidate)
        if len(heap) < count:
            heapq.heappush(heap, item)
        elif item > heap[0]:
            heapq.heapreplace(heap, item)
    selected = [item[2] for item in heap]
    if len(selected) != count:
        raise ValueError(f"Source has {len(selected)} usable rows; {count} required.")
    return sorted(selected, key=lambda item: (item.priority, item.row))


def parquet_candidates(
    source: dict[str, Any], paths: list[Path], seed: int, minimum: int
) -> Iterator[Candidate]:
    row_offset = 0
    text_column = source["textColumn"]
    audio_column = source["audioColumn"]
    for path in paths:
        file = parquet.ParquetFile(path)
        if text_column not in file.schema_arrow.names or audio_column not in file.schema_arrow.names:
            raise ValueError(
                f"{source['id']} required columns not in {file.schema_arrow.names}"
            )
        for batch in file.iter_batches(batch_size=64, columns=[text_column, audio_column]):
            values = batch.column(0).to_pylist()
            audio = batch.column(1).to_pylist()
            for index, value in enumerate(values):
                row = row_offset + index
                text = valid_text(value, minimum)
                embedded = audio[index]
                payload = embedded.get("bytes") if isinstance(embedded, dict) else None
                # Some public rows contain a valid transcript but only an empty WAV header. They
                # must never enter the deterministic priority pool: GPU Whisper runtimes can fault
                # in native code when invoked with zero samples.
                if text is not None and isinstance(payload, (bytes, bytearray)) and len(payload) > 44:
                    yield Candidate(source["id"], row, text, priority(seed, source["id"], row))
            row_offset += len(values)


def fleurs_rows(tsv_path: Path, columns: list[str]) -> list[dict[str, str]]:
    with tsv_path.open("r", encoding="utf-8", newline="") as stream:
        # The official FLEURS TSV deliberately has no header; treating its first clip as one would
        # silently drop exactly one row and make the official 775-clip test split impossible.
        reader = csv.DictReader(stream, delimiter="\t", fieldnames=columns)
        return list(reader)


def fleurs_candidates(source: dict[str, Any], paths: list[Path], seed: int, minimum: int) -> Iterator[Candidate]:
    tsv_path = next(path for path in paths if path.suffix == ".tsv")
    rows = fleurs_rows(tsv_path, source["tsvColumns"])
    for row_index, row in enumerate(rows):
        text = valid_text(row.get(source["textColumn"]), minimum)
        if text is not None:
            yield Candidate(source["id"], row_index, text, priority(seed, source["id"], row_index))


def audio_payload(value: Any) -> tuple[bytes, str]:
    if not isinstance(value, dict):
        raise ValueError(f"Expected embedded audio struct, got {type(value).__name__}.")
    payload = value.get("bytes")
    path = value.get("path") or "audio.bin"
    if not isinstance(payload, (bytes, bytearray)) or len(payload) == 0:
        raise ValueError("Embedded audio row does not contain bytes.")
    return bytes(payload), Path(path).suffix or ".bin"


def selected_parquet_rows(
    source: dict[str, Any], paths: list[Path], selected: list[Candidate]
) -> Iterator[tuple[Candidate, bytes, str]]:
    wanted = {item.row: item for item in selected}
    row_offset = 0
    audio_column = source["audioColumn"]
    text_column = source["textColumn"]
    for path in paths:
        file = parquet.ParquetFile(path)
        if audio_column not in file.schema_arrow.names:
            raise ValueError(
                f"{source['id']} audio column {audio_column!r} not in {file.schema_arrow.names}"
            )
        for batch in file.iter_batches(batch_size=64, columns=[text_column, audio_column]):
            texts = batch.column(0).to_pylist()
            audio = batch.column(1).to_pylist()
            for index, value in enumerate(texts):
                row = row_offset + index
                candidate = wanted.get(row)
                if candidate is None:
                    continue
                current = valid_text(value, 1)
                if current != candidate.text:
                    raise ValueError(f"Transcript changed during extraction: {source['id']} row {row}")
                payload, suffix = audio_payload(audio[index])
                yield candidate, payload, suffix
            row_offset += len(texts)


def selected_fleurs_rows(
    source: dict[str, Any], paths: list[Path], selected: list[Candidate]
) -> Iterator[tuple[Candidate, bytes, str]]:
    tsv_path = next(path for path in paths if path.suffix == ".tsv")
    archive_path = next(path for path in paths if path.name.endswith(".tar.gz"))
    rows = fleurs_rows(tsv_path, source["tsvColumns"])
    wanted_by_name: dict[str, Candidate] = {}
    for candidate in selected:
        row = rows[candidate.row]
        audio_name = row.get(source["audioColumn"]) or row.get("audio") or row.get("file_name")
        if not audio_name:
            raise ValueError(f"FLEURS row {candidate.row} has no audio file name.")
        key = Path(audio_name).name
        if key in wanted_by_name:
            raise ValueError(f"Duplicate FLEURS audio file name {key!r}.")
        wanted_by_name[key] = candidate

    # A gzip-compressed tar has no cheap random access. Calling extractfile for selected members in
    # TSV order makes tarfile repeatedly inflate the archive from the beginning and degrades toward
    # O(n²). Walk the stream once and map each member back to the deterministically selected row.
    found = 0
    with tarfile.open(archive_path, "r:gz") as archive:
        for member in archive:
            if not member.isfile():
                continue
            candidate = wanted_by_name.get(Path(member.name).name)
            if candidate is None:
                continue
            stream = archive.extractfile(member)
            if stream is None:
                raise ValueError(f"Cannot read FLEURS archive member {member.name!r}.")
            yield candidate, stream.read(), Path(member.name).suffix or ".wav"
            found += 1
    if found != len(selected):
        raise ValueError(f"FLEURS archive contains {found} of {len(selected)} selected files.")


def normalize_audio(
    payload: bytes,
    suffix: str,
    target: Path,
    volume_db: int | None = None,
    force: bool = False,
) -> None:
    target.parent.mkdir(parents=True, exist_ok=True)
    if not force and valid_normalized_audio(target):
        return
    with tempfile.TemporaryDirectory(prefix="egoist-voice-proxy-") as temp_directory:
        temp = Path(temp_directory)
        source = temp / f"source{suffix}"
        candidate = temp / "normalized.wav"
        source.write_bytes(payload)
        command = [
            "ffmpeg",
            "-hide_banner",
            "-loglevel",
            "error",
            "-nostdin",
            "-y",
            "-i",
            str(source),
        ]
        if volume_db is not None:
            command.extend(["-af", f"volume={volume_db}dB"])
        command.extend(["-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", str(candidate)])
        completed = subprocess.run(command, check=False)
        if completed.returncode != 0 or not valid_normalized_audio(candidate):
            raise RuntimeError(f"ffmpeg failed for {target.name} with exit code {completed.returncode}.")
        os.replace(candidate, target)


def clone_quiet(source: Path, target: Path, force: bool = False) -> None:
    if not force and valid_normalized_audio(target):
        return
    target.parent.mkdir(parents=True, exist_ok=True)
    candidate = target.with_suffix(".candidate.wav")
    completed = subprocess.run(
        [
            "ffmpeg",
            "-hide_banner",
            "-loglevel",
            "error",
            "-nostdin",
            "-y",
            "-i",
            str(source),
            "-af",
            "volume=-18dB",
            "-ac",
            "1",
            "-ar",
            "16000",
            "-c:a",
            "pcm_s16le",
            str(candidate),
        ],
        check=False,
    )
    if completed.returncode != 0 or not valid_normalized_audio(candidate):
        candidate.unlink(missing_ok=True)
        raise RuntimeError(f"ffmpeg quiet transform failed for {source.name}.")
    os.replace(candidate, target)


def valid_normalized_audio(path: Path, minimum_frames: int = 4_000) -> bool:
    if not path.exists():
        return False
    try:
        with wave.open(str(path), "rb") as stream:
            return (
                stream.getnchannels() == 1
                and stream.getframerate() == 16_000
                and stream.getsampwidth() == 2
                and stream.getnframes() >= minimum_frames
            )
    except (EOFError, OSError, wave.Error):
        return False


def previous_source_rows(output: Path) -> dict[str, int]:
    path = output / "source-index.jsonl"
    if not path.exists():
        return {}
    rows: dict[str, int] = {}
    with path.open("r", encoding="utf-8") as stream:
        for line in stream:
            if not line.strip():
                continue
            item = json.loads(line)
            if isinstance(item.get("id"), str) and isinstance(item.get("sourceRow"), int):
                rows[item["id"]] = item["sourceRow"]
    return rows


def script_fingerprint(lines: list[Materialized], sets: list[dict[str, Any]]) -> str:
    parts = [f"{SCHEMA_VERSION}\x1f{LOCAL_PRIVACY}{NEWLINE}"]
    for item in sorted(sets, key=lambda value: value["id"]):
        parts.append(
            f"{item['id']}\x1f{item['title']}\x1f{item['hint']}\x1f{item['count']}{NEWLINE}"
        )
    for item in lines:
        parts.append(
            f"{item.corpus_id}\x1f{item.text}\x1f{item.text}\x1f{item.set_id}\x1f\x1f\x1f\x1f{NEWLINE}"
        )
    return hashlib.sha256("".join(parts).encode("utf-8")).hexdigest()


def save_corpus(
    descriptor: dict[str, Any], output: Path, materialized: list[Materialized]
) -> None:
    titles = {
        "fleurs": ("FLEURS Russian test", "Independent clean multi-speaker Russian speech."),
        "ruls": ("Russian LibriSpeech test", "Independent audiobook Russian speech."),
        "tone": ("ToneWebinars validation shard", "Long-form webinar speech and acoustic variation."),
        "sova": ("SOVA device test", "Short device-oriented commands and near-field speech."),
        "quiet": ("Derived quiet stress proxy", "FLEURS clips attenuated by 18 dB; synthetic stress only."),
    }
    sets = []
    for set_id in ("fleurs", "ruls", "tone", "sova", "quiet"):
        count = sum(1 for item in materialized if item.set_id == set_id)
        if count:
            title, hint = titles[set_id]
            sets.append({"id": set_id, "title": title, "hint": hint, "count": count})
    set_order = {item["id"]: index for index, item in enumerate(sets)}
    ordered = sorted(materialized, key=lambda item: (set_order[item.set_id], item.index))
    fingerprint = script_fingerprint(ordered, sets)

    script_lines = [canonical_json({"kind": "schema", "version": 2, "privacy": LOCAL_PRIVACY})]
    for item in sets:
        script_lines.append(
            canonical_json(
                {
                    "kind": "set",
                    "set": item["id"],
                    "title": item["title"],
                    "hint": item["hint"],
                    "expectedCount": item["count"],
                }
            )
        )
        for clip in [value for value in ordered if value.set_id == item["id"]]:
            script_lines.append(
                canonical_json(
                    {
                        "kind": "line",
                        "id": clip.corpus_id,
                        "text": clip.text,
                        "tags": [clip.set_id],
                    }
                )
            )

    reference_lines = [
        canonical_json(
            {
                "kind": "corpus-reference",
                "schemaVersion": 2,
                "privacy": LOCAL_PRIVACY,
                "scriptSha256": fingerprint,
            }
        )
    ]
    for clip in ordered:
        reference_lines.append(
            canonical_json(
                {
                    "id": clip.corpus_id,
                    "audio": f"{clip.corpus_id}.wav",
                    "text": clip.text,
                    "tags": [clip.set_id],
                }
            )
        )

    write_utf8_exact(output / "script.jsonl", NEWLINE.join(script_lines) + NEWLINE)
    write_utf8_exact(
        output / "reference.jsonl", NEWLINE.join(reference_lines) + NEWLINE
    )
    with (output / "source-index.jsonl").open("w", encoding="utf-8", newline="") as stream:
        for clip in ordered:
            stream.write(
                canonical_json(
                    {
                        "id": clip.corpus_id,
                        "source": clip.source_id,
                        "sourceRow": clip.source_row,
                        "derivedFrom": clip.derived_from,
                        "audioBytes": clip.audio_bytes,
                        "audioSha256": clip.audio_sha256,
                        "textSha256": hashlib.sha256(clip.text.encode("utf-8")).hexdigest(),
                    }
                )
                + NEWLINE
            )
    manifest = {
        "schema": "egoist.voice.public-proxy-manifest/v1",
        "corpusId": descriptor["corpusId"],
        "descriptorSha256": sha256_file(Path(descriptor["_path"])),
        "scriptSha256": fingerprint,
        "clips": len(ordered),
        "sets": {item["id"]: item["count"] for item in sets},
        "audioBytes": sum(item.audio_bytes for item in ordered),
        "privacy": "local-only; transcripts and audio are ignored by Git",
        "claimBoundary": "Public proxy evidence only; no user-voice accuracy claim.",
        "sources": [
            {
                "id": source["id"],
                "repository": source["repository"],
                "revision": source["revision"],
                "license": source["license"],
                "files": [
                    {
                        "path": item["path"],
                        "bytes": item["bytes"],
                        "sha256": item.get("observedSha256") or item.get("sha256"),
                    }
                    for item in source["files"]
                ],
            }
            for source in descriptor["sources"]
        ],
    }
    write_utf8_exact(
        output / "manifest.json", json.dumps(manifest, ensure_ascii=False, indent=2) + NEWLINE
    )

    profiles = output / "profiles"
    profiles.mkdir(parents=True, exist_ok=True)
    for item in sets:
        ids = [clip.corpus_id for clip in ordered if clip.set_id == item["id"]]
        profile = {
            "schema": "egoist.voice.corpus-profile/v1",
            "id": f"public-proxy-{item['id']}",
            "description": f"Deterministic {item['id']} isolation profile for public-proxy-v1.",
            "buckets": {item["id"]: ids},
        }
        write_utf8_exact(
            profiles / f"{item['id']}.json",
            json.dumps(profile, ensure_ascii=False, indent=2) + NEWLINE,
        )
    for shard_index, offset in enumerate(range(0, len(ordered), 100), start=1):
        ids = [clip.corpus_id for clip in ordered[offset : offset + 100]]
        profile = {
            "schema": "egoist.voice.corpus-profile/v1",
            "id": f"public-proxy-shard-{shard_index:03d}",
            "description": "Deterministic 100-clip crash-isolation shard; final shard may be shorter.",
            "buckets": {"all": ids},
        }
        write_utf8_exact(
            profiles / f"shard-{shard_index:03d}.json",
            json.dumps(profile, ensure_ascii=False, indent=2) + NEWLINE,
        )


def main() -> int:
    args = parse_args()
    descriptor = load_descriptor(args.descriptor)
    descriptor["_path"] = str(args.descriptor.resolve())
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    source_paths = obtain_sources(descriptor, args.source_root.resolve(), args.no_download)

    minimum = int(descriptor["selection"]["minTextCharacters"])
    minimum_duration = float(descriptor["selection"]["minDurationSeconds"])
    if minimum_duration != 0.25:
        raise ValueError("The v1 importer currently pins minDurationSeconds to 0.25.")
    seed = int(descriptor["seed"])
    prior_rows = previous_source_rows(output)
    materialized: list[Materialized] = []
    selections: dict[str, list[Candidate]] = {}
    for source in descriptor["sources"]:
        iterator = (
            fleurs_candidates(source, source_paths[source["id"]], seed, minimum)
            if source["format"] == "fleurs-tsv-tar"
            else parquet_candidates(source, source_paths[source["id"]], seed, minimum)
        )
        selected = choose(iterator, int(source["targetCount"]))
        selections[source["id"]] = selected
        print(f"selected source={source['id']} clips={len(selected)}", flush=True)

        rows = (
            selected_fleurs_rows(source, source_paths[source["id"]], selected)
            if source["format"] == "fleurs-tsv-tar"
            else selected_parquet_rows(source, source_paths[source["id"]], selected)
        )
        by_row = {candidate.row: index + 1 for index, candidate in enumerate(selected)}
        produced = 0
        for candidate, payload, suffix in rows:
            index = by_row[candidate.row]
            target = output / source["id"] / f"{index:03d}.wav"
            corpus_id = f"{source['id']}/{index:03d}"
            source_changed = prior_rows.get(corpus_id) not in (None, candidate.row)
            normalize_audio(payload, suffix, target, force=args.force or source_changed)
            materialized.append(
                Materialized(
                    source["id"],
                    candidate.row,
                    source["id"],
                    index,
                    candidate.text,
                    sha256_file(target),
                    target.stat().st_size,
                )
            )
            produced += 1
            if produced % 100 == 0 or produced == len(selected):
                print(f"normalized source={source['id']} clips={produced}/{len(selected)}", flush=True)
        if produced != len(selected):
            raise ValueError(f"Extracted {produced} of {len(selected)} rows for {source['id']}.")

    quiet = next(item for item in descriptor["derivedSets"] if item["id"] == "quiet")
    base_candidates = choose(iter(selections[quiet["source"]]), int(quiet["count"]))
    base_by_row = {item.source_row: item for item in materialized if item.set_id == quiet["source"]}
    for index, candidate in enumerate(base_candidates, start=1):
        base = base_by_row[candidate.row]
        target = output / "quiet" / f"{index:03d}.wav"
        clone_quiet(
            output / base.set_id / f"{base.index:03d}.wav", target, force=args.force
        )
        materialized.append(
            Materialized(
                base.source_id,
                base.source_row,
                "quiet",
                index,
                base.text,
                sha256_file(target),
                target.stat().st_size,
                derived_from=base.corpus_id,
            )
        )
    print(f"normalized source=quiet clips={quiet['count']}", flush=True)

    save_corpus(descriptor, output, materialized)
    print(
        f"ready corpus={descriptor['corpusId']} clips={len(materialized)} path={output}", flush=True
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
