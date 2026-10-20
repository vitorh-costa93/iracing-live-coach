import struct, zlib, binascii
M64 = (1 << 64) - 1; M32 = 0xffffffff
T = [(b'tracks\\_data\\omtt\\seasonal.bmt', 0x8011ba1d22e5b071), (b'tracks\\_data\\omtt\\seasonal.meb', 0x0aaa51ea63b9aca7),
     (b'cameras\\cameraconfig.xml', 0x66d0064f2e79ae72)]
def rotl64(x, r): return ((x << r) | (x >> (64 - r))) & M64
def xxh64(b, seed=0):
    P1, P2, P3, P4, P5 = 11400714785074694791, 14029467366897019727, 1609587929392839161, 9650029242287828579, 2870177450012600261
    n = len(b); i = 0
    if n >= 32:
        v1 = (seed + P1 + P2) & M64; v2 = (seed + P2) & M64; v3 = seed; v4 = (seed - P1) & M64
        def rnd(a, x): a = (a + x * P2) & M64; a = rotl64(a, 31); return (a * P1) & M64
        while i + 32 <= n:
            v1 = rnd(v1, struct.unpack_from('<Q', b, i)[0]); v2 = rnd(v2, struct.unpack_from('<Q', b, i+8)[0])
            v3 = rnd(v3, struct.unpack_from('<Q', b, i+16)[0]); v4 = rnd(v4, struct.unpack_from('<Q', b, i+24)[0]); i += 32
        h = (rotl64(v1, 1) + rotl64(v2, 7) + rotl64(v3, 12) + rotl64(v4, 18)) & M64
        for v in (v1, v2, v3, v4):
            h ^= rnd(0, v); h = (h * P1 + P4) & M64
    else:
        h = (seed + P5) & M64
    h = (h + n) & M64
    while i + 8 <= n:
        k = struct.unpack_from('<Q', b, i)[0]; k = (k * P2) & M64; k = rotl64(k, 31); k = (k * P1) & M64
        h ^= k; h = (rotl64(h, 27) * P1 + P4) & M64; i += 8
    if i + 4 <= n:
        h ^= (struct.unpack_from('<I', b, i)[0] * P1) & M64; h = (rotl64(h, 23) * P2 + P3) & M64; i += 4
    while i < n:
        h ^= (b[i] * P5) & M64; h = (rotl64(h, 11) * P1) & M64; i += 1
    h ^= h >> 33; h = (h * P2) & M64; h ^= h >> 29; h = (h * P3) & M64; h ^= h >> 32
    return h
def murmur64a(b, seed=0):
    m = 0xc6a4a7935bd1e995; r = 47; n = len(b)
    h = (seed ^ (n * m)) & M64; i = 0
    while i + 8 <= n:
        k = struct.unpack_from('<Q', b, i)[0]; k = (k * m) & M64; k ^= k >> r; k = (k * m) & M64
        h ^= k; h = (h * m) & M64; i += 8
    rem = b[i:]
    for j in range(len(rem) - 1, -1, -1):
        h ^= rem[j] << (8 * j)
    if rem: h = (h * m) & M64
    h ^= h >> r; h = (h * m) & M64; h ^= h >> r
    return h
def fmix64(k):
    k ^= k >> 33; k = (k * 0xff51afd7ed558ccd) & M64; k ^= k >> 33; k = (k * 0xc4ceb9fe1a85ec53) & M64; k ^= k >> 33; return k
def murmur3_128(b, seed=0):
    c1, c2 = 0x87c37b91114253d5, 0x4cf5ad432745937f; n = len(b); h1 = h2 = seed; i = 0
    while i + 16 <= n:
        k1, k2 = struct.unpack_from('<QQ', b, i)
        k1 = (k1 * c1) & M64; k1 = rotl64(k1, 31); k1 = (k1 * c2) & M64; h1 ^= k1
        h1 = rotl64(h1, 27); h1 = (h1 + h2) & M64; h1 = (h1 * 5 + 0x52dce729) & M64
        k2 = (k2 * c2) & M64; k2 = rotl64(k2, 33); k2 = (k2 * c1) & M64; h2 ^= k2
        h2 = rotl64(h2, 31); h2 = (h2 + h1) & M64; h2 = (h2 * 5 + 0x38495ab5) & M64; i += 16
    t = b[i:]; k1 = k2 = 0
    for j in range(len(t) - 1, 7, -1): k2 ^= t[j] << (8 * (j - 8))
    if len(t) > 8:
        k2 = (k2 * c2) & M64; k2 = rotl64(k2, 33); k2 = (k2 * c1) & M64; h2 ^= k2
    for j in range(min(len(t), 8) - 1, -1, -1): k1 ^= t[j] << (8 * j)
    if len(t) > 0:
        k1 = (k1 * c1) & M64; k1 = rotl64(k1, 31); k1 = (k1 * c2) & M64; h1 ^= k1
    h1 ^= n; h2 ^= n; h1 = (h1 + h2) & M64; h2 = (h2 + h1) & M64
    h1 = fmix64(h1); h2 = fmix64(h2); h1 = (h1 + h2) & M64; h2 = (h2 + h1) & M64
    return h1, h2
