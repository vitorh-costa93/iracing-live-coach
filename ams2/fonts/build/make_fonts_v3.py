import numpy as np, cv2
from scipy import ndimage as ndi
src=open("make_fonts.py",encoding="utf-8").read()
head=src.split("vals=np.load")[0]
exec(head)                      # imports + build_font.py helpers (comps, patch, contours, UP=10)
import sr as SR
import refine as RF
UP=SR.UP
exec("def polys_of"+src.split("def polys_of")[1].split("CAP=700;SB=")[0])
def polys_of(img):
    img=cv2.GaussianBlur(img.astype(np.float32),(0,0),UP*0.18)
    pad=np.pad(img,3);out=[]
    for c in measure.find_contours(pad,0.5):
        c=c-3
        if abs(poly_area(c))<UP*UP*1.5: continue
        out.append(RF.refine(c,UP))
    return out   # polys_of..rect, build_font
# ---------- VALORES ----------
im=cv2.imread("src.png")[:,:,::-1].astype(np.float32)
Rr,Gg,Bb=im[...,0],im[...,1],im[...,2]
s=np.clip(((Rr+Gg)/2-Bb)/195.0,0,1)
rows=[(15,55),(62,98),(108,141),(151,186)];blocks=[(372,460),(795,892)]
order=[("L","A","P","2"),("0",".","9","4","7"),("2",".","6","4","3"),("3",".","3","1","9"),("4",".","3","6","5"),("5",".","5","5","3"),("8",".","6","2","9"),("9",".","1","4","6")]
samples={};k=0
for bx in blocks:
    for ry in rows:
        cs=comps(s,(bx[0],ry[0],bx[1],ry[1]))
        for ch,c in zip(order[k],cs):
            x,y,w,h=c;m=4
            p=s[max(0,y-m):y+h+m,max(0,x-m):x+w+m].copy()
            # isola a componente (remove vizinhos que entram na margem)
            lab,n=ndi.label(p>0.35)
            cy,cx=y-max(0,y-m)+h//2,x-max(0,x-m)+w//2
            keep=lab[min(cy,p.shape[0]-1),min(cx,p.shape[1]-1)]
            if keep==0: keep=np.argmax(ndi.sum(p>0.35,lab,range(1,n+1)))+1
            mask=ndi.binary_dilation(lab==keep,iterations=2)
            samples.setdefault(ch,[]).append(p*mask)
        k+=1
sr_vals={};info={}
for ch,ps in samples.items():
    rl,avg,n=SR.sr(ps,iters=15); sr_vals[ch]=rl; info[ch]=n
print("amostras por glifo:",info)
CAP=700;SB=22
H_up={}
for ch in "0123456789":
    ps_=polys_of(sr_vals[ch]);a=np.vstack(ps_);H_up[ch]=(-a[:,0]).max()-(-a[:,0]).min()
scale_v=CAP/np.median(list(H_up.values()))
gv={ch:to_font(polys_of(sr_vals[ch]),scale_v,SB) for ch in "0123456789."}
dot_w=gv["."][1];stem=int(dot_w*0.95)
dp=gv["."][0][0]
gv[":"]=([dp.copy(),dp+np.array([0,CAP*0.58])],gv["."][1])
bar_w=int(CAP*0.52);y0b=int(CAP*0.30)
gv["-"]=([rect(SB,y0b,SB+bar_w,y0b+stem)],bar_w)
a=SB+bar_w//2-stem//2;b=a+stem;hh=bar_w//2-stem//2
gv["+"]=([np.array([[SB,y0b],[SB,y0b+stem],[a,y0b+stem],[a,y0b+stem+hh],[b,y0b+stem+hh],[b,y0b+stem],[SB+bar_w,y0b+stem],[SB+bar_w,y0b],[b,y0b],[b,y0b-hh],[a,y0b-hh],[a,y0b],[SB,y0b]],float)],bar_w)
build_font("F1 Broadcast 98 Values",gv,CAP,SB,"../F1Broadcast98-Values.ttf")
# ---------- CAIXAS ----------
ink=np.clip((200-Rr)/200.0,0,1)
Lx=(5,43);Rx=(471,511);ys=[(12,50),(57,96),(104,142),(148,188)]
tiles=[(Lx,ys[i]) for i in range(4)]+[(Rx,ys[i]) for i in range(4)]
bp={}
for d,(xs,yy) in zip("12345678",tiles):
    sub=ink[yy[0]:yy[1],xs[0]:xs[1]]
    lab,n=ndi.label(sub>0.5);sizes=ndi.sum(sub>0.5,lab,range(1,n+1));kk=int(np.argmax(sizes))+1
    mask=ndi.binary_dilation(lab==kk,iterations=2);sl=ndi.find_objects((lab==kk).astype(int))[0]
    crop=(sub*mask)[max(0,sl[0].start-4):sl[0].stop+4,max(0,sl[1].start-4):sl[1].stop+4]
    rl,_,_=SR.sr([crop],iters=15);bp[d]=polys_of(rl)
hb=np.median([(-np.vstack(p)[:,0]).max()-(-np.vstack(p)[:,0]).min() for p in bp.values()])
scale_b=CAP/hb
gb={d:to_font(bp[d],scale_b,SB) for d in "12345678"}
p6,w6=gb["6"];cx=SB+w6/2;cy=CAP/2
gb["9"]=([np.stack([2*cx-p[:,0],2*cy-p[:,1]],1) for p in p6],w6)
w0=(gb["8"][1]+gb["6"][1])/2;t_side=gb["1"][1]*0.72;t_tb=t_side*0.7
def stadium(x0,y0,x1,y1,n=60,e=3.2):
    cxx=(x0+x1)/2;cyy=(y0+y1)/2;a_=(x1-x0)/2;b_=(y1-y0)/2
    th=np.linspace(0,2*np.pi,n,endpoint=False);c=np.cos(th);s_=np.sin(th)
    return np.stack([cxx+a_*np.sign(c)*np.abs(c)**(2/e),cyy+b_*np.sign(s_)*np.abs(s_)**(2/e)],1)
gb["0"]=([stadium(SB,0,SB+w0,CAP),stadium(SB+t_side,t_tb,SB+w0-t_side,CAP-t_tb,e=2.6)],w0)
build_font("F1 Broadcast 98 Box",gb,CAP,SB,"../F1Broadcast98-Box.ttf")
print("ok v2")
