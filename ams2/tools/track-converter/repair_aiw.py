"""Repair a converted main AI line against its existing collision and track limits.

python -I repair_aiw.py <Automobilista 2 folder> <track_id> <new output folder>

Copies the source to a new folder; changes only its AIW entry in Physics.bff.
The original AC source is unnecessary. Pit branches, grid, timing gates and other
resources remain byte-identical. Run check_out.py on the result before packaging.
"""
import argparse
import json
from pathlib import Path
import re
import shutil
import sys

import numpy as np
sys.path.insert(0, str(Path(__file__).resolve().parent))
from aiw_pit import parse_aiw, tup
from bff import Bff
from bff_write import write_bff
from csm import read_csm, parse_mesh
from gcl import read_gcl
from convert import min_curvature_offsets, est_laptime, corner_marks, line_curvature


class LateralSurface:
    """Exact intervals cut by a horizontal line through a triangle surface.

    Height is interpolated on each triangle, rejecting roads on another level.
    """
    def __init__(self, triangles):
        tri = np.asarray(triangles, dtype=float)
        if tri.ndim != 3 or tri.shape[1:] != (3, 3) or not np.isfinite(tri).all():
            raise ValueError('Surface requires finite triangles with shape (n,3,3)')
        a, b, c = tri[:, 0], tri[:, 1], tri[:, 2]
        ab, ac = b - a, c - a
        det = ab[:, 0] * ac[:, 2] - ab[:, 2] * ac[:, 0]
        keep = np.abs(det) > 1e-10
        self.a, self.ab, self.ac, self.det = a[keep], ab[keep], ac[keep], det[keep]
        self.mn, self.mx = tri[keep].min(axis=1), tri[keep].max(axis=1)

    def intervals(self, point, perp, reach=30.0, height=2.0):
        selected = ((self.mn[:, 0] <= point[0] + reach) & (self.mx[:, 0] >= point[0] - reach)
                    & (self.mn[:, 2] <= point[2] + reach) & (self.mx[:, 2] >= point[2] - reach)
                    & (self.mn[:, 1] <= point[1] + height) & (self.mx[:, 1] >= point[1] - height))
        a, ab, ac, det = self.a[selected], self.ab[selected], self.ac[selected], self.det[selected]
        q = point - a
        u = (q[:, 0] * ac[:, 2] - q[:, 2] * ac[:, 0]) / det
        du = (perp[0] * ac[:, 2] - perp[2] * ac[:, 0]) / det
        v = (ab[:, 0] * q[:, 2] - ab[:, 2] * q[:, 0]) / det
        dv = (ab[:, 0] * perp[2] - ab[:, 2] * perp[0]) / det
        y = a[:, 1] + u * ab[:, 1] + v * ac[:, 1]
        dy = du * ab[:, 1] + dv * ac[:, 1]
        lo, hi = np.full(len(a), -reach), np.full(len(a), reach)
        # Affine barycentric and height constraints, each c + t*d >= 0.
        for offset, slope in ((u, du), (v, dv), (1-u-v, -du-dv),
                              (y-point[1]+height, dy), (point[1]+height-y, -dy)):
            pos, neg, flat = slope > 1e-12, slope < -1e-12, np.abs(slope) <= 1e-12
            lo[pos] = np.maximum(lo[pos], -offset[pos]/slope[pos])
            hi[neg] = np.minimum(hi[neg], -offset[neg]/slope[neg])
            hi[flat & (offset < -1e-8)] = -np.inf
        return merge_intervals(zip(lo[lo <= hi], hi[lo <= hi]))


def merge_intervals(intervals):
    merged = []
    for lo, hi in sorted(intervals):
        if merged and lo <= merged[-1][1] + 1e-5:
            merged[-1] = (merged[-1][0], max(hi, merged[-1][1]))
        else:
            merged.append((float(lo), float(hi)))
    return merged


