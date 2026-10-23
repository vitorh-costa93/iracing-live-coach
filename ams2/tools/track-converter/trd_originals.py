"""Reduce the AI 'trenzinho' on ORIGINAL AMS2 tracks with loose .trd overrides (reversible, no game physics touched).

Mechanism: AMS2CM (console AMS2CM.exe) installs every 7z in Mods/Enabled whose tree is 'Automobilista 2\\Tracks\\<t>\\<t>.trd' (same as
'Pistas DLC liberadas.7z'); mods win over the bootfiles it extracts afterwards. Only 'data=' values of four AI props change:
  AIWidthScalarToReduceCorridors            > NEW_WIDTH (0.10)   (default 0.24, Monza 2020/2005 0.28; 0.0 and absent are kept)
  AIWidthScalarToReduceCorridorsFullThrottle> NEW_FT    (0.05)   (keeps the ~2:1 ratio of the stock 0.24/0.12)
  ScalarAIPassComplexityCutoffMax/MinAggression raised to at least 1.25 / 1.2 (what the stock 'racing' tracks already use)
Lower width scalar = wider AI corridor (converted tracks use F1 rule + 1 m per side, so this stays consistent). Values are only ever moved
in that direction (never undone for tracks already beyond them). AIOvertakeInsideEnabled, AI Grip, walls, steering, tyres: untouched.
Only 'Track Type' = Circuit (no Oval/Kart/Rallycross/Point to Point); tracks without the width prop, and files owned by other mods (converted tracks, DLC unlocks) are skipped.

Usage (headless, refuses while AMS2 is open):
  python trd_originals.py plan  [--only monza_2020 ...]                 table of old -> new values
  python trd_originals.py stage <out_dir> [--only ...]                  build 7z + backup (sha256, originals, rollback.ps1) in out_dir
  python trd_originals.py install <out_dir>                             copy 7z to Mods/Enabled and run AMS2CM.exe to completion
  python trd_originals.py rollback <out_dir>                            remove 7z from Mods/Enabled and re-run AMS2CM.exe
"""
import sys, re, json, shutil, hashlib, subprocess, tempfile, argparse
from pathlib import Path

game = Path(r'C:\Program Files (x86)\Steam\steamapps\common\Automobilista 2')
cm = Path(r'C:\Users\Vitor\Documents\AMS2CM\app\PFiles64\AMS2CM\AMS2CM.exe')
seven = r'C:\Program Files\7-Zip\7z.exe'
PKG = 'AI Corredor Originais (trd).7z'
NEW_WIDTH, NEW_FT, PASS_MAX, PASS_MIN = 0.10, 0.05, 1.25, 1.2
KEYS = {
    'AIWidthScalarToReduceCorridors': ('min', NEW_WIDTH),
    'AIWidthScalarToReduceCorridorsFullThrottle': ('min', NEW_FT),
    'ScalarAIPassComplexityCutoffMaxAggression': ('max', PASS_MAX),
    'ScalarAIPassComplexityCutoffMinAggression': ('max', PASS_MIN),
}


def sha(p):
    h = hashlib.sha256()
    with Path(p).open('rb') as f:
        for b in iter(lambda: f.read(1 << 20), b''):
            h.update(b)
    return h.hexdigest()


def pat(k):
    return re.compile(r'(name="' + re.escape(k) + r'"\s+data=")([^"]*)(")')


def get(t, k):
    m = pat(k).search(t)
    return m.group(2) if m else None


def fmt(v):
    return ('%.2f' % v).rstrip('0').rstrip('.') if v else '0.0'


def edit(t):
    """Return (new_text, {key: (old, new)}); only data= attribute values change."""
    ch = {}
    w = get(t, 'AIWidthScalarToReduceCorridors')
    if w is None or float(w) == 0.0 or get(t, 'Track Type') != 'Circuit':
        return t, ch
    for k, (mode, val) in KEYS.items():
        m = pat(k).search(t)
        if not m:
            continue
        old = float(m.group(2))
        if mode == 'min':
            new = val if old > val else old
        else:
            new = val if 1.0 <= old < val else old
        if new != old:
            t = t[:m.start(2)] + fmt(new) + t[m.end(2):]
            ch[k] = (m.group(2), fmt(new))
    return t, ch


def originals():
    """trd paths owned only by the generated bootfiles (i.e. stock game tracks)."""
    st = json.loads((game / 'Mods/state.json').read_text(encoding='utf-8-sig'))['Install']['Mods']
    n = lambda f: f.lower().replace('\\', '/')
    mods = set()
    for k, v in st.items():
        if k != '__bootfiles_generated':
            mods |= {n(f) for f in v['Files']}
    out = []
    for f in st['__bootfiles_generated']['Files']:
        if n(f).endswith('.trd') and n(f) not in mods and n(f).count('/') == 2:
            out.append(Path(f))
    return sorted(out, key=lambda p: str(p).lower())


