import sys, struct
sys.argv = ['x']
exec(open(r'D:\_ffds_temp\claude\C--Users-Vitor-Documents-iracing-live-coach\b9c08968-904a-487e-aeb8-5a3cb5c09722\scratchpad\bff_re\re.py').read().split("if __name__")[0])
o = va2off(0x14255a7e0)
X = [0xac, 0xc7, 0x91]
def deob(s):
    out = bytearray(); idx = 0; i = 0
    while i + 1 < len(s):
        a = s[i] ^ X[idx]; idx = (idx + 1) % 3
        b = s[i+1] ^ X[idx]; idx = (idx + 1) % 3
        out += bytes([b, a]); i += 2
    if i < len(s): out.append(s[i] ^ X[idx])
    return bytes(out)
keys = []
for k in range(18):
    raw = data[o + 27*k: o + 27*k + 27]
    keys.append(deob(raw[:raw.find(b'\0')]))
def rc4(key, buf):
    S = list(range(256)); j = 0
    for i in range(256):
        j = (j + S[i] + key[i % len(key)]) & 255; S[i], S[j] = S[j], S[i]
    i = j = 0; out = bytearray(len(buf))
    for n, c in enumerate(buf):
        i = (i + 1) & 255; j = (j + S[i]) & 255; S[i], S[j] = S[j], S[i]
        out[n] = c ^ S[(S[i] + S[j]) & 255]
    return bytes(out)
pak = open(r'C:\Program Files (x86)\Steam\steamapps\common\Automobilista 2\Pakfiles\TRACKS\SUM_interlagos_1991.bff', 'rb').read()
dataoff = struct.unpack_from('<I', pak, 0x0C)[0]
for k, key in enumerate(keys):
    for kk, name in ((key, 'str'), (key + b'\0', 'str0')):
        dec = rc4(kk, pak[0x130:0x130 + 84])
        off = struct.unpack_from('<Q', dec, 8)[0]
        if off == dataoff or dec[38:42].rstrip(b'\0').isalpha():
            print('KEY', k, name, kk, dec.hex())
print(keys)
key = keys[2]
cnt = struct.unpack_from('<I', pak, 8)[0]; tocsz, z, extsz, dho, dhs, fl = struct.unpack_from('<6I', pak, 0x118)
print(cnt, hex(tocsz), hex(extsz), hex(dho), hex(dhs), hex(fl))
eo = 0x130 + tocsz
print('ext raw', pak[eo:eo+16].hex(), 'dhsa raw', pak[dho:dho+16].hex())
print('ext fresh', rc4(key, pak[eo:eo+16]))
print('ext cont', rc4(key, pak[0x130:eo+16])[-16:])
print('dh fresh', rc4(key, pak[dho:dho+16]))
