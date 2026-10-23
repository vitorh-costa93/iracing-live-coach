"""Sequential conversion, independent validation and AMS2CM packaging of a reviewed JSON manifest.

python -I batch_convert.py manifest.json [--only track_id] [--force]
Input archives and generated third-party track assets stay outside the repository.
"""
import argparse
import hashlib
import json
import pathlib
import re
import subprocess
import sys
import time


TOOLS = pathlib.Path(__file__).resolve().parent


def run(command, logfile, cwd=None, timeout=1800):
    with logfile.open('w', encoding='utf-8') as log:
        result = subprocess.run(command, cwd=cwd, stdout=log, stderr=subprocess.STDOUT, timeout=timeout)
    return result.returncode


def file_hash(path):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''): digest.update(block)
    return digest.hexdigest()


def fingerprint(source, layout):
    paths = list(source.rglob('*.kn5'))
    paths += list(source.glob('models*.ini'))
    data = source / layout if layout else source
    paths += list((data / 'ai').glob('*.ai')) + list((data / 'data').glob('*.ini'))
    if layout: paths += list((source / 'data').glob('*.ini'))
    paths += list((source / 'ui').rglob('*'))
    digest = hashlib.sha256()
    for file in sorted(p for p in paths if p.is_file()):
        digest.update(str(file.relative_to(source)).encode())
        with file.open('rb') as stream:
            for block in iter(lambda: stream.read(1024 * 1024), b''): digest.update(block)
    return digest.hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('manifest', type=pathlib.Path)
    parser.add_argument('--only')
    parser.add_argument('--force', action='store_true')
    args = parser.parse_args()
    manifest = json.loads(args.manifest.read_text(encoding='utf-8'))
    work = pathlib.Path(manifest['work']).resolve()
    packages = pathlib.Path(manifest['packages']).resolve()
    seven = manifest.get('seven_zip', r'C:\Program Files\7-Zip\7z.exe')
    work.mkdir(parents=True, exist_ok=True)
    packages.mkdir(parents=True, exist_ok=True)
    summary_path = work / 'status.json'
    status = json.loads(summary_path.read_text(encoding='utf-8')) if summary_path.exists() else {}
    conversion_hash = hashlib.sha256()
    for file in sorted(TOOLS.glob('*.py')):
        if file.name not in ('check_out.py', 'batch_convert.py', 'validation_contracts.py') and not file.name.startswith('test'):
            conversion_hash.update(file.read_bytes())
    ids = [item['id'] for item in manifest['tracks']]
    if len(ids) != len(set(ids)): raise ValueError('Duplicate track ids in manifest')
    if args.only and args.only not in ids: raise ValueError('Unknown --only track id: ' + args.only)
    names = [(item['name'] + ' (AMS2).7z').casefold() for item in manifest['tracks']]
    if len(names) != len(set(names)): raise ValueError('Duplicate package names in manifest')
    reference_hash = hashlib.sha256()
    for folder in ('reference', 'extracted'):
        base = pathlib.Path(manifest[folder]).resolve()
        reference_hash.update(str(base).encode())
        for file in sorted(p for p in base.rglob('*') if p.is_file()):
            reference_hash.update(str(file.relative_to(base)).encode())
            reference_hash.update(file_hash(file).encode())
    failed = 0
    for item in manifest['tracks']:
        tid = item['id']
        if args.only and args.only != tid: continue
        if not re.fullmatch(r'[a-z][a-z0-9_]{1,55}', tid): raise ValueError('Invalid track id: ' + tid)
        source = pathlib.Path(item['source']).resolve()
        stamp = fingerprint(source, item.get('layout')) + conversion_hash.hexdigest() + json.dumps(item, sort_keys=True) + reference_hash.hexdigest()
        stamp = hashlib.sha256(stamp.encode()).hexdigest()
        logs = work / 'logs'; logs.mkdir(exist_ok=True)
        old = status.get(tid, {})
        record = dict(name=item['name'], source=str(source), layout=item.get('layout'), started=time.time(), fingerprint=stamp)
        status[tid] = record
        def save(): summary_path.write_text(json.dumps(status, indent=2, ensure_ascii=False), encoding='utf-8')
        output = work / 'output'
        root = output / tid / 'Automobilista 2'
        save()
        print('START', tid, flush=True)
        try:
            cmd = [sys.executable, '-I', '-u', str(TOOLS / 'convert.py'), str(source), tid, str(output), manifest['reference'], manifest['extracted'],
                   '--require-complete-ai', '--dump-aiw', str(work / (tid + '.aiw'))]
            if item.get('layout'): cmd += ['--layout', item['layout']]
            for key, value in item.get('meta', {}).items(): cmd += ['--' + key, str(value)]
            if item.get('street'): cmd += ['--street']
            if item.get('ground_pits'): cmd += ['--ground-pits']
            if item.get('racing_line'): cmd += ['--racing-line', item['racing_line']]
            if item.get('f1_line'): cmd += ['--f1-line', item['f1_line']]
            for flag in ('ai_width', 'ai_dry2'):
                if item.get(flag): cmd += ['--' + flag.replace('_', '-')]
            if item.get('ai_width_rule'): cmd += ['--ai-width-rule', item['ai_width_rule']]
            if item.get('f1_limits'): cmd += ['--f1-limits', item['f1_limits']]
            if args.force or old.get('fingerprint') != stamp or not old.get('converted'):
                record['phase'] = 'converting'; save()
                if run(cmd, logs / (tid + '-convert.log')): raise RuntimeError('Conversion failed; see convert log')
            record['converted'] = True
            record['phase'] = 'validating'; save()
            report = work / (tid + '-validation.json')
            cmd = [sys.executable, '-I', '-u', str(TOOLS / 'check_out.py'), str(root), tid, '--require-pits', '--json', str(report)]
            if run(cmd, logs / (tid + '-validate.log')): raise RuntimeError('Independent validation failed; see validate log')
            record['validation'] = json.loads(report.read_text(encoding='utf-8'))
            if not record['validation']['passed']: raise RuntimeError('Validation did not explicitly pass')
            record['phase'] = 'packaging'; save()
            # Fresh temporary archive prevents stale files surviving a rerun of 7z "a".
            # Keep staging on the destination volume so replacement is atomic on Windows.
            pending = packages / ('.' + tid + '-' + str(time.time_ns()) + '.pending.7z')
            cmd = [seven, 'a', '-t7z', '-mx=5', '-mmt=2', '-bsp0', str(pending), 'Automobilista 2']
            if run(cmd, logs / (tid + '-package.log'), cwd=str(root.parent)): raise RuntimeError('Packaging failed')
            if run([seven, 't', '-bsp0', str(pending)], logs / (tid + '-archive-test.log')): raise RuntimeError('Archive integrity failed')
            destination = packages / (item['name'] + ' (AMS2).7z')
            if destination.parent != packages: raise ValueError('Invalid archive name')
            pending.replace(destination)
            record.update(phase='ready', package=str(destination), bytes=destination.stat().st_size,
                          sha256=file_hash(destination), finished=time.time())
            print('READY', tid, record['bytes'], flush=True)
        except (RuntimeError, subprocess.TimeoutExpired, OSError, ValueError) as error:
            record.update(phase='failed', error=str(error), finished=time.time()); failed += 1
            print('FAILED', tid, str(error), flush=True)
        save()
    print('BATCH_FINISHED', 'failed=' + str(failed), flush=True)
    return int(failed > 0)


if __name__ == '__main__': raise SystemExit(main())
