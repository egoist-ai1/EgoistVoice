"""Check every ZIP entry against the staged portable manifest without extraction."""
import hashlib
import json
from pathlib import Path
import sys
import zipfile

archive, manifest, report = map(Path, sys.argv[1:])
expected = json.loads(manifest.read_text(encoding='utf-8-sig'))
files = {entry['path']: entry for entry in expected['files']}
with zipfile.ZipFile(archive) as zipped:
    all_names = zipped.namelist()
    assert len(all_names) == len(set(all_names)), 'Duplicate entries'
    names = [item.filename for item in zipped.infolist() if not item.is_dir()]
    assert len(names) == len(set(names)) == len(files), 'Duplicate/missing entries'
    assert set(names) == set(files), 'Unexpected/missing entries'
    for name in names:
        assert not name.startswith(('/', 'Data/')) and '..' not in name.split('/'), 'Unsafe/personal entry'
        info = zipped.getinfo(name)
        assert info.file_size == files[name]['bytes'], 'Length mismatch'
        with zipped.open(name) as stream:
            digest = hashlib.file_digest(stream, 'sha256').hexdigest()
        assert digest == files[name]['sha256'], 'Content mismatch'
assert archive.stat().st_size <= 600_000_000, 'ZIP exceeds budget'
with archive.open('rb') as stream:
    digest = hashlib.file_digest(stream, 'sha256').hexdigest()
result = dict(passed=True, fileCount=len(files), unpackedBytes=expected['unpackedBytes'],
              zipBytes=archive.stat().st_size, zipSha256=digest)
report.write_text(json.dumps(result, indent=2), encoding='utf-8')
print(json.dumps(result))
