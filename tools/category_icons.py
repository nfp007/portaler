from PIL import Image, ImageDraw
import math
SS=8  # supersampling
def _line(d,a,b,w,c): d.line([a,b],fill=c,width=int(w)); 
def draw(kind, size, col=(255,200,90,255), dark=(30,18,8,255)):
    S=size*SS; im=Image.new('RGBA',(S,S),(0,0,0,0)); d=ImageDraw.Draw(im)
    c=S/2; h=S*0.42; w=lambda f: max(1,int(S*f))
    if kind=='base':
        d.polygon([(c-h,c-h*0.05),(c,c-h),(c+h,c-h*0.05)],fill=col)
        d.rectangle([c-h*0.72,c-h*0.1,c+h*0.72,c+h*0.9],fill=col)
        d.rectangle([c-h*0.2,c+h*0.3,c+h*0.2,c+h*0.9],fill=dark)
        d.rectangle([c+h*0.35,c-h*0.85,c+h*0.6,c-h*0.4],fill=col)
    elif kind=='mine':   # pickaxe: diagonal handle + crescent head with pointed tips
        import math as _m
        hx,hy=c+h*0.32,c-h*0.32                      # where head sits on handle
        d.line([(c-h*0.9,c+h*0.9),(hx+h*0.12,hy-h*0.12)],fill=col,width=w(0.12))
        ux,uy=1/_m.sqrt(2),1/_m.sqrt(2)               # along head (perpendicular to handle)
        bx,by=-1/_m.sqrt(2),1/_m.sqrt(2)              # bend direction (towards handle bottom)
        L=h*1.05; bend=h*0.42; thick=h*0.30
        top=[];bot=[]
        N=24
        for i in range(N+1):
            t=-1+2*i/N
            px=hx+ux*L*t+bx*bend*(t*t)
            py=hy+uy*L*t+by*bend*(t*t)
            th=thick*(1-abs(t))**0.8
            top.append((px-bx*th*0.5,py-by*th*0.5)); bot.append((px+bx*th*0.5,py+by*th*0.5))
        d.polygon(top+bot[::-1],fill=col)
        d.ellipse([hx-h*0.17,hy-h*0.17,hx+h*0.17,hy+h*0.17],fill=col)
    elif kind=='farm':
        d.line([(c,c-h*0.3),(c,c+h)],fill=col,width=w(0.08))
        for i in range(3):
            y=c-h*0.75+i*h*0.4
            d.ellipse([c-h*0.6,y,c-h*0.08,y+h*0.32],fill=col); d.ellipse([c+h*0.08,y,c+h*0.6,y+h*0.32],fill=col)
        d.ellipse([c-h*0.18,c-h,c+h*0.18,c-h*0.5],fill=col)
    elif kind=='trader':
        d.ellipse([c-h,c-h,c+h,c+h],fill=col)
        d.ellipse([c-h*0.68,c-h*0.68,c+h*0.68,c+h*0.68],outline=dark,width=w(0.06))
        d.line([(c,c-h*0.42),(c,c+h*0.42)],fill=dark,width=w(0.09))
    elif kind=='boss':
        d.ellipse([c-h*0.62,c-h*0.6,c+h*0.62,c+h*0.5],fill=col)
        d.rectangle([c-h*0.36,c+h*0.25,c+h*0.36,c+h*0.8],fill=col)
        d.polygon([(c-h*0.45,c-h*0.35),(c-h,c-h),(c-h*0.15,c-h*0.6)],fill=col)
        d.polygon([(c+h*0.45,c-h*0.35),(c+h,c-h),(c+h*0.15,c-h*0.6)],fill=col)
        d.ellipse([c-h*0.4,c-h*0.15,c-h*0.1,c+h*0.17],fill=dark); d.ellipse([c+h*0.1,c-h*0.15,c+h*0.4,c+h*0.17],fill=dark)
        for k in (-0.2,0,0.2): d.line([(c+h*k,c+h*0.5),(c+h*k,c+h*0.8)],fill=dark,width=w(0.03))
    elif kind=='dungeon':
        d.pieslice([c-h*0.85,c-h,c+h*0.85,c+h*0.7],start=180,end=360,fill=col)
        d.rectangle([c-h*0.85,c-h*0.15,c+h*0.85,c+h],fill=col)
        d.pieslice([c-h*0.42,c-h*0.5,c+h*0.42,c+h*0.34],start=180,end=360,fill=dark)
        d.rectangle([c-h*0.42,c-h*0.09,c+h*0.42,c+h],fill=dark)
    elif kind=='explore':
        d.ellipse([c-h,c-h,c+h,c+h],outline=col,width=w(0.08))
        d.polygon([(c,c-h*0.78),(c+h*0.24,c),(c,c+h*0.78),(c-h*0.24,c)],fill=col)
        d.ellipse([c-h*0.1,c-h*0.1,c+h*0.1,c+h*0.1],fill=dark)
    elif kind=='danger':  # crossed swords
        for sgn in (1,-1):
            x0,y0=c-sgn*h*0.85,c-h*0.85; x1,y1=c+sgn*h*0.55,c+h*0.55
            d.line([(x0,y0),(x1,y1)],fill=col,width=w(0.09))                # blade
            gx,gy=c+sgn*h*0.5,c+h*0.5
            d.line([(gx-sgn*h*0.3,gy+h*0.3*(-1)*-1+0),(gx+sgn*h*0.3*0,gy)],fill=col,width=1)
            # crossguard (perpendicular)
            d.line([(gx+sgn*h*0.0-h*0.22,gy+h*0.22*sgn*0+h*0.22),(gx+h*0.22,gy-h*0.22)] if sgn==1 else [(gx-h*0.22,gy-h*0.22),(gx+h*0.22,gy+h*0.22)],fill=col,width=w(0.08))
            d.line([(x1,y1),(c+sgn*h*0.9,c+h*0.9)],fill=col,width=w(0.12))      # grip
            d.ellipse([c+sgn*h*0.9-h*0.12,c+h*0.9-h*0.12,c+sgn*h*0.9+h*0.12,c+h*0.9+h*0.12],fill=col)  # pommel
    elif kind=='none':
        d.line([(c-h*0.6,c-h*0.6),(c+h*0.6,c+h*0.6)],fill=col,width=w(0.09)); d.line([(c+h*0.6,c-h*0.6),(c-h*0.6,c+h*0.6)],fill=col,width=w(0.09))
    return im.resize((size,size),Image.LANCZOS)
CATS=[('base','Base'),('mine','Mine'),('farm','Farm'),('trader','Trader'),('boss','Boss'),('danger','Danger'),('dungeon','Dungeon'),('explore','Explore'),('none','None')]