def fnv1a64(b):
    h = 0xcbf29ce484222325
    for c in b: h = ((h ^ c) * 0x100000001b3) & M64
    return h
def crc64(b, poly, init, xo, refl=True):
    c = init
    if refl:
        for x in b:
            c ^= x
            for _ in range(8): c = (c >> 1) ^ (poly if c & 1 else 0)
    else:
        for x in b:
            c ^= x << 56
            for _ in range(8): c = ((c << 1) & M64) ^ (poly if c >> 63 else 0)
    return c ^ xo
def djb2_64(b):
    h = 5381
    for c in b: h = (h * 33 + c) & M64
    return h
def sdbm64(b):
    h = 0
    for c in b: h = (c + (h << 6) + (h << 16) - h) & M64
    return h
def crc32_variants(b):
    yield 'crc32', zlib.crc32(b) & M32
    yield '~crc32', (~zlib.crc32(b)) & M32
    yield 'crc32_noxor', (zlib.crc32(b) ^ M32) & M32
def variants(s):
    base = {'raw': s, 'lower': s.lower(), 'upper': s.upper()}
    for k, v in list(base.items()):
        base[k + '/'] = v.replace(b'\\', b'/')
    for k, v in list(base.items()):
        stem = v.rsplit(b'.', 1)[0]
        base[k + '-noext'] = stem
        base[k + '-file'] = v.replace(b'/', b'\\').rsplit(b'\\', 1)[-1]
    for k, v in list(base.items()):
        yield k, v
        yield k + '\\0', v + b'\0'
        for w in ('utf-16-le',):
            yield k + 'u16', v.decode().encode(w)
crcs = [(0x42F0E1EBA9EA3693, 0, 0, False), (0x42F0E1EBA9EA3693, M64, M64, False), (0xC96C5795D7870F42, M64, M64, True),
        (0xC96C5795D7870F42, 0, 0, True), (0xd800000000000000, 0, 0, True), (0x000000000000001B, 0, 0, False), (0x9A6C9329AC4BC9B5, M64, M64, True),
        (0x95AC9329AC4BC9B5, M64, M64, True), (0xC96C5795D7870F42, M64, 0, True), (0x42F0E1EBA9EA3693, M64, 0, False)]
for s, h in T:
    lo, hi = h & M32, h >> 32
    tg = {h, ((h & M32) << 32) | (h >> 32), int.from_bytes(h.to_bytes(8, 'little'), 'big')}
    for nm, v in variants(s):
        cands = {'xxh64': xxh64(v), 'mm64a': murmur64a(v), 'mm3lo': murmur3_128(v)[0], 'mm3hi': murmur3_128(v)[1], 'fnv1a64': fnv1a64(v),
                 'djb2': djb2_64(v), 'sdbm': sdbm64(v)}
        for seed in (0xFFFFFFFF, 0x9747b28c, 0x2a, 1):
            cands['xxh64s%x' % seed] = xxh64(v, seed); cands['mm64as%x' % seed] = murmur64a(v, seed)
        for j, c in enumerate(crcs): cands['crc64_%d' % j] = crc64(v, *c)
        for k, r in cands.items():
            if r in tg: print('HIT', s, nm, k)
            if (r & M32) in (lo, hi) or (r >> 32) in (lo, hi): print('HALF', s, nm, k)
        for k, r in crc32_variants(v):
            if r in (lo, hi): print('HALF32', s, nm, k, hex(r))
print('done')
