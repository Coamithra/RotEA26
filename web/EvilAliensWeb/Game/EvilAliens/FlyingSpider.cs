using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using EvilAliensWeb.Compat;

namespace EvilAliens;

internal class FlyingSpider : KillableAlien
{
	// Per-frame wing-root anchor: DESIGN-space offset from the body centre (scaled by `scale`),
	// indexed by loop frame (0 = FirstFrame). Authored in tools/upscale/wing_editor.html so the
	// flapping wings stay glued to the reared body's back as the 9-frame loop (sheet 22..30)
	// plays. Re-run that tool to retune.
	private static readonly Vector2[] WingAnchors =
	{
		new Vector2(21.47f, 2.71f),  // loop 0 (sheet frame 22)
		new Vector2(20.49f, 3.44f),  // loop 1 (sheet frame 23)
		new Vector2(19.75f, 3.93f),  // loop 2 (sheet frame 24)
		new Vector2(19.51f, 4.18f),  // loop 3 (sheet frame 25)
		new Vector2(19.51f, 4.42f),  // loop 4 (sheet frame 26)
		new Vector2(19.51f, 4.42f),  // loop 5 (sheet frame 27)
		new Vector2(20.00f, 3.69f),  // loop 6 (sheet frame 28)
		new Vector2(20.74f, 3.44f),  // loop 7 (sheet frame 29)
		new Vector2(21.96f, 2.95f),  // loop 8 (sheet frame 30)
	};

	// Flying-spider size (Trello: "make the flying spiders slightly smaller since their graphic is
	// now larger with the stance"). The port reuses the reared-up HD sheet (frames 22..30) instead
	// of the OG 1x4 crawl sheet, which draws taller and a touch wider — measured on-screen silhouette
	// ~147x174 design px vs the OG's ~122x93 — so the enemy reads bigger than in the XBLIG. This
	// factor multiplies BOTH the foreground (1.0) and background (0.67) base scales to bring it back
	// toward the original; 0.75 was dialled in by eye in live Level 2 play (0.85 still read too big
	// in-game — the reared stance is inherently taller/wider than the OG crawl sheet). Both the sprite AND its box hitbox (sized off
	// the frame via DrawScale in retrieveBoundsFromTexture) shrink together, so collision keeps
	// tracking the visible size. (Update already scales the vertical swivel amplitude by `scale`,
	// so a smaller spider also bobs proportionally less — intended, and true of the OG coupling too.)
	// Live-tune by eye with ?flyspiderscale=<f> (null => this default); once the value feels right,
	// update this constant. See Compat/DebugFlags.cs.
	public const float DefaultSizeFactor = 0.75f;

	private static float SizeFactor =>
		EvilAliensWeb.Compat.DebugFlags.FlySpiderScale ?? DefaultSizeFactor;

	// The bottom of the fog band: a BACKGROUND spider's rest height is held above this, so the
	// distant layer stays up near the Mars hills instead of wandering down over the play field.
	// Foreground spiders use the full 475 height.
	private const float BackgroundBandBottom = 350f;

	private bool isbackground;

	private Texture2D wing;

	private Timer swiveltimer = new Timer(2700f, repeating: true);

	private Timer flaptimer = new Timer(120f, repeating: true);

	private float startheight;

	// Net puppet only: forces Initialize's random grey-tint pick (foreground spiders only;
	// background ones are overridden to the fog colour) onto the host's choice. null in normal
	// play => the random pick. See NetForceColor.
	private byte? netForcedColorIndex;

	// Net puppet only (card c1a38ef9): the host's path anchor, forced onto Initialize's own
	// random entry height and Randomize()d swivel phase. null in normal play => the rolls stand.
	// See NetForceAnchor.
	private float? netForcedStartHeight;

	private float? netForcedSwivelPhase;

	// ?flyspidercount= bench only: this spider's slot in the pinned grid, set before bin.Add so
	// Initialize can place + freeze it. null in normal play => the random entry position and the
	// level's real crossing speed. See SetupBench / Level2.PopulateFlyingSpidersOnly.
	private int? benchIndex;

	private int benchCount;

	public override ICollisionType CollisionType
	{
		get
		{
			CollisionBox collisionBox = retrieveBoundsFromTexture();
			collisionBox.TopLeft *= 0.95f;
			collisionBox.BottomRight *= 0.95f;
			collisionBox.TopLeft += base.Position;
			collisionBox.BottomRight += base.Position;
			return collisionBox;
		}
	}

