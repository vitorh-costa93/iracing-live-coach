"""BFF writer (community / unencrypted layout, same as the Sochi mod).  See SPEC.md.
API:  write_bff(path, pak_name, items)  items: list of dict(name, data=<uncompressed bytes> | stored=<zlib bytes>,
      size=<uncompressed size, needed with stored>, timestamp=<packed u64, optional>)
CLI:  python -I bff_write.py repack <src.bff> <dst.bff>        (reuse stored streams -> must be byte-identical)
      python -I bff_write.py recompress <src.bff> <dst.bff>    (decompress + zlib level 6 -> semantically equal)
      python -I bff_write.py pack <dst.bff> <pak_name> <rootdir> (every file under rootdir, name = relative path)
"""
import os, struct, sys, time, zlib
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from uidhash import uid

VERSION = 0x10400004
DHSA_VERSION = 0x10000000
ALIGN = 0x10


def pack_time(t=None):
    """Packed timestamp: year<<48 | month<<44 | day<<39 | hour<<34 | min<<28 | sec<<22 | ms<<12.
    Layout inferred from the year (0x7ea) / month nibble and ext_ts = ts>>12; field widths below the
    month are an assumption (the game is not known to use them)."""
    lt = time.localtime(t)
    ms = int(((t or time.time()) % 1) * 1000)
    return (lt.tm_year << 48) | (lt.tm_mon << 44) | (lt.tm_mday << 39) | (lt.tm_hour << 34) | \
           (lt.tm_min << 28) | (lt.tm_sec << 22) | (ms << 12)


def _pad(n, a):
    return (-n) % a


def build_bff(pak_name, items, author='Reiza.xml', platform='PC'):
    rows = []
    for it in items:
        name = it['name'].replace('/', chr(92))
        if 'stored' in it:
            stored, size = it['stored'], it['size']
        else:
            stored, size = zlib.compress(it['data'], 6), len(it['data'])
        ext = name.rsplit('.', 1)[-1].encode('latin1')[:4] if '.' in name else b''
        rows.append(dict(name=name, stored=stored, size=size, ts=it.get('timestamp') or pack_time(),
                         ext=ext, uid=uid(name)))
    rows.sort(key=lambda r: r['name'].encode('latin1'))   # Sochi order = ordinal (case-sensitive) byte order
    n = len(rows)
    toc_size = 42 * n
    eo = 0x130 + toc_size
    names = [r['name'].encode('latin1') for r in rows]
    if any(len(x) > 255 for x in names):
        raise ValueError('name > 255 bytes')
    rec_off = eo + 0x308
    str_off = rec_off + 16 * n
    strings = b''.join(bytes([len(x)]) + x for x in names)
    ext_data = 16 * n + len(strings)
    ext_size = 0x308 + ext_data
    dhsa_offset = eo + ext_size + _pad(ext_data, 16)     # records+strings padded to 16
    dhsa_size = 16 + 16 * n
    data_offset = dhsa_offset + dhsa_size
    data_offset += _pad(data_offset, ALIGN)
    # data layout
    off = data_offset
    for r in rows:
        r['offset'] = off
        off += len(r['stored']); off += _pad(off, ALIGN)
    out = bytearray(off)
    hdr = struct.pack('<4sIIIII', b' KAP', VERSION, n, data_offset, 0, ALIGN)
    hdr += pak_name.encode('latin1')[:0xff].ljust(0x100, b'\0')
    hdr += struct.pack('<6I', toc_size, 0, ext_size, dhsa_offset, dhsa_size, 0)
    out[0:0x130] = hdr
    for i, r in enumerate(rows):
        crc = (~zlib.crc32(r['stored'])) & 0xffffffff
        struct.pack_into('<QQIIQHI4s', out, 0x130 + 42 * i, r['uid'], r['offset'], len(r['stored']), r['size'],
                         r['ts'], 1, crc, r['ext'].ljust(4, b'\0'))
    out[eo:eo + 8] = b' TXE' + struct.pack('<I', ext_data)
    out[eo + 8:eo + 0x208] = author.encode('latin1').ljust(0x200, b'\0')
    out[eo + 0x208:eo + 0x308] = platform.encode('latin1').ljust(0x100, b'\0')
    so = str_off
    for i, r in enumerate(rows):
        struct.pack_into('<QQ', out, rec_off + 16 * i, so, r['ts'] >> 12)   # absolute string offsets
        so += 1 + len(names[i])
    out[str_off:str_off + len(strings)] = strings
    struct.pack_into('<4sIII', out, dhsa_offset, b'DHSA', DHSA_VERSION, n, 0)
    for i, r in enumerate(rows):   # one group per entry, size = stored size (as in Sochi)
        struct.pack_into('<IIII', out, dhsa_offset + 16 + 16 * i, i, 1, len(r['stored']), 0)
    for r in rows:
        out[r['offset']:r['offset'] + len(r['stored'])] = r['stored']
    return bytes(out)


def write_bff(path, pak_name, items, **kw):
    blob = build_bff(pak_name, items, **kw)
    with open(path, 'wb') as f:
        f.write(blob)
    return len(blob)


def main():
    from bff import Bff
    cmd = sys.argv[1]
    if cmd in ('repack', 'recompress'):
        src, dst = sys.argv[2], sys.argv[3]
        b = Bff(src)
        items = []
        for e in b.entries:
            it = dict(name=e.name, timestamp=e.timestamp)
            if cmd == 'repack':
                it.update(stored=b.read_raw(e), size=e.size)
            else:
                it.update(data=b.read(e))
            items.append(it)
        write_bff(dst, b.name, items)
        a, c = open(src, 'rb').read(), open(dst, 'rb').read()
        if a == c:
            print('IDENTICAL', len(a))
        else:
            diff = next((i for i in range(min(len(a), len(c))) if a[i] != c[i]), None)
            print('DIFFER len %d vs %d, first diff at %s' % (len(a), len(c), hex(diff) if diff is not None else None))
            b2 = Bff(dst)
            ok = all(b2.read(x) == b.read(y) and x.name == y.name and x.uid == y.uid for x, y in zip(b2.entries, b.entries))
            print('roundtrip content equal:', ok and b2.count == b.count)
    elif cmd == 'pack':
        dst, pak_name, root = sys.argv[2], sys.argv[3], sys.argv[4]
        items = []
        for dp, _, fs in os.walk(root):
            for f in fs:
                full = os.path.join(dp, f)
                rel = os.path.relpath(full, root).replace(os.sep, chr(92))
                items.append(dict(name=rel, data=open(full, 'rb').read(), timestamp=pack_time(os.path.getmtime(full))))
        print('wrote', write_bff(dst, pak_name, items), 'bytes,', len(items), 'entries')


if __name__ == '__main__':
    main()
