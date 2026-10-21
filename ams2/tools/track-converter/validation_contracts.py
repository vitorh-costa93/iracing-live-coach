"""Independent, testable acceptance rules for generated AMS2 track data."""
import numpy as np


def pit_endpoint_errors(branches, pointers):
    pit = np.flatnonzero(branches == 1)
    if len(pit) < 2:
        return ['pit needs at least two waypoints']
    first, last = pit[0], pit[-1]
    fork, join = pointers[first, 0], pointers[last, 1]
    errors = []
    if not (0 <= fork < len(branches) and branches[fork] == 0): errors.append('pit fork not on main')
    elif pointers[fork, 2] != first: errors.append('main fork has no pit link')
    if not (0 <= join < len(branches) and branches[join] == 0): errors.append('pit join not on main')
    if pointers[first, 1] != first+1 or pointers[first, 3] != 1: errors.append('pit first next/branch')
    if pointers[last, 0] != last-1 or pointers[last, 2] != join or pointers[last, 3] != 1: errors.append('pit last prev/link/branch')
    return errors


def active_camera_coverage(centres, radii, references, waypoints):
    indices = sorted(set(i for i in references if 0 <= i < len(centres)))
    if not indices or not len(waypoints): return 0.0
    return float(np.mean([(np.linalg.norm(centres[indices] - p, axis=1) <= radii[indices]).any() for p in waypoints]))
