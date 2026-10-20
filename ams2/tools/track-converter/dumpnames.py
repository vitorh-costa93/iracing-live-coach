import struct, sys
d = open(sys.argv[1], 'rb').read()
cnt = struct.unpack_from('<I', d, 8)[0]; tocsz = struct.unpack_from('<I', d, 0x118)[0]; eo = 0x130 + tocsz + 0x308
for i in range(cnt):
    h = struct.unpack_from('<Q', d, 0x130 + 42 * i)[0]; so = struct.unpack_from('<Q', d, eo + 16 * i)[0]
    print('%016x %s' % (h, d[so+1:so+1+d[so]].decode('latin1')))
