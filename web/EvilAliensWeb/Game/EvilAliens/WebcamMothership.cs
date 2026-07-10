using System;
using EvilAliensWeb.Compat;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

// The webcam challenge's screen-bisecting mothership (F1). A stripped cousin of
// SpiderHelperMothership: it SLIDES in horizontally from a screen edge (SpiderHelper-style),
// WINDS UP a converging spark swarm (LazerGenerator, the exact charge effect a medium UFO
// uses), then fires a big laser that bisects the screen, holds a beat, and slides out. Unlike
// everything else it is a pure "get out the way!" hazard — it CANNOT be harmed (Collides=false,
// no CollisionHandler); standing in the beam costs a life (WebcamLevel tests the beam vs the
// person mask via WebcamInterop.HitBeam, since the beam isn't an ICollidable).
//
// Two orientations (WebcamLevel.PickBisectOrientation picks the mix):
//   VerticalDown            — slides in from a random side to top-CENTRE, parked HIGH so most of
//                             the hull is cut off at the top (belly peeking in), fires straight
//                             DOWN (dodge left/right), then continues out the far side (a pass).
//   HorizontalFromLeft/Right — slides in from that side to ~33% down (BisectY), parked so it
//                             PEEKS in from the edge, fires ACROSS (duck below), then retreats.
//
// The whole choreography is a pure function of `elapsed` ms since spawn (PoseAt / phase
// thresholds below): position eases in, parks through the charge+fire, eases out. That means
// the movement can be simulated in isolation and its trajectory read as DATA (see
// tools/sim/webcam_mothership_sim.py, which mirrors PoseAt) rather than screenshot-timed.
// A live `?wcmothershipfreeze=<ms>` halts `elapsed` at a chosen phase so a frozen appearance
// (e.g. the beam mid-fire, to check centring) can be captured without chasing the frame.
//
// The beam is drawn with a Quad directly (like Lazer does internally), so its length is driven
// HERE — a fixed, tier-independent sweep 0 -> full span then a hold — not Lazer's difficulty-
// scaled growth (which would crawl on Easy).
internal class WebcamMothership : AlienDrawableGameComponent
{
	public enum Bisect
	{
		VerticalDown,
		HorizontalFromLeft,
		HorizontalFromRight
	}

	// Choreography timings (ms). Windup is the telegraph; the sweep shoots the beam to full
	// span; the hold keeps it lethal a beat; enter/leave are the horizontal glide on/off screen.
	private const float EnterMs = 1400f;

	private const float WindupMs = 2500f;   // charge telegraph — long enough to give the player time to duck

	private const float BeamSweepMs = 500f;

	private const float BeamHoldMs = 6000f; // held lethal a long beat for max discomfort

	private const float LeaveMs = 1200f;

	private const float FireMs = BeamSweepMs + BeamHoldMs;

	// Phase thresholds along `elapsed` (ms since spawn).
	private const float ChargeStart = EnterMs;              // 1400 — parked, windup begins

	private const float FireStart = EnterMs + WindupMs;     // 3200 — beam fires

	private const float FireEnd = FireStart + FireMs;       // 5000 — beam off, start leaving

	private const float LeaveEnd = FireEnd + LeaveMs;       // 6200 — gone

	// Beam geometry (design px). FullSpan overshoots any edge from any origin, so the on-screen
	// portion always crosses fully (off-screen tail clipped). FireLead pushes the origin out of
	// the hull; BeamWidth is the Quad core; HitHalfWidth is the hit band (~ the visible bright
	// beam incl. glow) tested against the mask.
	private const float FullSpan = 900f;

	private const float FireLead = 55f;

	private const float BeamWidth = 24f;

	private const float HitHalfWidth = 26f;

