using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

public class Quad
{
	private bool alreadyloaded;

	private Game game;

	private Texture2D middle;

	private Texture2D glow;

	private Vector3 origin;

	private Vector3 upperLeft;

	private Vector3 lowerLeft;

	private Vector3 upperRight;

	private Vector3 lowerRight;

	private Vector3 direction;

	private Vector3 left;

	public float width;

	public float height;

	public float lead;

	private static Vector3 normal = Vector3.Backward;

	// --- Protoss-style beam FX (see Draw) -----------------------------------------
	private const float GlowWidthScale = 2.6f;   // blue glow halo width vs core width
	private const float TipFlareScale = 3.0f;    // leading-tip bloom diameter vs core width
	private const float MuzzleFlareScale = 2.0f; // muzzle bloom diameter vs core width
	private const float ArcThickness = 2.0f;     // electric tendril core thickness (design px)
	private const int ArcLevels = 3;             // midpoint-displacement subdivisions per tendril
	// Rounded end-caps (Trello "improve laser animation"): the lazermiddle strip has no soft
	// falloff ALONG its length, so the beam ends read as flat/"chopped" and the big tip/muzzle
	// blooms leave a seam where the rectangle meets them. A width-sized round cap (the glow
	// circle) at each end domes the flat edge off. Scale is tunable via ?lazercapscale=.
	private const float DefaultCapScale = 1.0f;
	// Electric tendrils SPAWN STOCHASTICALLY (a per-frame Bernoulli trial at DefaultArcRate/sec, the
	// RandomHelper.RandomFromAverage model) instead of a fixed handful on a shared cadence -- so they
	// pop up out of sync "all over" like real arcing energy. Each lives a random ArcLife seconds, then
	// dies; while alive it also DRIFTS along the beam at a random (signed) speed up to DefaultTendrilSpeed.
	// Defaults tuned by eye; overridable via ?lazerarcs= (rate) / ?lazertendrilspeed= / ?lazerarclife= (mean).
	private const float DefaultArcRate = 2f;         // average tendrils spawned per second
	private const float DefaultArcLifeMin = 0.25f;   // a tendril's lifespan is random in [min,max] seconds
	private const float DefaultArcLifeMax = 0.5f;
	private const float DefaultTendrilSpeed = 150f;  // max |drift| along the beam, design px/sec (dir randomised)
	private static float CapScale => EvilAliensWeb.Compat.DebugFlags.LazerCapScale ?? DefaultCapScale;
	private static float ArcRate => EvilAliensWeb.Compat.DebugFlags.LazerArcRate ?? DefaultArcRate;
	private static float TendrilSpeed => EvilAliensWeb.Compat.DebugFlags.LazerTendrilSpeed ?? DefaultTendrilSpeed;
	// Lifespan range: ?lazerarclife overrides the MEAN (range = mean +/-33%), else the baked 0.25..0.5.
	private static void ArcLifeRange(out float lo, out float hi)
	{
		float? mean = EvilAliensWeb.Compat.DebugFlags.LazerArcLife;
		if (mean.HasValue && mean.Value > 0f) { lo = mean.Value * 0.6667f; hi = mean.Value * 1.3333f; }
		else { lo = DefaultArcLifeMin; hi = DefaultArcLifeMax; }
	}
	private static readonly Color CoreColor = new Color(210, 235, 255);   // white-hot beam core
	private static readonly Color GlowColor = new Color(35, 110, 235);    // electric-blue beam glow
	private static readonly Color FlareColor = new Color(150, 215, 255);  // cyan-white bloom
	private static readonly Color ArcColor = new Color(195, 235, 255);    // tendril hot core
	private static readonly Color ArcGlowColor = new Color(45, 120, 235); // tendril blue glow
	// FX-only RNG, kept separate from the gameplay RandomHelper so render-time jitter
	// can't desync a future lockstep co-op session (Stage 11).
	private static readonly Random fxr = new Random();
	// Per-beam stable seed: each tendril is a deterministic function of (seed, time), so it
	// writhes smoothly with the clock instead of being re-randomised (= strobing) each frame.
	private readonly float fxPhase = RandF(0f, 1000f);
	// Reusable midpoint-displacement scratch buffers (no per-frame allocation in Draw).
	private static readonly Vector2[] boltA = new Vector2[64];
	private static readonly Vector2[] boltB = new Vector2[64];

