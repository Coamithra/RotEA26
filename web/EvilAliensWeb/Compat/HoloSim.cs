using System;

namespace EvilAliensWeb.Compat
{
	// Fullscreen "trial simulation" filter state (tools/shaders/src/holosim.fx, applied by
	// Game1.ApplyHoloSim on sceneTarget right before the gamma present blit — the same seam
	// as the slowmo ghost trail).
	//
	// Lifecycle is POKE-driven, not scene-wired: TutorialLevel.Update calls Poke() every
	// tick, and the filter stays on only while recently poked. So ANY exit path (victory,
	// quit-to-menu, game over, checkpoint revert) turns it off with no lifecycle plumbing,
	// and the mix eases in/out (like slowmoTrailMix) so engaging/leaving never pops.
	//
	// Burst() fires the "channel surf" spike: a short envelope that drives the shader's
	// row-jitter/static/contrast — used on "Activating/Terminating Tutorial..." and (small)
	// on the holodeck's Background.Jump() glitch hiccups, so the background slip and the
	// screen glitch land together.
	public static class HoloSim
	{
		// Baseline filter strength while the simulation runs; the shader's own constants
		// keep the look subtle at 1. ?holofilter=<f> scales it (0 disables).
		public const float DefaultIntensity = 1f;

		// Burst envelope length. Attack is a snap (the spike IS the pop); decay eases out.
		private const float BurstSeconds = 0.9f;

		private static bool pokedThisFrame;
		private static float mix;            // eased 0..1 master fade
		private static float burstLeft;      // seconds remaining in the current burst
		private static float burstStrength = 1f;
		private static float time;           // raw accumulated seconds, rolls the shader noise

		// True while the filter has any visible contribution (Game1 skips the pass otherwise).
		public static bool Visible => mix > 0.004f && EffectiveIntensity > 0f;

		public static float Time => time;

		// Shader params, pre-scaled by the eased mix + the ?holofilter / ?holoburst knobs.
		public static float Intensity => mix * EffectiveIntensity;

		public static float Burst
		{
			get
			{
				if (burstLeft <= 0f)
				{
					return 0f;
				}
				float p = burstLeft / BurstSeconds;
				// sin easing: fast rise at the tail end of p~1, smooth fall to 0.
				float env = (float)Math.Sin(p * Math.PI * 0.5);
				return mix * env * burstStrength * (DebugFlags.HoloBurst ?? 1f);
			}
		}

		private static float EffectiveIntensity => DefaultIntensity * (DebugFlags.HoloFilter ?? 1f);

		// Keep the filter alive this frame. Call every Update tick while the sim runs.
		public static void Poke()
		{
			pokedThisFrame = true;
		}

		// Fire a channel-surf glitch spike (strength 1 = full activate/terminate pop;
		// ~0.3-0.4 suits the background's small Jump() hiccups).
		public static void FireBurst(float strength = 1f)
		{
			burstLeft = BurstSeconds;
			burstStrength = strength;
		}

		// Advance envelopes on RAW (unscaled) time from Game1.Update, so the filter keeps
		// living through hit-stop/slowmo like the other Draw-time cosmetics.
		public static void Update(float dtSeconds)
		{
			time += dtSeconds;
			if (burstLeft > 0f)
			{
				burstLeft = Math.Max(0f, burstLeft - dtSeconds);
			}
			float target = pokedThisFrame ? 1f : 0f;
			pokedThisFrame = false;
			// Same dt-corrected ease as the slowmo trail (~0.25s ramp at 60Hz).
			float frames = MathClamp(dtSeconds, 0f, 0.1f) * 60f;
			float easeAlpha = 1f - (float)Math.Pow(1.0 - 0.15, frames);
			mix += (target - mix) * easeAlpha;
			if (mix < 0.004f)
			{
				mix = 0f;
			}
			else if (mix > 1f)
			{
				mix = 1f;
			}
		}

		private static float MathClamp(float v, float lo, float hi)
		{
			return v < lo ? lo : (v > hi ? hi : v);
		}
	}
}
