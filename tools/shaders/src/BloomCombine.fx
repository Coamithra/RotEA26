// BloomCombine.fx — combines the blurred bloom with the original scene.
// Faithful port of the classic XNA "Bloom Postprocess" sample.
//
// The bloom (blurred bright-pass) arrives as the SpriteBatch sprite texture on
// s0. The ORIGINAL scene is bound as an effect texture PARAMETER (BaseTexture),
// NOT via GraphicsDevice.Textures[1]: KNI's SpriteBatch custom-effect path does
// not preserve a manually-bound s1, so a register(s1) sampler reads black and the
// combine would output bloom only (everything blurry). A sampler_state bound to a
// texture parameter is set by pass.Apply() and works.

sampler BloomSampler : register(s0);

texture BaseTexture;
sampler BaseSampler = sampler_state
{
    Texture = <BaseTexture>;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;
    AddressU = Clamp;
    AddressV = Clamp;
};

float BloomIntensity;
float BaseIntensity;
float BloomSaturation;
float BaseSaturation;

float4 AdjustSaturation(float4 color, float saturation)
{
    // Human eye is more sensitive to green, less to blue.
    float grey = dot(color.rgb, float3(0.3, 0.59, 0.11));
    return lerp(grey, color, saturation);
}

float4 PixelShaderFunction(float4 color : COLOR0, float2 texCoord : TEXCOORD0) : COLOR0
{
    float4 bloom = tex2D(BloomSampler, texCoord);
    float4 base  = tex2D(BaseSampler, texCoord);

    bloom = AdjustSaturation(bloom, BloomSaturation) * BloomIntensity;
    base  = AdjustSaturation(base, BaseSaturation) * BaseIntensity;

    // Darken the base where bloom is strong, so it doesn't blow out.
    base *= (1 - saturate(bloom));

    return base + bloom;
}

technique BloomCombine
{
    pass P0
    {
        PixelShader = compile ps_2_0 PixelShaderFunction();
    }
}
