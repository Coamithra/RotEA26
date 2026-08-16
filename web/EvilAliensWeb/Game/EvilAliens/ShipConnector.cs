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
	// WorldTime reading at the last Draw, so fxTime advances on the world's clock rather
	// than raw draw time (card d79a2f48). Negative until the first Draw seeds it.
	private float lastWorldSeconds = -1f;
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
	//
	// The pull applies ONLY when an endpoint is a net PUPPET. Two LOCALLY-owned ships inside a
	// net session (couch co-op, ?netlocal=) take the rigid offline law instead -- there is no
	// staleness between two ships this peer simulates, so there is nothing to be soft about, and
	// the soft law moved only ONE of the two (see NetPullOwnShip's endpoint pick), which ran away
	// on its own. On the other peer both of those ships are puppets, so its NetPullOwnShip returns
	// early and the rigid positions arrive over the wire unchallenged -- no peer fights another.
	private const float NetRestPx = 78f;         // 2 x the 39px docking separation
	private const float NetPullK = 0.0018f;      // per ms: fraction of excess stretch recovered
	private const float NetMaxPullPxPerMs = 0.22f; // clamp below ship MaxSpeed 0.33 -> you can always fight it

	// --- The hard cap (card 2cfab019) ----------------------------------------------------
	// The soft law above is UNBOUNDED in separation, and the card ("ships can fly further and
	// further away from each other") is that runaway. It is a GAIN problem, not a latency
	// problem -- measured identical at one-way 0/50/100/200/300ms:
	//
	//   * ONE player thrusting away is already BOUNDED: the idle partner's own 0.22 pull makes up
	//     the 0.11px/ms shortfall (gap rate 0.33 - 2p = 0 -> p = 0.165 -> d = 78 + 0.165/0.0018
	//     = 169.7px PERCEIVED, 166.9px measured true -- the same discrete-tick offset as the
	//     220/214.5 pair below, so do NOT read the formula as producing the measured figure).
	//     That case was never broken, which is why the card's guess
	//     ("only the host moves itself back towards the client") is not the cause -- both peers
	//     always ran this, and each always pulled only the ship it owns.
	//   * The runaway needs the pull budget to be ONE-SIDED: BOTH players thrusting apart (the
	//     gap grows at 2 x (0.33 - 0.22) = 0.22px/ms forever), or one thrusting while the partner
	//     cannot move toward it -- pinned against the 800x600 clamp in PlayerShip.Update, which is
	//     the everyday trigger, since backing into a corner is the natural reaction to being
	//     dragged.
	//
	// THE CAP IS A RATE, NOT A POSITION CLAMP, and that is the whole design. A true clamp
	// (`if (dist > MAX) SetPosition(anchor +/- MAX)`) has loop gain EXACTLY 1: in steady clamp
	// x_A(t) = x_B(t-D) - MAX and x_B(t) = x_A(t-D) + MAX substitute to x_A(t) = x_A(t-2D), a pure
	// delay at unity gain -- marginally stable, so any perturbation persists forever as a
	// 2D-period oscillation with the two peers fighting each other. That is precisely the mutual
	// stale-anchor loop the block above warns about, so the cap is NOT built that way.
	//
	// Instead the pull's SPEED ceiling becomes distance-dependent: past NetHardPx it rises above
	// PlayerShip.ShipMaxSpeed, so thrust is OUT-RUN rather than refused. Separation then has a
	// hard equilibrium at NetHardPx + (ShipMaxSpeed - NetMaxPullPxPerMs)/NetHardK = 220px
	// perceived (~214.5px true -- the 5.5px is one tick of own-ship travel between the anchor
	// sample and the pull). Still first-order, still no velocity state, still one continuous
	// monotone function of dist; per-tick loop gain in the hard band is NetHardK * dt = 0.092, two
	// orders below a clamp's 1. Measured over one-way 0/50/100/200/300ms: the bound is IDENTICAL
	// at every latency (a speed equilibrium has no position memory to ring with), zero direction
	// reversals after input stops, and the settle after release is not worse than the shipped law.
	// NetPullK and NetMaxPullPxPerMs are UNTOUCHED and the knee is derived from where the soft law
	// saturates, so below 200px this is bit-for-bit the pre-card behaviour -- the "soften, never
	// stiffen" instruction above is respected literally.
	//
	// One honest caveat, measured rather than assumed. The law is a function of the PERCEIVED
	// separation, and in a steady drag both ships travel at the same speed v, so each peer's stale
	// anchor is displaced by v * (one-way + interp) ALONG the direction of travel: the LEADING
	// peer perceives true + v*delay, the trailing one true - v*delay. Past ~200ms one-way that is
	// ~+/-64px, enough for the leader to read past the knee while the TRUE gap is only ~162px --
	// so at high latency the cap does engage in an ordinary drag, on a stale reading. It is
	// bounded, it does not ring, and it acts in the TIGHTENING direction (a 300ms-one-way drag
	// settles at 162px instead of 181px, i.e. toward the 78px rest), so it is accepted rather than
	// designed away. Up to 100ms one-way -- where a real session lives -- nothing moves at all,
	// and tether_sim.py asserts exactly that split.
	//
	// NOT gated on peer freshness, deliberately. Against a STALLED peer the anchor freezes (the
	// jitter buffer extrapolates 250ms then holds), and a pull toward a frozen anchor is a
	// CONTRACTION with a fixed point at NetRestPx, not an integrator: total travel is exactly
	// dist - NetRestPx however long the stall runs (measured 136.5px, identical with the cap and
	// without -- the cap changes the speed of that fixed journey, never its length or its
	// destination). That is categorically unlike ShipStateBuffer's ExtrapolateCapMs or
	// Lazer.NetExtrapolateCapMs, which bound `pos + vel*t` integrators that diverge linearly in
	// time and therefore must be bounded in TIME. Gating the hard band on freshness would instead
	// restore the runaway for the whole 1200ms PeerStallMs grace, which is when it is most likely:
	// measured over one stall, the shipped law adds 264px of escape and keeps going, while the cap
	// holds the pair and leaves a ~20px correction on recovery.
	//
	// The cost, and it is the user's ruling ("soft, then hard cap"): past NetHardPx the pull
	// exceeds ship max speed, so you can no longer fight it there. That is 2.6x the rest length;
	// ordinary play sits at the ~167px drag equilibrium and never reaches it.
	private const float NetHardPx = 200f;        // knee. NetRestPx + NetMaxPullPxPerMs/NetPullK = 200.2px,
	                                             // i.e. exactly where the soft cap saturates and stops
	                                             // responding to separation at all -- the wall begins
	                                             // precisely where the runaway becomes possible, which is
	                                             // what makes "unchanged below the knee" exact rather
	                                             // than approximate.
	private const float NetHardK = 0.0055f;      // per ms: ~20px of give between the knee and the equilibrium
	// Absolute ceiling on the pull. NOT a tuned feel value -- a guard, and it DOES bind: an online
	// TeamChallenge creates its scripted tether once BOTH ships exist (netConnectorPending), and the
	// two ships enter from fixed off-screen points 567-696px apart, where the raw hard term would ask
	// for 37-49px per FRAME. 0.55px/ms is 1.67x ShipMaxSpeed -- deliberately under the 2x that
	// NetSession's own correction-pop detector calls "a step no real ship could make" -- so the
	// reel-in is fast (696 -> 220px in ~883ms) but still reads as ship motion. Offline the same
	// spawn is slammed together rigidly on frame 1, so this is strictly the gentler of the two.
	private const float NetMaxHardPullPxPerMs = 0.55f;

	// Sprite-harness mode (?harness=connector). The real connector needs two live PlayerShips as
	// endpoints; the frozen harness has none, so instead we derive the two orbs from this component's
	// own Position/rotation (which HarnessScene drives) at a fixed half-gap. The FX still animate
	// because fxTime advances in Draw, not Update, and the harness freezes the object with
	// Enabled=false rather than a pause layer, so the world clock it now reads keeps running.
	// Off in normal play (byte-identical).
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
		Vector2 aPosition = A.Position;
		Vector2 bPosition = B.Position;
		float num = MyMath.VectorToAngle(bPosition - aPosition);
		rotation = num;
		base.Position = aPosition + (bPosition - aPosition) * 0.5f;
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
		// Cosmetic FX advance on the WORLD's clock (Compat/WorldTime, card d79a2f48) rather than
		// raw draw time: this is a Draw-side clock on a component whose Update a pause freezes, so
		// on the raw one the connector kept crackling while the two ships it joins sat still. The
		// delta since the last Draw is zero under a pause and scaled by the 1-up slow-mo, and the
		// sprite harness is unaffected (it freezes the object with Enabled=false, not a pause
		// layer, so the world clock keeps running there).
		// It no longer crackles through a HIT-STOP, unlike BombRipple: a bomb ring is a
		// travelling wave that reads as a dropped frame if it stops, this is an idle ambience.
		float dt = (lastWorldSeconds < 0f) ? 0f : WorldTime.Seconds - lastWorldSeconds;
		lastWorldSeconds = WorldTime.Seconds;
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
		else
		{
			Vector2 aPosition = A.Position;
			Vector2 bPosition = B.Position;
			float angle = (rotation = MyMath.VectorToAngle(bPosition - aPosition));
			base.Position = aPosition + (bPosition - aPosition) * 0.5f;
			// Only a PUPPET endpoint needs the soft law. Two locally-owned ships inside a net
			// session have no staleness between them, so they take the rigid offline law -- see
			// the NetRestPx block's second paragraph.
			if (EvilAliensWeb.Compat.Net.NetSession.Active && (A.IsNetPuppet || B.IsNetPuppet))
			{
				// Base sprite between the two ON-SCREEN ships (staleness reads as elastic
				// stretch); the soft pull only ever moves the ship WE own.
				NetPullOwnShip(gameTime);
			}
			else
			{
				A.SetPosition(base.Position - MyMath.AngleToVector(angle) * 39f);
				B.SetPosition(base.Position + MyMath.AngleToVector(angle) * 39f);
			}
			base.Update(gameTime);
		}
	}

	// The tether's pull SPEED at a given separation, in design px per ms: the gentle first-order
	// recovery of the excess stretch, capped at NetMaxPullPxPerMs, plus -- past NetHardPx -- the
	// hard band that out-runs ship thrust and so bounds the separation. Continuous and monotone
	// in dist by construction (the soft term saturates at 200.2px, so the band's own term starts
	// from ~0 there). Pure and static so the headless logic oracle can call the REAL law:
	// tools/sim/logic_probe -> ProbeTetherWall.
	internal static float NetPullSpeedPxPerMs(float dist)
	{
		float soft = Math.Min(NetPullK * (dist - NetRestPx), NetMaxPullPxPerMs);
		if (dist <= NetHardPx || !DebugFlags.NetTetherWall)
		{
			return soft;
		}
		return Math.Min(soft + NetHardK * (dist - NetHardPx), NetMaxHardPullPxPerMs);
	}

	// First-order clamped pull on the locally-owned endpoint (see the Net* consts above).
	private void NetPullOwnShip(GameTime gameTime)
	{
		PlayerShip own;
		PlayerShip anchor;
		// !IsNetPuppet, not `Controller != Remote`: a RemoteFriend is a host-driven puppet too,
		// and treating one as the ship we own would have this peer moving a ship whose position
		// the wire is authoritative for.
		if (!A.IsNetPuppet)
		{
			own = A;
			anchor = B;
		}
		else if (!B.IsNetPuppet)
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
		float step = NetPullSpeedPxPerMs(dist) * dtMs;
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
