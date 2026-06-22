// channelflip.fx — TV "tune to a new channel" CROSSFADE between two splash images.
//
// Rewritten 2026: the original south->north vertical PUSH (with a sliding seam band)
// was removed. The transition now holds position and CROSSFADES — the OUTGOING image
// distorts (TV turbulence: row skew + horizontal jitter + static + scanlines) as it
// dissolves out, while the INCOMING image emerges ALREADY distorted and SETTLES to
// crisp. Distortion peaks mid-transition; both ends are clean. No vertical motion.
//
//   s0 (OldSampler) = the OUTGOING image (the SpriteBatch sprite texture).
//   NewTexture      = the INCOMING image, bound as an effect texture PARAMETER.
//                     (KNI's SpriteBatch custom-effect path does NOT preserve a
//                      manually-bound Textures[1]; see BloomCombine.fx. A sampler on
//                      a texture parameter is set by pass.Apply() and works.)
//
// The incoming image is letter/pillarboxed into the outgoing frame via
//   NewRect = (u0, v0, uScale, vScale)
// so a portrait "pure" splash shows with black side bars; outside the rect = black.
//
// Progress 0..1 drives the whole transition: 0 = pure old, 1 = pure new; the glitch
// and the crossfade live in between. Time (seconds) rolls the static/scanlines. The
// whole result is multiplied by the vertex colour, so the splash fade rides the tint.
//
// ps_3_0: the effect exceeds ps_2_0's ~64-instruction budget. The PS links fine with
// SpriteBatch's own vertex shader in the compiled GLSL.

sampler OldSampler : register(s0);

texture NewTexture;
sampler NewSampler = sampler_state
{
    Texture = <NewTexture>;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;
    AddressU = Clamp;
    AddressV = Clamp;
};

float Progress;
float Time;
float Fade;       // 0..1 splash fade (becomes the straight output alpha)
float4 NewRect;   // xy = uv offset of the incoming image, zw = uv scale

static const float PI = 3.14159265;

float hash21(float2 p)
{
    return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
}

// Horizontal-only TV turbulence (row shear + per-row jitter) at strength s. No
// vertical component — the old vertical scroll is gone.
float2 distortUV(float2 uv, float s)
{
    float skew = (uv.y - 0.5) * 0.05 * s;
    float row = floor(uv.y * 48.0);
    float jitter = (hash21(float2(row, floor(Time * 18.0))) - 0.5)
                 + (hash21(float2(row * 1.7, floor(Time * 7.0))) - 0.5) * 0.5;
    return float2(uv.x + skew + jitter * 0.06 * s, uv.y);
}

float4 PixelShaderFunction(float4 color : COLOR0, float2 texCoord : TEXCOORD0) : COLOR0
{
    float p = saturate(Progress);

    // Glitch envelope: rises fast, broad top, falls — strongest mid-transition, zero
    // at both ends so the old (p=0) and the revealed new (p=1) are both crisp.
    float g = pow(sin(p * PI), 0.5);
    // Crossfade weight: 0 = old, 1 = new, centred on mid-transition.
    float mixw = smoothstep(0.28, 0.72, p);

    float2 uv = texCoord;
    float2 duv = distortUV(uv, g);

    // Outgoing image (distorted by the same turbulence).
    float4 oldC = tex2D(OldSampler, duv);

    // Incoming image: remap the distorted uv into its letterboxed sub-rect; black
    // outside it (pillar/letterbox bars).
    float2 nuv = (duv - NewRect.xy) / NewRect.zw;
    float inside = step(0.0, nuv.x) * step(nuv.x, 1.0) * step(0.0, nuv.y) * step(nuv.y, 1.0);
    float4 newC = tex2D(NewSampler, nuv) * inside;

    // Crossfade old -> new.
    float4 col = lerp(oldC, newC, mixw);

    // Scanlines (bright lines added back ~ color-dodge).
    float scan = 0.5 + 0.5 * sin((uv.y * 600.0 + Time * 60.0) * PI * 0.5);
    col.rgb += scan * 0.10 * g;

    // Static grain.
    float n = hash21(uv * float2(640.0, 480.0) + frac(Time) * 97.0);
    col.rgb += (n - 0.5) * 0.32 * g;

    // Contrast boost during the glitch.
    float contrast = lerp(1.0, 1.45, g);
    col.rgb = saturate((col.rgb - 0.5) * contrast + 0.5);

    // Premultiplied output (rgb *= Fade): the overlay composites premultiplied, so the
    // splash fade rides Fade here (the vertex tint stays premultiplied white).
    col.a = 1.0;
    float4 o = saturate(col) * color;
    o.rgb *= Fade;
    o.a = Fade;
    return o;
}

technique ChannelFlip
{
    pass P0
    {
        PixelShader = compile ps_3_0 PixelShaderFunction();
    }
}
