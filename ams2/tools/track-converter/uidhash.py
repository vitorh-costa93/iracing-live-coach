"""AMS2 / Madness pak entry UID = Bob Jenkins lookup8 'hash()' (64-bit) of the normalised name.
Recovered from AMS2AVX.exe: 0x140c73f60 (normalise: ASCII lower-case, '/' -> '\\') ->
0x140c5ebe0 (lookup8, level=0, 24-byte blocks packed big-endian from *signed* bytes)."""
M = (1 << 64) - 1
GOLDEN = 0x9e3779b97f4a7c13
EMPTY_UID = 0x8db63936938575bf  # returned for empty / NULL names


def _sb(x):
    return x - 256 if x > 127 else x


def _mix(a, b, c):
    a = (a - b - c) & M; a ^= c >> 43
    b = (b - c - a) & M; b ^= (a << 9) & M
    c = (c - a - b) & M; c ^= b >> 8
    a = (a - b - c) & M; a ^= c >> 38
    b = (b - c - a) & M; b ^= (a << 23) & M
    c = (c - a - b) & M; c ^= b >> 5
    a = (a - b - c) & M; a ^= c >> 35
    b = (b - c - a) & M; b ^= (a << 49) & M
    c = (c - a - b) & M; c ^= b >> 11
    a = (a - b - c) & M; a ^= c >> 12
    b = (b - c - a) & M; b ^= (a << 18) & M
    c = (c - a - b) & M; c ^= b >> 22
    return a, b, c


def _be(s):  # big-endian pack of signed bytes, as compiled
    v = 0
    for x in s:
        v = (v << 8) + x
    return v


def lookup8(k, level=0):
    n = len(k); a = b = level & M; c = GOLDEN; i = 0; rem = n
    while rem >= 24:
        s = [_sb(x) for x in k[i:i + 24]]
        a = (a + _be(s[0:8])) & M
        b = (b + _be(s[8:16])) & M
        c = (c + _be(s[16:24])) & M
        a, b, c = _mix(a, b, c)
        i += 24; rem -= 24
    c = (c + n) & M
    t = [_sb(x) for x in k[i:]]
    for j in range(rem - 1, -1, -1):
        if j >= 16:
            c = (c + (t[j] << (8 * (j - 15)))) & M
        elif j >= 8:
            b = (b + (t[j] << (8 * (j - 8)))) & M
        else:
            a = (a + (t[j] << (8 * j))) & M
    a, b, c = _mix(a, b, c)
    return c


def normalise(name):
    out = bytearray()
    for ch in name.encode('latin1') if isinstance(name, str) else name:
        if 0x41 <= ch <= 0x5a:
            ch += 0x20
        if ch == 0x2f:
            ch = 0x5c
        out.append(ch)
    return bytes(out)


def uid(name):
    if not name:
        return EMPTY_UID
    return lookup8(normalise(name))


if __name__ == '__main__':
    import sys
    rows = [l.split(' ', 1) for l in open(sys.argv[1]).read().splitlines() if l.strip()]
    bad = [(h, n) for h, n in rows if int(h, 16) != uid(n)]
    print('checked', len(rows), 'mismatch', len(bad), 'empty_ok', lookup8(b'') == EMPTY_UID)
    for h, n in bad[:5]:
        print(h, n, '%016x' % uid(n))
