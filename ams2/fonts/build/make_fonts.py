import numpy as np, cv2, json
from scipy import ndimage as ndi
from skimage import measure
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen
exec(open("build_font.py").read().split("# ---------- fonte de VALORES")[0])  # funções: comps, patch, contours, UP
vals=np.load("values_avg.npz")
avg_vals={("." if k=="g_dot" else k[2:]):vals[k] for k in vals.files}

def polys_of(img):
    img=cv2.GaussianBlur(img.astype(np.float32),(0,0),UP*0.16)
    pad=np.pad(img,3); out=[]
    for c in measure.find_contours(pad,0.5):
        c=c-3
        p=measure.approximate_polygon(c,UP*0.10)
        if len(p)>=4 and abs(poly_area(p))>UP*UP*1.5: out.append(p)
    return out   # lista de arrays (row,col)
def poly_area(p):
    x=p[:,1];y=-p[:,0]
    return 0.5*np.sum(x[:-1]*y[1:]-x[1:]*y[:-1])
def to_font(polys,scale,sb,yshift=None):
    allpts=np.vstack(polys)
    minx=allpts[:,1].min();maxy=(-allpts[:,0]).max();miny=(-allpts[:,0]).min()
    base=miny if yshift is None else yshift
    res=[]
    for p in polys:
        x=(p[:,1]-minx)*scale+sb; y=((-p[:,0])-base)*scale
        res.append(np.stack([x,y],1))
    width=(allpts[:,1].max()-minx)*scale
    return res,width
def orient(polys):
    # profundidade de aninhamento -> buraco = ímpar
    out=[]
    for i,p in enumerate(polys):
        depth=sum(1 for j,q in enumerate(polys) if j!=i and measure.points_in_poly(p[:1],q)[0])
        area=poly_area_xy(p)
        hole=depth%2==1
        want_cw=not hole   # externo horário (área<0), buraco anti-horário
        if (area<0)!=want_cw: p=p[::-1]
        out.append(p)
    return out
def poly_area_xy(p):
    x=p[:,0];y=p[:,1]; return 0.5*np.sum(x[:-1]*y[1:]-x[1:]*y[:-1])+0.5*(x[-1]*y[0]-x[0]*y[-1])
def draw(polys):
    pen=TTGlyphPen(None)
    for p in polys:
        pts=[(int(round(x)),int(round(y))) for x,y in p]
        # remove duplicados consecutivos e o ponto de fechamento repetido
        clean=[pts[0]]
        for q in pts[1:]:
            if q!=clean[-1]: clean.append(q)
        if clean[0]==clean[-1]: clean.pop()
        if len(clean)<3: continue
        pen.moveTo(clean[0])
        for q in clean[1:]: pen.lineTo(q)
        pen.closePath()
    return pen.glyph()
def rect(x0,y0,x1,y1): return np.array([[x0,y0],[x0,y1],[x1,y1],[x1,y0],[x0,y0]],float)

def build_font(name,glyph_polys,cap,sb,outfile,extra_adv=None):
    # glyph_polys: char -> (polys_in_font_units(list), width)
    chars=list(glyph_polys)
    gn={".notdef":".notdef","space":"space"}
    order=[".notdef","space"]+[ {".":"period",":":"colon","+":"plus","-":"hyphen"}.get(c,"uni%04X"%ord(c)) for c in chars]
    fb=FontBuilder(1000,isTTF=True)
    fb.setupGlyphOrder(order)
    cmap={32:"space"}
    glyphs={".notdef":draw([rect(60,0,440,700),rect(120,60,380,640)[::-1]]),"space":TTGlyphPen(None).glyph()}
    hmtx={".notdef":(500,60),"space":(int(cap*0.35),0)}
    for c in chars:
        nm={".":"period",":":"colon","+":"plus","-":"hyphen"}.get(c,"uni%04X"%ord(c))
        polys,w=glyph_polys[c]
        cmap[ord(c)]=nm
        glyphs[nm]=draw(orient(polys))
        hmtx[nm]=(int(round(w+2*sb)),int(sb))
    fb.setupCharacterMap(cmap)
    fb.setupGlyf(glyphs)
    fb.setupHorizontalMetrics({k:v for k,v in hmtx.items()})
    fb.setupHorizontalHeader(ascent=int(cap*1.15),descent=-int(cap*0.3))
    fb.setupNameTable({"familyName":name,"styleName":"Regular","uniqueFontIdentifier":name+" Regular","fullName":name,"psName":name.replace(" ","")+"-Regular","version":"Version 1.000"})
    fb.setupOS2(sTypoAscender=int(cap*1.15),sTypoDescender=-int(cap*0.3),usWinAscent=int(cap*1.2),usWinDescent=int(cap*0.35),sCapHeight=cap,sxHeight=cap)
    fb.setupPost()
    fb.save(outfile)

