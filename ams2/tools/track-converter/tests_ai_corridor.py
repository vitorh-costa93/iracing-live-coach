"""--ai-width / --ai-dry2 (anti-"trenzinho", default OFF). Run: python -I tests_ai_corridor.py."""
import contextlib
import importlib.util
import io
from pathlib import Path
import sys
import unittest

import numpy as np

HERE = Path(__file__).parent
sys.path.insert(0, str(HERE))
spec = importlib.util.spec_from_file_location('track_convert_ai', HERE / 'convert.py')
convert = importlib.util.module_from_spec(spec)
spec.loader.exec_module(convert)
from aiw_pit import parse_aiw, tup  # noqa: E402


def sample_ai():
    angles = np.linspace(0, 2 * np.pi, 80, endpoint=False)
    fast = np.c_[60 * np.cos(angles), np.zeros(len(angles)), 60 * np.sin(angles)]
    extra = np.ones((len(fast), 18)); extra[:, 5:7] = 6
    dummies = {}
    for sector, index in enumerate((0, 26, 53)):
        for side in ('L', 'R'):
            m = np.eye(4); m[3, :3] = fast[index]; dummies['AC_TIME_%d_%s' % (sector, side)] = m
    for prefix in ('AC_START_', 'AC_PIT_'):
        for i in range(2):
            m = np.eye(4); m[3, :3] = fast[i]; dummies[prefix + str(i)] = m
    return fast, extra, dummies


def rounded_square(R=30.0, S=200.0):
    pts = []
    for q, (cx, cz) in enumerate([(S / 2, S / 2), (-S / 2, S / 2), (-S / 2, -S / 2), (S / 2, -S / 2)]):
        a0 = q * np.pi / 2
        p0 = np.array([cx + R * np.cos(a0), cz + R * np.sin(a0)]); d = np.array([-np.sin(a0), np.cos(a0)])
        pts += [p0 - d * (S - t) for t in np.arange(0, S, 5.0)]
        pts += [np.array([cx + R * np.cos(a0 + u / R), cz + R * np.sin(a0 + u / R)]) for u in np.arange(0, R * np.pi / 2, 5.0)]
    W = np.array([[p[0], 0.0, p[1]] for p in pts])
    t = np.roll(W, -1, 0) - np.roll(W, 1, 0); t /= np.linalg.norm(t, axis=1)[:, None]
    return W, np.c_[t[:, 2], np.zeros(len(W)), -t[:, 0]]


def waypoints(aiw):
    return [w for w in parse_aiw(aiw.split('[Waypoint]')[1]) if 'wp_pos' in w]


def build(*args, **kw):
    with contextlib.redirect_stdout(io.StringIO()):
        return convert.build_aiw(*args, **kw)


