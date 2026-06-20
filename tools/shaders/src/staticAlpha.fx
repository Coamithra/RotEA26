// staticAlpha.fx — force a constant alpha multiplier (param: Alpha).
//
// NOTE: staticAlphaEffect is never .Enable()d in the shipped Xbox build, so this
// never runs at draw time; it only needs to load (EffectHandler uses
// staticAlphaEffectFile != null as its "effects loaded" sentinel). Simple, since
// it isn't exercised.

sampler TextureSampler : register(s0);

float Alpha;

float4 PixelShaderFunction(float4 color : COLOR0, float2 texCoord : TEXCOORD0) : COLOR0
{
    float4 c = tex2D(TextureSampler, texCoord) * color;
    c.a *= Alpha;
    return c;
}

technique StaticAlpha
{
    pass P0
    {
        PixelShader = compile ps_2_0 PixelShaderFunction();
    }
}
