import numpy as np
from scipy.ndimage import gaussian_filter1d
# ---------- Schneider (ajuste de Bézier cúbico) ----------
def _bez(P,t):
    t=np.asarray(t)[:,None];mt=1-t
    return mt**3*P[0]+3*mt**2*t*P[1]+3*mt*t**2*P[2]+t**3*P[3]
def _bez1(P,t):
    mt=1-t;return 3*mt**2*(P[1]-P[0])+6*mt*t*(P[2]-P[1])+3*t**2*(P[3]-P[2])
def _bez2(P,t):
    return 6*(1-t)*(P[2]-2*P[1]+P[0])+6*t*(P[3]-2*P[2]+P[1])
def _chord(pts):
    d=np.hypot(*np.diff(pts,axis=0).T);u=np.concatenate([[0],np.cumsum(d)]);return u/u[-1]
def _gen(pts,u,tL,tR):
    p0,p3=pts[0],pts[-1]
    A1=(3*(1-u)**2*u)[:,None]*tL;A2=(3*(1-u)*u**2)[:,None]*tR
    C=np.zeros((2,2));X=np.zeros(2)
    for i in range(len(pts)):
        a1,a2=A1[i],A2[i]
        C[0,0]+=a1@a1;C[0,1]+=a1@a2;C[1,1]+=a2@a2
        tmp=pts[i]-_bez([p0,p0,p3,p3],np.array([u[i]]))[0]
        X[0]+=a1@tmp;X[1]+=a2@tmp
    C[1,0]=C[0,1]
    det=C[0,0]*C[1,1]-C[0,1]*C[1,0]
    seg=np.hypot(*(p3-p0))
    if abs(det)>1e-12:
        a1=(X[0]*C[1,1]-X[1]*C[0,1])/det;a2=(C[0,0]*X[1]-C[1,0]*X[0])/det
    else: a1=a2=0
    eps=1e-6*seg
    if a1<eps or a2<eps: a1=a2=seg/3
    return [p0,p0+tL*a1,p3+tR*a2,p3]
def _err(pts,B,u):
    d=np.sum((_bez(B,u)-pts)**2,axis=1);i=int(np.argmax(d));return d[i],i
def _reparam(B,pts,u):
    out=u.copy()
    for k,t in enumerate(u):
        d=_bez(B,np.array([t]))[0]-pts[k];d1=_bez1(B,t);d2=_bez2(B,t)
        den=d1@d1+d@d2
        if abs(den)>1e-12: out[k]=min(1,max(0,t-(d@d1)/den))
    return out
def fit_cubic(pts,tL,tR,err):
    if len(pts)==2:
        dist=np.hypot(*(pts[1]-pts[0]))/3;return [[pts[0],pts[0]+tL*dist,pts[1]+tR*dist,pts[1]]]
    u=_chord(pts);B=_gen(pts,u,tL,tR);e,sp=_err(pts,B,u)
    if e<err: return [B]
    if e<err*16:
        for _ in range(12):
            u=_reparam(B,pts,u);B=_gen(pts,u,tL,tR);e,sp=_err(pts,B,u)
            if e<err: return [B]
    sp=max(1,min(len(pts)-2,sp))
    tc=pts[sp-1]-pts[sp+1];n=np.hypot(*tc);tc=tc/n if n>0 else tL
    return fit_cubic(pts[:sp+1],tL,tc,err)+fit_cubic(pts[sp:],-tc,tR,err)
def unit(v):
    n=np.hypot(*v);return v/n if n>1e-9 else v
def end_tangents(pts,k=4):
    k=min(k,len(pts)-1);return unit(pts[k]-pts[0]),unit(pts[-1-k]-pts[-1])
