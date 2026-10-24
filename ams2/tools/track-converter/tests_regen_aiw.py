"""Focused tests for regen_aiw_original.py: python -I tests_regen_aiw.py (the official-pak tests read the game read-only
and are skipped without it)."""
from pathlib import Path
import sys
import unittest
import numpy as np
sys.path.insert(0, str(Path(__file__).resolve().parent))
import regen_aiw_original as R
from repair_aiw import LateralSurface, dense_clearance

GAME_PAK = Path(R.GAME) / 'Pakfiles' / 'TRACKS' / 'Interlagos_Physics.bff'


def ring(r0, r1, n=96, y=0.0):
    th = np.arange(n) * 2 * np.pi / n
    a = np.c_[r0 * np.cos(th), np.full(n, y), r0 * np.sin(th)]; b = np.c_[r1 * np.cos(th), np.full(n, y), r1 * np.sin(th)]
    t = []
    for i in range(n):
        j = (i + 1) % n; t.extend([[a[i], b[i], b[j]], [a[i], b[j], a[j]]])
    return np.array(t)


def wall(r, n=96, h=3.0):
    th = np.arange(n) * 2 * np.pi / n
    lo = np.c_[r * np.cos(th), np.zeros(n), r * np.sin(th)]; hi = lo + [0, h, 0]
    t = []
    for i in range(n):
        j = (i + 1) % n; t.extend([[lo[i], lo[j], hi[j]], [lo[i], hi[j], hi[i]]])
    return np.array(t)


