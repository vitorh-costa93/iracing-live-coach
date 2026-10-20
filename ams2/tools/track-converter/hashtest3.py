import sys
sys.path.insert(0, r'D:\_ffds_temp\claude\C--Users-Vitor-Documents-iracing-live-coach\b9c08968-904a-487e-aeb8-5a3cb5c09722\scratchpad\bff_re')
from hashtest2 import T, variants, M64, M32
P = 0x42F0E1EBA9EA3693
TAB = []
for i in range(256):
    c = i << 56
    for _ in range(8): c = ((c << 1) & M64) ^ (P if c >> 63 else 0)
    TAB.append(c)
def A(b, init, xo):
    c = init
    for x in b: c = TAB[((c >> 56) ^ x) & 0xff] ^ ((c << 8) & M64)
    return c ^ xo
def B(b, init, xo):
    c = init
    for x in b: c = TAB[(c ^ x) & 0xff] ^ (c >> 8)
    return c ^ xo
def fnv1_64(b):
    h = 0xcbf29ce484222325
    for c in b: h = ((h * 0x100000001b3) & M64) ^ c
    return h
for s, h in T:
    tg = {h, int.from_bytes(h.to_bytes(8, 'little'), 'big')}
    for nm, v in variants(s):
        for init in (0, M64):
            for xo in (0, M64):
                for fn in (A, B):
                    r = fn(v, init, xo)
                    if r in tg: print('HIT', s, nm, fn.__name__, hex(init), hex(xo))
                    if (r & M32) in (h & M32, h >> 32) or (r >> 32) in (h & M32, h >> 32): print('HALF', s, nm, fn.__name__)
        r = fnv1_64(v)
        if r in tg: print('HIT fnv1', s, nm)
print('done')
