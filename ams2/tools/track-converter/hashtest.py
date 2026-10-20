import struct, sys, zlib, hashlib
p = sys.argv[1]
d = open(p, 'rb').read()
cnt = struct.unpack_from('<I', d, 8)[0]
tocsz, _, extsz = struct.unpack_from('<3I', d, 0x118)
eo = 0x130 + tocsz + 0x308
items = []
for i in range(min(cnt, 6)):
    h, off, cs, us, ts, cf, crc, ext = struct.unpack_from('<QQIIQHI4s', d, 0x130 + 42 * i)
    so = struct.unpack_from('<Q', d, eo + 16 * i)[0]
    s = d[so+1:so+1+d[so]]
    blob = d[off:off+cs]
    items.append((h, crc, s, blob))

M64 = (1 << 64) - 1
def fnv1a64(b):
    h = 0xcbf29ce484222325
    for c in b: h = ((h ^ c) * 0x100000001b3) & M64
    return h
def fnv164(b):
    h = 0xcbf29ce484222325
    for c in b: h = ((h * 0x100000001b3) & M64) ^ c
    return h
def fnv1a32(b):
    h = 0x811c9dc5
    for c in b: h = ((h ^ c) * 0x01000193) & 0xffffffff
    return h
def fnv132(b):
    h = 0x811c9dc5
    for c in b: h = ((h * 0x01000193) & 0xffffffff) ^ c
    return h
def crc64ecma(b, poly=0xC96C5795D7870F42, init=M64, xo=M64):
    c = init
    for x in b:
        c ^= x
        for _ in range(8):
            c = (c >> 1) ^ (poly if c & 1 else 0)
    return c ^ xo
def jenkins_oaat(b):
    h = 0
    for c in b:
        h = (h + c) & 0xffffffff; h = (h + (h << 10)) & 0xffffffff; h ^= h >> 6
    h = (h + (h << 3)) & 0xffffffff; h ^= h >> 11; h = (h + (h << 15)) & 0xffffffff
    return h
def variants(s):
    for nm, v in [('raw', s), ('lower', s.lower()), ('upper', s.upper()), ('fwd', s.replace(b'\\', b'/')),
                  ('fwdlow', s.lower().replace(b'\\', b'/')), ('fwdup', s.upper().replace(b'\\', b'/'))]:
        yield nm, v
        yield nm + '+0', v + b'\0'
for h, crc, s, blob in items:
    u = zlib.decompress(blob)
    print(s, hex(h), hex(crc), 'adler', hex(struct.unpack('>I', blob[-4:])[0]))
    tgt64 = {h}; tgt32 = {crc, h & 0xffffffff, h >> 32}
    for nm, v in variants(s):
        for fn in (fnv1a64, fnv164, crc64ecma, lambda b: crc64ecma(b, 0xd800000000000000, 0, 0), lambda b: crc64ecma(b, 0x95AC9329AC4BC9B5, M64, M64)):
            r = fn(v)
            if r in tgt64 or (r & 0xffffffff) in tgt32 or (r >> 32) in tgt32: print('  HIT64', nm, fn, hex(r))
        for fn in (fnv1a32, fnv132, jenkins_oaat, lambda b: zlib.crc32(b) & 0xffffffff, lambda b: zlib.adler32(b) & 0xffffffff,
                   lambda b: (~zlib.crc32(b)) & 0xffffffff):
            r = fn(v)
            if r in tgt32: print('  HIT32', nm, hex(r))
        for alg in ('md5', 'sha1', 'sha256'):
            dg = hashlib.new(alg, v).digest()
            for k in range(0, len(dg) - 7):
                if struct.unpack_from('<Q', dg, k)[0] == h or struct.unpack_from('>Q', dg, k)[0] == h: print('  HITdig', alg, nm, k)
    for nm, v in [('u', u), ('c', blob)]:
        for fn in (fnv1a32, jenkins_oaat, lambda b: zlib.crc32(b) & 0xffffffff, lambda b: zlib.adler32(b) & 0xffffffff, lambda b: (~zlib.crc32(b)) & 0xffffffff):
            if fn(v) in tgt32: print('  HITDATA', nm, fn)