	// Live tendril pool. Each tendril is spawned stochastically, holds its per-appearance shape for
	// its whole (random) life, drifts along the beam, then dies -- so this is STATE, unlike the old
	// stateless hash-of-time approach. `lastArcTime` gives the per-frame dt that drives the spawn
	// probability (rate*dt). Pool is generous; at ~2/sec x ~0.375s life only ~1 is live at a time.
	private struct Tendril
	{
		public bool active;
		public float birth;   // time it spawned (seconds)
		public float life;    // its lifespan (seconds)
		public float seed;    // per-appearance shape seed (stable for its whole life)
		public float ap;      // base anchor along the beam, 0..1 (before drift)
		public float side;    // which side it whips out to, +/-1
		public float reach;   // how far out it whips (design px)
		public float lean;    // along-axis skew of the free end (design px)
		public float drift;   // signed drift speed along the beam (design px/sec)
	}
	private readonly Tendril[] tendrils = new Tendril[24];
	private float lastArcTime = float.NaN;

	public void LoadContent()
	{
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_009a: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a4: Expected O, but got Unknown
		//IL_00aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ba: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f6: Expected O, but got Unknown
		//IL_00fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_010c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0118: Unknown result type (might be due to invalid IL or missing references)
		//IL_013e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0148: Expected O, but got Unknown
		//IL_014e: Unknown result type (might be due to invalid IL or missing references)
		//IL_015e: Unknown result type (might be due to invalid IL or missing references)
		//IL_016a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0194: Unknown result type (might be due to invalid IL or missing references)
		//IL_019e: Expected O, but got Unknown
		if (!alreadyloaded)
		{
			alreadyloaded = true;
			ContentManager contentManager = ServiceHelper.Get<IContentManagerService>().ContentManager;
			middle = contentManager.Load<Texture2D>("GFX/Sprites/lazermiddle");
			glow = contentManager.Load<Texture2D>("GFX/Sprites/singleconnectorglow");
		}
	}

	public void UnloadGraphics()
	{
		alreadyloaded = false;
	}

	public Quad(Game game, Vector2 origin, float direction, float width, float height, float lead)
	{
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0062: Unknown result type (might be due to invalid IL or missing references)
		//IL_0082: Unknown result type (might be due to invalid IL or missing references)
		//IL_0087: Unknown result type (might be due to invalid IL or missing references)
		this.game = game;
		this.origin = new Vector3(origin.X - 400f, 300f - origin.Y, 0f);
		this.height = height;
		this.lead = lead;
		this.width = width;
		this.direction = convertToVector3(direction);
		calculatePoints();
	}

	// Web port: the original beam was three textured 3D quads pushed with BasicEffect via
	// DrawUserIndexedPrimitives -- on WebGL each is a marshalled WASM->JS GL call (vertex-
	// buffer upload + effect apply + draw) and the leading SpriteBatch Flush() shattered the
	// scene's sprite batch once per laser: cheap on Xbox/native, brutal in the browser. It now
	// draws as a handful of additive sprites through the batching wrapper (no flush, no
	// immediate-mode uploads), and the flat white bolt got a Protoss-style glow-up: a wide blue
	// glow + a white-hot core (each ONE continuous sprite, so there's no segment-seam crack) +
	// round flares blooming at the muzzle and leading tip + electric tendrils crackling off it.
	public void Draw(float time)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		SpriteBatchWrapper sb = ServiceHelper.Get<ISpriteBatchWrapperService>().SpriteBatchWrapper;
		// Beam axis in screen space (y-down): texture +Y runs along the beam, +X across it.
		Vector2 dirScreen = new Vector2(direction.X, 0f - direction.Y);
		float rotation = (float)Math.Atan2(dirScreen.X, 0f - dirScreen.Y);