	// Rest geometry. VertRestY parks the vertical ship HIGH so the hull is mostly cut off at the
	// top (belly peeking in). HorizPeekInset parks the horizontal ship so its centre is this far
	// in from the edge (mostly cut off, peeking). BisectY is the horizontal beam's height (~33%
	// down, so the player ducks under it). OffLeft/OffRight are the off-screen slide endpoints.
	private const float VertRestY = 5f;   // ~halfway between the original 80 and the cut-off -70

	private const float BisectY = 200f;

	private const float OffLeft = -280f;

	private const float OffRight = 1080f;

	// The mothershipB art bbox (measured) is 416x238 design px (scale 1) and its centre sits
	// (-16,-6) from the 456-frame centre. SpriteArtOffset re-centres the DRAW on Position so the
	// ship's VISUAL centre lines up with the beam (which fires from Position.x); ArtHalfWidth +
	// HorizVisibleFrac size the "40% on screen, 60% off" sideways peek.
	private const float SpriteArtOffsetX = 16f;

	private const float SpriteArtOffsetY = 6f;

	private const float ArtHalfWidth = 208f;

	private const float HorizVisibleFrac = 0.4f;

	// Vertical-bisect x positions: dead centre + two off-centre columns at 35% / 65% of the
	// 800-wide screen (280 / 520), rolled 50% / 25% / 25% in Setup.
	private const float LeftColumnX = 280f;

	private const float RightColumnX = 520f;

	private Bisect orientation;

	private Vector2 enterStart;

	private Vector2 restPos;

	private Vector2 exitPos;

	private float fireDir;

	private Vector2 beamOrigin;

	private float elapsed;

	private float beamLen;

	private bool beamActive;

	// Steady-contact accumulator for the beam's bad-collision leeway (WebcamLevel manages it).
	public float BeamContactMs;

	// one-shot latches for the phase side effects (spawn windup / fire beam), so they fire once
	// even if `elapsed` jumps (e.g. the freeze flag).
	private bool windupSpawned;

	private bool beamFired;

	private Quad beam;

	private LazerGenerator windup;

	private Texture2D firstHalfOfSpritesheet;

	private Texture2D secondHalfOfSpritesheet;

	// Degenerate (never-colliding) hitbox — the ship is Collides=false, so this is only a
	// defensive non-null return for the abstract CollisionType.
	private CollisionSimpleCircle noHit = new CollisionSimpleCircle(Vector2.Zero, 0f);

	public bool BeamActive => beamActive;

	public Vector2 BeamOrigin => beamOrigin;

	public float BeamDirection => fireDir;

	public float BeamLength => beamLen;

	public float BeamHalfWidth => HitHalfWidth;

	// Does the beam segment overlap a circle (a mine)? Point-to-segment distance vs halfWidth +
	// radius — the circle cousin of WebcamInterop.HitBeam's mask test. Used to let the beam sweep
	// space mines out of the player's way.
	public static bool BeamHitsCircle(Vector2 origin, float dir, float length, float halfWidth, Vector2 c, float radius)
	{
		if (length <= 0f)
		{
			return false;
		}
		float dx = (float)Math.Cos(dir);
		float dy = (float)Math.Sin(dir);
		float t = (c.X - origin.X) * dx + (c.Y - origin.Y) * dy;
		if (t < 0f) t = 0f; else if (t > length) t = length;
		float ex = c.X - (origin.X + dx * t);
		float ey = c.Y - (origin.Y + dy * t);
		float reach = halfWidth + radius;
		return ex * ex + ey * ey <= reach * reach;
	}

	public override ICollisionType CollisionType
	{
		get
		{
			noHit.Position = base.Position;
			noHit.Radius = 0f;
			return noHit;
		}
	}

	public WebcamMothership(Game game)
		: base(game)
	{
		LoadAnimation(new AnimationData("GFX/Sprites/mothershipB", 4, 4, 1, 16f));
		beam = new Quad(game, Vector2.Zero, 0f, BeamWidth, 0f, 0f);
		base.DrawOrder = 50;
		base.Collides = false;
		PointValue = 0f;
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		beam.LoadContent();
		firstHalfOfSpritesheet = content.Load<Texture2D>("GFX/Sprites/mothershipA");
		secondHalfOfSpritesheet = content.Load<Texture2D>("GFX/Sprites/mothershipB");
	}

