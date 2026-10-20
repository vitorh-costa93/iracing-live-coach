"""Read-only validation of the BFF reader over many paks: per entry check CRC (=~crc32 of stored bytes),
decrypt/decompress and compare to declared size.  usage: python -I validate.py <maxbytes_per_entry> pak...
(entries larger than maxbytes are only CRC-checked, to keep pure-python RC4 fast)"""
import sys, os, zlib, collections
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from bff import Bff

lim = int(sys.argv[1])
tot = collections.Counter()
for p in sys.argv[2:]:
    b = Bff(p)
    c = collections.Counter()
    for e in b.entries:
        raw = b.read_raw(e)
        c['crc_ok' if ((~zlib.crc32(raw)) & 0xffffffff) == e.crc else 'crc_bad'] += 1
        c['comp_%04x' % e.comp] += 1
        if e.pak_size > lim:
            c['skipped'] += 1; continue
        try:
            d = b.read(e)
            c['ok' if len(d) == e.size else 'size_bad'] += 1
        except Exception as ex:
            c['err:' + type(ex).__name__] += 1
    for r in getattr(b, 'dhsa_records', []):
        grp = b.entries[r[0]:r[0] + r[1]]
        c['dhsa_sum=size' if r[2] == sum(x.size for x in grp) else
          'dhsa_sum=pak' if r[2] == sum(x.pak_size for x in grp) else 'dhsa_other'] += 1
    tot.update(c)
    print('%-40s n=%5d flags=%x %s' % (os.path.basename(p), b.count, b.flags, dict(sorted(c.items()))))
print('TOTAL', dict(sorted(tot.items())))
