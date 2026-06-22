"""Generate a self-contained wing-attachment editor for the FlyingSpider.

The flying spider's body is the shared spider_sheet2 rear-up sheet, looping the reared
sub-range (packed frames 22..30). Two wing1 sprites are drawn rooted at one anchor that the
game currently holds CONSTANT for every frame -- so the wings drift off the body as it moves.
This tool lets you place that anchor PER FRAME and exports the list of offsets.

What it does:
  * pulls the 9 loop cells straight out of wwwroot/.../spider_sheet2.png (cols=7,rows=7,
    cellw=384,cellh=427,sep=1) -> pixel-identical to the in-game getFrameRectangle framing,
    so the body centre == the in-game draw Position (cell centre).
  * embeds them + wing1.png as base64 in ONE html file (no server / no CORS).
  * canvas editor: prev/next frame, drag the anchor, arrow-keys nudge, optional flap preview;
    each frame remembers its own anchor.
  * Save -> a ready-to-paste C# Vector2[] (design-space offset from body centre) + JSON.

Coordinate math (must match FlyingSpider.Draw):
  body centre (cell texels) = (cellW/2, cellH/2) = (192, 213.5)
  design<->texel factor      = SS = native/design = 384/160 = 2.4   (the body supersample)
  design_offset = (anchor_texel - centre) / SS      <- what we export
  wing draw      = scale/wf with origin (82,11)*wf and (6,11)*wf, wf=wing1 factor (4)
                   -> in texel space: wingScale = (1/wf)*SS = 0.6, origins (328,44)/(24,44)

usage:  python tools/upscale/build_wing_editor.py     (run from repo root or tools/upscale)
        -> writes tools/upscale/wing_editor.html ; just open it in a browser.
"""
from __future__ import annotations
import base64, io, os, json
from PIL import Image

# --- resolve paths whether run from repo root or tools/upscale ---
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
SHEET = os.path.join(ROOT, "web", "EvilAliensWeb", "wwwroot", "Content", "gfx", "sprites", "spider_sheet2.png")
WING = os.path.join(ROOT, "web", "EvilAliensWeb", "wwwroot", "Content", "gfx", "sprites", "wing1.png")
OUT = os.path.join(HERE, "wing_editor.html")

# --- sheet / loop geometry (mirror FlyingSpider + getFrameRectangle) ---
COLS, ROWS, SEP = 7, 7, 1
FIRST_FRAME, LAST_FRAME = 22, 31           # half-open [22,31) = 9 reared loop frames
DESIGN_W = 160                              # registry design width for spider_sheet2
# wing registry design width (368/92 = 4)
WING_DESIGN_W = 92
# initial constant anchor currently in FlyingSpider (design-space offset from body centre)
INIT_OFF = (6.0, -18.0)


def b64_png(im: Image.Image) -> str:
    buf = io.BytesIO()
    im.save(buf, "PNG")
    return "data:image/png;base64," + base64.b64encode(buf.getvalue()).decode("ascii")


def main() -> int:
    sheet = Image.open(SHEET).convert("RGBA")
    wing = Image.open(WING).convert("RGBA")
    SW, SH = sheet.size
    cw = (SW - (COLS - 1) * SEP) // COLS
    ch = (SH - (ROWS - 1) * SEP) // ROWS
    ss = cw / DESIGN_W                       # body supersample (=2.4)
    wf = wing.size[0] / WING_DESIGN_W        # wing supersample (=4)
    cx, cy = cw / 2.0, ch / 2.0

    frames = []
    for f in range(FIRST_FRAME, LAST_FRAME):
        col, row = f % COLS, f // COLS
        x, y = col * (cw + SEP), row * (ch + SEP)
        cell = sheet.crop((x, y, x + cw, y + ch))
        frames.append({"frame": f, "loop": f - FIRST_FRAME, "img": b64_png(cell)})

    cfg = {
        "cellW": cw, "cellH": ch, "cx": cx, "cy": cy, "ss": ss,
        "wingW": wing.size[0], "wingH": wing.size[1], "wf": wf,
        "wingScale": (1.0 / wf) * ss,                 # texel-space wing scale (0.6)
        "originA": [82 * wf, 11 * wf],                # flipped wing root (right wing)
        "originB": [6 * wf, 11 * wf],                 # normal wing root (left wing)
        "initOff": list(INIT_OFF),
        "first": FIRST_FRAME,
    }

    html = _TEMPLATE.replace("/*__CONFIG__*/", json.dumps(cfg)) \
                    .replace("/*__FRAMES__*/", json.dumps(frames)) \
                    .replace("/*__WING__*/", json.dumps(b64_png(wing)))
    with open(OUT, "w", encoding="utf-8") as fh:
        fh.write(html)
    print(f"wrote {OUT}")
    print(f"  cell {cw}x{ch}  ss {ss:.3f}  wf {wf:.3f}  frames {len(frames)} (sheet {FIRST_FRAME}..{LAST_FRAME-1})")
    print("  open it in a browser; drag the green anchor, use Prev/Next, then Save.")
    return 0


