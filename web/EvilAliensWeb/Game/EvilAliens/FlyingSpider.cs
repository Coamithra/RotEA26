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
		// In the sprite harness the object is frozen for a screenshot, so a randomized wing-flap
		// phase would make every boot a different pose -- and two boots that differ in pose cannot
		// be A/B'd against each other (the ?flyspiderflatten= comparison this exists for). Pin it.
		// Live play keeps the randomization: a swarm flapping in lockstep reads as one organism.
		if (EvilAliensWeb.Compat.DebugFlags.Harness == null)
		{
			flaptimer.Randomize();
		}
		base.Position = new Vector2(850f, RandomHelper.RandomNextFloat(0f, 475f));
		base.Direction = (float)Math.PI;
		base.MaxSpeed = base.Speed;
		rotation = RandomHelper.RandomNextFloat(-(float)Math.PI / 32f, (float)Math.PI / 32f);
		int colorPick = netForcedColorIndex ?? RandomHelper.Random.Next(3);
		switch (colorPick)
		{
		case 0:
			color = Color.DarkGray;
			break;
		case 1:
			color = Color.White;
			break;
		case 2:
			color = Color.DimGray;
			break;
		}
		startheight = base.Position.Y;
		if (isbackground)
		{
			base.Collides = false;
			color = new Color(new Vector4(1f, 1f, 1f, 0.2f));
			scale = 0.67f * SizeFactor;
			Vector2 backgroundSpeed = oracle.BackgroundSpeed;
			base.Speed = (backgroundSpeed).Length() * 1.11f;
			base.DrawOrder = 1;
			startheight = MathHelper.Min(BackgroundBandBottom, startheight);
			swiveltimer.Duration = 4000f;
		}
		else
		{
			scale = 1f * SizeFactor;
			base.Collides = true;
			Vector2 backgroundSpeed = oracle.BackgroundSpeed;
			base.Speed = (backgroundSpeed).Length() * 1.35f;
			base.DrawOrder = 20;
			swiveltimer.Duration = 2700f;
		}
		if (EvilAliensWeb.Compat.DebugFlags.Harness == null)
		{
			swiveltimer.Randomize();
		}
		ApplyBenchPlacement();
	}

	// Lay the bench spiders out on a deterministic grid over the play field and freeze them in X,
	// so the on-screen population is EXACTLY the requested N for the whole run. Speed 0 also keeps
	// Update's `Position.X < -100 => Die()` from ever firing, which is what removed the drift.
	// Everything time-varying is left alone: the swivel bob still moves them vertically and the
	// flap timer still animates the wings, so the per-frame draw work stays representative of real
	// play — only the birth/death churn is gone.
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
}
