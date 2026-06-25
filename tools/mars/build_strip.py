#!/usr/bin/env python
# Ring-stitch the 12 upscaled Mars quarters into ONE seamless, looping ground strip
# (no mirrorX). See STITCH_ALGORITHM.md. Outputs the magenta-backed strip + checks.
import numpy as np
from PIL import Image
from stitch_lib import load, alpha_from, terrain_fill, horizon, MAG

HR='hr'; OVL=100; D_LOW,D_HIGH,FMAX_FRAC=6.0,35.0,1.0; HSMOOTH=1500
order=[(n,h) for n in range(1,7) for h in ('bl','br')]      # Q0..Q11

def recolor(hr,orig):
    def terr(a):
        R,G,B=a[...,0],a[...,1],a[...,2]; return ~((R>175)&(G<95)&(B>175))
    ht,ot=terr(hr),terr(orig); out=hr.copy()
    for c in range(3):
        hm,hs=hr[ht][:,c].mean(),hr[ht][:,c].std()+1e-6
        om,os=orig[ot][:,c].mean(),orig[ot][:,c].std()+1e-6
        out[...,c]=(hr[...,c]-hm)*(os/hs)+om
    out=np.clip(out,0,255); out[~ht]=[255,0,255]              # restore pure magenta (else alpha detect breaks)
    return out
