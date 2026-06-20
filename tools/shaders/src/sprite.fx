// sprite.fx — master sprite-effect shader for the EffectHandler combinations.
//
// The original XNA 3.x .fx are lost. EffectHandler selects one of 13 compiled
// effects depending on which of {colorize, lighten, fade, interpolate} are
// enabled (see EffectHandler.LoadEffects). Rather than hand-write 13 near-
// duplicate files, this one master is compiled 13 times with different
// /processorParam:Defines (see tools/shaders/build_shaders.py):
//
//   COLORIZE     hue-range remap (recolour a band of hues toward a target hue)
//   LIGHTEN      hit-flash brighten
//   FADE         multiply by a colour/alpha (the sprite tint, set via FadeValue)
//   INTERPOLATE  tween between the current and next animation frame
//
// Pixel-shader only: KNI's SpriteBatch supplies the vertex transform, and a pass
// with no vertex shader keeps that VS bound. ps_3_0 for instruction headroom.
//
// Tinting: when FADE is defined the sprite tint arrives via FadeValue (the C#
// sets fadeEffect.Value = colour for exactly the effects that include fade), so
// those variants tint by FadeValue and ignore the vertex colour. All other
// variants tint by the vertex colour (COLOR0) the normal SpriteBatch way. This
// yields a single tint multiply in every case the game actually draws.

sampler TextureSampler : register(s0);

#ifdef COLORIZE
// (min, max, target) hue, normalised to 0..1 (the C# passes RangeTarget / 360).
float3 ColorizeRange;

float3 rgb2hsv(float3 c)
{
    float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
    float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
    float d = q.x - min(q.w, q.y);
    float e = 1.0e-10;
    return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
}

float3 hsv2rgb(float3 c)
{
    float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
    return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
}
#endif

#ifdef FADE
float4 FadeValue;
#endif

#ifdef INTERPOLATE
// Texel offset from the current frame to the next, and the blend amount.
float2 InterpOffset;
float InterpDelta;
#endif

float4 PixelShaderFunction(float4 color : COLOR0, float2 texCoord : TEXCOORD0) : COLOR0
{
#ifdef INTERPOLATE
    float4 c = lerp(tex2D(TextureSampler, texCoord),
                    tex2D(TextureSampler, texCoord + InterpOffset), InterpDelta);
#else
    float4 c = tex2D(TextureSampler, texCoord);
#endif

#ifdef COLORIZE
    // Feathered hue-range membership: ramp in/out over HUE_FEATHER at each bound
    // instead of a hard step, so pixels whose hue straddles [min,max] recolour
    // gradually rather than snapping (which banded sprites like the level-3 alien
    // ship). The original's coarse 32^3 HSV lookup tables were naturally fuzzy; this
    // restores that softness. (Grays are unaffected either way: hsv2rgb ignores hue
    // when saturation ~= 0.)
    #define HUE_FEATHER 0.06
    float3 hsv = rgb2hsv(c.rgb);
    float m = smoothstep(ColorizeRange.x - HUE_FEATHER, ColorizeRange.x + HUE_FEATHER, hsv.x)
            * (1.0 - smoothstep(ColorizeRange.y - HUE_FEATHER, ColorizeRange.y + HUE_FEATHER, hsv.x));
    // Blend between the original and the fully-recoloured pixel in RGB (not by
    // lerping the hue), so the feathered transition can't pass through intermediate
    // hues and produce a rainbow fringe. m==1 -> full recolour (target hue, same
    // sat/val); m==0 -> untouched.
    float3 recoloured = hsv2rgb(float3(ColorizeRange.z, hsv.y, hsv.z));
    c.rgb = lerp(c.rgb, recoloured, m);
#endif

#ifdef LIGHTEN
    c.rgb = saturate(c.rgb * 2.0);
#endif

#ifdef FADE
    return c * FadeValue;
#else
    return c * color;
#endif
}

technique Sprite
{
    pass P0
    {
        PixelShader = compile ps_3_0 PixelShaderFunction();
    }
}