_TEMPLATE = r"""<!doctype html><html><head><meta charset="utf-8">
<title>FlyingSpider wing-attachment editor</title>
<style>
  body{margin:0;background:#1c1c22;color:#ddd;font:14px/1.4 system-ui,sans-serif;}
  #wrap{display:flex;gap:18px;padding:16px;align-items:flex-start;}
  canvas{background:#0a0a10;border:1px solid #333;cursor:crosshair;image-rendering:auto;}
  .panel{width:430px;}
  button{background:#2c2c36;color:#eee;border:1px solid #444;border-radius:5px;padding:7px 12px;cursor:pointer;font-size:14px;}
  button:hover{background:#3a3a48;}
  .row{display:flex;gap:8px;align-items:center;margin:8px 0;flex-wrap:wrap;}
  textarea{width:100%;height:240px;background:#0e0e14;color:#9fd;border:1px solid #333;font:12px/1.35 monospace;border-radius:5px;padding:8px;box-sizing:border-box;}
  label{user-select:none;}
  .k{color:#8af;} .dim{color:#888;}
  h2{margin:0 0 6px;font-size:16px;}
  input[type=range]{width:160px;vertical-align:middle;}
</style></head><body>
<div id="wrap">
  <div>
    <canvas id="cv"></canvas>
    <div class="row">
      <button id="prev">&larr; Prev</button>
      <span id="fnum" style="min-width:170px;text-align:center;"></span>
      <button id="next">Next &rarr;</button>
    </div>
    <div class="row dim">Drag the <span style="color:#5e6">green</span> anchor. Arrow keys nudge 0.5 (Shift=0.1). Wings shown via the real <span class="k">wing1</span> sprite.</div>
    <div class="row">
      <label><input type="checkbox" id="showwings" checked> show wings</label>
      <label><input type="checkbox" id="flapanim"> animate flap</label>
      <label>flap <input type="range" id="flap" min="-1" max="1" step="0.01" value="0"></label>
    </div>
    <div class="row">
      <label><input type="checkbox" id="onion"> onion-skin prev frame</label>
      <label>zoom <input type="range" id="zoom" min="1" max="3" step="0.1" value="1.6"></label>
    </div>
  </div>
  <div class="panel">
    <h2>Per-frame wing anchors</h2>
    <div class="row">
      <button id="copyAll" style="background:#2e7d32;">Copy C#</button>
      <button id="copyJson">Copy JSON</button>
      <button id="dl">Download .json</button>
      <button id="resetAll" style="margin-left:auto;background:#5a2a2a;">Reset all</button>
    </div>
    <div class="row dim">Current frame offset (design px from body centre):
      <span id="curoff" class="k" style="margin-left:6px;"></span></div>
    <textarea id="out" readonly></textarea>
  </div>
</div>
<script>
const CFG = /*__CONFIG__*/;
const FRAMES = /*__FRAMES__*/;
const WINGSRC = /*__WING__*/;
const cv = document.getElementById('cv'), ctx = cv.getContext('2d');
let Z = 1.6;                        // display zoom
let idx = 0;                        // current loop frame
let dragging = false;
let flapAnim = false, flapVal = 0;
const imgs = [], wingImg = new Image();
let loaded = 0;
// per-frame anchors stored as DESIGN offsets from body centre; init to the current constant
const offs = FRAMES.map(()=>({x:CFG.initOff[0], y:CFG.initOff[1]}));

function texelFromDesign(o){ return {x: CFG.cx + o.x*CFG.ss, y: CFG.cy + o.y*CFG.ss}; }
function designFromTexel(t){ return {x:(t.x-CFG.cx)/CFG.ss, y:(t.y-CFG.cy)/CFG.ss}; }

function drawWing(anchorT, origin, flip, rot){
  ctx.save();
  ctx.translate(anchorT.x*Z, anchorT.y*Z);
  ctx.rotate(rot);
  ctx.scale((flip?-1:1)*CFG.wingScale*Z, CFG.wingScale*Z);
  ctx.globalAlpha = 0.92;
  ctx.drawImage(wingImg, -origin[0], -origin[1]);
  ctx.restore();
}

function render(){
  cv.width = CFG.cellW*Z; cv.height = CFG.cellH*Z;
  ctx.clearRect(0,0,cv.width,cv.height);
  // onion skin
  if(document.getElementById('onion').checked && idx>0){
    ctx.globalAlpha=0.25; ctx.drawImage(imgs[idx-1],0,0,cv.width,cv.height); ctx.globalAlpha=1;
  }
  // body cell
  ctx.drawImage(imgs[idx],0,0,cv.width,cv.height);
  const aT = texelFromDesign(offs[idx]);
  // wings
  if(document.getElementById('showwings').checked){
    const t = flapAnim ? flapVal : parseFloat(document.getElementById('flap').value);
    const rA = t*Math.PI/2, rB = -t*Math.PI/2;
    drawWing(aT, CFG.originA, true,  rA);   // flipped (right wing)
    drawWing(aT, CFG.originB, false, rB);   // normal  (left wing)
  }
  // body centre crosshair
  ctx.strokeStyle='rgba(120,160,255,.5)'; ctx.lineWidth=1;
  ctx.beginPath(); ctx.moveTo(CFG.cx*Z-8,CFG.cy*Z); ctx.lineTo(CFG.cx*Z+8,CFG.cy*Z);
  ctx.moveTo(CFG.cx*Z,CFG.cy*Z-8); ctx.lineTo(CFG.cx*Z,CFG.cy*Z+8); ctx.stroke();
  // anchor handle
  ctx.fillStyle='rgba(80,230,90,.95)'; ctx.strokeStyle='#063';
  ctx.beginPath(); ctx.arc(aT.x*Z, aT.y*Z, 7, 0, 7); ctx.fill(); ctx.stroke();
  // labels
  document.getElementById('fnum').textContent =
    `loop ${idx+1}/${FRAMES.length}  (sheet frame ${FRAMES[idx].frame})`;
  document.getElementById('curoff').textContent =
    `(${offs[idx].x.toFixed(1)}, ${offs[idx].y.toFixed(1)})`;
  updateOut();
}

function updateOut(){
  let cs = "// Per-frame wing anchor: design-space offset from body centre, indexed by\n";
  cs += "// loop frame (0 = FirstFrame "+CFG.first+"). Paste into FlyingSpider.\n";
  cs += "private static readonly Vector2[] WingAnchors =\n{\n";
  offs.forEach((o,i)=>{ cs += `    new Vector2(${o.x.toFixed(1)}f, ${o.y.toFixed(1)}f),  // loop ${i} (sheet ${FRAMES[i].frame})\n`; });
  cs += "};\n";
  document.getElementById('out').value = cs;
}
function jsonOut(){ return JSON.stringify(offs.map((o,i)=>({frame:FRAMES[i].frame,loop:i,x:+o.x.toFixed(2),y:+o.y.toFixed(2)})),null,2); }

// --- mouse drag ---
function evToTexel(e){
  const r = cv.getBoundingClientRect();
  return {x:(e.clientX-r.left)/Z, y:(e.clientY-r.top)/Z};
}
cv.addEventListener('mousedown',e=>{dragging=true; const t=evToTexel(e); offs[idx]=designFromTexel(t); render();});
window.addEventListener('mousemove',e=>{if(dragging){const t=evToTexel(e); offs[idx]=designFromTexel(t); render();}});
window.addEventListener('mouseup',()=>dragging=false);

// --- buttons / keys ---
document.getElementById('prev').onclick=()=>{idx=(idx-1+FRAMES.length)%FRAMES.length; render();};
document.getElementById('next').onclick=()=>{idx=(idx+1)%FRAMES.length; render();};
document.getElementById('resetAll').onclick=()=>{offs.forEach(o=>{o.x=CFG.initOff[0];o.y=CFG.initOff[1];}); render();};
document.getElementById('flap').oninput=render;
['showwings','onion'].forEach(id=>document.getElementById(id).onchange=render);
document.getElementById('zoom').oninput=e=>{Z=parseFloat(e.target.value); render();};
document.getElementById('flapanim').onchange=e=>{flapAnim=e.target.checked;};
document.getElementById('copyAll').onclick=()=>{navigator.clipboard.writeText(document.getElementById('out').value);};
document.getElementById('copyJson').onclick=()=>{navigator.clipboard.writeText(jsonOut());};
document.getElementById('dl').onclick=()=>{const b=new Blob([jsonOut()],{type:'application/json'});const a=document.createElement('a');a.href=URL.createObjectURL(b);a.download='wing_anchors.json';a.click();};
window.addEventListener('keydown',e=>{
  const step = e.shiftKey?0.1:0.5;
  if(e.key==='ArrowLeft'&&e.altKey){document.getElementById('prev').click();e.preventDefault();return;}
  if(e.key==='ArrowRight'&&e.altKey){document.getElementById('next').click();e.preventDefault();return;}
  if(e.key==='ArrowLeft'){offs[idx].x-=step;render();e.preventDefault();}
  if(e.key==='ArrowRight'){offs[idx].x+=step;render();e.preventDefault();}
  if(e.key==='ArrowUp'){offs[idx].y-=step;render();e.preventDefault();}
  if(e.key==='ArrowDown'){offs[idx].y+=step;render();e.preventDefault();}
});

// --- flap animation loop ---
let t0=null;
function tick(ts){ if(flapAnim){ if(t0===null)t0=ts; const p=((ts-t0)/600)%1; flapVal=Math.sin(p*Math.PI*2); render(); } requestAnimationFrame(tick); }

// --- load images ---
function onload(){ if(++loaded===FRAMES.length+1){ render(); requestAnimationFrame(tick);} }
FRAMES.forEach((f,i)=>{ imgs[i]=new Image(); imgs[i].onload=onload; imgs[i].src=f.img; });
wingImg.onload=onload; wingImg.src=WINGSRC;
</script></body></html>
"""

if __name__ == "__main__":
    raise SystemExit(main())
