import sys; sys.path.insert(0, r'D:\_ffds_temp\claude\C--Users-Vitor-Documents-iracing-live-coach\b9c08968-904a-487e-aeb8-5a3cb5c09722\scratchpad\bff_re')
from lookup2 import lookup2_madness as L
BS = chr(92)
rows = [l.split(' ', 1) for l in open(sys.argv[1]).read().splitlines()[:5]]
for h, n in rows:
    h = int(h, 16); lo, hi = h & 0xffffffff, h >> 32
    for nm, v in [('raw', n), ('up', n.upper()), ('fwd', n.replace(BS, '/')), ('upfwd', n.upper().replace(BS, '/'))]:
        b = v.encode()
        for init in (0, 0xffffffff, lo, hi):
            r = L(b, init)
            if r in (lo, hi): print('HIT', n, nm, hex(init), hex(r), 'lo' if r == lo else 'hi')
print('ok')
