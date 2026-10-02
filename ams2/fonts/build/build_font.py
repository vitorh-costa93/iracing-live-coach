import numpy as np, cv2, json
from scipy import ndimage as ndi
from skimage import measure
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib import TTFont

im=cv2.imread("src.png")[:,:,::-1].astype(np.float32)
R,G,B=im[...,0],im[...,1],im[...,2]
UP=10

def comps(ink,box,minarea=6):
    x0,y0,x1,y1=box
    lab,n=ndi.label(ink[y0:y1,x0:x1]>0.5)
    out=[]
    for sl in ndi.find_objects(lab):
        h=sl[0].stop-sl[0].start;w=sl[1].stop-sl[1].start
        if h*w>=minarea: out.append((sl[1].start+x0,sl[0].start+y0,w,h))
    return sorted(out)

def patch(ink,c,m=3):
    x,y,w,h=c
    p=ink[max(0,y-m):y+h+m, max(0,x-m):x+w+m]
    # cantos pretos fora da componente atrapalham: zera pixels de outras componentes é raro; mantém
    return cv2.resize(p,(p.shape[1]*UP,p.shape[0]*UP),interpolation=cv2.INTER_CUBIC), (x-max(0,x-m), y-max(0,y-m))

def contours(img,level=0.5):
    img=cv2.GaussianBlur(img,(0,0),UP*0.16)
    pad=np.pad(img,2)
    cs=measure.find_contours(pad,level)
    res=[]
    for c in cs:
        c=c-2
        poly=measure.approximate_polygon(c,UP*0.12)
        if len(poly)>=4 and abs(measure.subdivide_polygon(poly,degree=1)[0:1].size)>0: res.append(poly)
    return res

# ---------- fonte de VALORES (amarelo) ----------
s=np.clip(((R+G)/2-B)/195.0,0,1)
rows=[(15,55),(62,98),(108,141),(151,186)]
blocks=[(372,460),(795,892)]
strings=["LAP 2","0.947","2.643","3.319","4.365","5.553","8.629","9.146"]
samples={}
k=0
for bx in blocks:
    for ry in rows:
        cs=comps(s,(bx[0],ry[0],bx[1],ry[1]))
        txt=strings[k] if False else None
        k+=1
        samples.setdefault("rows",[]).append(cs)
order=[("L","A","P","2"),("0",".","9","4","7"),("2",".","6","4","3"),("3",".","3","1","9"),("4",".","3","6","5"),("5",".","5","5","3"),("8",".","6","2","9"),("9",".","1","4","6")]
# blocos: esquerda rows 0-3 = LAP 2, 0.947, 2.643, 3.319 ; direita = 4.365, 5.553, 8.629, 9.146
glyph_samples={}
for idx,cs in enumerate(samples["rows"]):
    # idx 0..3 left, 4..7 right
    chars=order[idx]
    assert len(chars)==len(cs),(idx,chars,len(cs))
    for ch,c in zip(chars,cs):
        glyph_samples.setdefault(ch,[]).append(patch(s,c))
json.dump({k:len(v) for k,v in glyph_samples.items()},open("val_counts.json","w"))
print({k:len(v) for k,v in glyph_samples.items()})

def average(samps):
    # alinha pelo centroide e média
    H=max(p.shape[0] for p,_ in samps)+4*UP;W=max(p.shape[1] for p,_ in samps)+4*UP
    acc=np.zeros((H,W),np.float32)
    for p,_ in samps:
        m=p>0.5
        cy,cx=ndi.center_of_mass(m)
        dy=int(round(H/2-cy));dx=int(round(W/2-cx))
        canvas=np.zeros((H,W),np.float32)
        y0=max(0,dy);x0=max(0,dx)
        sub=p[:H-y0,:W-x0] if dy>=0 and dx>=0 else p
        h,w=p.shape
        ys=slice(max(dy,0),min(H,dy+h));xs=slice(max(dx,0),min(W,dx+w))
        canvas[ys,xs]=p[ys.start-dy:ys.stop-dy, xs.start-dx:xs.stop-dx]
        acc+=canvas
    return acc/len(samps)
np.save("dummy.npy",np.zeros(1))
avg={ch:average(v) for ch,v in glyph_samples.items()}
def save_preview(avg,name):
    chars=sorted(avg)
    tiles=[]
    for ch in chars:
        a=(255*(1-np.clip(avg[ch],0,1))).astype(np.uint8)
        a=cv2.resize(a,(a.shape[1]//2,a.shape[0]//2))
        tiles.append(a)
    h=max(t.shape[0] for t in tiles);w=sum(t.shape[1] for t in tiles)
    sheet=np.full((h,w),255,np.uint8);x=0
    for t in tiles: sheet[:t.shape[0],x:x+t.shape[1]]=t;x+=t.shape[1]
    cv2.imwrite(name,sheet)
save_preview(avg,"values_avg.png")
np.savez("values_avg.npz",**{("g_"+("dot" if k=="." else k)):v for k,v in avg.items()})
print("ok")
