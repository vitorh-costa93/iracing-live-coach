"""python -I tests_validation.py"""
import importlib.util
from pathlib import Path
import sys
import struct
import contextlib
import io
import tempfile
import unittest
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
from validation_contracts import pit_endpoint_errors, active_camera_coverage
from batch_convert import fingerprint, file_hash
from csm_write import cook_mesh, ray_down_tree, ray_down_brute
from csm import parse_mesh
from kn5 import read_kn5
import convert


class ValidationTests(unittest.TestCase):
    def test_swapped_source_timing_gates_are_relabelled_in_driving_order(self):
        from tests_track_conversion import sample_ai
        fast,extra,dummies=sample_ai()
        swapped=dict(dummies)
        for side in ('L','R'):
            swapped['AC_TIME_1_'+side],swapped['AC_TIME_2_'+side]=dummies['AC_TIME_2_'+side],dummies['AC_TIME_1_'+side]
        with contextlib.redirect_stdout(io.StringIO()):corrected=convert.order_timing_sectors(fast,swapped)
        for side in ('L','R'):np.testing.assert_array_equal(dummies['AC_TIME_1_'+side],corrected['AC_TIME_1_'+side])
        self.assertIs(dummies,convert.order_timing_sectors(fast,dummies))

    def test_coincident_source_timing_gates_are_rejected(self):
        from tests_track_conversion import sample_ai
        fast,extra,dummies=sample_ai()
        for side in ('L','R'):dummies['AC_TIME_1_'+side]=dummies['AC_TIME_0_'+side]
        with self.assertRaisesRegex(ValueError,'distinct sectors'):convert.order_timing_sectors(fast,dummies)

    def test_paved_apron_keeps_asphalt_physics_outside_track_limits(self):
        self.assertEqual(0,convert.surf_id('PAVEMENT',{'PAVEMENT'}))
        self.assertEqual(7,convert.surf_id('GRASS',{'GRASS'}))

    def test_collision_smoothing_keeps_stacked_road_levels_separate(self):
        road=np.array([[0,0,0],[1,0,0],[0,0,1],[0,6,0],[1,6,0],[0,6,1]],dtype=np.float32)
        kerb=road.copy();kerb[:,1]+=.1
        items=[(road,np.array([[0,1,2],[3,4,5]]),0),(kerb,np.array([[0,1,2],[3,4,5]]),10)]
        with contextlib.redirect_stdout(io.StringIO()):result=convert.smooth_kerbs(items)
        np.testing.assert_allclose(result[0][0][:,1],road[:,1],atol=1e-6)
        np.testing.assert_allclose(result[1][0][:,1],road[:,1]+.065,atol=1e-6)

    def test_camera_corridor_uses_serialized_centres_and_widths(self):
        from tests_track_conversion import sample_ai
        from aiw_pit import parse_aiw,tup
        fast,extra,dummies=sample_ai();extra[:,5]=2;extra[:,6]=20
        with contextlib.redirect_stdout(io.StringIO()):text,info=convert.build_aiw(fast,extra,dummies,'camera')
        points=[w for w in parse_aiw(text.split('[Waypoint]')[1]) if 'wp_pos' in w and int(tup(w['wp_branchID'])[0])==0]
        centres=np.array([tup(w['wp_pos']) for w in points])
        widths=np.array([tup(w['wp_width'])[0] for w in points])
        np.testing.assert_allclose(info['camera_centres'],centres,atol=.0001)
        np.testing.assert_allclose(info['camera_half'],widths,atol=.001)

    def test_skinned_mesh_keeps_vertex_layout_and_following_sibling_aligned(self):
        def integer(value): return struct.pack('<i', value)
        def string(value): return integer(len(value)) + value.encode()
        def header(kind, name, children=0): return integer(kind)+string(name)+integer(children)+b'\1'
        identity = np.eye(4, dtype='<f4').tobytes()
        data = b'sc6969'+integer(5)+integer(0)+integer(0)
        data += header(1, 'root', 2)+identity
        data += header(3, 'worker')+b'\1\1\0'+integer(1)+string('bone')+identity
        vertices = np.zeros((3,19), dtype='<f4'); vertices[:,:3] = [[1,2,3],[4,5,6],[7,8,9]]
        vertices[:,6:8] = [[.1,.2],[.3,.4],[.5,.6]]
        data += integer(3)+vertices.tobytes()+integer(3)+struct.pack('<3H',0,1,2)+integer(0)+integer(0)+bytes(8)
        data += header(1, 'AC_START_0')+identity
        with tempfile.TemporaryDirectory() as folder:
            file=Path(folder)/'test.kn5'; file.write_bytes(data); result=read_kn5(file)
        self.assertEqual('AC_START_0',result['dummies'][-1][0])
        self.assertTrue(result['meshes'][0]['skinned_bind_pose'])
        np.testing.assert_allclose(vertices[:,:3],result['meshes'][0]['pos'])
        np.testing.assert_allclose(vertices[:,6:8],result['meshes'][0]['uv'])

    def test_bridge_above_car_does_not_hide_its_driving_surface(self):
        points = np.array([[0,0,0],[10,0,0],[0,0,10],[0,8,0],[10,8,0],[0,8,10]], dtype=float)
        triangles = np.array([[0,1,2],[3,4,5]])
        mesh = parse_mesh(cook_mesh(points, triangles))
        self.assertEqual(8, ray_down_tree(mesh, 1, 1))
        self.assertEqual(0, ray_down_tree(mesh, 1, 1, max_y=2.3))
        self.assertEqual(ray_down_brute(points, triangles, 1, 1, max_y=2.3), ray_down_tree(mesh,1,1,max_y=2.3))

    def test_disconnected_pit_endpoints_are_rejected(self):
        branches = np.array([0,0,0,0,1,1,1])
        pointers = np.array([[3,1,4,0],[0,2,-1,0],[1,3,-1,0],[2,0,-1,0],[0,5,-1,1],[4,6,-1,1],[5,2,2,1]])
        self.assertEqual([], pit_endpoint_errors(branches,pointers))
        for index,column,value in [(4,1,4),(6,0,6),(6,2,-1),(4,3,0),(6,3,0)]:
            corrupt=pointers.copy();corrupt[index,column]=value
            self.assertTrue(pit_endpoint_errors(branches,corrupt))

    def test_unreferenced_camera_areas_do_not_count_as_coverage(self):
        centres=np.array([[0,0,0],[100,0,0]])
        radii=np.array([10,10]);points=centres.copy()
        self.assertEqual(0,active_camera_coverage(centres,radii,[],points))
        self.assertEqual(.5,active_camera_coverage(centres,radii,[0],points))
        self.assertEqual(1,active_camera_coverage(centres,radii,[0,1],points))

    def test_shared_surfaces_invalidate_layout_conversion_cache(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder); (root/'data').mkdir(); file=root/'data/surfaces.ini';file.write_text('road=0')
            first=fingerprint(root,'gp');file.write_text('road=7')
            self.assertNotEqual(first,fingerprint(root,'gp'))
            self.assertEqual(file_hash(file),file_hash(file))


if __name__ == '__main__': unittest.main()