	public static WebcamMothership NewWebcamMothership(ComponentBin collection, Game game)
	{
		WebcamMothership ship = collection.Recycle<WebcamMothership>();
		if (ship == null)
		{
			ship = new WebcamMothership(game);
		}
		return ship;
	}

	public void Setup(Bisect orientation, bool allowCenter = true)
	{
		this.orientation = orientation;
		// sideways ship parks with its CENTRE off-screen so only HorizVisibleFrac (40%) shows.
		float horizPeekX = HorizVisibleFrac * (2f * ArtHalfWidth) - ArtHalfWidth;   // ~ -42
		switch (orientation)
		{
		case Bisect.VerticalDown:
		{
			// beam x: when allowCenter (the harder tiers) it's 50% dead centre / 25% left column
			// (35%) / 25% right column (65%); when not (Easy/Medium) it's a 50/50 pick of just the
			// off-centre 35%/65% columns — never centre. The ship parks over it (a fair telegraph of
			// where the beam will fall), slides in from a random side, passes out.
			float bx;
			if (allowCenter)
			{
				int roll = RandomHelper.Random.Next(4);
				bx = (roll < 2) ? 400f : ((roll == 2) ? LeftColumnX : RightColumnX);
			}
			else
			{
				bx = (RandomHelper.Random.Next(2) == 0) ? LeftColumnX : RightColumnX;
			}
			bool enterLeft = RandomHelper.Random.Next(2) == 0;
			enterStart = new Vector2(enterLeft ? OffLeft : OffRight, VertRestY);
			restPos = new Vector2(bx, VertRestY);
			exitPos = new Vector2(enterLeft ? OffRight : OffLeft, VertRestY);
			fireDir = MathHelper.PiOver2;               // down
			break;
		}
		case Bisect.HorizontalFromLeft:
			enterStart = new Vector2(OffLeft, BisectY);
			restPos = new Vector2(horizPeekX, BisectY);
			exitPos = new Vector2(OffLeft, BisectY);      // retreat back the way it came
			fireDir = 0f;                                 // right
			break;
		default: // HorizontalFromRight
			enterStart = new Vector2(OffRight, BisectY);
			restPos = new Vector2(800f - horizPeekX, BisectY);
			exitPos = new Vector2(OffRight, BisectY);
			fireDir = (float)Math.PI;                     // left
			break;
		}
		base.Position = enterStart;
	}

	public override void Initialize()
	{
		base.Initialize();
		interpolationOptions = InterpolationOptions.never;
		fps = 16f;
		elapsed = 0f;
		beamLen = 0f;
		beamActive = false;
		BeamContactMs = 0f;
		windupSpawned = false;
		beamFired = false;
		windup = null;
		color = Color.White;
		// The ship is hand-positioned each frame (PoseAt); keep Speed 0 so base.Update
		// doesn't ALSO drift it along Direction.
		base.Speed = 0f;
		base.Position = enterStart;
	}

