using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using EvilAliensWeb.Compat;

namespace EvilAliens;

internal class ShipConnector : AlienDrawableGameComponent
{
	public PlayerShip A;

	public PlayerShip B;

	// --- Live lightning FX (Trello "ship connector too static") -------------------------
	// The connector used to be ONE frozen GFX/Sprites/connector sprite (two orbs + baked
	// crackle) stretched between the ships -- dead-still, especially when two ships stay
	// docked. It now breathes (a brightness pulse on the base sprite) and, over the gap
	// between the ships, draws live fractal lightning: a few continuously-writhing main
	// bolts plus stochastic short crackle tendrils, both additive. Same midpoint-displacement
	// + time-driven Wiggle technique the Quad laser uses (Quad.cs), kept self-contained here
	// (own FX RNG, own scratch) so the connector doesn't drag in the whole beam pipeline. All
	// tunables have baked defaults overridable live via the ?connector* flags / eaConnector panel.
	private const int DefaultBoltCount = 2;      // continuously-writhing main bolts spanning the two ships
	private const float DefaultArcRate = 6f;     // average short crackle tendrils spawned per second
	private const float DefaultJitter = 1f;      // multiplies the bolt zig-zag amplitude
	private const float DefaultPulse = 2.5f;     // breathe frequency (Hz) of the base sprite + orb blooms
	private const float DefaultGlow = 1f;        // orb-bloom intensity/size vs baseline (0 = off)

	private const int ArcLevels = 4;             // midpoint-displacement subdivisions per bolt
	private const float BoltAmpFactor = 0.1f;    // main-bolt zig-zag amplitude as a fraction of the ship gap
	private const float MainGlowThick = 5f;      // main-bolt blue glow-pass thickness (design px)
	private const float MainCoreThick = 1.8f;    // main-bolt white core-pass thickness
	private const float ArcGlowThick = 3.2f;     // crackle-tendril glow-pass thickness
	private const float ArcCoreThick = 1.4f;     // crackle-tendril core-pass thickness
	private const float ArcLifeMin = 0.12f;      // a crackle tendril lives a random [min,max] seconds
	private const float ArcLifeMax = 0.30f;
	private const float ArcReachMin = 8f;        // how far a tendril whips out perpendicular (design px)
	private const float ArcReachMax = 20f;
	private const float OrbBloomDiameter = 40f;  // baseline orb-bloom diameter (design px), x ConnectorGlow

	private static int BoltCount => DebugFlags.ConnectorBoltCount ?? DefaultBoltCount;
	private static float ArcRate => DebugFlags.ConnectorArcRate ?? DefaultArcRate;
	private static float Jitter => DebugFlags.ConnectorJitter ?? DefaultJitter;
	private static float PulseHz => DebugFlags.ConnectorPulse ?? DefaultPulse;
	private static float GlowAmt => DebugFlags.ConnectorGlow ?? DefaultGlow;

	private static readonly Color BoltCore = new Color(215, 240, 255);  // white-hot bolt core
	private static readonly Color BoltGlow = new Color(60, 130, 245);   // electric-blue bolt glow
	// Orb "energy well" stack colours (== the laser chargeup well / Quad beam layers): a blue halo
	// over a cyan-white body over a white-hot core, additively saturating each orb centre to white.
	private static readonly Color OrbHalo = new Color(35, 110, 235);   // blue outer halo
	private static readonly Color OrbBody = new Color(150, 215, 255);  // cyan-white body
	private static readonly Color OrbCore = new Color(210, 235, 255);  // white-hot core

	// FX-only RNG, kept off the gameplay RandomHelper so render-time jitter can't desync a
	// future lockstep co-op session (Stage 11), exactly like Quad's fxr.
	private static readonly Random fxr = new Random();
	// Shared midpoint-displacement scratch (Draw is serial, so one buffer set is safe across
	// every connector -- same as Quad's static boltA/boltB).
	private static readonly Vector2[] boltA = new Vector2[64];
	private static readonly Vector2[] boltB = new Vector2[64];

	private struct Tendril
	{
		public bool active;
		public float birth;
		public float life;
		public float seed;
		public float ap;    // anchor along the gap, 0..1
		public float side;  // which way it whips, +/-1
		public float reach; // perpendicular reach (design px)
	}
	private readonly Tendril[] tendrils = new Tendril[16];
	private readonly float fxPhase = (float)(fxr.NextDouble() * 1000.0);
	private float fxTime;
	private float lastArcTime = float.NaN;

	private Texture2D lineTex;  // GFX/Sprites/lazermiddle -- the thin glowing strip for bolt segments
	private Texture2D glowTex;  // GFX/Sprites/lazerglow  -- the radial bloom for the orbs

