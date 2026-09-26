#!/usr/bin/env python3
"""Read a .tmod archive: header, file table, and optionally extract.

Format from tModLoader's Core/TmodFile.cs: "TMOD", a length-prefixed
tML version string, 20 bytes of hash, 256 of signature, an int32 data
length, then the mod name, its version, a file table, and the blob.
Entries are raw deflate when CompressedLength differs from Length.
"""
import sys, zlib, os

def read_string(f):
    # BinaryWriter.Write(string): 7-bit encoded length, then UTF-8.
    length = 0
    shift = 0
    while True:
        b = f.read(1)[0]
        length |= (b & 0x7F) << shift
        if not b & 0x80:
            break
        shift += 7
    return f.read(length).decode('utf-8')

def read_int(f):
    return int.from_bytes(f.read(4), 'little', signed=True)

def main(path, extract_to=None):
    with open(path, 'rb') as f:
        assert f.read(4) == b'TMOD', 'not a .tmod'
        tml = read_string(f)
        f.read(20)          # hash
        f.read(256)         # signature
        datalen = read_int(f)

        name = read_string(f)
        version = read_string(f)
        count = read_int(f)

        entries = []
        for _ in range(count):
            entry_name = read_string(f)
            length = read_int(f)
            compressed = read_int(f)
            entries.append((entry_name, length, compressed))

        print(f'mod:      {name} {version}')
        print(f'built by: tModLoader {tml}')
        print(f'entries:  {count}, data {datalen} bytes')

        for entry_name, length, compressed in entries:
            data = f.read(compressed)
            if compressed != length:
                data = zlib.decompress(data, -15)   # raw deflate

            if entry_name in ('build.txt', 'description.txt'):
                print(f'\n--- {entry_name} ---')
                print(data.decode('utf-8', 'replace').strip())

            if extract_to:
                out = os.path.join(extract_to, entry_name.replace('\\', '/'))
                os.makedirs(os.path.dirname(out), exist_ok=True)
                with open(out, 'wb') as o:
                    o.write(data)

        if not extract_to:
            print('\n--- entry names ---')
            for entry_name, length, _ in entries:
                print(f'  {length:>9} {entry_name}')

if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else None)
