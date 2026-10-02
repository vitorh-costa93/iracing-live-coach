import numpy as np, cv2
from scipy import ndimage as ndi
from skimage.registration import phase_cross_correlation
from skimage.restoration import richardson_lucy
SIGMA=0.45   # desfoque estimado da captura (px originais)
UP=8
def gauss_psf(sig):
    r=int(np.ceil(sig*3)); y,x=np.mgrid[-r:r+1,-r:r+1]
    k=np.exp(-(x*x+y*y)/(2*sig*sig)); return k/k.sum()
def sr(patches, iters=35):
    """patches: lista de arrays float (tinta 0..1) em resolução original. Retorna imagem UPx deconvolvida."""
    H=max(p.shape[0] for p in patches);W=max(p.shape[1] for p in patches)
    P=[np.pad(p,((0,H-p.shape[0]),(0,W-p.shape[1]))) for p in patches]
    ref=P[0];ups=[]
    for i,p in enumerate(P):
        if i==0: dy=dx=0.0
        else:
            (dy,dx),_,_=phase_cross_correlation(ref,p,upsample_factor=50,normalization=None)
        M=np.float32([[UP,0,dx*UP+(UP-1)/2],[0,UP,dy*UP+(UP-1)/2]])
        ups.append(cv2.warpAffine(p.astype(np.float32),M,(W*UP,H*UP),flags=cv2.INTER_CUBIC,borderValue=0))
    avg=np.clip(np.mean(ups,axis=0),0,1)
    rl=richardson_lucy(np.clip(avg,1e-3,1),gauss_psf(SIGMA*UP),num_iter=iters,clip=True)
    return rl, avg, len(patches)
