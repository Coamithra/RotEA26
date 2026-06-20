// GaussianBlur.fx — separable Gaussian blur (run once horizontally, once
// vertically). Faithful port of the classic XNA "Bloom Postprocess" sample.
// BloomComponent fills SampleOffsets/SampleWeights (15 taps) per pass and
// MyDebug.Assert requires exactly 15 weight elements.

#define SAMPLE_COUNT 15

sampler TextureSampler : register(s0);

float2 SampleOffsets[SAMPLE_COUNT];
float SampleWeights[SAMPLE_COUNT];

float4 PixelShaderFunction(float4 color : COLOR0, float2 texCoord : TEXCOORD0) : COLOR0
{
    float4 c = 0;
    for (int i = 0; i < SAMPLE_COUNT; i++)
    {
        c += tex2D(TextureSampler, texCoord + SampleOffsets[i]) * SampleWeights[i];
    }
    return c;
}

technique GaussianBlur
{
    pass P0
    {
        PixelShader = compile ps_2_0 PixelShaderFunction();
    }
}
