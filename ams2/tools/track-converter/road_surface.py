"""Slope-preserving local smoothing of welded asphalt collision vertices."""
import numpy as np
from scipy.spatial import cKDTree


def smooth_heights(points, radius=1.0, maxcorr=0.05):
    """Fit a local plane, rather than averaging heights on unevenly sampled slopes.

    Weld globally so mesh boundaries receive identical corrections. The 3D radius
    keeps stacked roads apart; compact weights avoid steps as neighbours enter it.
    Rank-deficient/sparse neighbourhoods retain the source height.
    """
    points = np.asarray(points, dtype=np.float64)
    unique, inverse = np.unique(points, axis=0, return_inverse=True)
    tree = cKDTree(unique)
    correction = np.zeros(len(unique))
    for start in range(0, len(unique), 2048):
        end = min(start + 2048, len(unique))
        distances, indices = tree.query(unique[start:end], k=min(64, len(unique)),
                                        distance_upper_bound=radius)
        if distances.ndim == 1:
            continue
        valid = np.isfinite(distances)
        neighbours = unique[np.minimum(indices, len(unique) - 1)]
        delta = neighbours - unique[start:end, None, :]
        weights = np.where(valid, np.maximum(0, 1 - (distances / radius)**2)**2, 0)
        design = np.stack((delta[:, :, 0], delta[:, :, 2], np.ones_like(distances)), axis=2)
        normal = np.einsum('bki,bk,bkj->bij', design, weights, design)
        rhs = np.einsum('bki,bk,bk->bi', design, weights, delta[:, :, 1])
        eigenvalues = np.linalg.eigvalsh(normal)
        good = (valid.sum(axis=1) >= 6) & (eigenvalues[:, 0] > 1e-8 * eigenvalues[:, -1])
        fit = np.linalg.solve(normal[good], rhs[good, :, None])[:, :, 0]
        correction[start:end][good] = np.clip(fit[:, 2], -maxcorr, maxcorr)
    return points[:, 1] + correction[inverse]