	// --- Online co-op tether (card 11.3) -------------------------------------------------
	// Offline the connector RIGIDLY pins both ships at midpoint +/-39px (SetPosition in
	// Update). Online that would fight the interpolation buffer driving the remote puppet
	// and rubber-band the local ship to a ~100ms-stale anchor. Instead each peer applies a
	// SOFT pull to its OWN ship only, toward the puppet's on-screen position: a FIRST-ORDER
	// positional step (no velocity state -> cannot self-oscillate). The one instability
	// channel is the mutual stale-anchor loop; constants are picked/validated by
	// tools/sim/tether_sim.py (overdamped up to 300ms one-way + the interp delay).
	// If it ever wobbles under a real transport: SOFTEN NetPullK, never stiffen.
	private const float NetRestPx = 78f;         // 2 x the 39px docking separation
	private const float NetPullK = 0.0018f;      // per ms: fraction of excess stretch recovered
	private const float NetMaxPullPxPerMs = 0.22f; // clamp below ship MaxSpeed 0.33 -> you can always fight it

	// Sprite-harness mode (?harness=connector). The real connector needs two live PlayerShips as
	// endpoints; the frozen harness has none, so instead we derive the two orbs from this component's
	// own Position/rotation (which HarnessScene drives) at a fixed half-gap. The FX still animate
	// because fxTime advances in Draw, not Update. Off in normal play (byte-identical).
	internal bool HarnessMode;
	private const float HarnessHalfGap = 39f;  // matches the live ±39px docking separation

	public override ICollisionType CollisionType
	{
		get
		{
			CollisionBox collisionBox = retrieveBoundsFromTexture();
			collisionBox.TopLeft = collisionBox.TopLeft * 0.8f + base.Position;
			collisionBox.BottomRight = collisionBox.BottomRight * 0.8f + base.Position;
			return collisionBox;
		}
	}

	public ShipConnector(Game game)
		: base(game)
	{
		LoadAnimation(new AnimationData("GFX/Sprites/connector"));
		base.DrawOrder = 11;
	}

