import sys, struct, mmap
consts = {
 'fnv64_off': 0xcbf29ce484222325, 'fnv64_prime': 0x100000001b3, 'xxh64_p1': 11400714785074694791, 'xxh64_p2': 14029467366897019727,
 'mm64a': 0xc6a4a7935bd1e995, 'mm3_c1': 0x87c37b91114253d5, 'fmix_a': 0xff51afd7ed558ccd, 'crc64_ecma_r': 0xC96C5795D7870F42,
 'crc64_ecma': 0x42F0E1EBA9EA3693, 'crc64_iso_r': 0xd800000000000000, 'city_k2': 0x9ae16a3b2f90404f, 'city_k1': 0xb492b66fbe98f273,
 'city_k0': 0xc3a5c85c97cb3127, 'kmul': 0x9ddfea08eb382d69, 'xxh3_p': 0x165667919E3779F9, 'golden64': 0x9E3779B97F4A7C15,
 'wyhash_p0': 0xa0761d6478bd642f, 'spooky': 0xdeadbeefdeadbeef, 'crc64_xz_tab1': 0xb32e4cbe03a75f6f,
}
c32 = {'fnv32_off': 0x811c9dc5, 'fnv32_prime': 0x01000193, 'crc32_tab1': 0x77073096, 'crc32c_tab1': 0xf26b8303, 'mm3_32c1': 0xcc9e2d51,
       'xxh32_p1': 2654435761, 'jenkins_golden': 0x9e3779b9, 'lookup3_deadbeef': 0xdeadbeef}
f = open(sys.argv[1], 'rb'); m = mmap.mmap(f.fileno(), 0, access=mmap.ACCESS_READ)
for k, v in consts.items():
    pat = struct.pack('<Q', v); i = m.find(pat); n = 0; locs = []
    while i != -1 and n < 50: locs.append(hex(i)); n += 1; i = m.find(pat, i + 1)
    print(k, n, locs[:6])
for k, v in c32.items():
    pat = struct.pack('<I', v); i = m.find(pat); n = 0; locs = []
    while i != -1 and n < 50: locs.append(hex(i)); n += 1; i = m.find(pat, i + 1)
    print(k, n, locs[:6])
