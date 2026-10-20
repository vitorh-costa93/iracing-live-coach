"""AMS2 / Madness engine BFF pak reader (read-only).  See SPEC.md.
usage: python -I bff.py list <pak.bff>
       python -I bff.py extract <pak.bff> <outdir> [substr]
"""
import struct, sys, os, zlib

# RC4 key table recovered from the game exe (obfuscated, 27-byte slots at VA 0x14255a7e0,
# xor ac c7 91 + byte-pair swap).  Only index 2 is used by the official paks seen so far.
RC4_KEYS = {2: b'vNva6iM!Cj3DWbVf0OKA0v'}

MAGIC = b' KAP'
ENTRY_FMT = '<QQIIQHI4s'   # 42 bytes
ENTRY_SIZE = 42


def rc4(key, buf):
    S = list(range(256)); j = 0; kl = len(key)
    for i in range(256):
        j = (j + S[i] + key[i % kl]) & 255; S[i], S[j] = S[j], S[i]
    i = j = 0; out = bytearray(buf)
    for n in range(len(out)):
        i = (i + 1) & 255; si = S[i]; j = (j + si) & 255; sj = S[j]; S[i] = sj; S[j] = si
        out[n] ^= S[(si + sj) & 255]
    return bytes(out)


class Entry:
    __slots__ = ('index', 'uid', 'offset', 'pak_size', 'size', 'timestamp', 'comp', 'crc', 'ext',
                 'name', 'ext_ts', 'ext_rec_raw', 'dhsa')

    def __repr__(self):
        return '<%d %016x %s off=%x pak=%x size=%x comp=%04x>' % (self.index, self.uid, self.name, self.offset,
                                                                     self.pak_size, self.size, self.comp)


class Bff:
    def __init__(self, path):
        self.path = path
        f = open(path, 'rb')
        head = f.read(0x130)
        (self.magic, self.version, self.count, self.data_offset, self.unk10, self.alignment) = struct.unpack_from('<4sIIIII', head, 0)
        if self.magic != MAGIC:
            raise ValueError('not a BFF: %r' % self.magic)
        self.name = head[0x18:0x118].split(b'\0')[0].decode('latin1')
        (self.toc_size, self.unk11c, self.ext_size, self.dhsa_offset, self.dhsa_size, self.flags) = struct.unpack_from('<6I', head, 0x118)
        self.key_index = (self.flags >> 8) & 0xff
        f.seek(0)
        self.header_blob = f.read(self.data_offset)
        f.close()
        hb = self.header_blob
        toc = hb[0x130:0x130 + self.toc_size]
        if self.key_index:
            toc = rc4(RC4_KEYS[self.key_index], toc)
        self.toc_plain = toc
        self.entries = []
        for i in range(self.count):
            e = Entry()
            e.index = i
            (e.uid, e.offset, e.pak_size, e.size, e.timestamp, e.comp, e.crc, ext) = struct.unpack_from(ENTRY_FMT, toc, i * ENTRY_SIZE)
            e.ext = ext.split(b'\0')[0].decode('latin1')
            e.name = None; e.ext_ts = None; e.dhsa = None
            self.entries.append(e)
        # EXT block (plain text even in encrypted paks)
        self.ext_author = self.ext_platform = None
        if self.ext_size:
            eo = 0x130 + self.toc_size
            if hb[eo:eo + 4] != b' TXE':
                raise ValueError('EXT magic missing')
            self.ext_data_size = struct.unpack_from('<I', hb, eo + 4)[0]
            self.ext_author = hb[eo + 8:eo + 0x208].split(b'\0')[0].decode('latin1')
            self.ext_platform = hb[eo + 0x208:eo + 0x308].split(b'\0')[0].decode('latin1')
            ro = eo + 0x308
            # string offsets are absolute (community tool) or relative to the record table (official);
            # the game relocates when first offset < record_table_end (exe 0x141adda70).
            self.ext_relative = struct.unpack_from('<Q', hb, ro)[0] < ro + 16 * self.count
            # official paks (key_index != 0) also encrypt the EXT records/strings (exe callback
            # 0x1416fc5f0, algorithm not recovered) -> names stay None, uid still identifies entries.
            for e in ([] if self.key_index else self.entries):
                so, ts = struct.unpack_from('<QQ', hb, ro + 16 * e.index)
                if self.ext_relative:
                    so += ro
                e.ext_rec_raw = hb[ro + 16 * e.index: ro + 16 * e.index + 16]
                n = hb[so]
                e.name = hb[so + 1:so + 1 + n].decode('latin1')
                e.ext_ts = ts
        # DHSA block: per-entry chunk table
        if self.dhsa_size:
            o = self.dhsa_offset
            if hb[o:o + 4] != b'DHSA':
                raise ValueError('DHSA magic missing')
            self.dhsa_version, self.dhsa_count, self.dhsa_unk = struct.unpack_from('<III', hb, o + 4)
            # fixed 16-byte records: (first entry index, number of consecutive entries, group size, 0)
            # = groups of entries read together; entries themselves stay independent zlib streams.
            self.dhsa_records = [struct.unpack_from('<IIII', hb, o + 16 + 16 * i) for i in range(self.dhsa_count)]
            for r in self.dhsa_records:
                for k in range(r[0], min(self.count, r[0] + r[1])):
                    self.entries[k].dhsa = r

    def read_raw(self, e):
        with open(self.path, 'rb') as f:
            f.seek(e.offset)
            return f.read(e.pak_size)

    def read(self, e):
        raw = self.read_raw(e)
        if (e.comp >> 8) & 0x04:
            # official paks: entry payload RC4-encrypted (same key as the TOC, restarted per entry),
            # then zlib; the TOC crc is over the encrypted bytes.  Verified on Interlagos_Physics.
            raw = rc4(RC4_KEYS[self.key_index or 2], raw)
        return decompress(e, raw)


def decompress(e, raw):
    method = e.comp & 0xff
    if e.pak_size == e.size and method == 0:
        return raw
    if raw[:2] in (b'\x78\x9c', b'\x78\xda', b'\x78\x01', b'\x78\x5e'):
        return zlib.decompress(raw)
    raise NotImplementedError('compression %04x (first bytes %s)' % (e.comp, raw[:8].hex()))


def main():
    cmd, path = sys.argv[1], sys.argv[2]
    b = Bff(path)
    if cmd == 'list':
        print('name=%s ver=%x count=%d dataoff=%x align=%x flags=%x author=%s platform=%s' % (
            b.name, b.version, b.count, b.data_offset, b.alignment, b.flags, b.ext_author, b.ext_platform))
        for e in b.entries:
            print('%5d %016x %08x %10d %10d %04x %s' % (e.index, e.uid, e.offset, e.pak_size, e.size, e.comp, e.name))
    elif cmd == 'extract':
        out = sys.argv[3]; flt = sys.argv[4].lower() if len(sys.argv) > 4 else None
        n = 0
        for e in b.entries:
            nm = e.name or ('%016x.%s' % (e.uid, e.ext))
            if flt and flt not in nm.lower():
                continue
            dst = os.path.join(out, nm.replace('\\', os.sep))
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            blob = b.read(e)
            if len(blob) != e.size:
                print('SIZE MISMATCH', e)
            open(dst, 'wb').write(blob); n += 1
        print('extracted', n)


if __name__ == '__main__':
    main()
