// gamma.fx — fullscreen gamma-correction post-process.
//
// Stage 5 port of the lost XNA 3.x "gamma" effect. It is applied by SpriteBatch
// when Game1.DrawInner composites the resolved scene to the back buffer, so it
// needs only a pixel shader: KNI's SpriteBatch supplies the vertex transform via
// its internal SpriteEffect (Setup() -> _spritePass.Apply()), and a pass with no
// vertex shader leaves that VS bound.
//
// One float parameter (Gamma). The original code sets it positionally
// (Parameters[0]); we index it by name in C# to stay robust to MGFX ordering.

float Gamma;

sampler TextureSampler : register(s0);

float4 PixelShaderFunction(float4 color : COLOR0, float2 texCoord : TEXCOORD0) : COLOR0
{
    float4 c = tex2D(TextureSampler, texCoord);
    // abs() guards against the pow(negative) producing NaN on some GL drivers.
    c.rgb = pow(abs(c.rgb), 1.0 / Gamma);
    return c * color;
}

technique Gamma
{
    pass P0
    {
        PixelShader = compile ps_2_0 PixelShaderFunction();
    }
}