	public FlyingSpider(Game game)
		: base(game)
	{
		// Reuse the grounded spider's 7x7 rear-up sheet, looping only its "reared" sub-range
		// (packed frames 22..30 = source 44..60 at half fps) via FirstFrame/LastFrame. No
		// separate flying sheet -- the shared sheet carries the HD body; the wings (below) add
		// the flight motion. (Old code sliced this same name as a 1x4 crawl, which broke once the
		// sheet was repurposed to the 49-frame rear-up.)
		LoadAnimation(new AnimationData("GFX/Sprites/spider_sheet2", 7, 7, 1, 12f, 22, 31));
		base.DrawOrder = 20;
		interpolationOptions = InterpolationOptions.never;
		SetHitPoints(2, scaleWithDifficulty: false);
		PointValue = 100f;
		timers.Add(flaptimer);
		timers.Add(swiveltimer);
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		wing = content.Load<Texture2D>("GFX/Sprites/wing1");
	}

	public static FlyingSpider NewFlyingSpider(ComponentBin collection, Game game)
	{
		FlyingSpider flyingSpider = collection.Recycle<FlyingSpider>();
		if (flyingSpider == null)
		{
			flyingSpider = new FlyingSpider(game);
		}
		return flyingSpider;
	}

	// The per-spawn reset seam: every spawn path calls this before bin.Add, and the instance may
	// have come out of the recycle pool with a previous life's settings still on it. So anything
	// an OPTIONAL later setter writes has to be cleared here, not just defaulted at construction:
	// a spider recycled out of a ?flyspidercount= bench would otherwise keep benchIndex and be
	// re-pinned by Initialize -- and a pinned spider can neither cross off-screen nor be shot
	// (Speed 0, Collides false), so a later Level 2 inherits permanently frozen immortal scenery.
	// netForcedColorIndex is the same shape (a recycled net puppet keeping the host's forced tint
	// in a local game). Both setters that can follow -- SetupBench and NetForceColor -- run AFTER
	// this call on every path.
	public void Setup(bool isbackground)
	{
		this.isbackground = isbackground;
		benchIndex = null;
		benchCount = 0;
		netForcedColorIndex = null;
		netForcedStartHeight = null;
		netForcedSwivelPhase = null;
		// NewFlyingSpider RECYCLES, so a puppet reusing a dead one's instance would otherwise
		// inherit its half-spent phase correction and its last amplitude -- and spend that
		// correction against the NEW wasp's path over the next 250 ms, on a collidable hitbox.
		// The same trap NetForceColor's note describes, and Lazer.NetResetExtrapolation's.
		netSwivelAmplitude = 50f;
		netSwivelAmplitudeTarget = 50f;
		netSwivelPhaseError = 0f;
	}

	// ?flyspidercount= bench (card 9c92962e): pin this spider to slot `index` of `count` instead of
	// letting it enter at a random height and cross the screen. Call BEFORE bin.Add — Initialize
	// reads it, and ComponentBin.Add runs Initialize synchronously (tools/audit_add_order.py).
	public void SetupBench(int index, int count)
	{
		benchIndex = index;
		benchCount = count;
	}

