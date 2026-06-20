// BloomExtract.fx — pulls the bright areas out of the scene.
// Faithful port of the classic XNA "Bloom Postprocess" sample shader.
// Pixel-shader-only: KNI's SpriteBatch supplies the vertex transform.

sampler TextureSampler : register(s0);

float BloomThreshold;

float4 PixelShaderFunction(float4 color : COLOR0, float2 texCoord : TEXCOORD0) : COLOR0
{
    float4 c = tex2D(TextureSampler, texCoord);
    // Keep only values above BloomThreshold, rescaled back to 0..1.
    return saturate((c - BloomThreshold) / (1 - BloomThreshold));
}

technique BloomExtract
{
    pass P0
    {
        PixelShader = compile ps_2_0 PixelShaderFunction();
    }
}
