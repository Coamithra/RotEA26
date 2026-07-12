// starwindow.fx — the procedural-starfield crossfade window (new space background).
//
// The space background is a deterministic grid of OVERLAPPING nebula/star tiles,
// each drawn ADDITIVELY into the black-cleared scene. A plain overlap would double
// the brightness in the seam band (two tiles' light summing). This shader fixes
// that: it multiplies each tile's output alpha by a separable, mirror-symmetric
// WINDOW that ramps from 0 at the tile edge to 1 over the overlap band, flat 1 in
// the middle. Because adjacent tiles' ramps are complementary (smoothstep is
// symmetric: S(x)+S(1-x)=1), the windows SUM TO 1 across every overlap — so the
// additive accumulation becomes a convex blend (a true crossfade), giving uniform
// brightness, no seams, no doubling. The window is symmetric in tc and 1-tc, so it
// is invariant under horizontal/vertical mirroring — every flip variant gets the
// identical screen-space window for free (which is why the tiles only mirror, never
// rotate).
//
// Drawn with BlendState.Additive (SrcAlpha/One): final.rgb += tile.rgb * window.
// Feather = overlap width in UV per axis (overlapPx / tileSizePx); per-axis because
// the tiles are 4:3, not square. color (vertex color) carries an optional per-tile
// tint / master fade.

sampler TextureSampler : register(s0);

float2 Feather;   // UV ramp width per axis: (overlapX/tileW, overlapY/tileH)
float2 ContentScale; // logical/padded per axis: the tile CONTENT fills tc in [0,ContentScale]; the rest
                  // is the transparent mult-of-4 pad a dxt sibling carries. Normalise tc by this so
                  // the crossfade window ramps to 0 at the CONTENT edge, not the padded texture edge
                  // (else the feather lands in the pad and the tile seams). (1,1) => unpadded, no-op.

float4 PixelShaderFunction(float4 color : COLOR0, float2 tc : TEXCOORD0) : COLOR0
{
    float4 t = tex2D(TextureSampler, tc);
    float2 n = tc / ContentScale;   // content-normalised [0,1] (pad region -> n > 1 -> window 0)
    float wx = smoothstep(0.0, Feather.x, n.x) * smoothstep(0.0, Feather.x, 1.0 - n.x);
    float wy = smoothstep(0.0, Feather.y, n.y) * smoothstep(0.0, Feather.y, 1.0 - n.y);
    float w = wx * wy;
    return float4(t.rgb * color.rgb, w * color.a);
}

technique StarWindow
{
    pass P0
    {
        PixelShader = compile ps_2_0 PixelShaderFunction();
    }
}
