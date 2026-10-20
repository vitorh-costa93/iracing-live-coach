import sys, pefile, capstone
exe = sys.argv[1]
pe = pefile.PE(exe, fast_load=True)
data = open(exe, 'rb').read()
base = pe.OPTIONAL_HEADER.ImageBase
def off2rva(o):
    for s in pe.sections:
        if s.PointerToRawData <= o < s.PointerToRawData + s.SizeOfRawData:
            return o - s.PointerToRawData + s.VirtualAddress
def rva2off(r):
    for s in pe.sections:
        if s.VirtualAddress <= r < s.VirtualAddress + s.Misc_VirtualSize:
            return r - s.VirtualAddress + s.PointerToRawData
md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
def dis(off, before=0x60, after=0x100):
    start = off - before
    rva = off2rva(start)
    for ins in md.disasm(data[start:off + after], base + rva):
        print('%x: %s %s' % (ins.address, ins.mnemonic, ins.op_str))
if __name__ == '__main__':
    for a in sys.argv[2:]:
        print('=====', a); dis(int(a, 16))