def safe_corridor(road, limits, point, perp, width, path, margin):
    candidates = []
    for rl, rh in road.intervals(point, perp):
        for gl, gh in limits.intervals(point, perp):
            left, right = max(rl, gl + .02, -width[0]), min(rh, gh - .02, width[1])
            if right - left >= 2*margin:
                candidates.append((left, right))
    if not candidates:
        raise ValueError('No asphalt/GCL corridor wide enough for the requested margin')
    distances = [max(left+margin-path, path-(right-margin), 0) for left, right in candidates]
    order = np.argsort(distances)
    if len(order) > 1 and abs(distances[order[0]] - distances[order[1]]) < 1e-5:
        raise ValueError('Ambiguous disconnected asphalt corridors')
    return candidates[order[0]]


def dense_clearance(line, road, limits, minimum=1.0):
    """Check the serialized line between waypoints, at intervals below one metre."""
    count, smallest = 0, float('inf')
    for i, (a, b) in enumerate(zip(line, np.roll(line, -1, axis=0))):
        tangent = b-a
        tangent[1] = 0
        length = np.linalg.norm(tangent)
        if length < .01:
            raise ValueError('Duplicate repaired racing-line waypoint')
        perp = np.array([tangent[2], 0, -tangent[0]]) / length
        for fraction in np.linspace(0, 1, int(np.ceil(np.linalg.norm(b-a)))+1, endpoint=False):
            point = a*(1-fraction) + b*fraction
            clearance = max((min(-lo, hi) for lo, hi in road.intervals(point, perp)
                             if lo <= 0 <= hi), default=-1)
            if clearance < minimum or not any(lo <= 0 <= hi for lo, hi in limits.intervals(point, perp)):
                raise ValueError('Unsafe repaired segment %d at %.3f: asphalt clearance %.3fm' %
                                 (i, fraction, clearance))
            smallest = min(smallest, clearance)
            count += 1
    return dict(dense_samples=count, dense_asphalt_clearance_min=smallest)