	public override void Initialize()
	{
		base.Initialize();
		// Pinned on the two A/B rigs, rolled in live play -- see PosePinned below.
		if (!PosePinned)
		{
			flaptimer.Randomize();
		}
		base.Position = new Vector2(850f, RandomHelper.RandomNextFloat(0f, 475f));
		base.Direction = (float)Math.PI;
		base.MaxSpeed = base.Speed;
		rotation = PosePinned
			? 0f
			: RandomHelper.RandomNextFloat(-(float)Math.PI / 32f, (float)Math.PI / 32f);
		// A pinned rig cannot roll its tint either: the three are grossly different brightnesses,
		// so a rolled one alone would make two boots incomparable. A BENCH spider then gets its
		// own tint back from the grid in ApplyBenchPlacement, which is where the row and column
		// that decorrelate it are known; this leaves the harness (one spider, no grid) on
		// DarkGray. Background spiders overwrite the tint outright a few lines down.
		color = TintFor(netForcedColorIndex ?? (PosePinned ? 0 : RandomHelper.Random.Next(3)));
		startheight = base.Position.Y;
		if (isbackground)
		{
			base.Collides = false;
			color = new Color(new Vector4(1f, 1f, 1f, 0.2f));
			scale = 0.67f * SizeFactor;
			base.Speed = oracle.BackgroundSpeed.Length() * 1.11f;
			base.DrawOrder = 1;
			startheight = MathHelper.Min(BackgroundBandBottom, startheight);
			swiveltimer.Duration = 4000f;
		}
		else
		{
			scale = 1f * SizeFactor;
			base.Collides = true;
			base.Speed = oracle.BackgroundSpeed.Length() * 1.35f;
			base.DrawOrder = 20;
			swiveltimer.Duration = 2700f;
		}
		if (!PosePinned)
		{
			swiveltimer.Randomize();
		}
		else
		{
			// REWIND, not merely "skip Randomize", and both timers. NewFlyingSpider RECYCLES, so a
			// pinned spider out of the pool would otherwise inherit the phase the last one died
			// on; and the swivel Duration set just above does not rewind the timer, so even a
			// brand-new one does not read as zero-elapsed. Either way two boots would differ.
			flaptimer.Reset();
			swiveltimer.Reset();
		}
		ApplyBenchPlacement();
		// LAST, after both the background branch's startheight clamp and the swivel Randomize:
		// a net puppet flies the HOST's path, so its anchor overrides every roll above rather
		// than being one more input to them. Absent in normal play (both fields null).
		if (netForcedStartHeight.HasValue)
		{
			startheight = netForcedStartHeight.Value;
		}
		if (netForcedSwivelPhase.HasValue)
		{
			swiveltimer.SetNormalized(netForcedSwivelPhase.Value);
		}
	}

	// The three body tints, by roll index. Extracted only because the bench re-picks from its grid
	// after Initialize has already picked, and two copies of the mapping would be two places to
	// drift.
	private static Color TintFor(int pick)
	{
		return pick switch
		{
			1 => Color.White,
			2 => Color.DimGray,
			_ => Color.DarkGray,
		};
	}

	// Both rigs that exist to be A/B'd freeze the pose, and for the same reason: two boots that
	// differ in wing-flap phase, swivel phase or tilt cannot be compared frame against frame.
	// The sprite harness parks ONE spider for a screenshot; ?flyspidercount= pins a whole grid of
	// them (ApplyBenchPlacement already fixes X/Y and Speed, which is the other half of it). Live
	// play keeps every roll -- a swarm flapping in lockstep reads as one organism.
	private bool PosePinned =>
		EvilAliensWeb.Compat.DebugFlags.Harness != null || benchIndex.HasValue;

	// What the bench REPORTS, and deliberately an OBSERVATION rather than a restatement of
	// PosePinned: it reads the three values the pin is supposed to have produced, so an edit that
	// drops a `Randomize()` gate (or the tilt) is caught even if PosePinned itself still says the
	// right thing. Read straight after the Add, before any Update has advanced a timer.
	internal bool PoseIsPinned =>
		flaptimer.TimeElapsed == 0f && swiveltimer.TimeElapsed == 0f && rotation == 0f;

