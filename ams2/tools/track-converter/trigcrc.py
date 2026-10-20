"""Find names for trigger 'Material CRC' values by hashing ASCII strings from the exe (read-only)."""
import re, sys, zlib, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from bmt import name_hash
T = [4110219313, 2964580884, 2964580887, 3836397042, 3449420218, 2734965195, 1583431227, 3439599616, 2734965192,
     747266536, 3456376832]
EXE = r'C:\Program Files (x86)\Steam\steamapps\common\Automobilista 2\AMS2AVX.exe'
data = open(EXE, 'rb').read()
words = set(m.group().decode() for m in re.finditer(rb'[A-Za-z0-9_ .\-]{3,48}', data))
extra = set()
for w in list(words):
    if any(k in w.lower() for k in ('trig', 'sector', 'pit', 'start', 'finish', 'split', 'line', 'speed', 'zone', 'garage', 'drs')):
        extra.update({w.upper(), w.lower()})
words |= extra
fs = {'name_hash': name_hash, 'crc32': lambda s: zlib.crc32(s.encode()), 'crc32_upper': lambda s: zlib.crc32(s.upper().encode()),
      'crc32_lower': lambda s: zlib.crc32(s.lower().encode())}
ts = set(T)
for fn, f in fs.items():
    for w in words:
        try:
            h = f(w)
        except Exception:
            continue
        if h in ts:
            print(fn, h, repr(w))
print('words', len(words))