def repair_aiw(text, road, limits, margin=1.5):
    if not np.isfinite(margin) or margin < 1.0:
        raise ValueError('Asphalt margin must be finite and at least 1m')
    prefix, waypoint_text = text.split('[Waypoint]', 1)
    parts = re.split(r'(?m)^(\\\\[^\r\n]*\n)', waypoint_text)
    blocks = [parts[i] + parts[i+1] for i in range(1, len(parts), 2)]
    wps = parse_aiw(waypoint_text)
    if len(blocks) != len(wps):
        raise ValueError('Unsupported AIW waypoint syntax')
    main = [i for i,w in enumerate(wps) if int(tup(w['wp_branchID'])[0]) == 0]
    if main != list(range(len(main))) or len(main) < 4:
        raise ValueError('Main branch must be the first contiguous waypoint block')
    P = np.array([tup(wps[i]['wp_pos']) for i in main])
    perp = np.array([tup(wps[i]['wp_perp']) for i in main])
    old = np.array([tup(wps[i]['wp_path'])[0] for i in main])
    widths = np.array([tup(wps[i]['wp_width']) for i in main])
    if not all(np.isfinite(a).all() for a in (P, perp, old, widths)) or np.any(widths <= 0):
        raise ValueError('Nonfinite geometry or nonpositive AI corridor width')
    if not np.allclose(np.linalg.norm(perp, axis=1), 1, atol=.001):
        raise ValueError('AI lateral vectors must have unit length')
    edges = np.array([safe_corridor(road, limits, p, pp, width, path, margin)
                      for p,pp,width,path in zip(P,perp,widths,old)])
    if np.any(edges[:,0] >= 0) or np.any(edges[:,1] <= 0):
        raise ValueError('Main AI centres outside repaired corridor require a full source conversion')
    lo, hi = edges[:,0] + margin - old, edges[:,1] - margin - old
    # Smooth the correction, preserving the existing line's geometry elsewhere.
    delta = min_curvature_offsets(np.zeros_like(P), perp, lo, hi, np.zeros(len(P)), reg=.02)
    path = np.round(old + delta, 4)
    if np.any(path < edges[:,0]+margin-.0001) or np.any(path > edges[:,1]-margin+.0001):
        raise ValueError('Serialized racing line violates repaired corridor')
    line = P + path[:,None]*perp
    dense = dense_clearance(line, road, limits)
    cty,cst = corner_marks(line)
    curvature,_ = line_curvature(line)
    hot = np.abs(curvature) > 1/200
    galpha = .45*(np.convolve(np.r_[hot[-6:],hot,hot[:6]].astype(float),np.ones(13),'valid') > 0)
    for i in main:
        updates = dict(wp_path='(%.4f)' % path[i], wp_width='(%.4f,%.4f)' % (-edges[i,0], edges[i,1]),
                       wp_groove_lat='(%.4f)' % (-.27*path[i]), wp_galpha='(%.4f)' % galpha[i],
                       wpd_CornerType='(%d)' % cty[i], wpd_CornerState='(%d)' % cst[i])
        # Never expand the kerb/escape corridor when reducing asphalt width.
        dwidth=tup(wps[i]['wp_dwidth'])
        updates['wp_dwidth']='(%.4f,%.4f)' % (min(dwidth[0], -edges[i,0]),min(dwidth[1],edges[i,1]))
        for key,value in updates.items():
            blocks[i], count = re.subn(r'(?m)^'+re.escape(key)+r'=[^\r\n]*',key+'='+value,blocks[i])
            if count != 1: raise ValueError('Missing or duplicated waypoint field '+key)
    tdry=est_laptime(line)
    header=re.sub(r'(?m)^times=[^\r\n]*', 'times=(%.4f,%.4f)' % (tdry,tdry*1.045), parts[0])
    result=prefix+'[Waypoint]'+header+''.join(blocks)
    return result, dict(**dense, main_waypoints=len(main),margin=margin,changed_paths=int(np.count_nonzero(np.abs(path-old)>.0001)),
                        max_shift=float(np.max(np.abs(path-old))),mean_shift=float(np.mean(np.abs(path-old))),
                        lap_est_before=est_laptime(P+old[:,None]*perp),lap_est_after=tdry,
                        pit_blocks_preserved=blocks[len(main):]==[parts[i]+parts[i+1] for i in range(1+2*len(main),len(parts),2)])


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source',type=Path);parser.add_argument('track_id');parser.add_argument('output',type=Path)
    parser.add_argument('--margin',type=float,default=1.5)
    args=parser.parse_args()
    source,output=args.source.resolve(),args.output.resolve()
    if source == output or source in output.parents or output.exists():
        raise ValueError('Output must be a new folder outside the source')
    pak=source/'Pakfiles'/'Tracks'/(args.track_id+'_Physics.bff')
    archive=Bff(str(pak));entries={e.name.lower():e for e in archive.entries}
    td='tracks\\'+args.track_id+'\\'
    aiw=entries['tracks\\_data\\aiw\\'+args.track_id+'.aiw']
    csm=archive.read(entries[td+'physics\\'+args.track_id+'.csm'])
    meshes=[parse_mesh(blob) for blob,sid in read_csm(csm)[1] if sid==0]
    road=LateralSurface(np.vstack([m.verts[m.tris] for m in meshes]))
    gcl=read_gcl(archive.read(entries[td+'track_cut\\'+args.track_id+'.gcl']))
    limits=LateralSurface(gcl['tris']['v'][gcl['tris']['f']==1])
    text,report=repair_aiw(archive.read(aiw).decode('latin1'),road,limits,args.margin)
    shutil.copytree(source,output)
    items=[dict(name=e.name,data=text.encode('latin1'),timestamp=e.timestamp) if e is aiw else
           dict(name=e.name,stored=archive.read_raw(e),size=e.size,timestamp=e.timestamp) for e in archive.entries]
    write_bff(str(output/'Pakfiles'/'Tracks'/pak.name),archive.name,items)
    (output.parent/(args.track_id+'-ai-repair.json')).write_text(json.dumps(report,indent=2),encoding='utf8')
    (output.parent/(args.track_id+'-repaired.aiw')).write_text(text,encoding='latin1')
    print(json.dumps(report,indent=2))


if __name__=='__main__':main()
