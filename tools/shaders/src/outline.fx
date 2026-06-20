// outline.fx — sprite outline (param: LineThickness).
//
// NOTE: outlineEffect is never .Enable()d anywhere in the shipped Xbox build, so
// this shader never actually runs — it only needs to exist so EffectHandler can
// load it (LoadGraphicsContent loads every *EffectFile and LoadEffects uses
// staticAlphaEffectFile != null as its "effects are loaded" sentinel). This is a
// plausible best-effort reconstruction, kept simple since it isn't exercised.

sampler TextureSampler : register(s0);

float LineThickness;

float4 PixelShaderFunction(float4 color : COLOR0, float2 texCoord : TEXCOORD0) : COLOR0
{
    float4 c = tex2D(TextureSampler, texCoord);
    if (c.a < 0.5)
    {
        float a = max(max(tex2D(TextureSampler, texCoord + float2(LineThickness, 0)).a,
                          tex2D(TextureSampler, texCoord - float2(LineThickness, 0)).a),
                      max(tex2D(TextureSampler, texCoord + float2(0, LineThickness)).a,
                          tex2D(TextureSampler, texCoord - float2(0, LineThickness)).a));
        return float4(0, 0, 0, a) * color.a;
    }
    return c * color;
}

technique Outline
{
    pass P0
    {
        PixelShader = compile ps_2_0 PixelShaderFunction();
    }
}