	public override void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		base.OnComponentRemoved(e);
		if (e.GameComponent == this && windup != null)
		{
			windup.Free();
			windup = null;
		}
	}

	// PURE position from `elapsed` ms — the deterministic core of the choreography, shared by
	// live Update and the isolation sim (tools/sim/webcam_mothership_sim.py mirrors this). Enter
	// eases OUT to a stop at restPos; parked through charge+fire; leave eases IN toward exitPos.
	public static Vector2 PoseAt(Vector2 enterStart, Vector2 restPos, Vector2 exitPos, float elapsed)
	{
		if (elapsed <= ChargeStart)
		{
			float p = MathHelper.Clamp(elapsed / EnterMs, 0f, 1f);
			return Vector2.Lerp(enterStart, restPos, 1f - (1f - p) * (1f - p));  // ease-out
		}
		if (elapsed < FireEnd)
		{
			return restPos;
		}
		float q = MathHelper.Clamp((elapsed - FireEnd) / LeaveMs, 0f, 1f);
		return Vector2.Lerp(restPos, exitPos, q * q);                            // ease-in
	}

	// Muzzle: FireLead px out from the hull centre along the fire direction.
	private Vector2 ComputeBeamOrigin()
	{
		return restPos + FireLead * MyMath.AngleToVector(fireDir);
	}

	private void BeginCharge()
	{
		beamOrigin = ComputeBeamOrigin();
		windup = LazerGenerator.NewLazerGenerator(collection, base.Game);
		windup.Setup(beamOrigin, 2f, 1f, 0f, 0f);
		windup.SetWindup(WindupMs / 1000f, loop: false);
		collection.Add((GameComponent)(object)windup);
	}

	private void FireBeam()
	{
		if (windup != null)
		{
			collection.Remove((GameComponent)(object)windup);
			windup = null;
		}
		beamOrigin = ComputeBeamOrigin();
		beam.SetProperties(beamOrigin, fireDir, 0f, 0f);
		beamLen = 0f;
		beamActive = true;
		BeamContactMs = 0f;
		sound.PlayCue("lazershotnoloop");
	}

	public override void Update(GameTime gameTime)
	{
		float prevFrame = curframe;
		base.Update(gameTime);
		// A/B sprite-sheet swap on animation wrap (mirrors SpiderHelperMothership/Boss).
		if (curframe < prevFrame && firstHalfOfSpritesheet != null)
		{
			texture = (texture == firstHalfOfSpritesheet) ? secondHalfOfSpritesheet : firstHalfOfSpritesheet;
		}
		// `?wcmothershipfreeze=<ms>` halts the choreography at a chosen phase so a frozen frame
		// can be captured (e.g. beam mid-fire); otherwise advance real time.
		if (DebugFlags.WebcamMothershipFreeze.HasValue)
		{
			elapsed = DebugFlags.WebcamMothershipFreeze.Value;
		}
		else
		{
			elapsed += (float)gameTime.ElapsedGameTime.TotalMilliseconds;
		}
		base.Position = PoseAt(enterStart, restPos, exitPos, elapsed);
		// Phase side effects (one-shot latches so a jumped `elapsed` still fires them once).
		if (!windupSpawned && elapsed >= ChargeStart)
		{
			windupSpawned = true;
			BeginCharge();
		}
		if (windupSpawned && !beamFired && elapsed >= FireStart)
		{
			beamFired = true;
			FireBeam();
		}
		if (beamActive)
		{
			beamLen = MathHelper.Min(FullSpan, FullSpan * ((elapsed - FireStart) / BeamSweepMs));
			beam.SetLength(beamLen);
			if (elapsed >= FireEnd)
			{
				beamActive = false;
				beamLen = 0f;
			}
		}
		if (elapsed >= LeaveEnd)
		{
			Die();
		}
	}

	public override void Draw(GameTime gameTime)
	{
		// Beam first (behind the hull), then the ship, then the charge swarm on top. The
		// LazerGenerator sets Visible=false in its ctor, so its owner must draw it by hand.
		if (beamActive && beamLen > 0f)
		{
			beam.Draw((float)gameTime.TotalGameTime.TotalSeconds);
		}
		// Re-centre the DRAW on Position: the art sits (-16,-6) px off the frame centre, so
		// shifting the sprite (+16,+6)*DrawScale puts its VISUAL centre on Position (= the beam x),
		// fixing the "beam looks right of the ship" impression.
		Vector2 realPos = base.Position;
		base.Position = realPos + new Vector2(SpriteArtOffsetX, SpriteArtOffsetY) * DrawScale;
		base.Draw(gameTime);
		base.Position = realPos;
		if (windup != null)
		{
			((DrawableGameComponent)windup).Draw(gameTime);
		}
	}
}