	public override void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		base.OnComponentRemoved(e);
		if (e.GameComponent == A)
		{
			A = null;
		}
		if (e.GameComponent == B)
		{
			B = null;
		}
	}

	public static ShipConnector NewAlien(ComponentBin collection, Game game)
	{
		ShipConnector shipConnector = collection.Recycle<ShipConnector>();
		if (shipConnector == null)
		{
			shipConnector = new ShipConnector(game);
		}
		return shipConnector;
	}

	public void Setup(PlayerShip A, PlayerShip B)
	{
		this.A = A;
		this.B = B;
		Vector2 position = A.Position;
		Vector2 position2 = B.Position;
		float num = MyMath.VectorToAngle(position2 - position);
		rotation = num;
		base.Position = position + (position2 - position) * 0.5f;
		// Fresh (or recycled) connector: drop any tendrils left from a previous docking so
		// they don't flash on the new link.
		for (int i = 0; i < tendrils.Length; i++)
		{
			tendrils[i].active = false;
		}
		lastArcTime = float.NaN;
	}

	// Sprite-harness factory (?harness=connector): no ships, endpoints derived from Position/rotation.
	public void HarnessSetup(Vector2 pos)
	{
		HarnessMode = true;
		base.Position = pos;
		rotation = 0f;
	}

	public override void Initialize()
	{
		color = new Color(new Vector4(1f, 1f, 1f, 0.65f));
		base.Initialize();
		// lazermiddle (thin strip) + lazerglow (radial bloom) drive the live lightning. Both are
		// already loaded by GameScene.LoadContent / the TeamChallenge preload; content.Load caches,
		// so this is a hit, and loading here guarantees availability for any multiplayer scene.
		lineTex = content.Load<Texture2D>("GFX/Sprites/lazermiddle");
		glowTex = content.Load<Texture2D>("GFX/Sprites/lazerglow");
	}

	public override void Draw(GameTime gameTime)
	{
		// Cosmetic FX advance on RAW draw time (like the metal sheen / brain overlays) so the
		// lightning keeps crackling through a hit-stop freeze.
		float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
		if (dt < 0f) dt = 0f; else if (dt > 0.1f) dt = 0.1f;
		fxTime += dt;

		// Base sprite: keep its straight-alpha look but breathe its brightness so even the baked
		// art shimmers instead of sitting dead-still.
		float pulse = 0.85f + 0.15f * (float)Math.Sin(fxTime * PulseHz * (float)Math.PI * 2f + fxPhase);
		color = new Color(new Vector4(pulse, pulse, pulse, 0.65f));
		base.Draw(gameTime);

		Vector2 pA;
		Vector2 pB;
		if (HarnessMode)
		{
			// Endpoints from our own Position/rotation (the harness drives both); no ships needed.
			Vector2 dir = MyMath.AngleToVector(rotation);
			pA = base.Position - dir * HarnessHalfGap;
			pB = base.Position + dir * HarnessHalfGap;
		}
		else
		{
			if (A == null || B == null)
			{
				return;
			}
			pA = A.Position;
			pB = B.Position;
		}
		DrawLightning(dt, pA, pB);
	}

	// Draws the live electricity over the gap between the two ships: N writhing main bolts, a
	// pool of stochastic crackle tendrils, and a throbbing bloom on each orb. All additive.
	private void DrawLightning(float dt, Vector2 pA, Vector2 pB)
	{
		Vector2 delta = pB - pA;
		float gap = delta.Length();
		if (gap < 1f)
		{
			return;
		}
		Vector2 axis = delta / gap;
		Vector2 perp = new Vector2(0f - axis.Y, axis.X);

		SpriteBlendMode oldMode = spriteBatch.BlendMode;
		spriteBatch.BlendMode = SpriteBlendMode.Additive;

		// Orb "energy wells": each ship gets a churning glow built like the laser chargeup's well
		// (LazerGenerator.DrawWell) -- a STACK of additive glows (blue halo -> cyan-white body ->
		// white-hot core) whose layers each shimmer on their own incommensurate sines, so the orbs
		// roil with gathering energy instead of sitting as a flat disc. The two orbs carry
		// decorrelated phases so they don't pulse in lockstep.
		float glow = GlowAmt;
		if (glow > 0f)
		{
			DrawEnergyOrb(pA, glow, fxPhase);
			DrawEnergyOrb(pB, glow, fxPhase + 41.7f);
		}

		// Main bolts: a handful of persistent fractal arcs spanning the gap, each writhing
		// smoothly via its own time-driven seed, with a subtle brightness flicker so they don't
		// read as a fixed shape.
		float amp = gap * BoltAmpFactor * Jitter;
		int bolts = BoltCount;
		for (int i = 0; i < bolts; i++)
		{
			float seed = fxPhase + (float)i * 17.31f;
			float flicker = 0.72f + 0.28f * Math.Abs(Wiggle(fxTime * 1.7f, seed));
			int n = BuildBolt(pA, pB, perp, amp, fxTime, seed);
			StrokeBolt(n, MainGlowThick, BoltGlow * flicker, MainCoreThick, BoltCore * flicker);
		}

		// Crackle tendrils: short offshoots that spawn stochastically and whip out, so the link
		// spits sparks "all over" rather than on a fixed cadence (the Quad laser's approach).
		AdvanceTendrils(dt);
		for (int i = 0; i < tendrils.Length; i++)
		{
			if (!tendrils[i].active)
			{
				continue;
			}
			float age = fxTime - tendrils[i].birth;
			if (age >= tendrils[i].life)
			{
				tendrils[i].active = false;
				continue;
			}
			DrawTendril(ref tendrils[i], pA, axis, perp, gap, age);
		}

		spriteBatch.BlendMode = oldMode;
	}

	// Spawn (stochastically) + expire the crackle-tendril pool. Rate*dt is the Bernoulli spawn
	// probability per frame (RandomHelper.RandomFromAverage model), on the FX RNG.
	private void AdvanceTendrils(float dt)
	{
		float rate = ArcRate;
		if (rate > 0f && (float)fxr.NextDouble() < rate * dt)
		{
			int slot = -1;
			for (int i = 0; i < tendrils.Length; i++)
			{
				if (!tendrils[i].active)
				{
					slot = i;
					break;
				}
			}
			if (slot >= 0)
			{
				tendrils[slot].active = true;
				tendrils[slot].birth = fxTime;
				tendrils[slot].life = RandF(ArcLifeMin, ArcLifeMax);
				tendrils[slot].seed = RandF(0f, 1000f);
				tendrils[slot].ap = RandF(0.12f, 0.88f);
				tendrils[slot].side = (fxr.NextDouble() < 0.5) ? 1f : -1f;
				tendrils[slot].reach = RandF(ArcReachMin, ArcReachMax);
			}
		}
	}

	// One crackle tendril: a short writhing bolt from an anchor on the gap axis out to a
	// perpendicular tip, faded in/out by a birth->death sin envelope.
	private void DrawTendril(ref Tendril tn, Vector2 pA, Vector2 axis, Vector2 perp, float gap, float age)
	{
		float env = (float)Math.Sin(age / tn.life * Math.PI);
		if (env <= 0.03f)
		{
			return;
		}
		Vector2 anchor = pA + axis * (gap * tn.ap);
		Vector2 tip = anchor + perp * (tn.side * tn.reach);
		Vector2 d = tip - anchor;
		float len = d.Length();
		if (len < 1f)
		{
			return;
		}
		Vector2 bperp = new Vector2(0f - d.Y, d.X) / len;
		float amp = tn.reach * 0.5f;
		int n = BuildBolt(anchor, tip, bperp, amp, fxTime, tn.seed);
		// fade toward the free tip, scaled by the envelope
		StrokeBoltFading(n, ArcGlowThick, BoltGlow, ArcCoreThick, BoltCore, env);
	}

	// Stroke a built bolt (boltA[0..n)) as a wide dim glow pass then a thin hot core pass, at
	// uniform brightness along its length.
	private void StrokeBolt(int n, float glowThick, Color glowCol, float coreThick, Color coreCol)
	{
		for (int k = 0; k < n - 1; k++)
		{
			DrawLine(boltA[k], boltA[k + 1], glowThick, glowCol);
		}
		for (int k = 0; k < n - 1; k++)
		{
			DrawLine(boltA[k], boltA[k + 1], coreThick, coreCol);
		}
	}

	// Stroke a built bolt fading toward its free end (k -> n) and scaled by env -- for the
	// short crackle tendrils that taper off as they whip out.
	private void StrokeBoltFading(int n, float glowThick, Color glowCol, float coreThick, Color coreCol, float env)
	{
		for (int pass = 0; pass < 2; pass++)
		{
			float thick = (pass == 0) ? glowThick : coreThick;
			Color col = (pass == 0) ? glowCol : coreCol;
			for (int k = 0; k < n - 1; k++)
			{
				float fade = (1f - 0.55f * ((float)k / (float)(n - 1))) * env;
				DrawLine(boltA[k], boltA[k + 1], thick, col * fade);
			}
		}
	}

	// Midpoint-displacement subdivision into boltA[0..return). Each level inserts a displaced
	// midpoint between every pair and halves the amplitude -> smooth fractal jaggedness; the
	// displacement is a time-driven Wiggle (deterministic per seed) so the bolt animates
	// smoothly frame to frame instead of strobing. Mirrors Quad.BuildBolt.
	private int BuildBolt(Vector2 start, Vector2 end, Vector2 perpUnit, float amp, float time, float seed)
	{
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

	// A thin glowing line p0->p1 as one stretched strip of the lazermiddle texture.
	private void DrawLine(Vector2 p0, Vector2 p1, float thickness, Color color)
	{
		Vector2 d = p1 - p0;
		float len = d.Length();
		if (len < 0.5f)
		{
			return;
		}
		float rot = (float)Math.Atan2(0f - d.X, d.Y);
		Vector2 scale = new Vector2(thickness / (float)lineTex.LogicalWidth(), len / (float)lineTex.LogicalHeight());
		spriteBatch.Draw(lineTex, (p0 + p1) * 0.5f, rot, scale, center: true, color);
	}

	// One orb drawn as a layered, shimmering "energy well" (mirrors LazerGenerator.DrawWell): a wide
	// blue halo, a cyan-white body and a white-hot core, each additive and each wobbling on its own
	// incommensurate sines (outer halo wobbles most/fastest, core smoothest) so the orb roils rather
	// than sitting flat. A shared slow breathe (tied to the ?connectorpulse knob) + a core glare give
	// the overall pump. `phase` decorrelates the two orbs.
	private void DrawEnergyOrb(Vector2 center, float glow, float phase)
	{
		float baseD = OrbBloomDiameter * glow;
		float breathe = 1f + 0.16f * (float)Math.Sin(fxTime * PulseHz * (float)Math.PI * 2f + phase);
		float wHalo = Wobble(0.22f, 2.3f, phase);
		float wBody = Wobble(0.14f, 1.7f, phase * 1.7f);
		float wCore = Wobble(0.08f, 1.2f, phase * 2.3f);
		// core glares hard so the orb visibly throbs hot (dominates the sprite's static painted core)
		float glare = 0.55f + 0.45f * (float)Math.Sin(fxTime * 3.1f + phase * 1.3f);
		DrawGlow(center, baseD * 1.35f * breathe * wHalo, OrbHalo * (0.55f * glow));
		DrawGlow(center, baseD * 0.85f * breathe * wBody, OrbBody * (0.50f * glow));
		DrawGlow(center, baseD * 0.45f * breathe * wCore, OrbCore * (0.62f * glow * glare));
	}

	// A gentle +/-amp size shimmer: two low, incommensurate sines -> a smooth organic wander (not a
	// jitter). Bigger amp/fscale => more erratic; each layer/orb passes a different phase to decorrelate.
	private float Wobble(float amp, float fscale, float phase)
	{
		float t = fxTime;
		return 1f + amp * (0.6f * (float)Math.Sin(t * 3.3f * fscale + phase)
			+ 0.4f * (float)Math.Sin(t * 5.1f * fscale + phase * 1.9f));
	}

	// Radial bloom of the lazerglow texture to ~diameterPx (it's radial, so rotation is moot).
	private void DrawGlow(Vector2 center, float diameterPx, Color color)
	{
		float s = diameterPx / (float)glowTex.LogicalWidth();
		spriteBatch.Draw(glowTex, center, 0f, new Vector2(s, s), center: true, color);
	}

	// Smooth, deterministic [-1,1] wiggle: two out-of-phase sines so the motion looks organic.
	private static float Wiggle(float time, float seed)
	{
		return 0.6f * (float)Math.Sin(time * 5.5f + seed) + 0.4f * (float)Math.Sin(time * 2.3f + seed * 1.7f);
	}

	private static float RandF(float min, float max)
	{
		return (float)(fxr.NextDouble() * (max - min)) + min;
	}

	public override void Update(GameTime gameTime)
	{
		if (A == null || B == null)
		{
			Die();
			if (A != null)
			{
				A.TemporaryInvulnerability();
			}
			if (B != null)
			{
				B.TemporaryInvulnerability();
			}
			A = null;
			B = null;
			// An endpoint vanished under us -- make sure the peer's tether goes too
			// (idempotent; usually its own endpoint edge already broke it).
			EvilAliensWeb.Compat.Net.NetSession.OnTetherBreak();
		}
		else if (EvilAliensWeb.Compat.Net.NetSession.Active)
		{
			// Base sprite between the two ON-SCREEN ships (staleness reads as elastic
			// stretch); the soft pull only ever moves the ship WE own.
			Vector2 position = A.Position;
			Vector2 position2 = B.Position;
			rotation = MyMath.VectorToAngle(position2 - position);
			base.Position = position + (position2 - position) * 0.5f;
			NetPullOwnShip(gameTime);
			base.Update(gameTime);
		}
		else
		{
			Vector2 position = A.Position;
			Vector2 position2 = B.Position;
			float angle = (rotation = MyMath.VectorToAngle(position2 - position));
			base.Position = position + (position2 - position) * 0.5f;
			A.SetPosition(base.Position - MyMath.AngleToVector(angle) * 39f);
			B.SetPosition(base.Position + MyMath.AngleToVector(angle) * 39f);
			base.Update(gameTime);
		}
	}

	// First-order clamped pull on the locally-owned endpoint (see the Net* consts above).
	private void NetPullOwnShip(GameTime gameTime)
	{
		PlayerShip own;
		PlayerShip anchor;
		if (A.Controller != ControlDevice.Remote)
		{
			own = A;
			anchor = B;
		}
		else if (B.Controller != ControlDevice.Remote)
		{
			own = B;
			anchor = A;
		}
		else
		{
			return;
		}
		float dtMs = (float)gameTime.ElapsedGameTime.TotalMilliseconds;
		Vector2 d = anchor.Position - own.Position;
		float dist = d.Length();
		if (dist <= NetRestPx || dtMs <= 0f)
		{
			return;
		}
		float step = Math.Min(NetPullK * (dist - NetRestPx), NetMaxPullPxPerMs) * dtMs;
		own.SetPosition(own.Position + d / dist * step);
	}

	public override void CollidesWith(ICollidable other)
	{
		base.CollidesWith(other);
	}

	public void TakeHit()
	{
		// Null-safe: a peer's EvTetherBreak can null A/B a tick before the ships'
		// connectors lists clear, and a local hit can land in that window.
		if (A == null && B == null)
		{
			return;
		}
		Die();
		A?.TemporaryInvulnerability();
		B?.TemporaryInvulnerability();
		A = null;
		B = null;
		// Or-of-either-peer break: this side saw the hit; the peer breaks silently.
		EvilAliensWeb.Compat.Net.NetSession.OnTetherBreak();
	}

	// The PEER broke the tether (EvTetherBreak): same break, no echo back.
	internal void NetBreakSilently()
	{
		if (A == null && B == null)
		{
			return;
		}
		Die();
		if (A != null)
		{
			A.TemporaryInvulnerability();
		}
		if (B != null)
		{
			B.TemporaryInvulnerability();
		}
		A = null;
		B = null;
	}
}
