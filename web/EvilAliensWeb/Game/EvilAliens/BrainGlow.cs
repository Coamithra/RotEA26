using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

// The soft blue glow drawn additively BEHIND an animated cyborg brain (the BrainAura recipe,
// blue tinted). The brainanimated sheet is chroma-keyed off a magenta backdrop and carries no
// halo of its own, unlike the brainlargetransglow sprite it replaced, which had one baked in --
// so every consumer of the sheet has to draw this or the brain reads as a flat cut-out.
//
// Card c25883a2 gave it a second in-world consumer (ParatrooperBrain, which was still on the old
// sprite) and lifted it out of Braineroid.DrawGlow verbatim rather than copying the constants a
// third time. The glow texture is pre-tinted blue, so it is drawn white-with-alpha.
//
// CastDisplayer keeps its own copy on purpose: it draws the cast brain directly (not through
// AlienDrawableGameComponent) with its own ?castbrain-tunable scale, so it has no DrawScale to
// hand this and shares no call shape.
internal static class BrainGlow
{
	private const float Omega = 2.6f;          // ~2.4s shimmer period

	private const float ScaleBase = 1.05f;     // glow drawn at brain DrawScale * this

	private const float ScaleShimmer = 0.04f;  // +/-4% breathe

	private const float AlphaBase = 0.5f;

	private const float AlphaShimmer = 0.12f;  // alpha rides 0.38..0.62

	// A per-instance phase offset, so a cluster of brains doesn't pulse in unison. Callers stash
	// one of these at Initialize time.
	internal static float RandomPhase()
	{
		return RandomHelper.RandomNextFloat(0f, MathHelper.TwoPi);
	}

	// `drawScale` is the brain's effective on-screen scale (AlienDrawableGameComponent.DrawScale),
	// already carrying any pulsate the caller applied; `blendMode` is what to restore afterwards.
	internal static void Draw(SpriteBatchWrapper spriteBatch, Texture2D glowTexture, Vector2 position,
		float rotation, float drawScale, float phase, GameTime gameTime, SpriteBlendMode blendMode)
	{
		spriteBatch.BlendMode = (SpriteBlendMode)2;
		DrawCore(spriteBatch, glowTexture, position, rotation, drawScale, phase, gameTime);
		spriteBatch.BlendMode = blendMode;
	}

	// The same draw with NO blend-state change, for a caller that has set additive ONCE for a whole
	// population instead of twice per brain (BraineroidGlows, card 391e11d2). Kept as the one body
	// so the two paths cannot drift -- the batched path has to be pixel-identical to the per-brain
	// one or the A/B seam it ships with would be comparing two different glows.
	internal static void DrawCore(SpriteBatchWrapper spriteBatch, Texture2D glowTexture, Vector2 position,
		float rotation, float drawScale, float phase, GameTime gameTime)
	{
		if (glowTexture == null)
		{
			return;
		}
		float t = (float)gameTime.TotalGameTime.TotalSeconds;
		float s = (float)Math.Sin(t * Omega + phase);
		float glowScale = drawScale * ScaleBase * (1f + ScaleShimmer * s);
		float alpha = AlphaBase + AlphaShimmer * s;
		spriteBatch.Draw(glowTexture, position, rotation, glowScale, center: true, new Color(new Vector4(1f, 1f, 1f, alpha)));
	}
}
