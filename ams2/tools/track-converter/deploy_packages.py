"""Install batch_convert packages through the AMS2CM backend (no UI): back up state, copy 7z to Mods/Enabled, run AMS2CM console, verify.
Usage: python deploy_packages.py <packages_dir> <backup_dir>.  Refuses to run while AMS2 is open."""
import sys, json, shutil, hashlib, subprocess
from pathlib import Path
game = Path(r'C:\Program Files (x86)\Steam\steamapps\common\Automobilista 2')
runner = Path(r'C:\Users\Vitor\Documents\AMS2CM\app\PFiles64\AMS2CM\AMS2CM.exe')
enabled, state = game / 'Mods/Enabled', game / 'Mods/state.json'


def sha(p):
    h = hashlib.sha256()
    with p.open('rb') as f:
        for b in iter(lambda: f.read(1 << 20), b''):
            h.update(b)
    return h.hexdigest()


def guard():
    r = subprocess.run(['powershell', '-NoProfile', '-Command', 'if (Get-Process AMS2,AMS2AVX -ErrorAction SilentlyContinue) {exit 3}'])
    if r.returncode:
        sys.exit('AMS2 is running: nothing installed')


pk, bk = Path(sys.argv[1]), Path(sys.argv[2])
guard(); bk.mkdir(parents=True, exist_ok=True)
shutil.copy2(state, bk / 'state-before.json')
pkgs = sorted(pk.glob('*.7z'))
for p in pkgs:
    if (enabled / p.name).exists():
        shutil.copy2(enabled / p.name, bk / p.name)
    tmp = enabled / (p.name + '.staging'); shutil.copy2(p, tmp); assert sha(tmp) == sha(p); tmp.replace(enabled / p.name)
guard()
log = bk / 'headless-install.log'
with log.open('w') as f:
    # AMS2CM ends with Console.ReadKey, which throws when stdin is redirected: expected after the work is done
    subprocess.run([str(runner)], cwd=runner.parent, stdout=f, stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL)
assert 'Post-processing' in log.read_text(errors='replace'), 'backend did not complete'
after = json.loads(state.read_text(encoding='utf-8-sig'))['Install']['Mods']
missing = [p.name for p in pkgs if p.name not in after]
print('installed', len(pkgs), 'archives; not registered:', missing)

# AMS2CM appends every mod .trd to tracklist.lst even when the base game already lists it (DLC tracks unlocked by files),
# which shows each of them twice in the menu: drop the AMS2CM-block lines already present in the base part (case-insensitive).
tl = game / 'Tracks/_data/tracklist.lst'
lines = tl.read_text(encoding='utf-8-sig').splitlines()
try:
    b, e = lines.index('### BEGIN AMS2CM'), lines.index('### END AMS2CM')
except ValueError:
    b = e = None
if b is not None:
    base = {l.strip().lower() for l in lines[:b] + lines[e + 1:]}
    seen, kept = set(), []
    for l in lines[b + 1:e]:
        k = l.strip().lower()
        if k in base or k in seen:
            continue
        seen.add(k); kept.append(l)
    out = lines[:b + 1] + kept + lines[e:]
    tl.write_text('\r\n'.join(out) + '\r\n', encoding='utf-8')
    print('tracklist: removed', len(lines) - len(out), 'duplicate entries')
