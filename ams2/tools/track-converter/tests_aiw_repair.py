"""Focused collision-corridor repair tests: python -I tests_aiw_repair.py."""
from pathlib import Path
import sys
import unittest
import numpy as np
sys.path.insert(0,str(Path(__file__).resolve().parent))
from repair_aiw import LateralSurface, merge_intervals, safe_corridor, repair_aiw, dense_clearance
from aiw_pit import parse_aiw, tup


def strip(x0,x1,y=0,z0=-100,z1=100):
    return np.array([[[x0,y,z0],[x1,y,z0],[x1,y,z1]],[[x0,y,z0],[x1,y,z1],[x0,y,z1]]])


def circle_fixture(n=64):
    theta=np.arange(n)*2*np.pi/n
    P=np.c_[40*np.cos(theta),np.full(n,.3),40*np.sin(theta)]
    perp=np.c_[np.cos(theta),np.zeros(n),np.sin(theta)]
    inner=np.c_[36*np.cos(theta),np.zeros(n),36*np.sin(theta)]
    outer=np.c_[44*np.cos(theta),np.zeros(n),44*np.sin(theta)]
    triangles=[]
    for i in range(n):
        j=(i+1)%n;triangles.extend([[inner[i],outer[i],outer[j]],[inner[i],outer[j],inner[j]]])
    blocks=[]
    for i,(p,pp) in enumerate(zip(P,perp)):
        blocks.append('\\\\%d\nwp_pos=(%s)\nwp_perp=(%s)\nwp_path=(5.0)\nwp_width=(8,8)\nwp_dwidth=(10,10)\nwp_branchID=(0)\nwp_groove_lat=(0)\nwp_galpha=(0)\nwpd_CornerType=(0)\nwpd_CornerState=(0)\nWP_PTRS=(%d,%d,-1,0)\nwp_score=(0,%d)\n' % (i,','.join(map(str,p)),','.join(map(str,pp)),(i-1)%n,(i+1)%n,i*4))
    pit='\\\\pit-unchanged\nwp_branchID=(1)\nwp_pos=(45,.3,0)\nwp_path=(0)\nWP_PTRS=(0,1,-1,0)\n'
    text='[Features]\npitspots=1\n[GRID]\nPos=(40,.3,0)\n[PITS]\nPitPos=(45,.3,0)\n[Waypoint]\ntimes=(100,105)\n'+''.join(blocks)+pit
    return text,LateralSurface(triangles),pit


class RepairTests(unittest.TestCase):
    def test_exact_triangle_intersection_and_height_filter(self):
        surface=LateralSurface(np.concatenate([strip(-4,6),strip(-20,20,y=8)]))
        intervals=surface.intervals(np.array([0,.3,0]),np.array([1,0,0]))
        np.testing.assert_allclose(intervals,[[-4,6]])

    def test_disconnected_strips_are_not_bridged(self):
        surface=LateralSurface(np.concatenate([strip(-8,-2),strip(2,8)]))
        np.testing.assert_allclose([(-8.,-2.),(2.,8.)],surface.intervals(np.array([0,.3,0]),np.array([1,0,0])))
        with self.assertRaisesRegex(ValueError,'Ambiguous'):
            safe_corridor(surface,LateralSurface(strip(-20,20)),np.array([0,.3,0]),np.array([1,0,0]),[10,10],0,1)

    def test_corridor_intersects_gcl_and_original_width(self):
        road=LateralSurface(strip(-10,10));limits=LateralSurface(strip(-3,4))
        lo,hi=safe_corridor(road,limits,np.array([0,.3,0]),np.array([1,0,0]),[2,8],0,1)
        self.assertEqual(-2,lo);self.assertAlmostEqual(3.98,hi)
        with self.assertRaisesRegex(ValueError,'wide enough'):
            safe_corridor(LateralSurface(strip(-.8,.8)),limits,np.array([0,.3,0]),np.array([1,0,0]),[8,8],0,1)

    def test_repair_moves_line_to_asphalt_preserves_pits_and_grid(self):
        text,surface,pit=circle_fixture()
        result,report=repair_aiw(text,surface,surface)
        self.assertEqual(text.split('[Waypoint]')[0],result.split('[Waypoint]')[0])
        self.assertTrue(result.endswith(pit));self.assertTrue(report['pit_blocks_preserved'])
        old=parse_aiw(text.split('[Waypoint]')[1]);new=parse_aiw(result.split('[Waypoint]')[1])
        for before,after in zip(old[:-1],new[:-1]):
            for key in ('wp_pos','wp_perp','WP_PTRS','wp_score','wp_branchID'):
                self.assertEqual(before[key],after[key])
            left,right=tup(after['wp_width']);path=tup(after['wp_path'])[0]
            self.assertGreaterEqual(min(left+path,right-path),.9998)
            self.assertLess(path,3.01)
        self.assertGreater(report['max_shift'],1.9)

    def test_small_or_nonfinite_margin_rejected(self):
        text,surface,_=circle_fixture()
        for margin in (0,.9,float('nan')):
            with self.assertRaisesRegex(ValueError,'at least 1m'):repair_aiw(text,surface,surface,margin)

    def test_dense_check_rejects_escape_between_safe_waypoints(self):
        line=np.array([[-5,.3,-5],[5,.3,-5],[5,.3,5],[-5,.3,5]])
        road=LateralSurface(np.concatenate([strip(-10,-1),strip(1,10)]))
        with self.assertRaisesRegex(ValueError,'Unsafe repaired segment'):
            dense_clearance(line,road,LateralSurface(strip(-20,20)))

    def test_nonfinite_surface_rejected(self):
        with self.assertRaisesRegex(ValueError,'finite triangles'):
            LateralSurface(np.full((1,3,3),float('nan')))

    def test_touching_intervals_merge_but_holes_remain(self):
        self.assertEqual([(-4,2),(3,5)],merge_intervals([(-4,-1),(-1,2),(3,5)]))


if __name__=='__main__':unittest.main()