	// Lay the bench spiders out on a deterministic grid over the play field and freeze them in X,
	// so the on-screen population is EXACTLY the requested N for the whole run. Speed 0 also keeps
	// Update's `Position.X < -100 => Die()` from ever firing, which is what removed the drift.
	// The timers still RUN -- the swivel bob still moves them vertically and the flap timer still
	// animates the wings, so the per-frame draw work stays representative of real play -- but their
	// phases are pinned along with the tilt (PosePinned above), so the population flaps in
	// lockstep. That is the price of a boot-to-boot diffable capture, and it costs this rig
	// nothing: every spider draws the same three sprites whatever phase it is at.
	private void ApplyBenchPlacement()
	{
		if (!benchIndex.HasValue)
		{
			return;
		}
		base.Speed = 0f;
		base.MaxSpeed = 0f;
		// Pinning N also means nothing may REMOVE a bench spider, and a foreground one is
		// shootable (Initialize sets Collides=true for that variant) -- the player would decay N
		// mid-measurement, and an un-invulned ship could be killed by the grid it is measuring.
		// Background spiders are already Collides=false, so this only ever changes the foreground
		// bench, and it changes it into what the background one always was. Consequence, and it is
		// the right trade for what this rig measures: a foreground bench sits out the collision
		// pass, so it is a DRAW-cost bench (GL calls / frame ms), not a whole-frame one.
		base.Collides = false;
		int i = benchIndex.Value;
		int n = Math.Max(1, benchCount);
		// Widest grid that stays roughly square, so the same N always lands the same way and the
		// spiders spread over the field instead of stacking in one column.
		int cols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(n)));
		int rows = (int)Math.Ceiling((double)n / cols);
		int col = i % cols;
		int row = i / cols;
		// Scale the rows to the band the variant actually occupies. Spreading the grid over the
		// full 475 and letting Initialize's fog-band clamp bite instead would fold every row below
		// the band onto one line -- 12 of 40 spiders stacked on y=350 -- which is both the opposite
		// of the spread this grid exists for and a pile of extra overlap for the flatten to chew
		// on. Update drives the drawn Y off startheight, not off Position.Y, so that clamp is what
		// decides where a background spider is SEEN; keeping the rows inside the band means the
		// grid is the only thing placing them.
		float ySpan = isbackground ? BackgroundBandBottom : 475f;
		// Inset half a cell so nothing sits on the screen edge, where it would be part-clipped and
		// draw less than a whole spider.
		float x = 800f * (col + 0.5f) / cols;
		float y = ySpan * (row + 0.5f) / rows;
		base.Position = new Vector2(x, y);
		startheight = y;
		// Cycle the three tints across the grid so a tint-dependent draw bug cannot hide behind a
		// single-tint bench. Keyed on col+row, not on the raw index: `col = i % cols`, so an index
		// mod 3 would make the tint a pure function of the COLUMN whenever cols is a multiple of 3
		// (N=7..9, N=64..81, ...). Foreground only -- Initialize gives a background spider the flat
		// fog tint, which is what the whole variant is.
		if (!isbackground && !netForcedColorIndex.HasValue)
		{
			color = TintFor((col + row) % 3);
		}
	}

	// Baked half-extent of the per-spider group-flatten box, in DESIGN px before `scale`. Generous
	// on purpose: it must hold the reared body plus both wings at every point of their ±90° swing
	// without clipping. Overridable with ?flyspiderbox=<half> — see DebugFlags for why the size is
	// the discriminator between a per-call and a fill-bound flatten cost (card 9c92962e).
	public const float DefaultFlattenBoxHalf = 200f;

	internal static float FlattenBoxHalfDesign =>
		EvilAliensWeb.Compat.DebugFlags.FlySpiderBox ?? DefaultFlattenBoxHalf;

	// The design-space box the flatten captures into. Also used by FlyingSpiderSwarm to union the
	// whole swarm's boxes into one.
	internal Rectangle FlattenBox
	{
		get
		{
			float half = FlattenBoxHalfDesign * scale;
			return new Rectangle(
				(int)Math.Floor(base.Position.X - half),
				(int)Math.Floor(base.Position.Y - half),
				(int)Math.Ceiling(2f * half),
				(int)Math.Ceiling(2f * half));
		}
	}

	public override void Draw(GameTime gameTime)
	{
		spriteBatch.BlendMode = (SpriteBlendMode)1;
		if (!isbackground)
		{
			// Foreground spiders are opaque (alpha 1) -- no overlap double-up to flatten away.
			DrawSprites(gameTime);
			return;
		}
		switch (EvilAliensWeb.Compat.DebugFlags.FlySpiderFlatten)
		{
		case EvilAliensWeb.Compat.DebugFlags.FlySpiderFlattenMode.Swarm
			when FlyingSpiderSwarm.Active:
			// FlyingSpiderSwarm brackets ONE flatten around every background spider and calls
			// DrawFlattened on each; drawing ourselves here as well would double them. Falls
			// through to the per-spider path when nothing is driving the swarm (the sprite
			// harness has no Level2 to add the component), so the flag can never blank a scene.
			break;
		case EvilAliensWeb.Compat.DebugFlags.FlySpiderFlattenMode.None:
			// The un-flattened look: the overlaps composite to ~0.36 against a 0.2 body, so the
			// wings read more solid than the body. This is what "drop the flatten for the fog
			// layer" would ship.
			DrawSprites(gameTime);
			break;
		default:
			// Fog spiders are translucent (alpha 0.2). Drawing wing+body+wing separately at 0.2 with
			// straight-alpha blending makes the overlaps composite to ~0.36, so the wings read more
			// solid than the body — the reported "opacity is off". Flatten the three sprites OPAQUE
			// into a shared RT (the union has no internal double-up), then composite the whole
			// silhouette ONCE at the fog alpha, so body + wings fade as one.
			Color fog = color;
			spriteBatch.BeginGroupFlatten(FlattenBox);
			DrawFlattened(gameTime);
			spriteBatch.BlendMode = (SpriteBlendMode)1;
			spriteBatch.EndGroupFlatten(new Color((byte)255, (byte)255, (byte)255, fog.A));
			break;
		}
	}

	// Draw the three sprites OPAQUE, for capture inside an already-open group flatten. The fog
	// alpha is applied once by the composite, so it is lifted off `color` here and put back
	// straight away — callers own the bracket, not this.
	internal void DrawFlattened(GameTime gameTime)
	{
		Color fog = color;
		color = new Color(fog.R, fog.G, fog.B, (byte)255);
		DrawSprites(gameTime);
		color = fog;
	}

	// The fog alpha every background spider shares (set in Initialize). The swarm composite needs
	// one alpha for the whole group; they are all the same value, so any live spider answers for
	// the swarm.
	internal byte FogAlpha => color.A;

	private void DrawSprites(GameTime gameTime)
	{
		float num = flaptimer.Duration / 2f;
		if (base.hittimeractive)
		{
			spriteBatch.lightenEffect.Enable();
		}
		float timeElapsed = flaptimer.TimeElapsed;
		timeElapsed %= num * 2f;
		if (timeElapsed > num)
		{
			timeElapsed = num - (timeElapsed - num);
		}
		// wing1 is a 4x supersampled sheet; divide the draw scale by its factor (and scale the
		// design-space pivots up by it) so the wing renders at its true on-screen size. Anchor
		// both wings on the body in DESIGN space relative to its centre (Position) -- the old
		// texel-space offset assumed cell texels == screen px, which the supersampled rear-up
		// sheet blows far out of place.
		float wf = SuperSampleFactor("GFX/Sprites/wing1", wing.LogicalWidth());
		int wingIdx = (int)curframe - FirstFrame;
		if (wingIdx < 0) wingIdx = 0;
		else if (wingIdx >= WingAnchors.Length) wingIdx = WingAnchors.Length - 1;
		Vector2 wingAnchor = base.Position + WingAnchors[wingIdx] * scale;
		spriteBatch.Draw(wing, wingAnchor, MathHelper.Lerp(0f, (float)Math.PI / 2f, timeElapsed / num), scale / wf, new Vector2(82f, 11f) * wf, color, (SpriteEffects)1);
		base.Draw(gameTime);
		spriteBatch.Draw(wing, wingAnchor, MathHelper.Lerp(0f, -(float)Math.PI / 2f, timeElapsed / num), scale / wf, new Vector2(6f, 11f) * wf, color, (SpriteEffects)0);
		if (base.hittimeractive)
		{
			spriteBatch.lightenEffect.Disable();
		}
	}

	public override void Update(GameTime gameTime)
	{
		float num = 50f * Settings.GetInstance().DifficultyModifier;
		base.Position = new Vector2(base.Position.X, startheight + num * scale * (float)Math.Sin(swiveltimer.Normalized * ((float)Math.PI * 2f)));
		base.Update(gameTime);
		if (base.Position.X < -100f)
		{
			Die();
		}
	}

	public override void CollidesWith(ICollidable other)
	{
		base.CollidesWith(other);
		if (other is Floorbottom && base.DirectionalVector.Y > 0f)
		{
			base.DirectionalVector = new Vector2(base.DirectionalVector.X, 0f - base.DirectionalVector.Y);
		}
		if (other is Lazer || other is SweepUFO)
		{
			KilledBy(other, isComboGenerator: false);
		}
	}

	protected override void KilledBy(ICollidable other, bool isComboGenerator)
	{
		if (!base.IsDead)
		{
			Die();
			if (!(other is Lazer) && !(other is SweepUFO))
			{
				AwardScore(isComboGenerator, other);
			}
			BloodExplosion bloodExplosion = BloodExplosion.NewExplosion(collection, base.Game);
			bloodExplosion.Setup(base.Position, 5f, 0.75f, MathHelper.Min(base.SpeedVector.Length(), 0.24f), MyMath.VectorToAngle(base.SpeedVector));
			bloodExplosion.MakeGreen();
			collection.Add((GameComponent)(object)bloodExplosion);
			bloodExplosion = BloodExplosion.NewExplosion(collection, base.Game);
			bloodExplosion.Setup(base.Position, 3f, 0.5f, MathHelper.Min(base.SpeedVector.Length(), 0.24f), MyMath.VectorToAngle(base.SpeedVector));
			bloodExplosion.MakeGreen();
			collection.Add((GameComponent)(object)bloodExplosion);
			if (RandomHelper.RandomNextFloat(0f, 1f) <= 0.2f)
			{
				sound.PlayCue("bugdies");
			}
			sound.PlayCue("small head asplode");
		}
	}

	// ---- Online co-op replication seams (Compat/Net/Descriptors/FlyingSpiderDescriptor) ---
	// Client puppets run Enabled=false. isbackground (the Setup bool) picks the WHOLE look:
	// fog alpha + smaller scale + DrawOrder + Collides=false + the group-flatten Draw path, so
	// it is pinned as a construction arg. Wing flap (flaptimer, ticked by NetTickTimers) + the
	// vertical bob (carried by base pos) + curframe self-animate, so there is no continuous
	// state extra. Foreground spiders take a random grey tint, forced via netForcedColorIndex.

	internal bool NetIsBackground => isbackground;

	// Card 9a3175d0: the background form is fog. It spawns Collides=false and Initialize is the
	// only thing that ever writes Collides (including its ApplyBenchPlacement tail, which only
	// ever writes false as well), so it can never turn into a hazard; the swarm is
	// replicated as one NetCosmeticKind.FlyingSpiderBackground beat instead and the joiner runs
	// its own spawner. `isbackground` is pinned by Setup before bin.Add, which is when this is
	// read. The FOREGROUND form is a real killable enemy and stays fully replicated.
	internal override bool NetCosmeticOnly => isbackground;

	internal byte NetColorIndex
	{
		get
		{
			if (color == Color.White)
			{
				return 1;
			}
			if (color == Color.DimGray)
			{
				return 2;
			}
			return 0; // DarkGray (Initialize's case 0)
		}
	}

	// Set BEFORE bin.Add triggers Initialize (which reads it); null => the random pick.
	internal void NetForceColor(byte idx)
	{
		netForcedColorIndex = idx;
	}

	// ---- anchored motion (card c1a38ef9) -------------------------------------------------
	// Update is `Position.Y = startheight + amp * scale * sin(2pi * swiveltimer.Normalized)` over
	// a plain linear X drift, i.e. a DETERMINISTIC path: a client that knows startheight, the
	// swivel phase and the amplitude can integrate it exactly instead of dead-reckoning on a
	// velocity finite-differenced across a snapshot turn -- which for this shape measures a chord
	// of the sine and is wrong by construction (see NetPathAnchored's header).
	//
	// X is honest as a declared velocity: Initialize sets Direction = PI and a constant Speed, and
	// nothing ever writes Position.X directly. That is the precondition the seam requires.

	internal override bool NetPathAnchored => true;

	// The swivel, as an offset from that baseline. swiveltimer is in `timers`, so the driver's
	// NetTickTimers advances it on a frozen puppet -- which is what makes a local evaluation
	// possible at all. `scale` rides the base state (the driver lerps it), and netSwivelAmplitude
	// is the host's own `50 * DifficultyModifier` off the wire, so both peers use the same number
	// rather than each applying its own drifting modifier.
	//
	// Zero-mean by construction (a full sine), which the driver relies on: it differences this
	// across the tick, so a non-zero mean would simply never be seen.
	internal override Vector2 NetPathOffset =>
		new Vector2(0f, netSwivelAmplitude * scale * (float)Math.Sin(swiveltimer.Normalized * ((float)Math.PI * 2f)));

	// Update's own coefficient, read on the HOST for the wire. Kept beside the Update expression
	// it mirrors -- if that constant moves, both must move together.
	internal float NetSwivelAmplitude => 50f * Settings.GetInstance().DifficultyModifier;

	// The swivel phase, 0..1 over the cycle. This is what the EvSpawn anchor pins and what the
	// state extras re-assert: Initialize calls swiveltimer.Randomize(), so a client's own phase is
	// an unrelated roll and the wasp would bob in antiphase with the host's.
	internal float NetSwivelPhase => swiveltimer.Normalized;

	// ---- puppet side: the two DRIFTING parameters, eased per TICK ------------------------
	//
	// Both are corrected in NetDriveExtras rather than in the descriptor's apply, and that is the
	// load-bearing detail. NetPathOffset is DIFFERENCED across the tick by NetPuppets.Drive, so
	// anything that moves the amplitude or the phase moves the puppet by that much immediately --
	// applying a whole turn's correction inside ApplyStateExtra would put it into ONE tick, which
	// is the very step this card exists to remove. (It would also land after that turn's position
	// error was measured, so the correction blend could not absorb it either.) Spreading it over
	// NetParamEaseMs of real time makes it a nudge instead.
	private const float NetParamEaseMs = 250f;

	// The host's swivel amplitude in design px, DifficultyModifier already applied. Defaults to
	// the un-modified 50 so a puppet that has not yet had a state extra still swivels sanely
	// (DifficultyModifier starts at 1), rather than standing flat.
	private float netSwivelAmplitude = 50f;

	private float netSwivelAmplitudeTarget = 50f;

	// The remaining phase correction, in cycles, SIGNED along the shortest arc. Held as an amount
	// still to spend rather than a target phase, because the phase itself keeps advancing on its
	// own timer -- a stored target would be stale the moment it was recorded.
	private float netSwivelPhaseError;

	// Narrow readback for NetMotionTest, the NetFxTest precedent: the amplitude a puppet is
	// actually swivelling at is private state that moves no metric and appears in no frame, and
	// whether it EASES or snaps is the whole subject of that suite's section 3.
	internal float NetLocalSwivelAmplitude => netSwivelAmplitude;

	// Puppet only. Records what the host reported; NetDriveExtras spends it.
	internal void NetApplySwivel(float amplitude, float phase01)
	{
		netSwivelAmplitudeTarget = amplitude;
		if (swiveltimer.Duration <= 0f)
		{
			return;
		}
		// WRAPPED SHORTEST ARC: a naive (target - current) walks the long way round whenever the
		// pair straddles the 1 -> 0 wrap, which for a 2.7 s cycle is a wasp swinging a whole
		// period the wrong way.
		float delta = phase01 - swiveltimer.Normalized;
		if (delta > 0.5f)
		{
			delta -= 1f;
		}
		else if (delta < -0.5f)
		{
			delta += 1f;
		}
		netSwivelPhaseError = delta;
	}

	// Called by NetPuppets.Drive once per tick on a frozen puppet (the NetChargeGlow seam).
	internal override void NetDriveExtras(GameTime gameTime)
	{
		base.NetDriveExtras(gameTime);
		float fraction = MathHelper.Clamp(
			(float)gameTime.ElapsedGameTime.TotalMilliseconds / NetParamEaseMs, 0f, 1f);
		netSwivelAmplitude += (netSwivelAmplitudeTarget - netSwivelAmplitude) * fraction;
		if (netSwivelPhaseError != 0f && swiveltimer.Duration > 0f)
		{
			float spend = netSwivelPhaseError * fraction;
			netSwivelPhaseError -= spend;
			float next = swiveltimer.Normalized + spend;
			// Wrap into [0,1) before handing it back -- Timer.SetNormalized CLAMPS, so an
			// out-of-range value would silently park the phase at an end of the cycle instead of
			// carrying it round.
			next -= (float)Math.Floor(next);
			swiveltimer.SetNormalized(next);
		}
	}

	// The path anchor: the Y the swivel oscillates ABOUT. Initialize rolls it (a random entry
	// height), so it must be pinned from the host or the client's wasp flies a parallel path at
	// the wrong altitude -- which the position correction would then fight every turn.
	internal float NetStartHeight => startheight;

	// Set BEFORE bin.Add, like NetForceColor: Initialize WRITES both of these, and Add runs it
	// synchronously, so the host's values are stored here and applied at the end of Initialize.
	internal void NetForceAnchor(float startHeightY, float swivelPhase01)
	{
		netForcedStartHeight = startHeightY;
		netForcedSwivelPhase = swivelPhase01;
	}
}