class AiCorridorTests(unittest.TestCase):
    def test_flags_default_off(self):
        args = convert.parse_args(['s', 'id', 'o', 'r', 'x'])
        self.assertEqual((args.ai_width, args.ai_dry2), (False, False))
        args = convert.parse_args(['s', 'id', 'o', 'r', 'x', '--ai-width', '--ai-dry2'])
        self.assertEqual((args.ai_width, args.ai_dry2), (True, True))
        self.assertFalse(hasattr(args, 'ai_wet_line'))
        fast, extra, dummies = sample_ai()
        aiw, info = build(fast, extra, dummies, 't')
        self.assertNotIn('dry2', aiw.lower())
        self.assertEqual(info['ai'], {})
        for w in waypoints(aiw):
            self.assertEqual(w['wp_path'].split(',')[1], '0.0000)')
            dw = tup(w['wp_dwidth']); self.assertEqual(dw[0], dw[1])

    def test_ai_width_reaches_runoff_stops_at_wall_and_caps(self):
        fast, extra, dummies = sample_ai()
        n = len(fast)
        reach = np.c_[np.full(n, 20.0), np.full(n, 3.0)]           # col 0 (+perp AC = first dwidth) paved 20 m, col 1 short
        wallf = np.c_[np.full(n, 10.0), np.full(n, 30.0)]
        aiw, info = build(fast, extra, dummies, 't', wallf=wallf, reach=reach)
        for w in waypoints(aiw):
            wl, wr = tup(w['wp_width']); dl, dr = tup(w['wp_dwidth'])
            self.assertAlmostEqual(dl - wl, 10.0 - convert.WALL_CLEAR - 6.0, delta=0.01)   # wall - WALL_CLEAR wins
            self.assertAlmostEqual(dr - wr, convert.AI_DW_FLOOR, delta=0.01)             # asphalt only -> previous floor
        aiw, _ = build(fast, extra, dummies, 't', reach=np.full((n, 2), 30.0))
        for w in waypoints(aiw):
            self.assertAlmostEqual(tup(w['wp_dwidth'])[0] - tup(w['wp_width'])[0], convert.AI_DW_MAX, delta=0.01)

    def test_ai_width_rule_f1_track_limit_kerb_wall_and_floor(self):
        args = convert.parse_args(['s', 'id', 'o', 'r', 'x', '--ai-width', '--ai-width-rule', 'f1', '--f1-limits', 'l.csv'])
        self.assertEqual((args.ai_width_rule, args.f1_limits), ('f1', 'l.csv'))
        fast, extra, dummies = sample_ai()
        n = len(fast)
        wide = extra.copy(); wide[:, 5:7] = 8.0                       # asphalt 6 m + 2 m kerb per side
        W = fast * convert.MIRROR
        t = np.roll(W, -1, 0) - np.roll(W, 1, 0); t[:, 1] = 0; t /= np.linalg.norm(t, axis=1)[:, None]
        pw = np.c_[t[:, 2], np.zeros(n), -t[:, 0]]
        lim = {'line': W, 'left_track_limit': W - 7.0 * pw, 'right_track_limit': W + 9.5 * pw,
               'left_racing_limit': W - 5.0 * pw, 'right_racing_limit': W + 5.0 * pw}
        wallf = np.c_[np.full(n, 30.0), np.full(n, 9.0)]              # AC col 1 = right/+perp side wall 9 m away
        aiw, _ = build(fast, extra, dummies, 't', wide=wide, wallf=wallf, f1lim=lim)
        for w in waypoints(aiw):
            wl, wr = tup(w['wp_width']); dl, dr = tup(w['wp_dwidth'])
            self.assertAlmostEqual(dl - wl, 1.0, delta=0.1)           # F1 track limit 7 m < kerb 8 m
            self.assertAlmostEqual(dr - wr, 9.0 - convert.WALL_CLEAR - 6.0, delta=0.1)    # kerb 8, F1 9.5 -> wall wins
        aiw, _ = build(fast, extra, dummies, 't', wide=wide, f1lim={})  # no F1 data: AC asphalt+kerb edge
        for w in waypoints(aiw):
            self.assertAlmostEqual(tup(w['wp_dwidth'])[0] - tup(w['wp_width'])[0], 2.0 + convert.F1_TL_VS_KERB, delta=0.05)
        lim['left_track_limit'] = W - 4.0 * pw                        # F1 limit inside the asphalt: dwidth = width, never less
        aiw, _ = build(fast, extra, dummies, 't', wide=wide, f1lim=lim)
        for w in waypoints(aiw):
            self.assertAlmostEqual(tup(w['wp_dwidth'])[0], tup(w['wp_width'])[0], delta=1e-3)

    def test_ai_reach_grass_budget_and_gravel(self):
        fast = np.array([[0.0, 0.0, z] for z in np.arange(0.0, 100.0, 5.0)])
        quad = lambda x0, x1: dict(pos=np.array([[x0, 0, -10], [x1, 0, -10], [x1, 0, 110], [x0, 0, 110]], float),
                                   idx=np.array([0, 1, 2, 0, 2, 3]), mat=0, visible=False, renderable=False)
        k = dict(materials=[dict(name='coll')], meshes=[dict(name='1ROAD', **quad(-5.0, 5.0)), dict(name='1GRASS', **quad(5.0, 20.0)),
                                                         dict(name='1GRAVEL', **quad(-20.0, -5.0))])
        r = convert.ai_reach(k, fast, {'ROAD', 'GRASS', 'GRAVEL'})[1:-1]
        # +z line: col 0 = +x (road 5 m + AI_GRASS of grass), col 1 = -x (stops where the gravel trap starts)
        self.assertTrue(np.all(np.abs(r[:, 0] - (5.0 + convert.AI_GRASS)) <= 0.5), r[:, 0])
        self.assertTrue(np.all(np.abs(r[:, 1] - 5.0) <= 0.5), r[:, 1])

    def test_dry2_inside_in_braking_back_before_apex(self):
        W, pw = rounded_square()
        n = len(W); lo, hi, sh = -6.0 * np.ones(n), 6.0 * np.ones(n), np.zeros(n)
        d2 = convert.secondary_line(W, pw, sh, lo, hi)
        self.assertTrue(np.all((d2 >= lo - 1e-9) & (d2 <= hi + 1e-9)))
        self.assertLess(np.max(np.abs(np.diff(np.r_[d2, d2[:1]]))), 0.8)                 # smooth
        inner = np.sign(np.sum(pw * -W, axis=1))
        n_arc, n_str = int(np.ceil(30.0 * np.pi / 2 / 5.0)), 40
        for q in range(4):
            a = q * (n_str + n_arc) + n_str; b = a + n_arc - 1
            brake = d2[a - 12:a - 2] * inner[a - 12:a - 2]
            self.assertGreaterEqual(brake.max(), 2.0)                                       # inside before the corner
            self.assertGreaterEqual(brake.min(), 0.0)
            self.assertLess(np.max(np.abs(d2[a + 1:b + 1])), 0.05)                          # back on the main line at the apex
        narrow = convert.secondary_line(W, pw, sh, -0.5 * np.ones(n), 0.5 * np.ones(n))
        self.assertLessEqual(np.max(np.abs(narrow)), 0.5 + 1e-9)                            # never outside the corridor

    def test_all_flags_emit_fields_and_keep_pit_intact(self):
        from validation_contracts import pit_endpoint_errors
        fast, extra, dummies = sample_ai()
        extra[:, 5], extra[:, 6] = 12., 2.7
        pitlane = fast.copy(); pitlane[20:51, [0, 2]] *= 0.82
        m1 = np.eye(4); m1[3, :3] = pitlane[35]; m2 = np.eye(4); m2[3, :3] = pitlane[36]
        dummies['AC_PIT_0'], dummies['AC_PIT_1'] = m1, m2
        branch = convert.build_pit(fast, extra, pitlane, [('AC_PIT_0', m1), ('AC_PIT_1', m2)])
        base, _ = build(fast, extra, dummies, 'p', pit=branch)
        aiw, info = build(fast, extra, dummies, 'p', pit=branch, reach=np.full((len(fast), 2), 30.0), dry2=True)
        head = aiw.split('[Waypoint]')[1].splitlines()
        self.assertTrue(head[2].startswith('times=') and head[3].startswith('timesDry2=('))
        wb, wa = waypoints(base), waypoints(aiw)
        self.assertEqual(len(wb), len(wa))
        self.assertTrue(all('wp_dry2' in w for w in wa))
        for b, a in zip(wb, wa):
            for key in ('wp_pos', 'wp_perp', 'wp_width', 'wp_branchID', 'WP_PTRS', 'wp_score'):
                self.assertEqual(a[key], b[key])
            self.assertEqual(a['wp_path'], b['wp_path'])                # dry and wet (wp_path[1]) unchanged
            if tup(a['wp_branchID'])[0] != 0:                         # pit: same dwidth, dry2 = its own path
                self.assertEqual(a['wp_dwidth'], b['wp_dwidth'])
                self.assertEqual(tup(a['wp_dry2'])[0], tup(a['wp_path'])[0])
            else:                                                     # main: dry2 inside the road width
                wl, wr = tup(a['wp_width'])
                self.assertTrue(-wl < tup(a['wp_dry2'])[0] < wr)
        branches = np.array([int(tup(w['wp_branchID'])[0]) for w in wa])
        self.assertEqual(pit_endpoint_errors(branches, np.array([tup(w['WP_PTRS']) for w in wa], dtype=int)), [])
        self.assertGreaterEqual(info['ai']['tdry2'], info['tdry'])


if __name__ == '__main__':
    unittest.main()
