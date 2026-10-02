import numpy as np
from scipy.ndimage import gaussian_filter1d
def resample_closed(p,step):
    q=np.vstack([p,p[:1]]);d=np.hypot(*np.diff(q,axis=0).T);s=np.concatenate([[0],np.cumsum(d)]);L=s[-1]
    n=max(8,int(L/step));t=np.linspace(0,L,n,endpoint=False)
    return np.stack([np.interp(t,s,q[:,0]),np.interp(t,s,q[:,1])],1)
def fit_line(pts):
    c=pts.mean(0);u,s,vt=np.linalg.svd(pts-c);d=vt[0];return c,d
def line_inter(c1,d1,c2,d2):
    A=np.array([d1,-d2]).T
    if abs(np.linalg.det(A))<1e-3: return None
    t=np.linalg.solve(A,c2-c1);return c1+t[0]*d1
def refine(poly_up,UP,step=0.1,corner_deg=52,win=1.1,straight_tol=0.24,smooth=0.95,snap=1.8):
    p=resample_closed(poly_up/UP,step)   # unidades: px originais, colunas (row,col)
    n=len(p);k=max(2,int(round(win/step)))
    v1=p-np.roll(p,k,0);v2=np.roll(p,-k,0)-p
    ang=np.abs(np.arctan2(v1[:,0]*v2[:,1]-v1[:,1]*v2[:,0],(v1*v2).sum(1)))
    cand=np.where(ang>np.deg2rad(corner_deg))[0]
    corners=[]
    for i in cand:
        w=[(i+j)%n for j in range(-k,k+1)]
        if ang[i]>=ang[w].max()-1e-9 and all((i-c)%n>k or (c-i)%n>k for c in corners): corners.append(i)
    corners=sorted(corners)
    if len(corners)<2:
        sm=np.stack([gaussian_filter1d(p[:,0],smooth/step,mode='wrap'),gaussian_filter1d(p[:,1],smooth/step,mode='wrap')],1)
        return sm[::max(1,int(0.35/step))]*UP
    m=len(corners);segs=[]
    for a in range(m):
        i0=corners[a];i1=corners[(a+1)%m]
        idx=np.arange(i0,i1+1 if i1>i0 else i1+n+1)%n
        segs.append(p[idx])
    info=[]
    for sg in segs:
        trim=int(0.5/step)
        core=sg[trim:-trim] if len(sg)>2*trim+4 else sg
        c,d=fit_line(core)
        dev=np.abs((core-c)@np.array([-d[1],d[0]])).max()
        length=np.hypot(*(sg[-1]-sg[0]))
        info.append((c,d,dev<straight_tol and length>1.0))
    cp=[p[i].copy() for i in corners]
    for a in range(m):
        prev=info[(a-1)%m];nxt=info[a]
        if prev[2] and nxt[2]:
            x=line_inter(prev[0],prev[1],nxt[0],nxt[1])
            if x is not None and np.hypot(*(x-cp[a]))<snap: cp[a]=x
        elif prev[2] or nxt[2]:
            c,d=(prev[0],prev[1]) if prev[2] else (nxt[0],nxt[1])
            cp[a]=c+d*np.dot(cp[a]-c,d)
    out=[]
    for a in range(m):
        sg=segs[a].copy();c0=cp[a];c1=cp[(a+1)%m]
        if info[a][2]:
            out.append(c0[None,:]); continue
        sg[0]=c0;sg[-1]=c1
        # suaviza mantendo extremos (reflexão ímpar)
        pad=int(3*smooth/step)
        L=np.vstack([2*sg[0]-sg[1:pad+1][::-1],sg,2*sg[-1]-sg[-pad-1:-1][::-1]])
        sm=np.stack([gaussian_filter1d(L[:,0],smooth/step),gaussian_filter1d(L[:,1],smooth/step)],1)[pad:-pad]
        sm[0]=c0;sm[-1]=c1
        out.append(sm[::max(1,int(0.3/step))][:-0 or None])
    res=np.vstack(out)
    return res*UP
