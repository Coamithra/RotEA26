// metal.fx — metallic "chrome sheen" for marquee menufont text (per-element).
//
// Applied as a one-off composite of a TEXT-ONLY render target (white/tinted glyphs on
// transparent), drawn as a SINGLE quad so texCoord spans 0..1 across THAT text element.
// That makes the sheen relative to the text, NOT the screen — so a title up top and a
// prompt down low get identical sheen (a screen-space VPOS gradient would slice them
// differently). See SpriteBatchWrapper.DrawMetalString for the RT plumbing.
//
//   s0 = the text RT. STRAIGHT alpha; rgb already carries the per-glyph tint (the text
//        was drawn in its real colour into the RT). The sheen MODULATES that tint, so
//        white -> chrome-white and red -> chrome-red — the "usually white but not
//        always" colours survive. A white-hot animated glint is then added on top.
//
// Output is straight alpha (mask = RT alpha), composited NonPremultiplied over the
// scene; the unified bloom pass catches the glint for a free specular pop.
//
// ps_3_0: a few smoothsteps + a frac sweep — comfortably within budget; links fine with
// SpriteBatch's own vertex shader in the compiled GLSL (same as channelflip.fx).

sampler TextureSampler : register(s0);

float Time;          // seconds — drives the animated glint sweep
float GradTop;       // brightness multiplier at the top of the glyph band
float GradMid;       // brightness multiplier in the mid "shadow" band
float GradBot;       // brightness multiplier at the bottom (rim light)
float GlintStrength; // white-hot streak intensity (added, then masked by alpha)
float GlintWidth;    // half-width of the sweep band, in UV
float SweepPeriod;   // seconds per glint cycle (crossing + rest gap)
float SweepActive;   // fraction of the period the glint spends crossing (0..1)
float PadFracTop;    // top transparent inset baked into the RT, as a fraction of box height.
float PadFracBot;    // bottom transparent inset, as a fraction of box height. Separate top/bottom
                     // so the gradient spans the GLYPH band, not the padded box, even when the box
                     // is vertically ASYMMETRIC (DrawShadowString's +Npx drop shadow extends the
                     // bottom). Equal top==bottom for the symmetric DrawMetalString (menu titles).
float2 UvExtent;     // the (u,v) sub-rect of the RT the text actually occupies. The RT is
                     // GROW-ONLY and shared across many strings per frame, so a given
                     // string fills only its top-left corner; UvExtent = (usedW/texW,
                     // usedH/texH) and local = tc / UvExtent remaps that corner to 0..1.

float4 PixelShaderFunction(float4 color : COLOR0, float2 tc : TEXCOORD0) : COLOR0
{
    float4 t = tex2D(TextureSampler, tc);                 // sample the real (sub-rect) texels
    float mask = t.a;
    float2 local = tc / max(UvExtent, float2(1e-4, 1e-4)); // 0..1 across THIS text element

    // Remap V across just the glyph band (drop the transparent padding) so the chrome
    // highlight / shadow / rim always land on the letters regardless of pad size. Top and
    // bottom insets are independent so an asymmetric box (drop-shadow overshoot at the
    // bottom) doesn't drag the gradient ~2px below the real glyphs.
    float gv = saturate((local.y - PadFracTop) / max(1e-3, 1.0 - PadFracTop - PadFracBot));

    // Chrome vertical gradient: bright top -> shadow mid -> rim-light bottom.
    float topToMid = lerp(GradTop, GradMid, smoothstep(0.0, 0.55, gv));
    float grad     = lerp(topToMid, GradBot, smoothstep(0.72, 1.0, gv));
    float3 metalRGB = t.rgb * grad;

    // Animated glint: a near-horizontal band sweeps left->right, then rests off-screen.
    // phase runs 0..(1/SweepActive); the band is live only while phase <= 1.
    float phase = frac(Time / SweepPeriod) / max(1e-3, SweepActive);
    float gu    = local.x * 0.88 + (1.0 - local.y) * 0.12;      // slight diagonal lean
    float glint = smoothstep(GlintWidth, 0.0, abs(gu - phase)) * step(phase, 1.0);
    metalRGB += glint * GlintStrength;                          // alpha below masks it

    return float4(metalRGB, mask) * color;
}

technique Metal
{
    pass P0
    {
        PixelShader = compile ps_3_0 PixelShaderFunction();
    }
}
