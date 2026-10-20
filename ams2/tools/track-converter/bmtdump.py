import struct, sys
d = open(sys.argv[1], 'rb').read()
n = struct.unpack_from('<I', d, 4)[0]
ch = {}
for i in range(n):
    t, sz, off, z = struct.unpack_from('<4sIII', d, 16 + 16 * i)
    ch[t.decode()] = (off, sz)
print(ch)
def recs(tag, size):
    off, sz = ch[tag]
    return [struct.unpack_from('<%dI' % (size // 4), d, off + size * i) for i in range(sz // size)]
S = ch['STRS']
def s_at(o):
    b = d[S[0] + o:]
    return b[:b.index(b'\0')].decode('latin1')
print('HEAD', struct.unpack_from('<7I', d, ch['HEAD'][0]))
for i, r in enumerate(recs('ELMT', 28)):
    print('E%-2d' % i, ' '.join('%08x' % x for x in r))
for i, r in enumerate(recs('ATTR', 20)):
    print('A%-2d' % i, ' '.join('%08x' % x for x in r))
for i, r in enumerate(recs('COLL', 16)):
    print('C%-2d' % i, ' '.join('%08x' % x for x in r))
print('NUMB', struct.unpack_from('<%df' % (ch['NUMB'][1] // 4), d, ch['NUMB'][0]))
off = 0
strs = d[S[0]:S[0] + S[1]]
o = 0
while o < len(strs):
    e = strs.index(b'\0', o)
    if e > o:
        print('  str@%x %r' % (o, strs[o:e]))
    o = e + 1
