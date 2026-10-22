import sys
from pathlib import Path
import unittest
import numpy as np
sys.path.insert(0, str(Path(__file__).parent))
from road_surface import smooth_heights


class RoadSurfaceTests(unittest.TestCase):
    def test_irregular_sampling_preserves_banked_plane_and_mesh_seams(self):
        rng = np.random.default_rng(42)
        xz = rng.uniform(-3, 3, (1500, 2))
        p = np.column_stack((xz[:, 0], .12*xz[:, 0] + .07*xz[:, 1], xz[:, 1]))
        p = np.vstack((p, p[:50]))
        np.testing.assert_allclose(smooth_heights(p), p[:, 1], atol=1e-12)

    def test_reduces_short_wave_noise_without_flattening_slope(self):
        x, z = np.meshgrid(np.arange(0, 8, .15), np.arange(0, 4, .15))
        plane = .08*x + .05*z
        noisy = plane + .008*np.sin(x*18)*np.cos(z*16)
        p = np.column_stack((x.ravel(), noisy.ravel(), z.ravel()))
        result = smooth_heights(p)
        self.assertLess(np.std(result - plane.ravel()), np.std(noisy-plane)*.4)

    def test_bridge_levels_and_sparse_geometry_preserved(self):
        x, z = np.meshgrid(np.arange(0, 3, .2), np.arange(0, 3, .2))
        p = np.column_stack((x.ravel(), x.ravel()*.1, z.ravel()))
        p = np.vstack((p, p+[0, 5, 0], [[100, 3, 100]]))
        np.testing.assert_allclose(smooth_heights(p), p[:, 1], atol=1e-12)

    def test_noisy_seams_weld_and_correction_is_bounded(self):
        x, z = np.meshgrid(np.arange(-2, 2, .15), np.arange(-2, 2, .15))
        y = np.zeros(x.size); y[len(y)//2] = .2
        p = np.column_stack((x.ravel(), y, z.ravel()))
        p = np.vstack((p, p[:100]))
        result = smooth_heights(p, maxcorr=.01)
        self.assertLessEqual(np.max(np.abs(result-p[:, 1])), .01000000001)
        np.testing.assert_array_equal(result[-100:], result[:100])


if __name__ == '__main__':
    unittest.main()
