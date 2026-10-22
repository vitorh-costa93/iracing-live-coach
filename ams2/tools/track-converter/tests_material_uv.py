import importlib.util
from pathlib import Path
import unittest
import numpy as np
spec = importlib.util.spec_from_file_location('convert', Path(__file__).with_name('convert.py'))
c = importlib.util.module_from_spec(spec); spec.loader.exec_module(c)


class MaterialUvTests(unittest.TestCase):
    def test_small_dense_patch_does_not_set_large_surface_scale(self):
        # One 100 m2 triangle and twenty tiny triangles with another UV density.
        large = np.array([[0., 0, 0], [20, 0, 0], [0, 0, 10]])
        small = np.array([[30., 0, 0], [30.1, 0, 0], [30, 0, .1]])
        points = np.vstack([large] + [small]*20)
        uv = np.tile([[0., 0], [1, 0], [0, 1]], (21, 1))
        np.testing.assert_allclose(c.metric_uv_scale(points, uv, np.arange(len(points)), 4), [5, 2.5])

    def test_uniform_mapping_and_degenerate_mesh(self):
        points = np.array([[0., 0, 0], [8, 0, 0], [0, 0, 4]])
        uv = np.array([[0., 0], [1, 0], [0, 1]])
        np.testing.assert_allclose(c.metric_uv_scale(points, uv, np.array([0,1,2]), 4), [2,1])
        np.testing.assert_array_equal(c.metric_uv_scale(points*0, uv, np.array([0,1,2]), 4), [1,1])


if __name__ == '__main__':
    unittest.main()
