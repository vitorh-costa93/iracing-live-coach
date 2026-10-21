"""python -I tests_wall_clearance.py"""
from pathlib import Path
import sys
import unittest
import numpy as np
sys.path.insert(0,str(Path(__file__).resolve().parent))
from convert import wall_free


class WallClearanceTests(unittest.TestCase):
    def wall(self, bottom, top):
        return {'meshes':[{'name':'1WALL','pos':np.array([[2,bottom,-5],[2,bottom,5],[2,top,5]],float),'idx':np.array([0,1,2])}]}

    def test_wall_on_lower_level_does_not_close_bridge_corridor(self):
        fast=np.array([[0,4,-5],[0,4,0],[0,4,5]],float)
        self.assertEqual(30,wall_free(self.wall(-5,2.9),fast)[1,0])

    def test_overhead_wall_does_not_close_lower_corridor(self):
        fast=np.array([[0,0,-5],[0,0,0],[0,0,5]],float)
        self.assertEqual(30,wall_free(self.wall(8,12),fast)[1,0])

    def test_tall_wall_counts_even_when_samples_are_above_and_below_car(self):
        fast=np.array([[0,4,-5],[0,4,0],[0,4,5]],float)
        self.assertEqual(2,wall_free(self.wall(-10,10),fast)[1,0])


if __name__=='__main__':unittest.main()
