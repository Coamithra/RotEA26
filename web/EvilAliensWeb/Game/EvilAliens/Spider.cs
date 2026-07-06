using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class Spider : KillableAlien
{
	// Packed-sheet frame the spider snaps to ONCE on landing: the crouched settled stance that best
	// matches the descending airborne pose (dialed to 42 by eye). It resumes animating from there on
	// the following frames. Overridable live via ?spiderlandframe= for dialing; bake the value here.
	private const float LandFrame = 42f;

	// Ground baseline Y (design space): the spider rests here and lands back to it. Dialed up from
	// 505 to 485 (~20px) by eye so the whole spider assembly (sprite + shadow + hitbox + jump arc)
	// sits a touch higher on the Mars ground. Spider-only (nothing else keys off this).
	public const float GroundY = 485f;

	// Sheet frame the rear-up "launch" beat fires on (spider_sheet2 is a 7x7 rear-up->fling->settle
	// cycle). LIVE play fires the jump when the animation reaches this beat (a count-back preset makes
	// it coincide with a random launch X). Dialed to frame 5 by eye (the early "coil" reads best as
	// the launch moment). Overridable live via ?spiderjumpframe= (null => this baked default, so a
	// plain boot is unchanged); the ?harness=spiderjump tool + tuner panel reuse it. Bake it here.
	private const float DefaultJumpFrame = 5f;

	private float yspeed;

	private bool hasJumped;

	private bool hasLanded;

	private float rotationspeed;

	private float jumpXposition;

	// Animation-driven jump state. animAcc is an UNWRAPPED frame accumulator (base.Update wraps
	// curframe mod the sheet, which can't be crossing-tested); the count-back presets it below the
	// launch beat so it reaches jumpBeatFrame exactly as the spider passes jumpXposition. jumpArmed
	// gates the one-time preset to the first Update (oracle.BackgroundSpeed is valid by then).
	private float animAcc;

	private float jumpBeatFrame;

	private bool jumpArmed;

	private Texture2D spiderJump;

	public override ICollisionType CollisionType
	{
		get
		{
			//IL_0009: Unknown result type (might be due to invalid IL or missing references)
			//IL_0013: Unknown result type (might be due to invalid IL or missing references)
			//IL_001f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0029: Unknown result type (might be due to invalid IL or missing references)
			//IL_0035: Unknown result type (might be due to invalid IL or missing references)
			//IL_003b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0040: Unknown result type (might be due to invalid IL or missing references)
			//IL_004c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0052: Unknown result type (might be due to invalid IL or missing references)
			//IL_0057: Unknown result type (might be due to invalid IL or missing references)
			CollisionBox collisionBox = retrieveBoundsFromTexture();
			collisionBox.TopLeft *= 0.9f;
			collisionBox.BottomRight *= 0.9f;
			collisionBox.TopLeft += base.Position;
			collisionBox.BottomRight += base.Position;
			return collisionBox;
		}
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		spiderJump = content.Load<Texture2D>("GFX/Sprites/spiderjump");
	}

	public Spider(Game game)
		: base(game)
	{
		// spider_sheet2 is the 7x7 (49-frame) "rear up" animation (AnimGen take, half-fps of the
		// 98 source frames), replacing the old 4-frame crawl. The supersample registry (design
		// width 160) draws it at the same on-screen size; its 384px cells render ~1:1 at the 1440
		// render cap (160 * 2.4). ~12 fps. The FlyingSpider reuses this same sheet, looping just
		// its reared sub-range via FirstFrame/LastFrame.
		LoadAnimation(new AnimationData("GFX/Sprites/spider_sheet2", 7, 7, 1, 12f));
		base.DrawOrder = 20;
		interpolationOptions = InterpolationOptions.never;
		scale = 1f;
		base.Direction = (float)Math.PI;
		PointValue = 100f;
		SetHitPoints(3, scaleWithDifficulty: true);
	}

	public static Spider NewSpider(ComponentBin collection, Game game)
	{
		Spider spider = collection.Recycle<Spider>();
		if (spider == null)
		{
			spider = new Spider(game);
		}
		return spider;
	}

	public void Setup()
	{
	}

	public override void Initialize()
	{
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0081: Unknown result type (might be due to invalid IL or missing references)
		//IL_0086: Unknown result type (might be due to invalid IL or missing references)
		//IL_008d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0092: Unknown result type (might be due to invalid IL or missing references)
		//IL_0099: Unknown result type (might be due to invalid IL or missing references)
		//IL_009e: Unknown result type (might be due to invalid IL or missing references)
		base.Position = new Vector2(1000f, GroundY);
		base.Initialize();
		yspeed = 0f;
		hasJumped = false;
		hasLanded = false;
		rotation = 0f;
		rotationspeed = 0f;
		// Random launch X per spider (so a cluster jumps at different spots). ?spiderjumpx= pins it
		// to a fixed X for testing a specific launch point; null => the random default.
		jumpXposition = EvilAliensWeb.Compat.DebugFlags.SpiderJumpX ?? RandomHelper.RandomNextFloat(300f, 900f);
		jumpBeatFrame = EvilAliensWeb.Compat.DebugFlags.SpiderJumpFrame ?? DefaultJumpFrame;
		jumpArmed = false;
		// Dialed shadow tuning rides the generic Floor shadow via ShadowOffset/ShadowSize (identity
		// by default -> a plain boot casts the same shadow). Reset on every (incl. recycled) spawn.
		ShadowOffset = new Vector2(EvilAliensWeb.Compat.DebugFlags.SpiderShadowX, EvilAliensWeb.Compat.DebugFlags.SpiderShadowY);
		ShadowSize = EvilAliensWeb.Compat.DebugFlags.SpiderShadowScale;
		// Start each spider at a RANDOM point in the rear-up animation so a cluster crawls out of
		// lock-step. The count-back preset in Update OVERRIDES this on the first tick to line the
		// launch beat up with jumpXposition; this is only the pre-preset value.
		curframe = RandomHelper.RandomNextFloat(0f, (float)(rows * columns));
		switch (RandomHelper.Random.Next(3))
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
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_006f: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f0: Unknown result type (might be due to invalid IL or missing references)
		//IL_0102: Unknown result type (might be due to invalid IL or missing references)
		//IL_0119: Unknown result type (might be due to invalid IL or missing references)
		//IL_014e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0153: Unknown result type (might be due to invalid IL or missing references)
		//IL_017d: Unknown result type (might be due to invalid IL or missing references)
		spriteBatch.BlendMode = (SpriteBlendMode)1;
		if (!hasJumped)
		{
			base.Draw(gameTime);
			return;
		}
		if (base.hittimeractive)
		{
			spriteBatch.lightenEffect.Enable();
		}
		// spiderjump is now a 6x4 soar ANIMATION sheet (the AnimGen flying-spider take). Play it
		// looping while airborne: draw one source-rect cell, footprint-scaled by 1/factor so the
		// on-screen size matches the old static jump body, with the jump tumble (rotation). The
		// fake flapping wings are gone -- the animation carries the motion now.
		int cols = 6, rows = 4, sep = 1;
		int cellW = (spiderJump.Width - (cols - 1) * sep) / cols;
		int cellH = (spiderJump.Height - (rows - 1) * sep) / rows;
		float fJump = SuperSampleFactor("GFX/Sprites/spiderjump", cellW);
		int frame = (int)(gameTime.TotalGameTime.TotalMilliseconds / 55f) % (cols * rows);
		Rectangle src = new Rectangle(frame % cols * (cellW + sep), frame / cols * (cellH + sep), cellW, cellH);
		// The airborne "flying" sheet has a different visual anchor than the ground rear-up sheet, so
		// the first in-air pose can pop away from the last ground frame at launch (and the last in-air
		// pose from the land frame on touchdown). Nudge the flying sprite by a dialed offset so both
		// transitions line up. Design px, +y down -> a NEGATIVE y lifts the flying sprite ("start y of
		// flying mode higher"). Identity default (0,0) => a plain boot is unchanged; ?spiderairx=/
		// ?spiderairy= + the tuner panel dial it. Applies in live play AND the ?harness=spiderjump viz.
		Vector2 airOffset = new Vector2(EvilAliensWeb.Compat.DebugFlags.SpiderAirX, EvilAliensWeb.Compat.DebugFlags.SpiderAirY);
		spriteBatch.Draw(spiderJump, src, base.Position + airOffset, rotation, 1f / fJump, center: true, color);
		if (base.hittimeractive)
		{
			spriteBatch.lightenEffect.Disable();
		}
	}

	public override void Update(GameTime gameTime)
	{
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_0055: Unknown result type (might be due to invalid IL or missing references)
		//IL_005a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0142: Unknown result type (might be due to invalid IL or missing references)
		//IL_0196: Unknown result type (might be due to invalid IL or missing references)
		//IL_01db: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ea: Unknown result type (might be due to invalid IL or missing references)
		base.Update(gameTime);
		if (!jumpArmed)
		{
			// One-time "count back": preset the unwrapped launch-beat accumulator (and the visible
			// frame) from the REAL Mars scroll so the animation reaches jumpBeatFrame exactly as the
			// spider passes jumpXposition. entryFrame = J - fps * (time from spawn X to jumpX).
			// Done here (first Update) because oracle.BackgroundSpeed is valid by now.
			float total = MathHelper.Max(1f, rows * columns);
			float scrollPxPerMs = Math.Abs(oracle.BackgroundSpeed.X);
			float dist = base.Position.X - jumpXposition;
			float entryFrame = jumpBeatFrame;
			if (scrollPxPerMs > 0.0001f && dist > 0f)
			{
				float tJumpSec = dist / scrollPxPerMs / 1000f;
				entryFrame = jumpBeatFrame - fps * tJumpSec;
			}
			animAcc = entryFrame;
			curframe = WrapFrame(entryFrame, total);
			jumpArmed = true;
		}
		if (base.Position.X < -500f)
		{
			Die();
		}
		base.Position += (oracle.BackgroundSpeed + new Vector2(0f, yspeed)) * (float)gameTime.ElapsedGameTime.TotalMilliseconds;
		if (hasJumped & !hasLanded)
		{
			yspeed += 0.02f * (float)gameTime.ElapsedGameTime.TotalMilliseconds / 16.666666f;
			rotation += rotationspeed * (float)gameTime.ElapsedGameTime.TotalMilliseconds;
			if (yspeed < 0f)
			{
				rotationspeed = MathHelper.Max(rotationspeed - 3E-05f * (float)gameTime.ElapsedGameTime.TotalMilliseconds / 16.666666f, 0f);
			}
			else
			{
				rotationspeed -= 6E-05f * (float)gameTime.ElapsedGameTime.TotalMilliseconds / 16.666666f;
			}
		}
		if (!hasJumped & !hasLanded)
		{
			// ANIMATION-DRIVEN launch: advance the unwrapped launch-beat accumulator at the sprite's
			// fps and fire when it reaches the tuned rear-up beat. The count-back preset makes that
			// coincide with the spider passing jumpXposition, so it still launches at a (random) X.
			animAcc += fps * (float)gameTime.ElapsedGameTime.TotalSeconds;
			if (animAcc >= jumpBeatFrame)
			{
				hasJumped = true;
				rotation = -0.1f;
				rotationspeed = 0.0018f;
				yspeed = RandomHelper.RandomNextFloat(-8f, -19f) / 16.666666f;
			}
		}
		if (hasJumped & (base.Position.Y > GroundY))
		{
			hasJumped = false;
			hasLanded = true;
			rotation = 0f;
			rotationspeed = 0f;
			yspeed = 0f;
			base.Position = new Vector2(base.Position.X, GroundY);
			// Snap to the settled "landed" frame ONCE on touchdown, then let it keep animating
			// from there (base.Update advances curframe normally on the following frames).
			// ?spiderlandframe= overrides the beat for dialing; null => the baked LandFrame.
			curframe = EvilAliensWeb.Compat.DebugFlags.SpiderLandFrame ?? LandFrame;
		}
	}

	public override void CollidesWith(ICollidable other)
	{
		base.CollidesWith(other);
		if (other is Lazer)
		{
			KilledBy(other, isComboGenerator: false);
		}
	}

	protected override void KilledBy(ICollidable other, bool isComboGenerator)
	{
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_006b: Unknown result type (might be due to invalid IL or missing references)
		//IL_007b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ca: Unknown result type (might be due to invalid IL or missing references)
		//IL_00da: Unknown result type (might be due to invalid IL or missing references)
		//IL_00df: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_010c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0111: Unknown result type (might be due to invalid IL or missing references)
		if (!(other is Lazer))
		{
			AwardScore(isComboGenerator, other);
		}
		BloodExplosion bloodExplosion = BloodExplosion.NewExplosion(collection, base.Game);
		BloodExplosion bloodExplosion2 = bloodExplosion;
		Vector2 position = base.Position;
		Vector2 val = oracle.BackgroundSpeed + new Vector2(0f, yspeed);
		bloodExplosion2.Setup(position, 5f, 0.75f, MathHelper.Min((val).Length(), 0.24f), MyMath.VectorToAngle(oracle.BackgroundSpeed + new Vector2(0f, yspeed)));
		bloodExplosion.MakeGreen();
		collection.Add((GameComponent)(object)bloodExplosion);
		bloodExplosion = BloodExplosion.NewExplosion(collection, base.Game);
		BloodExplosion bloodExplosion3 = bloodExplosion;
		Vector2 position2 = base.Position;
		Vector2 val2 = oracle.BackgroundSpeed + new Vector2(0f, yspeed);
		bloodExplosion3.Setup(position2, 3f, 0.5f, MathHelper.Min((val2).Length(), 0.24f), MyMath.VectorToAngle(oracle.BackgroundSpeed + new Vector2(0f, yspeed)));
		bloodExplosion.MakeGreen();
		collection.Add((GameComponent)(object)bloodExplosion);
		Die();
		if (RandomHelper.RandomNextFloat(0f, 1f) <= 0.2f)
		{
			sound.PlayCue("bugdies");
		}
		sound.PlayCue("small head asplode");
	}

	// ---- Sprite-harness jump-cycle visualiser (?harness=spiderjump) -------------------------
	// The whole crawl -> launch -> arc -> land cycle is otherwise only reachable by driving a live
	// level. HarnessScene LOOPS this deterministic sim instead so the Mars jumping-spider alignment
	// values (shadow position, jump-start X, land-anim resume frame) can be tuned by eye. It sets
	// Position/curframe/rotation/hasJumped so the object's OWN Draw shows the right sprite (ground
	// sheet vs the airborne spiderjump sheet); the harness overlays the shadow + markers + readout.
	// LIVE gameplay is now animation-driven too (Update fires on jumpBeatFrame via the same count-back
	// preset), and the ?spider* knobs (jumpframe/landframe/jumpx/shadow*) apply to LIVE play as well as
	// this viz -- but all default to identity, so a shipped build (no query) is byte-identical. This
	// viz still LOOPS the whole cycle deterministically for eyeball-dialing (its arc is illustrative).
	public struct JumpVizState
	{
		public float ScrollPxPerSec;
		public float EntryFrame;
		public float JumpFrame;
		public float LandFrameOut;
		public float JumpX;
		public float CurFrame;
		public bool Airborne;
		public float GroundYOut;
	}

	// phase in [0,1) loops one crawl->jump->land cycle. Returns the derived numbers for the readout.
	public JumpVizState HarnessApplyPhase(float phase)
	{
		float total = MathHelper.Max(1f, rows * columns);
		float jumpFrame = WrapFrame(EvilAliensWeb.Compat.DebugFlags.SpiderJumpFrame ?? DefaultJumpFrame, total);
		float landFrame = EvilAliensWeb.Compat.DebugFlags.SpiderLandFrame ?? LandFrame;
		float jumpX = EvilAliensWeb.Compat.DebugFlags.SpiderJumpX ?? 400f;
		float loopSec = MathHelper.Max(0.5f, EvilAliensWeb.Compat.DebugFlags.SpiderLoopSeconds);

		// The spider crosses the whole screen over one loop (enter just off the right edge, exit
		// off the left). The viz scroll speed is DERIVED from the loop so the spider stays on
		// screen and slow enough to watch at any loop length; the real Mars ground scroll (~0.6
		// px/ms) is much faster, so driving the viz at it would flick the spider past in a frame.
		// The entry-frame "count back" below uses THIS same speed, so the jump still lines up on
		// jumpX exactly -- the number the live wiring re-derives from the real scroll (For-me card).
		const float xEnter = 880f;
		const float xExit = -120f;
		float sPxPerSec = (xEnter - xExit) / loopSec;

		// Clamp the launch X into the visible crossing so the "count back" time stays positive.
		float jumpXeff = MathHelper.Clamp(jumpX, xExit + 60f, xEnter - 60f);
		float tJump = (xEnter - jumpXeff) / sPxPerSec;   // time from spawn to reaching jumpX
		float t = phase * loopSec;

		// The "count back": preset the entry frame so curframe hits the jump beat EXACTLY when the
		// spider reaches jumpX. entryFrame = J - fps * (time from spawn to jumpX).
		float entryFrame = jumpFrame - fps * tJump;
		float x = xEnter - sPxPerSec * t;

		// Deterministic arc, ILLUSTRATIVE only -- it is NOT live play's physics (live: yspeed
		// rand(-8..-19)/16.67 px/ms + 0.02 px/ms^2 gravity, which arcs higher and varies per jump).
		// This viz just needs a readable hop at a representative HEIGHT so the flying sprite is clearly
		// airborne (to line up the launch/land transitions + the air offset); the beat/land-frame/air
		// alignment is what's being tuned. Kept deterministic so a phase freeze is repeatable; the
		// natural per-jump variance is only in live play (?level=Level2&spiders). ~200px apex, ~1.3s.
		const float v0 = -600f;
		const float g = 900f;
		float airDur = -2f * v0 / g;
		bool airborne = t >= tJump && t < tJump + airDur;

		float y = GroundY;
		if (t < tJump)
		{
			hasJumped = false;
			curframe = WrapFrame(entryFrame + fps * t, total);
			rotation = 0f;
		}
		else if (airborne)
		{
			float tau = t - tJump;
			y = GroundY + v0 * tau + 0.5f * g * tau * tau;
			hasJumped = true;
			rotation = 1.2f * (tau / airDur);
		}
		else
		{
			hasJumped = false;
			float tGround = t - (tJump + airDur);
			curframe = WrapFrame(landFrame + fps * tGround, total);
			rotation = 0f;
		}

		base.Position = new Vector2(x, y);

		return new JumpVizState
		{
			ScrollPxPerSec = sPxPerSec,
			EntryFrame = WrapFrame(entryFrame, total),
			JumpFrame = jumpFrame,
			LandFrameOut = landFrame,
			JumpX = jumpX,
			CurFrame = curframe,
			Airborne = airborne,
			GroundYOut = GroundY
		};
	}

	private static float WrapFrame(float f, float total)
	{
		return ((f % total) + total) % total;
	}
}
