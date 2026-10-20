"""dump a binary as 4-byte words: float if plausible, else hex; ascii column.  usage: hexf.py file [start] [len]"""
import struct, sys
d = open(sys.argv[1], 'rb').read()
s = int(sys.argv[2], 16) if len(sys.argv) > 2 else 0
n = int(sys.argv[3], 16) if len(sys.argv) > 3 else len(d)
print('len', len(d))
for o in range(s, min(len(d), s + n), 16):
    w = d[o:o + 16]
    w4 = w[:len(w) // 4 * 4]
    cells = []
    for x, i in zip(struct.unpack_from('<%df' % (len(w4) // 4), w4), struct.unpack_from('<%dI' % (len(w4) // 4), w4)):
        cells.append('%10.4f' % x if (1e-4 < abs(x) < 1e5) else '%10s' % hex(i))
    asc = ''.join(chr(c) if 32 <= c < 127 else '.' for c in w)
    print('%05x %s  %s' % (o, ' '.join(cells), asc))
