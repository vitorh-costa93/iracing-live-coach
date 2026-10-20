import struct, sys, collections, itertools
d = open(sys.argv[1], 'rb').read()
cnt = struct.unpack_from('<I', d, 8)[0]
tocsz = struct.unpack_from('<I', d, 0x118)[0]
eo = 0x130 + tocsz + 0x308
H = {}
for i in range(cnt):
    h = struct.unpack_from('<Q', d, 0x130 + 42 * i)[0]
    so = struct.unpack_from('<Q', d, eo + 16 * i)[0]
    H[d[so+1:so+1+d[so]]] = h
bylen = collections.defaultdict(list)
for s in H: bylen[len(s)].append(s)
tested = lin = 0
for L, ss in bylen.items():
    ss = ss[:60]
    xs = {}
    for a, b in itertools.combinations(ss, 2):
        x = bytes(p ^ q for p, q in zip(a, b))
        if x in xs:
            c, e = xs[x]
            if len({a, b, c, e}) == 4:
                tested += 1
                if H[a] ^ H[b] ^ H[c] ^ H[e] == 0: lin += 1
                elif tested < 4: print(a, b, c, e, hex(H[a] ^ H[b] ^ H[c] ^ H[e]))
        else:
            xs[x] = (a, b)
print('tested', tested, 'linear', lin)
for s in list(H)[:3]: print(s, hex(H[s]))
