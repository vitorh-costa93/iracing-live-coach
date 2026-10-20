"""Study the pit-lane branch of an AIW (Sochi): WP_PTRS, branch ids, junctions, score/width of pit waypoints."""
import sys, re, collections
import numpy as np


def parse_aiw(text):
    wps = []
    cur = None
    for line in text.splitlines():
        if line.startswith(chr(92) * 2):
            cur = {'hdr': line[2:]}; wps.append(cur); continue
        if cur is not None and '=' in line:
            k, v = line.split('=', 1)
            cur[k] = v
    return wps


def tup(s):
    return [float(x) for x in s.strip('()').split(',')]


if __name__ == '__main__':
    t = open(sys.argv[1], encoding='latin1').read()
    wps = parse_aiw(t)
    print('n', len(wps), 'hdr distinct', len({w['hdr'] for w in wps}))
    br = collections.Counter(w.get('wp_branchID') for w in wps); print('branch', br)
    P = np.array([tup(w['wp_pos']) for w in wps])
    ptr = [tuple(int(float(x)) for x in tup(w['WP_PTRS'])) for w in wps]
    pit = [i for i, w in enumerate(wps) if w.get('wp_branchID') == '(1)']
    print('pit idx range', pit[0], pit[-1], 'count', len(pit))
    main_n = pit[0]
    # junctions: main wps whose prev/next is a pit wp, or pit wps whose prev/next is main
    for i, (p, n, x, y) in enumerate(ptr):
        isp = i in set(pit)
        if (not isp and (p >= main_n or n >= main_n)) or (isp and (p < main_n or n < main_n)):
            print('junction', i, 'pit' if isp else 'main', ptr[i], 'score', wps[i]['wp_score'], 'width', wps[i]['wp_width'])
    for i in pit[:4] + pit[150:153] + pit[-4:]:
        w = wps[i]
        print(i, w['hdr'], ptr[i], w['wp_score'], w['wp_width'], w['wp_dwidth'], w['wp_event'], w.get('wp_bitfields'), w.get('wp_path'))
    # third pointer statistics: main wps with x != -1 point to pit wp? distance
    d = [np.linalg.norm(P[i] - P[x]) for i, (p, n, x, y) in enumerate(ptr) if x >= 0]
    print('3rd ptr used', len(d), 'dist median', np.median(d), 'max', max(d))
    cnt = collections.Counter((i >= main_n, x >= main_n if x >= 0 else None) for i, (p, n, x, y) in enumerate(ptr))
    print('3rd ptr (from pit?, to pit?)', cnt)
    print('4th field values', collections.Counter(y for *_, y in ptr))
    # sequence contiguity of pit
    bad = [(i, ptr[i]) for i in pit if ptr[i][1] != i + 1 and ptr[i][1] >= main_n]
    print('pit next not i+1:', bad[:5])
    print('pit spacing', np.median(np.linalg.norm(np.diff(P[pit], axis=0), axis=1)))
    for k in ('wp_score', 'wp_event', 'wp_bitfields', 'wpd_CornerType'):
        print(k, collections.Counter(wps[i].get(k) for i in pit).most_common(3))