def smoothc(x,k):
    k=int(k)|1; ker=np.ones(k)/k; xp=np.concatenate([x[-(k//2):],x,x[:k//2]]); return np.convolve(xp,ker,'valid')
def smv(x,k):
    k=int(k)|1; return x if k<3 else np.convolve(np.pad(x,k//2,'edge'),np.ones(k)/k,'valid')
def color_diff(A,B):
    dR=A[...,0]-B[...,0]; dG=A[...,1]-B[...,1]; dB=A[...,2]-B[...,2]; return np.sqrt(2*dR**2+4*dG**2+3*dB**2)
def warp_cols(img, shifts):                                  # per-column vertical shift (sub-px)
    Hh,Ww=img.shape[:2]; yy=np.arange(Hh); out=np.empty_like(img)
    for x in range(Ww):
        src=yy-shifts[x]; s0=np.floor(src).astype(int); fr=src-s0
        s0c=np.clip(s0,0,Hh-1); s1c=np.clip(s0+1,0,Hh-1)
        out[:,x]=img[s0c,x]*(1-fr)[:,None]+img[s1c,x]*fr[:,None] if img.ndim==3 else img[s0c,x]*(1-fr)+img[s1c,x]*fr
    return out

# ---- load + prep ----
Q=[]
for n,h in order:
    hr=load(f'{HR}/mars{n}_{h}.png')                          # all 1619x971; mars6 kept as-is (squished, by choice)
    orig=load(f'mars{n}_{h}.png')
    hr=recolor(hr,orig)
    a=alpha_from(hr); tf=terrain_fill(hr,a)
    Q.append({'tf':tf,'a':a,'w':hr.shape[1],'hz':horizon(a).astype(np.float32)})
H=Q[0]['tf'].shape[0]; widths=[q['w'] for q in Q]
pos=[0]
for k in range(11): pos.append(pos[-1]+widths[k]-OVL)
Wtot=sum(widths)-12*OVL
print('quarters',widths,'Wtot',Wtot,'H',H)

# ---- loop-closed global horizon ----
acc=np.zeros(Wtot); cnt=np.zeros(Wtot)
for k,q in enumerate(Q):
    c=(pos[k]+np.arange(widths[k]))%Wtot
    np.add.at(acc,c,q['hz']); np.add.at(cnt,c,1.0)
glob=acc/np.maximum(cnt,1); Tgt=smoothc(glob,HSMOOTH)
print('global horizon: raw %.0f..%.0f  smoothed %.0f..%.0f'%(glob.min(),glob.max(),Tgt.min(),Tgt.max()))
for k,q in enumerate(Q):                                      # warp each quarter onto Tgt
    c=(pos[k]+np.arange(widths[k]))%Wtot
    sh=Tgt[c]-q['hz']
    q['tf']=warp_cols(q['tf'],sh); q['a']=warp_cols(q['a'],sh)

# ---- adaptive-feather weight per junction (0=left quarter,1=right) ----
def junction_w(L,R):                                          # L right-OVL, R left-OVL  (HxOVLx3)
    d=color_diff(L,R); xb=np.argmin(d,1).astype(np.float32); db=d[np.arange(H),xb.astype(int)]
    xb=smv(xb,25); db=smv(db,25)
    b=np.clip((db-D_LOW)/(D_HIGH-D_LOW),0,1); wdt=np.maximum(b*FMAX_FRAC*OVL,1.0); ctr=xb*(1-b)+(OVL/2)*b
    xx=np.arange(OVL)[None,:].astype(np.float32); e0=(ctr-wdt/2)[:,None]; e1=(ctr+wdt/2)[:,None]
    t=np.clip((xx-e0)/np.maximum(e1-e0,1e-6),0,1); return t*t*(3-2*t)
Wj=[]
for k in range(12):
    nk=(k+1)%12
    Wj.append(junction_w(Q[k]['tf'][:,widths[k]-OVL:], Q[nk]['tf'][:,:OVL]))

# ---- assemble (weighted, circular) ----
strip=np.zeros((H,Wtot,3),np.float32); stripa=np.zeros((H,Wtot),np.float32); wsum=np.zeros((H,Wtot),np.float32)
for k,q in enumerate(Q):
    wq=np.ones((H,widths[k]),np.float32)
    wq[:,:OVL]=Wj[(k-1)%12]                      # left overlap: this quarter is the RIGHT side -> ramp 0->1
    wq[:,widths[k]-OVL:]=1.0-Wj[k]               # right overlap: this quarter is the LEFT side -> ramp 1->0
    c=(pos[k]+np.arange(widths[k]))%Wtot                      # unique within a quarter -> += is safe
    strip[:,c]+=q['tf']*wq[...,None]
    stripa[:,c]+=q['a']*wq
    wsum[:,c]+=wq
strip/=np.maximum(wsum[...,None],1e-6); stripa=np.clip(stripa/np.maximum(wsum,1e-6),0,1)
out=np.clip(strip*stripa[...,None]+MAG*(1-stripa[...,None]),0,255).astype(np.uint8)
Image.fromarray(out,'RGB').save('stitch/strip_magenta.png')
print('wrote stitch/strip_magenta.png',out.shape[1],'x',out.shape[0])

# ---- finish: straight-alpha + 2px horizon-halo erode, pad to full 600-design tile height, split into 6 ----
import os
ae=np.minimum(stripa,np.roll(stripa,2,axis=0))               # trim the green horizon rim 2px
rgba=np.dstack([strip,np.clip(ae,0,1)*255.0])
TILE_H=H*2                                                   # bottom-half (300 design) -> full 600 design = 2x
full=np.zeros((TILE_H,Wtot,4),np.float32); full[TILE_H-H:]=rgba   # terrain strip at the bottom, top transparent
nt=Wtot//6
os.makedirs('tiles_out',exist_ok=True)
for i in range(6):
    x0=i*nt; x1=Wtot if i==5 else (i+1)*nt
    Image.fromarray(np.clip(full[:,x0:x1],0,255).astype(np.uint8),'RGBA').save(f'tiles_out/mars{i+1}.png')
print(f'wrote tiles_out/mars1..6.png  {nt}x{TILE_H} each  -> Background.SetMars size = {500/1619:.4f}, NO mirrorX/realsize*2')
# preview tile3 over a checker so the alpha reads
def checker(w,h,s=24):
    yy,xx=np.mgrid[0:h,0:w]; m=((xx//s+yy//s)%2)==0; a=np.zeros((h,w,3),np.uint8); a[m]=(70,90,110); a[~m]=(40,55,70); return a
t=np.clip(full[:,2*nt:3*nt],0,255); al=t[:,:,3:4]/255.0; bg=checker(t.shape[1],t.shape[0]).astype(np.float32)
comp=(bg*(1-al)+t[:,:,:3]*al).astype(np.uint8)
Image.fromarray(comp,'RGB').resize((1000,int(TILE_H*1000/nt)),Image.LANCZOS).save('stitch/_tile3_checker.png')
print('wrote _tile3_checker.png')

# ---- verification: wrap seam (end|start) + a downscaled overview ----
wrapL=out[:,-260:]; wrapR=out[:,:260]
Image.fromarray(np.concatenate([wrapL,wrapR],1),'RGB').save('stitch/_check_wrap.png')   # center = the loop seam
Image.fromarray(out,'RGB').resize((2400,int(H*2400/Wtot)),Image.LANCZOS).save('stitch/_check_overview.png')
print('wrote _check_wrap.png (center column = the Q12->Q1 loop seam) + _check_overview.png')
