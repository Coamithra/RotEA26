// glowblur.fx — separable Gaussian blur for the title glow (bloom on the native-res
// overlay). Run twice (horizontal then vertical) on a half-res copy of the overlay
// target; the blurred result is composited back additively as the bloom halo.
//
// Operates on premultiplied-alpha data (the overlay target is premultiplied), which is
// the correct space to blur in. 9 taps, weights sum to ~1. Texel is the per-pass step.

sampler TextureSampler : register(s0);

float2 Texel;   // (radius/width, 0) horizontal pass; (0, radius/height) vertical pass

float4 PixelShaderFunction(float4 color : COLOR0, float2 uv : TEXCOORD0) : COLOR0
{
    float4 c = tex2D(TextureSampler, uv) * 0.227027;
    c += tex2D(TextureSampler, uv + Texel * 1.0) * 0.194595;
    c += tex2D(TextureSampler, uv - Texel * 1.0) * 0.194595;
    c += tex2D(TextureSampler, uv + Texel * 2.0) * 0.121622;
    c += tex2D(TextureSampler, uv - Texel * 2.0) * 0.121622;
    c += tex2D(TextureSampler, uv + Texel * 3.0) * 0.054054;
    c += tex2D(TextureSampler, uv - Texel * 3.0) * 0.054054;
    c += tex2D(TextureSampler, uv + Texel * 4.0) * 0.016216;
    c += tex2D(TextureSampler, uv - Texel * 4.0) * 0.016216;
    return c * color;
}

technique GlowBlur
{
    pass P0
    {
        PixelShader = compile ps_2_0 PixelShaderFunction();
    }
}