def plan(only):
    rows = []
    for rel in originals():
        t = (game / rel).read_text(encoding='latin1')
        if only and rel.stem.lower() not in only:
            continue
        new, ch = edit(t)
        rows.append((rel, t, new, ch))
    return rows


def guard():
    r = subprocess.run(['powershell', '-NoProfile', '-Command', 'if (Get-Process AMS2,AMS2AVX -ErrorAction SilentlyContinue) {exit 3}'])
    if r.returncode:
        sys.exit('AMS2 is running: nothing installed')


def run_cm(log):
    guard()
    with open(log, 'w') as f:  # AMS2CM ends with Console.ReadKey, which throws when stdin is redirected: expected after the work is done
        subprocess.run([str(cm)], stdout=f, stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL)
    txt = Path(log).read_text(errors='replace')
    assert 'Post-processing' in txt, 'AMS2CM did not reach post-processing'
    return txt


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('cmd', choices=['plan', 'stage', 'install', 'rollback'])
    ap.add_argument('out', nargs='?')
    ap.add_argument('--only', nargs='*', default=[])
    a = ap.parse_args()
    only = {o.lower() for o in a.only}
    if a.cmd == 'plan':
        for rel, _, _, ch in plan(only):
            print(rel.stem, ' | '.join('%s %s>%s' % (k.replace('AIWidthScalarToReduceCorridors', 'W').replace('ScalarAIPassComplexityCutoff', 'P'), o, n) for k, (o, n) in ch.items()) or 'unchanged')
        return
    out = Path(a.out)
    enabled = game / 'Mods/Enabled'
    if a.cmd == 'stage':
        bk = out / 'backup'
        (bk / 'originals').mkdir(parents=True, exist_ok=True)
        tree = out / 'tree'
        shutil.rmtree(tree, ignore_errors=True)
        manifest, n = {}, 0
        for rel, old, new, ch in plan(only):
            if not ch:
                continue
            shutil.copy2(game / rel, bk / 'originals' / (rel.stem + '.trd'))
            dst = tree / 'Automobilista 2' / rel
            dst.parent.mkdir(parents=True, exist_ok=True)
            dst.write_bytes(new.encode('latin1'))
            assert re.sub(r'data="[^"]*"', '', old) == re.sub(r'data="[^"]*"', '', new)  # structure identical, only data values moved
            import xml.dom.minidom as md
            md.parseString(dst.read_bytes())  # well formed
            manifest[str(rel)] = {'sha256_original': sha(game / rel), 'sha256_new': sha(dst), 'changes': ch}
            n += 1
        (bk / 'manifest.json').write_text(json.dumps(manifest, indent=1), encoding='utf-8')
        pk = out / PKG
        pk.unlink(missing_ok=True)
        subprocess.run([seven, 'a', '-t7z', '-mx=5', str(pk), 'Automobilista 2'], cwd=tree, check=True, stdout=subprocess.DEVNULL)
        shutil.copy2(game / 'Mods/state.json', bk / 'state-before.json')
        (bk / 'rollback.ps1').write_text(
            "# Rollback: remove the override package and let AMS2CM reinstall (restores stock trd from bootfiles). Run with AMS2 closed.\r\n"
            "Remove-Item -LiteralPath '%s' -Force -ErrorAction SilentlyContinue\r\n"
            "if (Get-Process AMS2,AMS2AVX -ErrorAction SilentlyContinue) { throw 'AMS2 is running' }\r\n"
            "Start-Process -FilePath '%s' -WindowStyle Hidden -Wait -RedirectStandardInput NUL -RedirectStandardOutput '%s'\r\n"
            % (enabled / PKG, cm, bk / 'rollback-cm.log'), encoding='utf-8')
        print('staged', n, 'trd ->', pk)
        return
    if a.cmd == 'install':
        guard()
        pk = out / PKG
        tmp = enabled / (PKG + '.staging')
        shutil.copy2(pk, tmp)
        assert sha(tmp) == sha(pk)
        tmp.replace(enabled / PKG)
        txt = run_cm(out / 'backup' / 'cm-install.log')
        st = json.loads((game / 'Mods/state.json').read_text(encoding='utf-8-sig'))['Install']['Mods']
        assert PKG in st, 'package not registered'
        man = json.loads((out / 'backup/manifest.json').read_text())
        bad = [r for r, m in man.items() if sha(game / r) != m['sha256_new']]
        print('installed; trd not matching expected hash:', bad)
        return
    if a.cmd == 'rollback':
        (enabled / PKG).unlink(missing_ok=True)
        run_cm(out / 'backup' / 'cm-rollback.log')
        man = json.loads((out / 'backup/manifest.json').read_text())
        bad = [r for r, m in man.items() if sha(game / r) != m['sha256_original']]
        print('rolled back; trd not back to original:', bad)


main()
