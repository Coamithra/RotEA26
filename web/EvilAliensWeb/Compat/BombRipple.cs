using System;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat
{
	// Bomb-detonation ripple state (tools/shaders/src/bombripple.fx, applied by
	// Game1.ApplyBombRipple on sceneTarget right after ApplyHoloSim — the same post
	// seam as the slowmo ghost trail and the holo filter). Trello card 5f38ed35.
	//
	// A stone-in-water refraction ring radiating from where the bomb went off.
	// Blast.Initialize fires one for every NON-mini blast, which covers the local
	// bomb (PlayerShip.doBlast) and a remote peer's bomb (PlayerShip.NetDoBlast)
	// with no extra plumbing and no net traffic — the effect is Draw-time only, so
	// co-op determinism (and the build-hash compatibility key) is untouched.
	//
	// The ring FOLLOWS its blast in both location and duration (card 03c379f2):
	// PlayerShip.Update drags the live Blast with the ship every tick, so a ring
	// parked at the detonation point was left behind the explosion it decorates,
	// and its fixed 0.75 s life ended 0.25..4.25 s before the blast's own
	// 1000ms*(power+1). Fire therefore returns a generation TOKEN and seeds the
	// ring's duration from the blast's real lifetime; Blast.Update pushes its live
	// position back through MoveRing(token, pos). A stale token (ring evicted by a
	// fifth bomb, or a pool-recycled Blast) no-ops rather than dragging someone
	// else's ring — the push is the only coupling, so Compat still references no
	// game type.
	//
	// Four slots, so overlapping bombs each get their own ring; a fifth evicts the
	// oldest. Every knob the tuner exposes is a baked Default* const read through a
	// `?ripple*` override, so a shipped build with no flags is byte-identical to the
	// tuned look. The three shaping constants BELOW that set (AmplitudePerPower,
	// RadiusPerPower, MiniScale) are deliberately flagless -- they are the effect's
	// internal proportions, not things a taste pass should be dragging.
	public static class BombRipple
	{
		// How many concurrent rings the shader carries (bombripple.fx has one uniform
		// per slot). Overlapping bombs are rare; four is plenty and costs nothing.
		public const int MaxRings = 4;

		// Peak UV displacement of a power-0 bomb's wavefront, in fractions of the
		// target width/height. ~1.8% of the screen is a clear deformation that still
		// reads as refraction rather than as a tear. ?rippleamp=
		public const float DefaultAmplitude = 0.018f;

		// How far the wavefront travels over its life, in fractions of screen HEIGHT
		// (the shader measures aspect-corrected distance, so this is a true radius).
		// 0.55 puts the final ring a little past the screen's short half-axis, which
		// is where it has already faded out. ?rippleradius=
		public const float DefaultRadius = 0.55f;

		// FALLBACK life of one ring in seconds — since card 03c379f2 every real ring
		// carries its OWN duration (its blast's lifetime, seeded at Fire), so this is
		// only what a ring fired without one gets. ?rippleduration= overrides both.
		public const float DefaultDuration = 0.75f;

		// Gaussian half-width of the wavefront, same units as the radius. Narrow =>
		// a crisp single wave; wide => a slow swell. ?ripplewidth=
		public const float DefaultWidth = 0.055f;

		// Exponent on the (1 - t) amplitude decay. >1 keeps the ring strong early and
		// drops it away fast, which is what stops the tail reading as a wobble.
		// ?ripplefalloff=
		public const float DefaultFalloff = 1.6f;

		// Additive caustic glint on the crest. Deliberately faint — the deformation is
		// the effect; this only keeps the wavefront legible over flat dark space.
		// ?ripplerim=
		public const float DefaultRim = 0.10f;

		// Extra amplitude and radius per bomb power level (Score powerup level 0..4),
		// so a fully powered bomb ripples visibly bigger than a bare one.
		private const float AmplitudePerPower = 0.22f;
		private const float RadiusPerPower = 0.18f;

		// The mini blasts (asploding bullets, Bullet.cs) are OFF by default: a dozen
		// of them at once would strobe the whole frame. ?ripplemini turns them on, at
		// a fraction of the size. Card 5f38ed35 ruling: sensible default + the knob.
		private const float MiniScale = 0.45f;

		// A live ring stores ONLY what is per-detonation: where it went off, how long it has
		// been going, and the two multipliers its bomb earned (power level + the mini
		// shrink). Every tunable -- amplitude, radius, duration, falloff -- is resolved LIVE
		// in PackedRings instead of being baked in here, so a slider drag on the eaRipple
		// panel retunes a ring that is already travelling. That is the whole point of the
		// panel on a taste-call card; baking them made three of the seven sliders look dead
		// until the next bomb.
		private struct Ring
		{
			public Vector2 Centre;   // design-space (800x600) blast position, pushed live via MoveRing
			public float Elapsed;    // seconds since it fired
			public float Duration;   // this ring's own life in seconds (the blast's lifetime); <= 0 => DefaultDuration
			public float SizeScale;  // 1 for a bomb, MiniScale for an asploding bullet
			public float Power;      // bomb powerup level 0..4, clamped at Fire
			public int Token;        // the handle Fire returned; MoveRing must match it exactly
			public bool Alive;
		}

		private static readonly Ring[] rings = new Ring[MaxRings];
		private static readonly Vector4[] packed = new Vector4[MaxRings];
		private static int nextSlot;

		// Monotone per-Fire stamp folded into every token, so a token outlives neither its ring
		// nor its slot: an evicted slot's next ring gets a new generation and the old token stops
		// matching. Starts at 1 because 0 is the "no ring was fired" sentinel Fire returns.
		private static int generation;

		// The resolved knobs. All public so DebugInput.RippleState can REPORT the values the
		// renderer actually uses rather than re-deriving the `?? Default` fallbacks (and
		// silently dropping Duration's floor, which is how a readout starts lying).
		// Read every frame -- see the Ring comment above.

		// Master scale; 0 = the whole effect off (the kill switch). ?ripple=
		public static float Master => DebugFlags.Ripple ?? 1f;

		public static float Amplitude => DebugFlags.RippleAmp ?? DefaultAmplitude;
		public static float Radius => DebugFlags.RippleRadius ?? DefaultRadius;
		public static float Duration => Math.Max(0.01f, DebugFlags.RippleDuration ?? DefaultDuration);
		public static float Falloff => DebugFlags.RippleFalloff ?? DefaultFalloff;

		// The duration one specific ring actually runs on: the ?rippleduration= override (live,
		// so the slider still retunes rings in flight and the committed probe's expiry window
		// stays pinned by the flag) beats the ring's own blast-seeded life, which beats the
		// baked fallback. Same 0.01 s floor as Duration, for the same reason.
		private static float RingDuration(in Ring r)
		{
			if (DebugFlags.RippleDuration.HasValue)
			{
				return Math.Max(0.01f, DebugFlags.RippleDuration.Value);
			}
			return r.Duration > 0f ? r.Duration : DefaultDuration;
		}
		public static float Width => Math.Max(0.001f, DebugFlags.RippleWidth ?? DefaultWidth);
		public static float Rim => DebugFlags.RippleRim ?? DefaultRim;

		// True while any ring has a visible contribution. Game1 skips the whole pass
		// otherwise, so a frame with no bomb out costs exactly nothing. Amplitude is tested
		// as well as Master because ?rippleamp=0 is a legal (clamped, not rejected) value,
		// and a zero-amplitude ring would otherwise drive two full-screen blits and a shader
		// pass that displace nothing for the whole 0.75 s.
		public static bool Visible
		{
			get
			{
				if (Master <= 0f || Amplitude <= 0f)
				{
					return false;
				}
				for (int i = 0; i < MaxRings; i++)
				{
					if (rings[i].Alive)
					{
						return true;
					}
				}
				return false;
			}
		}

		// Fire a ring at a design-space position. `power` is the bomb's powerup level
		// (Blast's own `power` is that + 1; callers pass the level). Minis are gated on
		// ?ripplemini and ripple at MiniScale. `durationSeconds` is the blast's own
		// lifetime so the ring expires when the explosion does (<= 0 keeps the baked
		// fallback). Returns the token MoveRing needs; 0 = no ring fired.
		public static int Fire(Vector2 designPosition, int power, bool mini = false,
			float durationSeconds = 0f)
		{
			if (Master <= 0f || (mini && !DebugFlags.RippleMini))
			{
				return 0;
			}
			int slot = nextSlot;
			nextSlot = (nextSlot + 1) % MaxRings;
			int token = MaxRings * ++generation + slot;
			rings[slot] = new Ring
			{
				Centre = designPosition,
				Elapsed = 0f,
				Duration = durationSeconds,
				SizeScale = mini ? MiniScale : 1f,
				Power = MathHelper.Clamp(power, 0, 4),
				Token = token,
				Alive = true
			};
			return token;
		}

		// Re-centre a live ring on its blast's current position -- called from Blast.Update
		// every tick, which is what makes the ring ride the ship exactly as the blast does
		// (PlayerShip.Update drags the blast; the blast drags its ring). A token that no
		// longer matches (evicted slot, recycled Blast, master off at Fire) is a no-op, and
		// the parked scrub ring (?ripplephase=) is EnsureParked's alone.
		public static void MoveRing(int token, Vector2 designPosition)
		{
			if (token == 0 || DebugFlags.RipplePhase.HasValue)
			{
				return;
			}
			int slot = token % MaxRings;
			if (rings[slot].Alive && rings[slot].Token == token)
			{
				rings[slot].Centre = designPosition;
			}
		}

		// Advance every live ring. Called from Game1.ApplyBombRipple on RAW (unscaled)
		// Draw time, so the ripple keeps travelling through hit-stop / slowmo like the
		// other Draw-time cosmetics (HoloSim does the same).
		public static void Update(float dtSeconds)
		{
			if (DebugFlags.RipplePhase.HasValue)
			{
				// ?ripplephase= parks a single ring at a fixed phase for screenshots —
				// EnsureParked owns its state, so live decay must not touch it.
				EnsureParked();
				return;
			}
			for (int i = 0; i < MaxRings; i++)
			{
				if (!rings[i].Alive)
				{
					continue;
				}
				rings[i].Elapsed += dtSeconds;
				// Against the LIVE resolved duration, so shortening it on the tuner retires
				// the rings already in flight instead of leaving them stuck past their own end.
				if (rings[i].Elapsed >= RingDuration(in rings[i]))
				{
					rings[i].Alive = false;
				}
			}
		}

		// The four packed ring uniforms for bombripple.fx: xy = centre in target UV,
		// z = current radius, w = current amplitude. Design->UV is a plain divide:
		// RenderScale.Matrix is a pure per-axis scale onto the whole scene target, so
		// there is no letterbox offset INSIDE it (the letterbox happens later, on the
		// present blit).
		public static Vector4[] PackedRings()
		{
			for (int i = 0; i < MaxRings; i++)
			{
				if (!rings[i].Alive)
				{
					packed[i] = Vector4.Zero;
					continue;
				}
				float t = MathHelper.Clamp(rings[i].Elapsed / RingDuration(in rings[i]), 0f, 1f);
				float decay = (float)Math.Pow(1f - t, Falloff);
				float amp = Amplitude * Master * rings[i].SizeScale
					* (1f + AmplitudePerPower * rings[i].Power);
				float radius = Radius * rings[i].SizeScale
					* (1f + RadiusPerPower * rings[i].Power);
				packed[i] = new Vector4(
					rings[i].Centre.X / RenderScale.DesignWidth,
					rings[i].Centre.Y / RenderScale.DesignHeight,
					t * radius,
					amp * decay);
			}
			return packed;
		}

		// ?ripplephase=<0..1> (+ ?ripplecenter=x,y, ?ripplepower=<0..4>): park ONE ring at
		// a chosen point in its life and hold it there, so a still screenshot shows the
		// deformation at a known phase. This is the card's scrub rig — the effect is
		// time-varying, so a timed live screenshot would prove nothing (root CLAUDE.md,
		// "never verify motion with timed live screenshots"). Re-derived every frame so a
		// slider drag on the eaRipple panel retunes the parked frame immediately.
		//
		// It carries a POWER like a real detonation (default 0, a bare bomb): a maxed bomb
		// is 1.88x the amplitude and 1.72x the radius, and that is the case most likely to
		// look wrong, so the screenshot rig has to be able to reach it.
		private static void EnsureParked()
		{
			float phase = MathHelper.Clamp(DebugFlags.RipplePhase.Value, 0f, 1f);
			Vector2 centre = DebugFlags.RippleCenter
				?? new Vector2(RenderScale.DesignWidth * 0.5f, RenderScale.DesignHeight * 0.5f);
			for (int i = 1; i < MaxRings; i++)
			{
				rings[i].Alive = false;
			}
			float power = MathHelper.Clamp(DebugFlags.RipplePower ?? 0f, 0f, 4f);
			// The parked ring carries the duration a real bomb of that power would seed
			// (Blast.Setup: 1000ms * (power+1)), so the scrubbed phase maps onto the same
			// point of a real detonation's life. ?rippleduration= still wins inside
			// RingDuration, exactly as it does for a live ring.
			float duration = 1f + power;
			rings[0] = new Ring
			{
				Centre = centre,
				Elapsed = phase * Math.Max(0.01f, DebugFlags.RippleDuration ?? duration),
				Duration = duration,
				SizeScale = 1f,
				Power = power,
				Token = 0,
				Alive = true
			};
			nextSlot = 1 % MaxRings;
		}

		// Per-ring readout for eaRipple.state() / `eval RippleState` -- centre, elapsed and
		// the RESOLVED duration each live ring is actually running on. The follow behaviour
		// (card 03c379f2) moves no counter and changes pixels only mid-motion, so this line
		// is what the committed probe asserts a moving blast against.
		public static string DescribeRings()
		{
			string s = "";
			for (int i = 0; i < MaxRings; i++)
			{
				if (!rings[i].Alive)
				{
					continue;
				}
				s += " r" + i + "=(" + rings[i].Centre.X.ToString("0.#") + ","
					+ rings[i].Centre.Y.ToString("0.#")
					+ " e=" + rings[i].Elapsed.ToString("0.00")
					+ "/" + RingDuration(in rings[i]).ToString("0.00") + ")";
			}
			return s.Length == 0 ? " rings=none" : s;
		}
	}
}
