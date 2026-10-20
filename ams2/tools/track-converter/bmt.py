"""BMT = binary XML ('BLMY') compiled from a material .mtx (XML).  See SPEC.md.
usage: python -I bmt.py check <mtx_dir> <bmt_dir>   encode every .mtx and compare to the .bmt byte-for-byte
       python -I bmt.py encode <in.mtx> <out.bmt>
       python -I bmt.py decode <in.bmt>               print reconstructed XML
"""
import os, struct, sys
import xml.etree.ElementTree as ET

M32 = 0xffffffff
NONE = 0xffffffff


def name_hash(s):
    """h = rotl(h,5)*31 + c  (32-bit); single chars hash to their own code."""
    h = 0
    for c in s.encode('latin1'):
        h = ((((h << 5) | (h >> 27)) & M32) * 31 + c) & M32
    return h


def _classify(v):
    if v in ('true', 'false'):
        return 1, v == 'true'
    try:
        float(v)
        if v.strip() and v.strip().lower() not in ('nan', 'inf', '-inf', 'infinity'):
            return 0, float(v)
    except ValueError:
        pass
    return 2, v


def encode(root, packed_strings=False):
    """packed_strings=False: OMTT style (each string 4-aligned, HEAD[4]=STRS bytes);
    True: alternate writer seen in 15/297 Sochi files (no per-string padding, HEAD[4]=string count,
    STRS chunk padded to 4)."""
    elems, attrs, nums, bools = [], [], [], []
    strs = bytearray(); str_off = {}

    def add_str(s):
        if s not in str_off:
            str_off[s] = len(strs)
            strs.extend(s.encode('latin1') + b'\0')
            if not packed_strings:
                strs.extend(b'\0' * ((-len(strs)) % 4))
        return str_off[s]

    def emit_attrs(node):
        for k, v in node.attrib.items():
            t, val = _classify(v)
            if t == 0:
                ref = len(nums); nums.append(val)
            elif t == 1:
                ref = len(bools); bools.append(val)
            else:
                ref = add_str(val)
            attrs.append([name_hash(k), t, ref, 1, NONE])

    def walk(node, idx):
        # node already appended at idx; children appended (pre-order) then own attributes (post-order)
        elems[idx][1] = len(attrs)
        prev = None
        kids = list(node)
        for ch in kids:
            ci = len(elems)
            elems.append([name_hash(ch.tag), 0, len(ch.attrib), len(list(ch)), NONE, NONE, NONE])
            if prev is None:
                elems[idx][4] = ci
            else:
                elems[prev][5] = ci
            prev = ci
            walk(ch, ci)
        emit_attrs(node)

    # root goes last: process children into the list first
    root_rec = [name_hash(root.tag), 0, len(root.attrib), len(list(root)), NONE, NONE, NONE]
    elems.append(root_rec)            # temporary slot 0
    walk(root, 0)
    # move root to the end and fix indices (all child indices shift by -1)
    rest = elems[1:]
    fix = lambda i: NONE if i == NONE else i - 1
    for e in rest:
        e[4], e[5] = fix(e[4]), fix(e[5])
    root_rec[4] = fix(root_rec[4])
    elems = rest + [root_rec]
    # collections: element names then attribute names, each sorted by hash
    def coll(items):
        seen = {}
        for i, it in enumerate(items):
            if it[0] not in seen:
                seen[it[0]] = [i, 0]
            seen[it[0]][1] += 1
        return [[h, s, c, NONE] for h, (s, c) in sorted(seen.items())]
    colls = coll(elems) + coll(attrs)
    if packed_strings:
        strs.extend(b'\0' * ((-len(strs)) % 4))
    bool_bytes = bytearray((len(bools) + 7) // 8)
    for i, b in enumerate(bools):
        if b:
            bool_bytes[i // 8] |= 1 << (i % 8)
    chunks = [
        (b'HEAD', struct.pack('<7I', len(elems), len(attrs), len(colls), len(nums), len(str_off) if packed_strings else len(strs), len(bools), 0)),
        (b'ELMT', b''.join(struct.pack('<7I', *e) for e in elems)),
        (b'ATTR', b''.join(struct.pack('<5I', *a) for a in attrs)),
        (b'COLL', b''.join(struct.pack('<4I', *c) for c in colls)),
        (b'NUMB', struct.pack('<%df' % len(nums), *nums)),
        (b'BOOL', bytes(bool_bytes)),
        (b'STRS', bytes(strs)),
    ]
    off = 16 + 16 * len(chunks)
    table = b''; body = b''
    for tag, data in chunks:
        table += struct.pack('<4sIII', tag, len(data), off + len(body), 0)
        body += data + b'\0' * ((-len(data)) % 4)
    total = off + len(body)
    # observed: the final chunk (STRS) is not padded beyond its size
    last_pad = (-len(chunks[-1][1])) % 4
    total -= last_pad; body = body[:len(body) - last_pad] if last_pad else body
    return struct.pack('<4sIII', b'BLMY', len(chunks), total, 0) + table + body


def encode_file(path, packed_strings=False):
    return encode(ET.parse(path).getroot(), packed_strings)


def decode(d, names=()):
    """best-effort decode back to an XML string (names resolved from a hash dictionary)."""
    n = struct.unpack_from('<I', d, 4)[0]
    ch = {}
    for i in range(n):
        t, sz, off, _ = struct.unpack_from('<4sIII', d, 16 + 16 * i)
        ch[t] = d[off:off + sz]
    hd = struct.unpack('<7I', ch[b'HEAD'])
    E = [struct.unpack_from('<7I', ch[b'ELMT'], 28 * i) for i in range(hd[0])]
    A = [struct.unpack_from('<5I', ch[b'ATTR'], 20 * i) for i in range(hd[1])]
    N = struct.unpack('<%df' % hd[3], ch[b'NUMB'])
    S = ch[b'STRS']; B = ch[b'BOOL']
    dic = {name_hash(x): x for x in names}
    nm = lambda h: dic.get(h, '_%08x' % h)

    def val(a):
        if a[1] == 0:
            return repr(N[a[2]])
        if a[1] == 1:
            return 'true' if B[a[2] // 8] >> (a[2] % 8) & 1 else 'false'
        e = S.index(b'\0', a[2]); return S[a[2]:e].decode('latin1')

    def own_attrs(i):
        e = E[i]
        sub = e[1]
        # own attrs follow the attrs of the whole subtree
        def count(j):
            c = E[j][2]; k = E[j][4]
            while k != NONE:
                c += count(k); k = E[k][5]
            return c
        start = sub + count(i) - e[2]
        return A[start:start + e[2]]

    def out(i, ind):
        e = E[i]
        at = ''.join(' %s="%s"' % (nm(a[0]), val(a)) for a in own_attrs(i))
        if e[4] == NONE:
            return '%s<%s%s />\n' % (ind, nm(e[0]), at)
        s = '%s<%s%s>\n' % (ind, nm(e[0]), at)
        k = e[4]
        while k != NONE:
            s += out(k, ind + '  '); k = E[k][5]
        return s + '%s</%s>\n' % (ind, nm(e[0]))
    return out(len(E) - 1, '')


KNOWN = ('material shaderparam value type depthparams enabled writeenabled alphablendparams name VERSION shader '
         'technique supportsSpecialisedLighting fog antialias numparams cull v t e w').split()


def main():
    cmd = sys.argv[1]
    if cmd == 'encode':
        open(sys.argv[3], 'wb').write(encode_file(sys.argv[2]))
    elif cmd == 'decode':
        print(decode(open(sys.argv[2], 'rb').read(), KNOWN))
    elif cmd == 'check':
        md, bd = sys.argv[2], sys.argv[3]
        ok = bad = miss = alt = 0; shown = 0
        lower = {f.lower(): f for f in os.listdir(bd)}
        for f in sorted(os.listdir(md)):
            if not f.lower().endswith('.mtx'):
                continue
            b = lower.get(f[:-4].lower() + '.bmt')
            if not b:
                miss += 1; continue
            ref = open(os.path.join(bd, b), 'rb').read()
            try:
                got = encode_file(os.path.join(md, f))
            except Exception as ex:
                bad += 1; print('ERR', f, ex); continue
            if got == ref:
                ok += 1
            elif encode_file(os.path.join(md, f), True) == ref:
                ok += 1; alt += 1
            else:
                bad += 1
                if shown < 4:
                    shown += 1
                    k = next((i for i in range(min(len(got), len(ref))) if got[i] != ref[i]), None)
                    print('DIFF', f, len(got), len(ref), hex(k) if k is not None else None)
        print('identical', ok, '(of which packed-strings variant', alt, ') different', bad, 'no bmt', miss)


if __name__ == '__main__':
    main()
