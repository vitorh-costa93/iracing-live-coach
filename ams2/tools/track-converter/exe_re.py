"""Static disassembly helper (read-only). usage:
  re.py dis <fileoff_hex|va_hex> [before] [after]
  re.py xref <va_hex>      # rip-relative refs + call/jmp targets to va
"""
import sys, pefile, capstone, re as _re
EXE = r'C:\Program Files (x86)\Steam\steamapps\common\Automobilista 2\AMS2AVX.exe'
pe = pefile.PE(EXE, fast_load=True)
data = open(EXE, 'rb').read()
base = pe.OPTIONAL_HEADER.ImageBase
text = [s for s in pe.sections if s.Name.startswith(b'.text')][0]
def off2va(o):
    for s in pe.sections:
        if s.PointerToRawData <= o < s.PointerToRawData + s.SizeOfRawData:
            return o - s.PointerToRawData + s.VirtualAddress + base
def va2off(v):
    r = v - base
    for s in pe.sections:
        if s.VirtualAddress <= r < s.VirtualAddress + max(s.Misc_VirtualSize, s.SizeOfRawData):
            return r - s.VirtualAddress + s.PointerToRawData
md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
md.detail = False
def dis_va(va, before, after):
    off = va2off(va) - before
    for ins in md.disasm(data[off:off + before + after], off2va(off)):
        print('%x: %s %s' % (ins.address, ins.mnemonic, ins.op_str))
def xref(target):
    import struct
    t0 = text.PointerToRawData; t1 = t0 + text.SizeOfRawData
    tva0 = off2va(t0)
    buf = data[t0:t1]
    hits = []
    # rel32 scan: any 4-byte disp at position i where next-instr = i+4 (+imm). Check common encodings.
    for i in range(len(buf) - 4):
        disp = int.from_bytes(buf[i:i+4], 'little', signed=True)
        for extra in (0, 1, 4):
            if tva0 + i + 4 + extra + disp == target:
                hits.append((tva0 + i, extra))
    for h, e in hits[:200]:
        print('ref near %x (extra %d)' % (h, e))
if __name__ == '__main__':
    cmd = sys.argv[1]
    if cmd == 'dis':
        a = int(sys.argv[2], 16)
        va = a if a > base else off2va(a)
        dis_va(va, int(sys.argv[3], 16) if len(sys.argv) > 3 else 0x40, int(sys.argv[4], 16) if len(sys.argv) > 4 else 0x100)
    elif cmd == 'xref':
        xref(int(sys.argv[2], 16))
    elif cmd == 'va':
        print(hex(off2va(int(sys.argv[2], 16))))

def sweep(lo, hi):
    """robust linear sweep (skips undecodable bytes)"""
    off = va2off(lo); end = off + (hi - lo); va = lo
    while off < end:
        got = False
        for ins in md.disasm(data[off:min(end, off + 0x4000)], va):
            yield ins
            off += ins.size; va += ins.size; got = True
        if not got or True:
            # disasm stopped (invalid byte or chunk end)
            if off < end and not got:
                off += 1; va += 1
