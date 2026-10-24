"""--ai-brake-physics (default ON since 23/10, --no-ai-brake-physics): braking only where the downforce model needs it; no clip kinks. Run: python -I tests_ai_brake_physics.py."""
import importlib.util
from pathlib import Path
import sys
import unittest

import numpy as np

HERE = Path(__file__).parent
sys.path.insert(0, str(HERE))
spec = importlib.util.spec_from_file_location('track_convert_brake', HERE / 'convert.py')
convert = importlib.util.module_from_spec(spec)
spec.loader.exec_module(convert)


def rounded_square(R, S, step=5.0):
    """Closed line: 4 straights of S m joined by quarter circles of radius R (x, 0, z)."""
    pts = []
    for q, (cx, cz) in enumerate([(S / 2, S / 2), (-S / 2, S / 2), (-S / 2, -S / 2), (S / 2, -S / 2)]):
        a0 = q * np.pi / 2
        p0 = np.array([cx + R * np.cos(a0), cz + R * np.sin(a0)]); d = np.array([-np.sin(a0), np.cos(a0)])
        pts += [p0 - d * (S - t) for t in np.arange(0, S, step)]
        pts += [np.array([cx + R * np.cos(a0 + u / R), cz + R * np.sin(a0 + u / R)]) for u in np.arange(0, R * np.pi / 2, step)]
    return np.array([[p[0], 0.0, p[1]] for p in pts])


class BrakePhysicsTests(unittest.TestCase):
    def test_flag_default_on(self):
        self.assertTrue(convert.parse_args(['s', 'id', 'o', 'r', 'x']).ai_brake_physics)
        self.assertFalse(convert.parse_args(['s', 'id', 'o', 'r', 'x', '--no-ai-brake-physics']).ai_brake_physics)

    def test_fast_corner_without_brake_needs_no_reduction(self):
        L = rounded_square(R=300.0, S=600.0)          # 300 m sweepers after 600 m straights: flat in an F1 (F1 2020: no brake_line)
        self.assertEqual(convert.braking_zones(L, physics=True), [])
        v, _, fwd = convert.speed_profile_aero(L, with_fwd=True)
        self.assertLess(float((fwd - v).max()), convert.BRAKE_CUT)
        self.assertGreater(len(convert.braking_zones(L)), 0)   # the old 1.2 g model brakes for it (the bug)

    def test_fast_sequence_without_brake_needs_no_reduction(self):
        L = rounded_square(R=240.0, S=150.0)          # short straights: a chain of quick corners
        self.assertEqual(convert.braking_zones(L, physics=True), [])

    def test_slow_corner_with_brake_gets_reduction(self):
        L = rounded_square(R=30.0, S=500.0)           # hairpins after long straights (F1 2020: brake_line)
        zones = convert.braking_zones(L, physics=True)
        self.assertEqual(len(zones), 4)
        v, _ = convert.speed_profile_aero(L)
        for a, n in zones:
            self.assertGreater(v[a] - v[(a + n) % len(L)], 20.0)

    def test_corridor_fit_keeps_shape_without_kink(self):
        n = 200; s = np.arange(n)
        target = 4.0 * np.sin(2 * np.pi * s / n)       # wanted line pokes 0.8 m out of a 3.2 m corridor
        lo, hi = np.full(n, -3.2), np.full(n, 3.2)
        fit = convert.corridor_fit(lo, hi, target)
        clip = np.clip(target, lo, hi)
        self.assertTrue(np.all(fit >= lo - 1e-9) and np.all(fit <= hi + 1e-9))
        d2 = lambda a: np.abs(np.roll(a, -1) - 2 * a + np.roll(a, 1)).max()
        self.assertLess(d2(fit), 0.5 * d2(clip))       # no kink where the clip pins the line
        inside = np.abs(target) < 2.0
        self.assertLess(np.abs(fit - target)[inside].max(), 1.0)
        self.assertTrue(np.allclose(convert.corridor_fit(lo, hi, 0.5 * target), 0.5 * target))   # inside: untouched


if __name__ == '__main__':
    unittest.main()
