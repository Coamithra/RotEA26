// bombripple.fx — screen-space refraction ring radiating from a bomb detonation
// (Trello card 5f38ed35, "like throwing a stone in water").
//
// Applied by Game1.ApplyBombRipple as a sceneTarget -> rippleRT pass right after
// ApplyHoloSim, i.e. the same post seam as the slowmo ghost trail. Pixel shader
// only: KNI's SpriteBatch supplies the vertex transform (same as gamma.fx /
// holosim.fx).
//
// Up to four concurrent rings (Compat/BombRipple owns the slots; an inactive slot
// has amplitude 0 and contributes nothing). They are FOUR SEPARATE uniforms rather
// than a float4[4] array on purpose: a plain uniform is the one form MojoShader ->
// BlazorGL GLSL is guaranteed to handle, and four unrolled terms cost the same as
// a fixed-count loop anyway.
//
// Each ring is (xy = centre in target UV, z = current radius, w = amplitude):
//   * distances are measured in ASPECT-CORRECTED units — d.x is multiplied by
//     Aspect (= target W/H) so the wavefront is a circle on a 4:3 target, not an
//     ellipse. Radius/width are therefore in units of "fraction of screen HEIGHT".
//   * the wavefront is a single sine cycle under a Gaussian envelope centred on the
//     ring radius, so the frame is pushed OUT just ahead of the crest and pulled IN
//     just behind it — the compression/rarefaction pair that reads as water rather
//     than as a smeared blur. Displacement is zero exactly on the crest and decays
//     to nothing within ~1.5 ring widths either side.
//   * RimBoost adds a faint brightness lift proportional to |wave| (the caustic
//     glint on a real ripple). Kept low by default; ?ripplerim= tunes it.
//
// The amplitude decay over the ring's life is done on the C# side (BombRipple), so
// this shader is a pure function of the packed ring state.
//
// ps_3_0 like holosim: four wavefront evaluations plus the rim exceed ps_2_0's
// instruction budget.

float4 Ring0;
float4 Ring1;
float4 Ring2;
float4 Ring3;
float RingWidth;
float RimBoost;
float Aspect;

sampler TextureSampler : register(s0);

static const float TWO_PI = 6.28318531;

// Accumulate one ring's contribution. Returns the UV displacement; `wave` collects
// |crest| for the rim highlight.
float2 RippleTerm(float4 ring, float2 uv, inout float wave)
{
    // Amplitude 0 => dead slot. The math below stays finite either way (the guarded
    // direction and the Gaussian envelope), so this is a cost saver, not a guard.
    if (ring.w <= 0.0)
    {
        return float2(0.0, 0.0);
    }

    float2 d = (uv - ring.xy) * float2(Aspect, 1.0);
    float r = length(d);
    // Guarded direction: at the exact centre r is 0 and normalize() is undefined.
    // The envelope there is ~0 for any grown ring, so the value never shows.
    float2 dir = d / max(r, 1e-4);

    float u = (r - ring.z) / max(RingWidth, 1e-4);
    float envelope = exp(-u * u * 3.0);
    float crest = envelope * sin(u * TWO_PI);
    wave += abs(crest) * ring.w;

    // dir.x is in aspect-corrected space; divide it back out so the displacement is
    // in real UV units and the push stays radial on screen.
    return float2(dir.x / Aspect, dir.y) * (crest * ring.w);
}

float4 PixelShaderFunction(float4 color : COLOR0, float2 texCoord : TEXCOORD0) : COLOR0
{
    float wave = 0.0;
    float2 offset = RippleTerm(Ring0, texCoord, wave)
                  + RippleTerm(Ring1, texCoord, wave)
                  + RippleTerm(Ring2, texCoord, wave)
                  + RippleTerm(Ring3, texCoord, wave);

    // saturate the sample coord: the pass runs over the whole target, so a ring near
    // an edge would otherwise reach outside it. LinearClamp already clamps, this just
    // makes the intent explicit and costs nothing.
    float4 c = tex2D(TextureSampler, saturate(texCoord + offset));

    // Caustic glint on the crest. Additive and small — the ripple is a DEFORMATION
    // first; the lift only stops the wavefront disappearing over flat dark space.
    c.rgb = saturate(c.rgb + wave * RimBoost);

    return c * color;
}

technique BombRipple
{
    pass P0
    {
        PixelShader = compile ps_3_0 PixelShaderFunction();
    }
}
