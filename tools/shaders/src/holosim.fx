// holosim.fx — fullscreen "trial simulation" filter for the tutorial holodeck.
//
// Applied by Game1.ApplyHoloSim as a sceneTarget -> holoRT pass right before the
// gamma present blit (the ApplySlowmoTrail seam). Pixel shader only: KNI's
// SpriteBatch supplies the vertex transform (same as gamma.fx).
//
// Two intensity drivers, both already eased/scaled on the C# side (Compat/HoloSim):
//   Intensity  the always-on baseline while the simulation runs — scanlines, a cool
//              cyan cast that strengthens toward the screen edges (vignette-shaped,
//              so the play area stays true-colour), and a faint interlace shimmer.
//   Burst      the "channel surf" spike (row jitter + static + contrast crunch +
//              hard scanlines), fired on Activating/Terminating Tutorial and on the
//              holodeck's Jump() glitch hiccups. 0 outside a burst.
// Time (seconds) rolls the noise/scanlines. The distortion recipe follows
// channelflip.fx (same hash + row-jitter idea) so the two read as one effect family.
//
// ps_3_0 like channelflip: the hash/branchless mix exceeds ps_2_0's budget.

float Intensity;
float Burst;
float Time;

sampler TextureSampler : register(s0);

static const float PI = 3.14159265;

float hash21(float2 p)
{
    return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
}

float4 PixelShaderFunction(float4 color : COLOR0, float2 texCoord : TEXCOORD0) : COLOR0
{
    float2 uv = texCoord;

    // Burst row displacement (channelflip's TV turbulence: row shear + per-row jitter).
    float skew = (uv.y - 0.5) * 0.05 * Burst;
    float row = floor(uv.y * 48.0);
    float jitter = (hash21(float2(row, floor(Time * 18.0))) - 0.5)
                 + (hash21(float2(row * 1.7, floor(Time * 7.0))) - 0.5) * 0.5;
    uv.x += skew + jitter * 0.06 * Burst;

    float4 c = tex2D(TextureSampler, uv);

    // Cool holo cast, strongest at the edges: pull toward a cyan-tinted luminance so
    // the frame reads as a projection without recolouring the action in the centre.
    float2 d = texCoord - 0.5;
    float edge = saturate(dot(d, d) * 2.8);
    float luma = dot(c.rgb, float3(0.299, 0.587, 0.114));
    float3 holo = luma * float3(0.55, 1.0, 0.95);
    c.rgb = lerp(c.rgb, holo, edge * 0.45 * Intensity + 0.20 * Burst);

    // Scanlines (subtle darkening baseline, harder in a burst) + a faint interlace
    // shimmer so the projection never sits perfectly still.
    float scan = 0.5 + 0.5 * sin((texCoord.y * 600.0 - Time * 14.0) * PI);
    c.rgb *= 1.0 - scan * (0.10 * Intensity + 0.22 * Burst);
    c.rgb *= 1.0 + 0.015 * Intensity * sin(Time * 37.0 + texCoord.y * 600.0 * PI);

    // Static grain + contrast crunch, burst only.
    float n = hash21(texCoord * float2(640.0, 480.0) + frac(Time) * 97.0);
    c.rgb += (n - 0.5) * 0.30 * Burst;
    c.rgb = saturate((c.rgb - 0.5) * (1.0 + 0.45 * Burst) + 0.5);

    return c * color;
}

technique HoloSim
{
    pass P0
    {
        PixelShader = compile ps_3_0 PixelShaderFunction();
    }
}
