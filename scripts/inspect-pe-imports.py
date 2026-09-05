"""Report direct PE import DLLs without loading or executing the inspected binary."""
import json
from pathlib import Path
import struct
import sys


def imports(path):
    data = path.read_bytes()
    pe = struct.unpack_from('<I', data, 0x3C)[0]
    if data[pe:pe + 4] != b'PE\0\0':
        raise ValueError('Not a PE binary')
    count = struct.unpack_from('<H', data, pe + 6)[0]
    optional_size = struct.unpack_from('<H', data, pe + 20)[0]
    optional = pe + 24
    magic = struct.unpack_from('<H', data, optional)[0]
    directory = optional + (112 if magic == 0x20B else 96)
    import_rva = struct.unpack_from('<I', data, directory + 8)[0]
    sections = []
    for index in range(count):
        base = optional + optional_size + index * 40
        virtual_size, rva, raw_size, raw_pointer = struct.unpack_from('<IIII', data, base + 8)
        sections.append((rva, max(virtual_size, raw_size), raw_pointer))

    def offset(rva):
        for start, size, raw in sections:
            if start <= rva < start + size:
                return raw + rva - start
        raise ValueError('RVA outside sections')

    if not import_rva:
        return []
    cursor = offset(import_rva)
    result = []
    for _ in range(4096):
        entry = struct.unpack_from('<IIIII', data, cursor)
        if not any(entry):
            return result
        name = offset(entry[3])
        result.append(data[name:data.index(b'\0', name)].decode('ascii'))
        cursor += 20
    raise ValueError('Invalid import table')


if __name__ == '__main__':
    print(json.dumps({Path(name).name: imports(Path(name)) for name in sys.argv[1:]}, indent=2))
