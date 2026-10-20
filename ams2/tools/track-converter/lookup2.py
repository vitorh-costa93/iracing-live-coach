M = 0xffffffff


def _sb(x):  # movsx byte
    return x - 256 if x > 127 else x


def mix(a, b, c):
    a = (a - b - c) & M; a ^= c >> 13
    b = (b - c - a) & M; b ^= (a << 8) & M
    c = (c - a - b) & M; c ^= b >> 13
    a = (a - b - c) & M; a ^= c >> 12
    b = (b - c - a) & M; b ^= (a << 16) & M
    c = (c - a - b) & M; c ^= b >> 5
    a = (a - b - c) & M; a ^= c >> 3
    b = (b - c - a) & M; b ^= (a << 10) & M
    c = (c - a - b) & M; c ^= b >> 15
    return a, b, c


def lookup2_madness(k, initval=0):
    """Bob Jenkins lookup2 as compiled in AMS2 (exe 0x140c5e920): main loop packs 12-byte
    blocks big-endian, tail is the standard little-endian switch; bytes are sign-extended."""
    n = len(k); a = b = 0x9e3779b9; c = initval & M; i = 0; rem = n
    while rem >= 12:
        s = [_sb(x) for x in k[i:i + 12]]
        a = (a + ((s[0] << 24) + (s[1] << 16) + (s[2] << 8) + s[3])) & M
        b = (b + ((s[4] << 24) + (s[5] << 16) + (s[6] << 8) + s[7])) & M
        c = (c + ((s[8] << 24) + (s[9] << 16) + (s[10] << 8) + s[11])) & M
        a, b, c = mix(a, b, c)
        i += 12; rem -= 12
    c = (c + n) & M
    t = [_sb(x) for x in k[i:]]
    if rem >= 11: c = (c + (t[10] << 24)) & M
    if rem >= 10: c = (c + (t[9] << 16)) & M
    if rem >= 9: c = (c + (t[8] << 8)) & M
    if rem >= 8: b = (b + (t[7] << 24)) & M
    if rem >= 7: b = (b + (t[6] << 16)) & M
    if rem >= 6: b = (b + (t[5] << 8)) & M
    if rem >= 5: b = (b + t[4]) & M
    if rem >= 4: a = (a + (t[3] << 24)) & M
    if rem >= 3: a = (a + (t[2] << 16)) & M
    if rem >= 2: a = (a + (t[1] << 8)) & M
    if rem >= 1: a = (a + t[0]) & M
    a, b, c = mix(a, b, c)
    return c
