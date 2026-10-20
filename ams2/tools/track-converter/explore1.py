import struct, sys, zlib, collections
p = sys.argv[1]
d = open(p, 'rb').read()
magic, ver, cnt, dataoff, z10, align = struct.unpack_from('<4sIIIII', d, 0)
name = d[0x18:0x118].split(b'\0')[0]
tocsz, z11c, extsz, dhsaoff, dhsasz, flags = struct.unpack_from('<6I', d, 0x118)
print(magic, hex(ver), cnt, hex(dataoff), z10, align, name, hex(tocsz), z11c, hex(extsz), hex(dhsaoff), hex(dhsasz), hex(flags))
ents = []
for i in range(cnt):
    o = 0x130 + 42 * i
    h, off, cs, us, ts, cf, crc, ext = struct.unpack_from('<QQIIQHI4s', d, o)
    ents.append((h, off, cs, us, ts, cf, crc, ext))
eo = 0x130 + tocsz
print('EXT', d[eo:eo+4], struct.unpack_from('<I', d, eo+4), d[eo+8:eo+0x208].split(b'\0')[0], d[eo+0x208:eo+0x308].split(b'\0')[0])
ro = eo + 0x308
recs = []
for i in range(cnt):
    so, h32, u = struct.unpack_from('<QII', d, ro + 16 * i)
    n = d[so]
    s = d[so+1:so+1+n].decode('latin1')
    recs.append((so, h32, u, s))
print('ext end', hex(recs[-1][0] + 1 + len(recs[-1][3])))
print(d[dhsaoff:dhsaoff+12])
cf = collections.Counter(e[5] for e in ents)
print('compflags', cf)
print('ext unk', collections.Counter(r[2] for r in recs).most_common(5))
for i in list(range(8)) + [cnt-1]:
    e = ents[i]; r = recs[i]
    print(i, hex(e[0]), hex(e[1]), hex(e[2]), hex(e[3]), hex(e[4]), e[5], hex(e[6]), e[7], '|', hex(r[0]), hex(r[1]), hex(r[2]), r[3])
# check data contiguity & zlib
bad = 0
for i, e in enumerate(ents[:50]):
    blob = d[e[1]:e[1]+e[2]]
    try:
        u = zlib.decompress(blob)
        ok = len(u) == e[3]
        c1 = zlib.crc32(u) & 0xffffffff
        c2 = zlib.crc32(blob) & 0xffffffff
        if i < 5: print(i, 'zlib ok', ok, hex(c1), hex(c2), hex(e[6]), hex(recs[i][1]))
    except Exception as ex:
        bad += 1
        if bad < 5: print(i, 'not zlib', blob[:16].hex(), e[5], e[2], e[3])
print('bad', bad)