CAP=700;SB=22
# ---------------- VALORES ----------------
H_up={}
for ch,a in avg_vals.items():
    if ch in "LAP":continue
    ps=polys_of(a); allp=np.vstack(ps); H_up[ch]=(-allp[:,0]).max()-(-allp[:,0]).min()
digit_h=np.median([H_up[c] for c in "0123456789"])
scale_v=CAP/digit_h
gv={}
for ch in "0123456789.":
    polys,w=to_font(polys_of(avg_vals[ch]),scale_v,SB)
    gv[ch]=(polys,w)
# espessura do traço: largura do '1' (haste) aprox. = largura do glifo 1 menos bandeira; usa ponto como referência
dot_w=gv["."][1]; stem=int(dot_w*0.95)
mid=int(CAP*0.55)
# dois-pontos: dois pontos copiados
dp=gv["."][0][0]
dh=dp[:,1].max()-dp[:,1].min()
gv[":"]=([dp.copy(),dp+np.array([0,CAP*0.58])],gv["."][1])
bar_w=int(CAP*0.52)
gv["-"]=([rect(SB,int(CAP*0.30),SB+bar_w,int(CAP*0.30)+stem)],bar_w)
gv["+"]=([ np.vstack([[SB,int(CAP*0.30)+0],[SB,int(CAP*0.30)+stem],[SB+bar_w//2-stem//2,int(CAP*0.30)+stem],[SB+bar_w//2-stem//2,int(CAP*0.30)+stem+bar_w//2-stem//2],[SB+bar_w//2+stem//2,int(CAP*0.30)+stem+bar_w//2-stem//2],[SB+bar_w//2+stem//2,int(CAP*0.30)+stem],[SB+bar_w,int(CAP*0.30)+stem],[SB+bar_w,int(CAP*0.30)],[SB+bar_w//2+stem//2,int(CAP*0.30)],[SB+bar_w//2+stem//2,int(CAP*0.30)-bar_w//2+stem//2],[SB+bar_w//2-stem//2,int(CAP*0.30)-bar_w//2+stem//2],[SB+bar_w//2-stem//2,int(CAP*0.30)],[SB,int(CAP*0.30)]]).astype(float)],bar_w)
build_font("F1 Broadcast 98 Values",gv,CAP,SB,"../F1Broadcast98-Values.ttf")

# ---------------- CAIXAS ----------------
imrgb=cv2.imread("src.png")[:,:,::-1].astype(np.float32)
ink=np.clip((200-imrgb[...,0])/200.0,0,1)
Lx=(5,43);Rx=(471,511);ys=[(12,50),(57,96),(104,142),(148,188)]
tiles=[(Lx,ys[i]) for i in range(4)]+[(Rx,ys[i]) for i in range(4)]
bp={}
for d,(xs,yy) in zip("12345678",tiles):
    sub=ink[yy[0]:yy[1],xs[0]:xs[1]]
    lab,n=ndi.label(sub>0.5); sizes=ndi.sum(sub>0.5,lab,range(1,n+1)); k=int(np.argmax(sizes))+1
    mask=ndi.binary_dilation(lab==k,iterations=2)
    sl=ndi.find_objects((lab==k).astype(int))[0]
    crop=(sub*mask)[max(0,sl[0].start-3):sl[0].stop+3, max(0,sl[1].start-3):sl[1].stop+3]
    up=cv2.resize(crop,(crop.shape[1]*UP,crop.shape[0]*UP),interpolation=cv2.INTER_CUBIC)
    bp[d]=polys_of(up)
hb=np.median([ (-np.vstack(p)[:,0]).max()-(-np.vstack(p)[:,0]).min() for p in bp.values()])
scale_b=CAP/hb
gb={}
for d in "12345678":
    gb[d]=to_font(bp[d],scale_b,SB)
# 9 = 6 girado 180°
p6,w6=gb["6"]
cx=SB+w6/2;cy=CAP/2
gb["9"]=([np.stack([2*cx-p[:,0],2*cy-p[:,1]],1) for p in p6],w6)
# 0 = anel
w0=(gb["8"][1]+gb["6"][1])/2
t_side=gb["1"][1]*0.72  # espessura lateral ~ haste do 1
t_tb=t_side*0.7
def stadium(x0,y0,x1,y1,n=40,e=3.2):
    cxx=(x0+x1)/2;cyy=(y0+y1)/2;a=(x1-x0)/2;b=(y1-y0)/2
    th=np.linspace(0,2*np.pi,n,endpoint=False)
    c=np.cos(th);s=np.sin(th)
    return np.stack([cxx+a*np.sign(c)*np.abs(c)**(2/e),cyy+b*np.sign(s)*np.abs(s)**(2/e)],1)
outer=stadium(SB,0,SB+w0,CAP)
inner=stadium(SB+t_side,t_tb,SB+w0-t_side,CAP-t_tb,e=2.6)
gb["0"]=([outer,inner],w0)
build_font("F1 Broadcast 98 Box",gb,CAP,SB,"../F1Broadcast98-Box.ttf")
print("ok",scale_v,scale_b)