def track_fixture(n=120, R0=60.0, pit=True, bump=False, dry2=True):
    """Oval-ish closed loop (ellipse) with CRLF, // comments, a pit branch and wp_dry2; bump = original line 6.5 m out
    (past the 7 - 1.2 m asphalt corridor) on waypoints 58..64."""
    th = np.arange(n) * 2 * np.pi / n
    P = np.c_[R0 * 1.6 * np.cos(th), np.full(n, .3), R0 * np.sin(th)]
    tg = np.c_[-R0 * 1.6 * np.sin(th), np.zeros(n), R0 * np.cos(th)]; tg /= np.linalg.norm(tg, axis=1)[:, None]
    perp = np.c_[tg[:, 2], np.zeros(n), -tg[:, 0]]       # outward
    blocks = []
    for i in range(n):
        third = n if (pit and i == 10) else -1
        blocks.append(
            '\\\\%d\r\nwp_pos=(%s) // pos\r\nwp_perp=(%s)\r\nwp_path=(%s,0.0000)\r\nwp_dry2=(0.0000,0.0000)\r\n'
            'wp_width=(7.000,7.000)\r\nwp_dwidth=(8.000,8.000)\r\nwp_branchID=(0)\r\nwp_groove_lat=(0.0000)\r\n'
            'wp_galpha=(0.100)\r\nwpd_CornerType=(0)\r\nwpd_CornerState=(%d)\r\nwp_bitfields=(0)\r\nWP_PTRS=(%d,%d,%d,0)\r\n'
            'wp_score=(%d,%d)\r\n' % (i, ','.join('%.4f' % x for x in P[i]), ','.join('%.6f' % x for x in perp[i]),
                                      '6.5000' if bump and 58 <= i <= 64 else '0.0000',
                                      1 if i % 30 == 0 else 0, (i - 1) % n, (i + 1) % n, third, i * 3 // n, i * 4))
    pitb = ''
    if pit:
        pitb = ('\\\\%d\r\nwp_pos=(%s)\r\nwp_perp=(1,0,0)\r\nwp_path=(0.0000,0.0000)\r\nwp_width=(3,3)\r\nwp_dwidth=(3,3)\r\n'
                'wp_branchID=(1)\r\nwpd_CornerState=(0)\r\nWP_PTRS=(10,20,-1,0)\r\nwp_score=(0,0)\r\n'
                % (n, ','.join('%.4f' % x for x in P[15] - 10 * perp[15])))
    text = ('[Features]\r\npitspots=1\r\nOval=0\r\n[GRID]\r\nPos=(1,2,3)\r\n[PITS]\r\nPitPos=(1,2,3)\r\n[Waypoint]\r\n'
            'times=(100.000,105.000) // t\r\ntimesDry2=(101.000,106.000)\r\n' + ''.join(blocks) + pitb)
    if not dry2:
        text = text.replace('wp_dry2=(0.0000,0.0000)\r\n', '').replace('timesDry2=(101.000,106.000)\r\n', '')
    return text, P, perp


class TextTests(unittest.TestCase):
    def test_fmt_like_keeps_decimals_and_no_negative_zero(self):
        self.assertEqual(R.fmt_like('1.2340', 2.5), '2.5000')
        self.assertEqual(R.fmt_like('1.20', -0.001), '0.00')
        self.assertEqual(R.fmt_like('3', 2.4), '2')

    def test_set_field_keeps_comment_and_crlf(self):
        b = 'wp_path=(1.0000,2.0000) // racing, wet\r\nwp_x=(1)\r\n'
        out = R.set_field(b, 'wp_path', ['3.0000', '2.0000'])
        self.assertEqual(out, 'wp_path=(3.0000,2.0000) // racing, wet\r\nwp_x=(1)\r\n')
        with self.assertRaises(ValueError):
            R.set_field(b, 'wp_missing', ['1'])

    def test_text_diff_rejects_forbidden_keys_and_pit_changes(self):
        text, _, _ = track_fixture()
        _, _, blocks = R.split_aiw(text)
        order, *_ = R.topology(blocks)
        ids = set(int(i) for i in order)
        ok = text.replace('wp_path=(0.0000,0.0000)\r\nwp_dry2', 'wp_path=(0.5000,0.0000)\r\nwp_dry2', 1)
        self.assertEqual(R.text_diff(text, ok, ids, True)[0], [])
        bad = text.replace('wp_galpha=(0.100)', 'wp_galpha=(0.200)', 1)
        self.assertTrue(any('wp_galpha' in e for e in R.text_diff(text, bad, ids, True)[0]))
        wet = text.replace('wp_path=(0.0000,0.0000)\r\nwp_dry2', 'wp_path=(0.0000,0.5000)\r\nwp_dry2', 1)
        self.assertTrue(any('wet line' in e for e in R.text_diff(text, wet, ids, True)[0]))
        pit = text.replace('wp_width=(3,3)', 'wp_width=(4,3)')
        self.assertTrue(any('pit/branch' in e for e in R.text_diff(text, pit, ids, True)[0]))
        grid = text.replace('Pos=(1,2,3)', 'Pos=(1,2,4)', 1)
        self.assertIn('text before [Waypoint] changed', R.text_diff(text, grid, ids, True)[0])
        hdr = text.replace('times=(100.000,105.000)', 'times=(99.000,105.000)')
        self.assertEqual(R.text_diff(text, hdr, ids, True)[0], [])


class TopologyTests(unittest.TestCase):
    def test_closed_loop_order_and_forks(self):
        text, _, _ = track_fixture()
        order, br, ptr, forks = R.topology(R.split_aiw(text)[2])
        self.assertEqual(list(order), list(range(120)))
        self.assertEqual(forks, [10, 20])

    def test_pointer_order_not_index_order(self):
        text, _, _ = track_fixture(pit=False)
        pre, hdr, blocks = R.split_aiw(text)
        n = len(blocks)
        perm = list(range(n)); perm[5], perm[6] = 6, 5        # swap two blocks, rewrite pointers to keep the loop
        pos = {old: new for new, old in enumerate(perm)}
        out = []
        for new, old in enumerate(perm):
            b = blocks[old]
            b = R.set_field(b, 'WP_PTRS', [str(pos[(old - 1) % n]), str(pos[(old + 1) % n]), '-1', '0'])
            out.append(b)
        order, *_ = R.topology(out)
        self.assertEqual([perm[i] for i in order], list(range(n)))

    def test_open_layout_refused(self):
        text, _, _ = track_fixture(pit=False)
        blocks = R.split_aiw(text)[2]
        blocks[-1] = R.set_field(blocks[-1], 'WP_PTRS', ['118', '-1', '-1', '0'])
        with self.assertRaises(ValueError):
            R.topology(blocks)

    def test_refusal_oval_and_kart(self):
        text, _, _ = track_fixture()
        self.assertEqual(R.refusal('InterlagosKart1', text, set(), 'x'), 'kart track')
        self.assertTrue(R.refusal('X', text.replace('Oval=0', 'Oval=1'), set(), 'x').startswith('Oval'))
        self.assertTrue(R.refusal('X', text, set(), 'x').startswith('no csm'))
        pak = {R.uid('tracks/x/physics/x.csm'), R.uid('tracks/x/track_cut/x.gcl')}
        self.assertIsNone(R.refusal('X', text, pak, 'x'))

    def test_ramp_weights(self):
        w = R.ramp_weights(100, [0], pin=3, ramp=15)
        self.assertTrue(np.all(w[[0, 1, 3, 97, 99]] == 0))
        self.assertTrue(np.all(w[18:83] == 1))
        self.assertTrue(np.all(np.diff(w[3:19]) >= 0))


def ellipse_surfaces():
    """asphalt = the band +-7 m around the ellipse; paved run-off to 12 m inside, GCL flag 1 to 9 m inside / 7.5 m outside;
    wall 9.5 m outside."""
    th = np.arange(240) * 2 * np.pi / 240
    def band(off):
        Q = np.c_[96 * np.cos(th), np.zeros(240), 60 * np.sin(th)]
        t = np.c_[-96 * np.sin(th), np.zeros(240), 60 * np.cos(th)]; t /= np.linalg.norm(t, axis=1)[:, None]
        return Q + off * np.c_[t[:, 2], np.zeros(240), -t[:, 0]]
    def strip(a, b):
        A, B = band(a), band(b); tr = []
        for i in range(240):
            j = (i + 1) % 240; tr.extend([[A[i], B[i], B[j]], [A[i], B[j], A[j]]])
        return np.array(tr)
    W = band(9.5); wl = []
    for i in range(240):
        j = (i + 1) % 240; wl.extend([[W[i], W[j], W[j] + [0, 3, 0]], [W[i], W[j] + [0, 3, 0], W[i] + [0, 3, 0]]])
    return dict(road=LateralSurface(strip(-7, 7)), drive=LateralSurface(strip(-12, 7)), grass=None,
                limits=LateralSurface(strip(-9, 7.5)), walls=R.Walls(np.array(wl)), obstacles=R.Walls(np.array(wl)))


class RegenTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.text, P, perp = track_fixture(bump=True)
        cls.S = ellipse_surfaces()
        cls.new, cls.rep, cls.lines = R.regen(cls.text, cls.S, log=lambda *a: None)

    def test_dwidth_f1_rule_paved_and_gcl_wall(self):
        """inside: min(paved 12, GCL 9) = 9 m (> width + 1); outside: wall 9.5 - WALL_CLEAR = 7.7 < original 8 -> kept."""
        _, _, blocks = R.split_aiw(self.new)
        order = R.topology(blocks)[0]
        wr = R.ramp_weights(len(order), [10, 20])
        dw = np.array([R.field(blocks[i], 'wp_dwidth') for i in order])
        np.testing.assert_allclose(dw[wr == 1, 0], 9.0, atol=0.15)
        np.testing.assert_allclose(dw[:, 1], 8.0)
        np.testing.assert_allclose(dw[wr < 1, 0], 8.0)                # pit ramp untouched

    def test_only_allowed_keys_and_pit_untouched(self):
        _, _, blocks = R.split_aiw(self.text)
        ids = set(int(i) for i in R.topology(blocks)[0])
        errs, changed = R.text_diff(self.text, self.new, ids, True)
        self.assertEqual(errs, [])
        self.assertIn('wp_path', changed)
        self.assertEqual(R.split_aiw(self.text)[2][-1], R.split_aiw(self.new)[2][-1])
        self.assertTrue(self.new.count('\r\n') == self.text.count('\r\n') and '\n' not in self.new.replace('\r\n', ''))

    def test_corridor_wall_clear_and_dense_clearance(self):
        blocks = R.split_aiw(self.new)[2][:120]
        P = np.array([R.field(b, 'wp_pos') for b in blocks]); perp = np.array([R.field(b, 'wp_perp') for b in blocks])
        path = np.array([R.field(b, 'wp_path')[0] for b in blocks])
        self.assertLessEqual(path.max(), 9.5 - 1.8 + 1e-3)          # WALL_CLEAR from the outside wall
        line = P + path[:, None] * perp
        d = dense_clearance(line, self.S['road'], self.S['limits'], R.DENSE_MIN)
        self.assertGreaterEqual(d['dense_asphalt_clearance_min'], R.DENSE_MIN)

    def test_lap_not_slower_curvature_ok_dwidth_not_narrower(self):
        self.assertLessEqual(self.rep['lap_est_after'], self.rep['lap_est_before'] + 1e-9)
        cb, ca = self.rep['curvature_before'], self.rep['curvature_after']
        self.assertLessEqual(ca['k_max'], cb['k_max'] * (1 + R.CURV_TOL) + 1e-4)
        ob, nb = R.split_aiw(self.text)[2][:120], R.split_aiw(self.new)[2][:120]
        for a, b in zip(ob, nb):
            self.assertTrue(np.all(np.array(R.field(b, 'wp_dwidth')) >= np.array(R.field(a, 'wp_dwidth'))))
            self.assertEqual(R.field(a, 'wp_width'), R.field(b, 'wp_width'))


class CreateDry2Tests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.text, _, _ = track_fixture(bump=True, dry2=False)
        cls.new, cls.rep, cls.lines = R.regen(cls.text, cls.S if hasattr(cls, 'S') else ellipse_surfaces(), log=lambda *a: None)

    def test_dry2_created_after_wp_path_and_timesdry2_after_times(self):
        self.assertTrue(self.rep['dry2_created'] and self.rep['times_dry2_created'])
        _, hdr, blocks = R.split_aiw(self.new)
        self.assertRegex(hdr, r'times=\([^)]*\) // t\r\ntimesDry2=\(\d+\.\d{4}\)\r\n')
        for b in blocks[:120]:
            self.assertRegex(b, r'wp_path=\([^)]*\)\r\nwp_dry2=\(-?\d+\.\d{4}\)\r\n')
        self.assertNotIn('wp_dry2', blocks[120])                      # pit untouched (no dry2 added)
        order = set(int(i) for i in R.topology(R.split_aiw(self.text)[2])[0])
        errs, changed = R.text_diff(self.text, self.new, order, True)
        self.assertEqual(errs, [])
        self.assertEqual(changed['wp_dry2_added'], 120); self.assertEqual(changed['timesDry2_added'], 1)
        self.assertTrue(R.text_diff(self.text, self.new, order, False)[0])   # not allowed -> error

    def test_misplaced_insertion_rejected(self):
        order = set(range(120))
        bad = self.text.replace('wp_galpha=(0.100)\r\n', 'wp_galpha=(0.100)\r\nwp_dry2=(0.0000)\r\n', 1)
        self.assertTrue(any('wrong place' in e for e in R.text_diff(self.text, bad, order, True)[0]))
        pit = self.text.replace('wp_path=(0.0000,0.0000)\r\nwp_width=(3,3)', 'wp_path=(0.0000,0.0000)\r\nwp_dry2=(0.0000)\r\nwp_width=(3,3)')
        self.assertTrue(any('pit/branch' in e for e in R.text_diff(self.text, pit, order, True)[0]))

    def test_dry2_dense_clearance(self):
        d = dense_clearance(self.lines['L2'], ellipse_surfaces()['road'], ellipse_surfaces()['limits'], R.DENSE_MIN)
        self.assertGreaterEqual(d['dense_asphalt_clearance_min'], R.DENSE_MIN)


class KeepOriginalTests(unittest.TestCase):
    """keep_original mode: the original line is never pushed off an edge it already uses (no clip/bump kink)."""
    @classmethod
    def setUpClass(cls):
        cls.text, _, _ = track_fixture(bump=True)
        cls.S = ellipse_surfaces()
        cls.new, cls.rep, cls.lines = R.regen(cls.text, cls.S, keep_original=True, log=lambda *a: None)

    def test_keep_corridor_relaxes_to_floor_then_holds(self):
        lo, hi = np.full(4, -5.8), np.full(4, 5.8)
        lo1, hi1 = np.full(4, -6.0), np.full(4, 6.0)
        ref = np.array([0.0, 5.9, 6.5, -6.2])
        l, h, keep, rel = R.keep_corridor(lo, hi, lo1, hi1, ref)
        self.assertEqual(list(keep), [False, False, True, True])
        self.assertEqual(list(rel), [False, True, False, False])
        self.assertAlmostEqual(h[1], 5.9); self.assertAlmostEqual(l[1], -5.8)     # local margin = the original's (1.1 m)
        self.assertEqual((l[2], h[2]), (6.5, 6.5)); self.assertEqual((l[3], h[3]), (-6.2, -6.2))
        self.assertTrue(l[0] == -5.8 and h[0] == 5.8)                 # untouched where the original is inside

    def test_bump_kept_on_original_and_counted(self):
        blocks = R.split_aiw(self.new)[2][:120]
        path = np.array([R.field(b, 'wp_path')[0] for b in blocks])
        np.testing.assert_array_equal(path[58:65], 6.5)
        self.assertGreaterEqual(self.rep['wps_kept_original'], 7)
        self.assertEqual(self.rep['corridor_mode'], 'keep_original')
        cb, ca = self.rep['curvature_before'], self.rep['curvature_after']
        for k in ('k_max', 'kink_max'):
            self.assertLessEqual(ca[k], cb[k] * (1 + R.CURV_TOL) + 1e-4)

    def test_dense_exemption_only_on_identical_chords(self):
        with self.assertRaises(ValueError):                           # the kept stretch is 0.5 m from the asphalt edge
            R.dense_check(self.lines['L'], self.S['road'], self.S['limits'])
        d = R.dense_check(self.lines['L'], self.S['road'], self.S['limits'], exempt=self.lines['exempt'])
        self.assertGreaterEqual(d['dense_asphalt_clearance_min'], R.DENSE_MIN)
        self.assertAlmostEqual(d['exempt_clearance_min'], 0.5, places=2)       # = the original's clearance
        ex = self.lines['exempt']
        self.assertTrue(ex[58:64].all())
        R.dense_check(self.lines['L2'], self.S['road'], self.S['limits'], exempt=self.lines['exempt2'])
        ids = set(int(i) for i in R.topology(R.split_aiw(self.text)[2])[0])
        self.assertEqual(R.text_diff(self.text, self.new, ids, True)[0], [])


class SourcePakTests(unittest.TestCase):
    def test_orig_preferred_and_backup_sha_checked(self):
        import tempfile
        from pathlib import Path
        with tempfile.TemporaryDirectory() as d:
            d = Path(d); tr = d / 'Pakfiles' / 'TRACKS'; tr.mkdir(parents=True)
            (tr / 'T_Physics.bff').write_bytes(b'modified')
            saved = R.KNOWN_BACKUPS
            try:
                R.KNOWN_BACKUPS = (d / 'bk' / '{t}',)
                self.assertEqual(R.source_pak(d, 'T')[0].name, 'T_Physics.bff')            # no .orig: the game pak
                (tr / 'T_Physics.bff.orig').write_bytes(b'original')
                src, s = R.source_pak(d, 'T')
                self.assertEqual(src.name, 'T_Physics.bff.orig')
                bk = d / 'bk' / 'T'; bk.mkdir(parents=True)
                (bk / 'T_Physics.bff.sha256').write_text('%s  T_Physics.bff\n' % s, encoding='ascii')
                (bk / 'T_Physics.bff').write_bytes(b'original')
                self.assertEqual(R.source_pak(d, 'T')[1], s)
                (bk / 'T_Physics.bff').write_bytes(b'other')
                with self.assertRaises(ValueError):
                    R.source_pak(d, 'T')
                (bk / 'T_Physics.bff').write_bytes(b'original')
                (tr / 'T_Physics.bff.orig').unlink()                                     # modified pak, backup says otherwise
                with self.assertRaises(ValueError):
                    R.source_pak(d, 'T')
            finally:
                R.KNOWN_BACKUPS = saved


class TimesSentinelTests(unittest.TestCase):
    def test_flt_max_sentinel_not_scaled(self):
        text, _, _ = track_fixture(bump=True)
        fmax = '340282346638528859811704183484516925440.0000'
        text = text.replace('times=(100.000,105.000)', 'times=(100.000,%s)' % fmax)
        new, rep, _ = R.regen(text, ellipse_surfaces(), log=lambda *a: None)
        hdr = R.split_aiw(new)[1]
        self.assertIn(',%s) // t' % fmax, hdr)
        self.assertNotIn('times=(100.000,', hdr)                     # the real time is still scaled

    def test_created_timesdry2_keeps_sentinel(self):
        text, _, _ = track_fixture(bump=True, dry2=False)
        fmax = '340282346638528859811704183484516925440.0000'
        text = text.replace('times=(100.000,105.000)', 'times=(%s,%s)' % (fmax, fmax))
        new, rep, _ = R.regen(text, ellipse_surfaces(), log=lambda *a: None)
        hdr = R.split_aiw(new)[1]
        self.assertIn('times=(%s,%s) // t\r\ntimesDry2=(%s)\r\n' % (fmax, fmax, fmax), hdr)


class SummaryTests(unittest.TestCase):
    def test_summary_row_min_clearance_and_refused(self):
        r = dict(track='X', accepted=False, failures=['3 est lap'], dense_line={'dense_asphalt_clearance_min': 1.2},
                 dense_dry2={'dense_asphalt_clearance_min': 1.1}, margin_used=1.0, margin_fallback=True)
        row = R.summary_row(r)
        self.assertEqual(row['min_clearance'], 1.1); self.assertEqual(row['reason'], '3 est lap')
        self.assertIsNone(R.summary_row(dict(track='Y', accepted=False, failures=['refused: kart track']))['min_clearance'])


@unittest.skipUnless(GAME_PAK.exists(), 'official pak not available')
class OfficialPakTests(unittest.TestCase):
    def test_patch_official_identity_and_aiw_roundtrip(self):
        import os, tempfile
        from bff import Bff
        from bff_write import patch_official
        b = Bff(str(GAME_PAK)); raw = GAME_PAK.read_bytes()
        self.assertEqual(patch_official(b, {}), raw)
        aid = R.uid('tracks/_data/aiw/interlagos.aiw')
        e = next(x for x in b.entries if x.uid == aid)
        plain = b.read(e) + b'\r\n// test\r\n'
        fd, tmp = tempfile.mkstemp(suffix='.bff'); os.close(fd)
        try:
            Path(tmp).write_bytes(patch_official(b, {aid: plain}))
            b2 = Bff(tmp)
            self.assertEqual([x.uid for x in b2.entries], [x.uid for x in b.entries])
            for x, y in zip(b.entries, b2.entries):
                if x.uid == aid:
                    self.assertEqual(b2.read(y), plain)
                else:
                    self.assertEqual(b.read_raw(x), b2.read_raw(y))
        finally:
            os.remove(tmp)


if __name__ == '__main__':
    unittest.main()
