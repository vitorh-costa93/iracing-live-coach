import numpy as np
from scipy.ndimage import gaussian_filter1d
from refine import resample_closed, fit_line, line_inter
import curvefit as CF
def unit_(v):
    n=np.hypot(*v);return v/n if n>1e-9 else v
def snap_dir(d,deg=9):
    a=np.degrees(np.arctan2(d[0],d[1]))%180      # d = (dx,dy) em (x,y)
    for t,vec in ((0,np.array([1.,0.])),(180,np.array([1.,0.])),(90,np.array([0.,1.]))):
        if abs(a-t)<deg: return vec
    return d
def closed_segments(poly_up,UP,err=0.04,step=0.06,corner_deg=44,win=0.62,straight_tol=0.32,smooth=0.55,snap=1.8):
    """poly_up: (row,col) em px*UP. Retorna lista de segmentos [('L',p0,p1)|('C',p0,c1,c2,p1)] em coords (x,y) px originais, y para cima."""
    q=resample_closed(poly_up/UP,step)
    p=np.stack([q[:,1],-q[:,0]],1)          # (x,y)
    n=len(p);k=max(2,int(round(win/step)))
    v1=p-np.roll(p,k,0);v2=np.roll(p,-k,0)-p
    ang=np.abs(np.arctan2(v1[:,0]*v2[:,1]-v1[:,1]*v2[:,0],(v1*v2).sum(1)))
    cand=np.where(ang>np.deg2rad(corner_deg))[0];corners=[]
    for i in cand:
        w=[(i+j)%n for j in range(-k,k+1)]
        if ang[i]>=ang[w].max()-1e-9 and all((i-c)%n>k and (c-i)%n>k for c in corners): corners.append(i)
    corners=sorted(corners)
    sm=lambda a:gaussian_filter1d(a,smooth/step,mode='wrap')
    if len(corners)<2:
        ps=np.stack([sm(p[:,0]),sm(p[:,1])],1)
        # 4 arcos nos extremos
        ex=sorted({int(np.argmin(ps[:,0])),int(np.argmax(ps[:,0])),int(np.argmin(ps[:,1])),int(np.argmax(ps[:,1]))})
        segs=[]
        for a in range(len(ex)):
            i0=ex[a];i1=ex[(a+1)%len(ex)]
            idx=np.arange(i0,i1+1 if i1>i0 else i1+n+1)%n
            arc=ps[idx]
            tL,tR=CF.end_tangents(arc,6)
            # tangentes nos extremos: eixo
            for nm in ('L','R'):
                t=tL if nm=='L' else tR
                for vec in (np.array([1.,0.]),np.array([0.,1.])):
                    if abs(abs(t@vec)-1)<0.04: t=vec*np.sign(t@vec) if abs(t@vec)>0 else vec
                if nm=='L': tL=t
                else: tR=t
            for B in CF.fit_cubic(arc,tL,tR,err): segs.append(('C',B[0],B[1],B[2],B[3]))
        return segs
    m=len(corners);raw=[]
    for a in range(m):
        i0=corners[a];i1=corners[(a+1)%m]
        idx=np.arange(i0,i1+1 if i1>i0 else i1+n+1)%n;raw.append(p[idx])
    info=[]
    for sg in raw:
        trim=int(0.5/step);core=sg[trim:-trim] if len(sg)>2*trim+4 else sg
        c,d=fit_line(core);dev=np.abs((core-c)@np.array([-d[1],d[0]])).max()
        length=np.hypot(*(sg[-1]-sg[0]))
        kk=max(2,len(sg)//4)
        a1=unit_(sg[kk]-sg[0]);a2=unit_(sg[-1]-sg[-1-kk])
        rot=np.degrees(np.arccos(np.clip(a1@a2,-1,1)))
        straight=(dev<straight_tol+0.02*length and length>1.0 and rot<7.0)
        if straight: d=snap_dir(d)
        info.append([c,d,straight])
    cp=[p[i].copy() for i in corners]
    for a in range(m):
        pv=info[(a-1)%m];nx=info[a]
        if pv[2] and nx[2]:
            x=line_inter(pv[0],pv[1],nx[0],nx[1])
            if x is not None and np.hypot(*(x-cp[a]))<snap: cp[a]=x
        elif pv[2] or nx[2]:
            c,d=(pv[0],pv[1]) if pv[2] else (nx[0],nx[1]);cp[a]=c+d*np.dot(cp[a]-c,d)
    segs=[]
    for a in range(m):
        c0=cp[a];c1=cp[(a+1)%m];sg=raw[a].copy()
        if info[a][2]: segs.append(('L',c0,c1));continue
        sg[0]=c0;sg[-1]=c1;pad=int(3*smooth/step)
        L=np.vstack([2*sg[0]-sg[1:pad+1][::-1],sg,2*sg[-1]-sg[-pad-1:-1][::-1]])
        s2=np.stack([gaussian_filter1d(L[:,0],smooth/step),gaussian_filter1d(L[:,1],smooth/step)],1)[pad:-pad]
        s2[0]=c0;s2[-1]=c1
        tL,tR=CF.end_tangents(s2,6)
        for B in CF.fit_cubic(s2,tL,tR,err): segs.append(('C',B[0],B[1],B[2],B[3]))
    return segs
