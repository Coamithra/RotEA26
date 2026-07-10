// faceshade.fx — per-face directional shading for the Level-3 wall tower slices.
//
// The towers are drawn as stacked sprite slices (Game/EvilAliens/Wall.cs). Once the side texture
// runs continuously across block boundaries, nothing distinguishes a tower's north face from its
// east face and the CORNERS vanish. A sprite carries ONE tint, but the part of a slice you actually
// see is the BORDER RING of its square — which spans two faces at once — so a per-sprite tint can
// never shade them differently.
//
// So do it per pixel: shade by which of the four edges the pixel is NEAREST. Where two faces are
// equidistant the boundary is a diagonal, so a corner comes out MITRED, as a real box corner is.
// The face is a pure function of the pixel's position inside the slice, so there is no per-block
// branching, no second draw, and the whole shaft stays in one batch.
//
// ONLY OUTER EDGES ARE FACES. A block inside a solid cluster has neighbours, and the sides it shares
// with them are not faces of the wall at all. Shading them anyway puts a dark mitre wedge at the top
// corner of every block — two of them meeting at each interior boundary, which reads as a seam grid
// across an otherwise continuous surface. So each sprite carries a MASK of which of its four sides
// are exposed (Wall.isfree, the same test the top-face edge lines use), and hidden sides are excluded
// from the nearest-edge search. A north band then runs unbroken to the block's edge when there is no
// east face to mitre against, and the mitre appears only at genuine corners of the wall.
//
// The mask has to be PER BLOCK, and a per-block uniform would break the batch. But the slice tint is
// per SLICE — identical for every block at one depth — so the two swap places: the tint becomes the
// SliceTint uniform, and the sprite's vertex colour carries the mask. Costs nothing.
//   vertex colour rgb: 1 = that side is exposed (north, south, east), 0 = interior.
//   vertex colour a:   1 = west exposed, 0.5 = interior. Never 0, so no batcher can mistake the
//                      sprite for fully transparent and drop it.
//
// GOTCHA — SpriteBatch hands the pixel shader ATLAS texcoords, not the sprite's local 0..1. Each
// block samples its own Window-sized window of the sheet, so a naive test on texCoord would compare
// against the wrong origin. Recovering the local coordinate needs the window's origin, which is
// per-block... except that it isn't, modulo Window: origins are `(j*Window + off) mod sheet`, and
// `j*Window` is a multiple of Window, so EVERY window origin is congruent to `off` mod Window. Hence
//     local = frac((texCoord * SheetSize - WindowOrigin) / Window)
// is exact for every block from one uniform. `off` changes per slice, not per block.
//
// Factors are DARKEN-ONLY (all <= 1): SliceTint already carries the fog lerp, so a factor > 1 would
// clip the hazy base to white. Wall.cs also lerps the factors toward 1 with the haze, so the shading
// washes out into the fog at the tower's base rather than fighting it.
//
// Straight (non-premultiplied) alpha throughout: rgb is scaled, alpha is left alone.

sampler TextureSampler : register(s0);

// (north, south, east, west) multipliers, each <= 1.
float4 FaceFactors;

// This slice's tint: rgb = the fogged side colour, a = the dissolve alpha. Uniform because every
// block at one depth shares it — which is what frees the vertex colour to carry the face mask.
float4 SliceTint;

// Width of the sampling window, and of the whole sheet, in texels.
float Window;
float SheetSize;

// This slice's shared window origin, in texels, mod Window.
float WindowOrigin;

// Pushed onto a hidden face's distance so it can never win the nearest-edge search. Any value
// greater than the 0.5 max of a real distance, and below the NONE sentinel, works.
static const float HIDDEN = 10.0;

float4 PixelShaderFunction(float4 color : COLOR0, float2 texCoord : TEXCOORD0) : COLOR0
{
    float4 c = tex2D(TextureSampler, texCoord) * SliceTint;

    float2 local = frac((texCoord * SheetSize - WindowOrigin) / Window);

    // Which sides of THIS block are outer edges of the wall? (north, south, east, west)
    float4 mask = float4(step(0.5, color.rgb), step(0.75, color.a));

    // Distance from the pixel to each of the four edges, in local units. Hidden sides are pushed
    // out of reach, so the nearest EXPOSED edge wins; ties fall on a diagonal, giving the mitre.
    float4 d = float4(local.y, 1.0 - local.y, 1.0 - local.x, local.x)
             + (1.0 - mask) * HIDDEN;

    // Start beyond any real distance but below HIDDEN: a block with no exposed side (fully interior,
    // its shaft entirely covered by its neighbours') keeps factor 1 and is simply not shaded.
    float f = 1.0;
    float m = 9.0;
    if (d.x < m) { m = d.x; f = FaceFactors.x; }
    if (d.y < m) { m = d.y; f = FaceFactors.y; }
    if (d.z < m) { m = d.z; f = FaceFactors.z; }
    if (d.w < m) { m = d.w; f = FaceFactors.w; }

    c.rgb *= f;
    return c;
}

technique FaceShade
{
    pass P0
    {
        PixelShader = compile ps_2_0 PixelShaderFunction();
    }
}