		Vector2 tip = ToScreen((upperLeft + upperRight) * 0.5f);
		Vector2 tail = ToScreen((lowerLeft + lowerRight) * 0.5f);
		Vector2 bodyCenter = (tip + tail) * 0.5f;
		float bodyLen = Vector2.Distance(tip, tail);
		Vector2 axis = (bodyLen > 0.001f) ? (tip - tail) / bodyLen : new Vector2(0f, -1f);
		Vector2 perp = new Vector2(0f - axis.Y, axis.X);

		SpriteBlendMode oldMode = sb.BlendMode;
		sb.BlendMode = SpriteBlendMode.Additive;
		// wide soft blue glow, then the bright hot core -- each a single continuous sprite, so
		// the old core/cap rasterisation crack can't form.
		DrawBeam(sb, middle, bodyCenter, rotation, width * GlowWidthScale, bodyLen + width, GlowColor);
		DrawBeam(sb, middle, bodyCenter, rotation, width, bodyLen, CoreColor);
		// Rounded end-caps: dome off the beam's otherwise flat/"chopped" ends (and hide the
		// core/flare seam) with a width-sized round glow at each end -- a wide glow-colour cap
		// under a hot core-colour cap, matching the beam's two layers.
		float cap = CapScale;
		if (cap > 0f)
		{
			DrawFlare(sb, tip, width * GlowWidthScale * cap, GlowColor);
			DrawFlare(sb, tail, width * GlowWidthScale * cap, GlowColor);
			DrawFlare(sb, tip, width * cap, CoreColor);
			DrawFlare(sb, tail, width * cap, CoreColor);
		}
		// electric tendrils crackling off the beam (shortlived, respawn all over -- see DrawArcs)
		DrawArcs(sb, tail, axis, perp, bodyLen, time);
		// round flares blooming at the leading tip (gently pulsing) and the muzzle
		float pulse = 1f + 0.12f * (float)Math.Sin(time * 9f + fxPhase);
		DrawFlare(sb, tip, width * TipFlareScale * pulse, FlareColor);
		DrawFlare(sb, tail, width * MuzzleFlareScale, FlareColor);
		sb.BlendMode = oldMode;
	}

	// Quad world space is centred + y-up (origin at screen centre); convert to screen pixels.
	private static Vector2 ToScreen(Vector3 p)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		return new Vector2(p.X + 400f, 300f - p.Y);
	}

	// Stretches the soft beam strip to acrossPx x alongPx, centred and rotated about `center`.
	private void DrawBeam(SpriteBatchWrapper sb, Texture2D tex, Vector2 center, float rotation, float acrossPx, float alongPx, Color color)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		Vector2 scale = new Vector2(acrossPx / (float)tex.Width, alongPx / (float)tex.Height);
		sb.Draw(tex, center, rotation, scale, center: true, color);
	}

	// Blooms the round glow texture to ~diameterPx, centred (it's radial, so rotation is moot).
	private void DrawFlare(SpriteBatchWrapper sb, Vector2 center, float diameterPx, Color color)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		float s = diameterPx / (float)glow.Width;
		sb.Draw(glow, center, 0f, new Vector2(s, s), center: true, color);
	}

	// Draws a thin glowing line p0->p1 as one stretched strip -- a single electric tendril edge.
	private void DrawLine(SpriteBatchWrapper sb, Vector2 p0, Vector2 p1, float thickness, Color color)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		Vector2 d = p1 - p0;
		float len = d.Length();
		if (len < 0.5f)
		{
			return;
		}
		float rot = (float)Math.Atan2(0f - d.X, d.Y);
		DrawBeam(sb, middle, (p0 + p1) * 0.5f, rot, thickness, len, color);
	}

	// Electric tendrils crackling off the beam. Tendrils SPAWN STOCHASTICALLY (see DrawArcs's
	// per-frame Bernoulli trial) rather than on a fixed shared cadence, so they pop up out of sync
	// "all over" like real arcing energy (Trello "improve laser animation"). Each holds its
	// per-appearance shape for its whole random life, DRIFTS along the beam at a random signed
	// speed, and fades in/out via a sin envelope; within a life the bolt still WRITHES via smooth
	// time-driven midpoint displacement (no per-frame RNG strobing). This method advances the pool
	// (spawn + expire) and draws every live tendril.
	private void DrawArcs(SpriteBatchWrapper sb, Vector2 tailPt, Vector2 axis, Vector2 perp, float bodyLen, float time)
	{
		// Per-frame dt drives the spawn probability (rate*dt = the RandomHelper.RandomFromAverage
		// model). Clamp it: a fresh/recycled beam's first frame (lastArcTime NaN) or a long stall
		// shouldn't dump a burst. Kept on the FX RNG so render-time jitter can't desync co-op.
		float dt = float.IsNaN(lastArcTime) ? 0f : (time - lastArcTime);
		lastArcTime = time;
		if (dt < 0f) dt = 0f; else if (dt > 0.1f) dt = 0.1f;

		bool longEnough = bodyLen >= width;
		float rate = ArcRate;
		if (longEnough && rate > 0f && (float)fxr.NextDouble() < rate * dt)
		{
			SpawnTendril();
		}

		for (int i = 0; i < tendrils.Length; i++)
		{
			if (!tendrils[i].active) continue;
			float age = time - tendrils[i].birth;
			if (age >= tendrils[i].life || !longEnough)
			{
				tendrils[i].active = false;
				continue;
			}
			DrawTendril(sb, ref tendrils[i], tailPt, axis, perp, bodyLen, time, age);
		}
	}

	// Roll a fresh tendril into a free pool slot (skipped if the pool is somehow full -- never at
	// sane rates). Anchor/side/reach/lean mirror the old per-appearance rolls; life is random in
	// the ArcLife range and drift is a random SIGNED speed (dir + magnitude) up to TendrilSpeed.
	private void SpawnTendril()
	{
		int slot = -1;
		for (int i = 0; i < tendrils.Length; i++) { if (!tendrils[i].active) { slot = i; break; } }
		if (slot < 0) return;
		ArcLifeRange(out float lo, out float hi);
		tendrils[slot].active = true;
		tendrils[slot].birth = lastArcTime;
		tendrils[slot].life = RandF(lo, hi);
		tendrils[slot].seed = RandF(0f, 1000f);
		tendrils[slot].ap = RandF(0.06f, 0.94f);
		tendrils[slot].side = (fxr.NextDouble() < 0.5) ? 1f : -1f;
		tendrils[slot].reach = width * (1.1f + 1.5f * (float)fxr.NextDouble());
		tendrils[slot].lean = width * 1.8f * ((float)fxr.NextDouble() - 0.5f);
		tendrils[slot].drift = RandF(-1f, 1f) * TendrilSpeed;
	}

	// Draw one live tendril: slide its anchor along the beam by drift*age (clamped to the beam
	// span), build the writhing bolt, and stroke a wide dim glow pass + a thin hot core, both
	// fading toward the free end and scaled by the birth->death sin envelope.
	private void DrawTendril(SpriteBatchWrapper sb, ref Tendril tn, Vector2 tailPt, Vector2 axis, Vector2 perp, float bodyLen, float time, float age)
	{
		float env = (float)Math.Sin(age / tn.life * Math.PI);
		if (env <= 0.02f) return;
		float along = bodyLen * tn.ap + tn.drift * age;
		if (along < 0f) along = 0f; else if (along > bodyLen) along = bodyLen;
		Vector2 anchor = tailPt + axis * along;
		Vector2 endPt = anchor + perp * (tn.side * tn.reach) + axis * tn.lean;
		Vector2 d = endPt - anchor;
		float len = d.Length();
		if (len < 1f) return;
		Vector2 bperp = new Vector2(0f - d.Y, d.X) / len;
		float amp = Math.Min(len, tn.reach) * 0.55f;
		int n = BuildBolt(anchor, endPt, bperp, amp, time, tn.seed);
		for (int pass = 0; pass < 2; pass++)
		{
			float thick = (pass == 0) ? ArcThickness * 2.6f : ArcThickness;
			Color col = (pass == 0) ? ArcGlowColor : ArcColor;
			for (int k = 0; k < n - 1; k++)
			{
				float fade = (1f - 0.6f * ((float)k / (float)(n - 1))) * env;
				DrawLine(sb, boltA[k], boltA[k + 1], thick, col * fade);
			}
		}
	}

	// Clear the live tendrils (called when a pooled beam is re-Set up for a new laser, so stale
	// tendrils from the previous use don't flash on the new one).
	private void ResetArcs()
	{
		for (int i = 0; i < tendrils.Length; i++) tendrils[i].active = false;
		lastArcTime = float.NaN;
	}

	// Midpoint-displacement subdivision into boltA[0..return). Each level inserts a displaced
	// midpoint between every pair and HALVES the displacement amplitude, giving smooth fractal
	// jaggedness. Displacement uses a time-driven wiggle (deterministic per seed), so the bolt
	// animates smoothly frame to frame instead of being re-rolled.
	private int BuildBolt(Vector2 start, Vector2 end, Vector2 perpUnit, float amp, float time, float seed)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		Vector2[] cur = boltA;
		Vector2[] nxt = boltB;
		cur[0] = start;
		cur[1] = end;
		int n = 2;
		float a = amp;
		for (int lvl = 0; lvl < ArcLevels; lvl++)
		{
			int m = 0;
			for (int i = 0; i < n - 1; i++)
			{
				nxt[m++] = cur[i];
				Vector2 mid = (cur[i] + cur[i + 1]) * 0.5f;
				mid += perpUnit * (a * Wiggle(time, seed + (float)(lvl * 31 + i) * 2.39f));
				nxt[m++] = mid;
			}
			nxt[m++] = cur[n - 1];
			n = m;
			Vector2[] tmp = cur;
			cur = nxt;
			nxt = tmp;
			a *= 0.5f;
		}
		if (cur != boltA)
		{
			Array.Copy(cur, boltA, n);
		}
		return n;
	}

	// Smooth, deterministic [-1,1] wiggle: two out-of-phase sines so the motion looks organic
	// instead of one obvious oscillation. Driven by time, so it animates without any RNG.
	private static float Wiggle(float time, float seed)
	{
		return 0.6f * (float)Math.Sin(time * 5.5f + seed) + 0.4f * (float)Math.Sin(time * 2.3f + seed * 1.7f);
	}

	private static float RandF(float min, float max)
	{
		return (float)(fxr.NextDouble() * (max - min)) + min;
	}

	public void SetProperties(Vector2 position, float direction, float length, float lead)
	{
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		origin = new Vector3(position.X - 400f, 300f - position.Y, 0f);
		this.direction = convertToVector3(direction);
		height = length;
		this.lead = lead;
		calculatePoints();
		ResetArcs(); // fresh laser: drop any tendrils left over from a recycled beam
	}

	public void SetLead(float lead)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		Vector3 val = direction * (height - lead);
		this.lead = lead;
		lowerLeft = upperLeft - val;
		lowerRight = upperRight - val;
	}

	private void calculatePoints()
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		//IL_0055: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0068: Unknown result type (might be due to invalid IL or missing references)
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		//IL_0077: Unknown result type (might be due to invalid IL or missing references)
		//IL_007c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0083: Unknown result type (might be due to invalid IL or missing references)
		//IL_0089: Unknown result type (might be due to invalid IL or missing references)
		//IL_009b: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ac: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ce: Unknown result type (might be due to invalid IL or missing references)
		left = Vector3.Cross(normal, direction);
		Vector3 val = direction * height + origin;
		upperLeft = val + left * width / 2f;
		upperRight = val - left * width / 2f;
		lowerLeft = upperLeft - direction * (height - lead);
		lowerRight = upperRight - direction * (height - lead);
	}

	public void SetLength(float length)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		Vector3 val = direction * (length - height);
		height = length;
		upperLeft += val;
		upperRight += val;
	}

	public void MoveTo(Vector2 position)
	{
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		origin = new Vector3(position.X - 400f, 300f - position.Y, 0f);
		calculatePoints();
	}

	public void AimAt(float direction)
	{
		//IL_0003: Unknown result type (might be due to invalid IL or missing references)
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		this.direction = convertToVector3(direction);
		calculatePoints();
	}

	private Vector3 convertToVector3(float direction)
	{
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		Vector2 val = default(Vector2);
		(val) = new Vector2(Convert.ToSingle(Math.Cos(direction)), -1f * Convert.ToSingle(Math.Sin(direction)));
		return new Vector3(val, 0f);
	}
}
