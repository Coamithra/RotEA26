// channelflip.fx — TV "change the channel" transition between two splash images.
//
// Real-time port of the Premiere channel-flip recipe (noise + scanlines + horizontal
// turbulent displacement + skew + contrast + a fast south->north push with a bright
// seam). Applied by SpriteBatch as a pixel-shader-only effect on the channel-flip
// splash, drawn through the native-res HiResOverlay so the reveal stays crisp.
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
// and the push live in between. Time (seconds) rolls the static/scanlines. The whole
// result is multiplied by the vertex colour, so the splash fade rides the tint.
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

float4 PixelShaderFunction(float4 color : COLOR0, float2 texCoord : TEXCOORD0) : COLOR0
{
    float p = saturate(Progress);

    // Glitch envelope: rises fast, broad top, falls — strongest mid-transition.
    float g = pow(sin(p * PI), 0.5);
    // Push envelope: a fast shove through the back half of the transition.
    float push = smoothstep(0.55, 0.92, p);

    float2 uv = texCoord;

    // Skew (shear x by row) + horizontal turbulent displacement, both scaled by g.
    float skew = (uv.y - 0.5) * 0.06 * g;
    float row = floor(uv.y * 48.0);
    float jitter = (hash21(float2(row, floor(Time * 18.0))) - 0.5)
                 + (hash21(float2(row * 1.7, floor(Time * 7.0))) - 0.5) * 0.5;
    float dx = skew + jitter * 0.06 * g;

    // Vertical push: old slides up & out, new enters from the bottom; seam between.
    float seam = 1.0 - push;
    float2 oldUV = float2(uv.x + dx, uv.y + push);
    float2 newUVframe = float2(uv.x + dx, uv.y - seam);

    float4 oldC = tex2D(OldSampler, oldUV);

    // Incoming image: remap into its letterboxed sub-rect, black outside it.
    float2 nuv = (newUVframe - NewRect.xy) / NewRect.zw;
    float inside = step(0.0, nuv.x) * step(nuv.x, 1.0) * step(0.0, nuv.y) * step(nuv.y, 1.0);
    float4 newC = tex2D(NewSampler, nuv) * inside;

    // Split at the seam: below -> new, above -> old.
    float below = step(seam, uv.y);
    float4 col = lerp(oldC, newC, below);

    // Scanlines (bright lines added back ~ color-dodge).
    float scan = 0.5 + 0.5 * sin((uv.y * 600.0 + Time * 60.0) * PI * 0.5);
    col.rgb += scan * 0.10 * g;

    // Static grain.
    float n = hash21(uv * float2(640.0, 480.0) + frac(Time) * 97.0);
    col.rgb += (n - 0.5) * 0.35 * g;

    // Contrast boost during the glitch.
    float contrast = lerp(1.0, 1.55, g);
    col.rgb = saturate((col.rgb - 0.5) * contrast + 0.5);

    // Bright seam band, only while the push is actively moving.
    float band = smoothstep(0.018, 0.0, abs(uv.y - seam)) * step(0.001, push) * step(push, 0.999);
    col.rgb += band;

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
