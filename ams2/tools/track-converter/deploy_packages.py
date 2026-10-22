"""Install batch_convert packages through the AMS2CM backend (no UI): back up state, copy 7z to Mods/Enabled, run Headless, verify.
Usage: python deploy_packages.py <packages_dir> <backup_dir>.  Refuses to run while AMS2 is open."""
import sys, json, shutil, hashlib, subprocess
from pathlib import Path
game = Path(r'C:\Program Files (x86)\Steam\steamapps\common\Automobilista 2')
runner = Path(r'D:\_ffds_temp\ac-ams2-batch-20261021\headless-cm\bin\Release\net6.0-windows\Headless.exe')
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
    subprocess.run([str(runner)], cwd=runner.parent, stdout=f, stderr=subprocess.STDOUT, check=True)
assert 'HEADLESS_INSTALL_COMPLETED' in log.read_text(errors='replace'), 'backend did not complete'
after = json.loads(state.read_text(encoding='utf-8-sig'))['Install']['Mods']
missing = [p.name for p in pkgs if p.name not in after]
print('installed', len(pkgs), 'archives; not registered:', missing)
