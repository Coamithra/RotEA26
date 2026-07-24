using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using EvilAliensWeb.Compat;

namespace EvilAliens;

public class Background : Scene
{
	public delegate void XFadeFinishedEvent();

	private Timer XFade = new Timer(1500f, repeating: false);

	private RenderTarget2D rendertarget;

	private TimeSpan timer = TimeSpan.Zero;

	private BackgroundState state;

	private List<BackgroundImage> backgroundLayers;

	private List<BackgroundImage> foregroundLayers;

	private Timer layerXFadeTimer = new Timer(1000f, repeating: false);

	private Texture2D blank;

	private Vector2 scrollspeed;

	private Vector2 targetscrollspeed;

	private Vector2 scrollspeedinitial;

	private Vector2 scrollspeedreset;

	private Timer scrollspeedchangetimer = new Timer(1333f, repeating: false);

	private float scrollspeedmodifier;

	private float oscilatereach;

	private float oscilatespeed;

	private Texture2D doodad;

	private string doodadname;

	private Vector2 doodadscrollspeed;

	private Vector2 doodadPos;

	private float doodadscale;

	private bool showdoodad;

	private Color doodadcolor;

	private SpriteBlendMode doodadblendmode;

	// Earth fly-by parallax fix (card: "Physics of star background and earth"). While a planet
	// doodad crosses, the BACKGROUND speed (scrollspeedmodifier — which the earth doodad ignores)
	// is ramped down so both starfields nearly freeze and the earth reads as the fastest, nearest
	// object instead of being overtaken by the near drifting stars. doodadStarSlowdown is the target
	// star-speed fraction while THIS doodad crosses (1 = no effect: andromeda / holodeck sim-earth).
	// doodadEnterFromTop is the crossing direction, captured at queue time. These describe the
	// doodad (like doodadscale/doodadcolor); they are NOT a second speed modifier.
	private float doodadStarSlowdown = 1f;

	private bool doodadEnterFromTop = true;

	// Wall-clock durations for the planet-crossing star-slowdown envelope (see
	// DoodadStarSlowdownFactor). The card wants a RAPID slow-down as the earth enters, a long
	// hold while it crosses, then a gentler ~1.6s speed-up as it leaves -- in real seconds, not
	// as fractions of the (very slow, ~90s) crossing, which would stretch the "rapid" ramp to
	// ~14s. They are converted to progress fractions each frame via the doodad's crossing speed.
	private const float DoodadSlowHoldInMs = 350f;

	private const float DoodadSlowRampInMs = 1200f;

	private const float DoodadSlowRampOutMs = 1600f;

	// Asteroid-belt star-slowdown (card: "same as earth but for the asteroid field"). The Level 1
	// sideways asteroid belt is a WAVE (many asteroids over ~42s), not a single crossing doodad, so
	// it can't ride the per-doodad DoodadStarSlowdownFactor position hook. Instead Level1 EXPLICITLY
	// engages/disengages this while the belt wave is active. Same idea + ramp feel as the earth: slow
	// the near stars so the SLOWEST asteroid class (the dim decorative background asteroids, ~0.137
	// design px/ms) reads clearly faster than the fastest near star. At the modifier=1 gameplay
	// baseline the fastest near star is scrollspeedmag(0.039) * maxParallax(3.8) ~= 0.148 px/ms -- as
	// fast as (and sometimes past) the ~0.137 px/ms decor asteroids, which is the "some stars are
	// faster than asteroids" weirdness. BeltStarSlowdown = 0.37 pulls the fastest star to ~0.055 px/ms
	// so the decor asteroid is ~2.5x it and the collidable foreground asteroids (~0.342 px/ms) are 5x+.
	private const float BeltStarSlowdown = 0.37f;

	// Wall-clock ramp durations for the belt envelope, mirroring the doodad ramps: a rapid slow-down
	// as the belt engages and a gentler speed-up as it leaves. Unlike the doodad's crossing-progress
	// mapping, the belt has no position, so the envelope is driven directly by beltRampMs over real
	// time (0 = full star speed, 1 = fully slowed to BeltStarSlowdown).
	private const float BeltRampInMs = 1200f;

	private const float BeltRampOutMs = 1600f;

	private bool beltSlowActive;

	// Join-in-progress catch-up (card 45a4e48d): the LAST latching op the level script ran, so a
	// peer that arrives mid-level can be brought up to the host's scenery instead of the level's
	// initial one. Tracked HERE rather than sniffed off NetSession's send path because
	// OnBackgroundOp early-returns while no peer is connected -- for a listed single-player game
	// that is precisely the window whose ops have to be remembered. null = the script never
	// touched it, which is NOT the same as "set to the default": before the first SetSpeed,
	// targetscrollspeed is still zero while the real scrollspeed is whatever SetSpace()/SetMars()
	// put there at Initialize, so replaying a blind zero would freeze the joiner's starfield.
	private Vector2? netLastSpeed;

	private EvilAliensWeb.Compat.Net.NetBackgroundOp? netLastAlienBase;

	// Which Queue* op owns the doodad currently crossing. Tracked explicitly rather than
	// inferred from doodadname, because the holodeck sim-earth (QueueEarthSim) reuses the hero
	// earth's texture with a different tint/blend and has no wire op of its own -- it sets this
	// to null so it is simply not replayed, instead of a joiner being handed a white
	// star-freezing hero earth in a Tutorial/Classic holodeck.
	private EvilAliensWeb.Compat.Net.NetBackgroundOp? netLastDoodad;

	// Eased 0..1 slowdown amount for the belt: rises to 1 while engaged, falls to 0 while disengaged,
	// stepped each frame in Update by the ramp durations above.
	private float beltSlowAmount;

	private float fadeFactor;

	// Stage 13 reskin: the new space background — a procedural, infinite, scrolling
	// grid of overlapping high-res nebula tiles, crossfaded by starwindow.fx. Set by
	// SetSpace (which then leaves backgroundLayers empty); null for every other scene.
	private ProceduralStarfield starfield;

	// The near (foreground) star layer: a handful of INDIVIDUAL stars (DriftingStars)
	// drawn additively on top of the far nebula, each with its own speed / scale / twinkle
	// so they don't read as one uniform moving wall. Set alongside starfield in SetSpace;
	// null otherwise.
	private DriftingStars nearStars;

	// Holodeck (trial-simulation chamber). When the simulator background is active, Jump()
	// fires a brief, deliberate "projection hiccup" instead of teleporting the layers:
	// a stutter-slip and/or a brightness flicker driven over glitchTimer.
	private bool isHolodeck;

	private BackgroundImage holoGrid;

	// The far (dimmer) holo-grid layer, tracked alongside holoGrid (the near one) so both can be
	// held back and drawn AFTER a fly-by doodad (card 02c0e9c0: the nebula/sim-earth doodad in
	// ClassicAliens was drawing on top of the grid; the grid is a see-through simulation overlay
	// and should render in front of anything projected "inside" it).
	private BackgroundImage holoGridFar;

	private Timer glitchTimer = new Timer(170f, repeating: false);

	private Vector2 glitchSlip;

	// A light pulse that sweeps down through the holodeck grid every once in a while.
	// pulseTimer = one sweep's travel; pulseCooldown = the (randomised) gap between sweeps.
	private Timer pulseTimer = new Timer(1500f, repeating: false);

	private Timer pulseCooldown = new Timer(10000f, repeating: false);

	private bool pulseActive;

	public Vector2 ScrollSpeed => scrollspeed;

	// Average composite colour of the alien-base FLOOR the Level-3 towers stand on -- layer 0's
	// current base texture PLUS the two constant additive 2331-v5 fog layers -- measured offline per
	// variant (tools: the mean-colour snippet in the Wall-tower commit). The floor is SWITCHED five
	// times across the level (SetAlienBase / SetAlienBase2..6, each a StartSwitch crossfade), and the
	// variants differ a lot (a deep (32,56,150) to a lighter (70,77,154)), so a fixed fog colour would
	// be wrong for most of the map. Wall.DrawTowerShafts3D fogs its bases toward THIS so a shaft always
	// recedes into whatever floor is currently scrolling under it. The additive contribution is the
	// same for every state (those layers never switch), so it is folded into each composite.
	private static readonly Dictionary<string, Color> AlienBaseFloorColors = new Dictionary<string, Color>
	{
		{ "GFX/Base/756",    new Color(46, 125, 201) },   // initial (SetAlienBase)
		{ "GFX/Base/756-v5", new Color(49,  77, 176) },   // SetAlienBase2
		{ "GFX/Base/756-v3", new Color(32,  56, 150) },   // SetAlienBase3
		{ "GFX/Base/756-v4", new Color(70,  77, 154) },   // SetAlienBase4
		{ "GFX/Base/756-v6", new Color(62,  97, 182) },   // SetAlienBase5
		{ "GFX/Base/756-v8", new Color(51,  85, 168) },   // SetAlienBase6
	};

	// The current floor colour, crossfaded during a texture switch, or null when there is no
	// alien-base floor (any non-Level-3 scene) or its texture isn't in the table -- the caller then
	// keeps its own default. Layer 0 is the switchable base floor. During a switch the current texture
	// draws at switchTimer.Normalized (fading OUT) and the new one at 1 - Normalized (fading IN), so
	// Color.Lerp(next, current, Normalized) reproduces exactly what is on screen.
	public Color? AlienBaseFloorColor()
	{
		if (backgroundLayers == null || backgroundLayers.Count == 0)
		{
			return null;
		}
		BackgroundImage floor = backgroundLayers[0];
		if (floor.texturenames == null || !AlienBaseFloorColors.TryGetValue(floor.texturenames[0, 0], out Color current))
		{
			return null;
		}
		if (floor.switchTimer.Active && floor.new_texturenames != null
			&& AlienBaseFloorColors.TryGetValue(floor.new_texturenames[0, 0], out Color next))
		{
			return Color.Lerp(next, current, floor.switchTimer.Normalized);
		}
		return current;
	}

	// True while a fly-by doodad (hero earth / sim-earth / small earth / andromeda)
	// is crossing the screen. WaitForDoodadEvent polls this so Level 1 can hold the
	// sideways asteroid-belt phase until the earth has left the screen.
	public bool DoodadActive => showdoodad;

	public event XFadeFinishedEvent OnXFadeFinished;

	// Read-only views of the live layer lists for the eaBgCull census (Compat/BgCullTest),
	// which dry-runs each layer's real Draw to count its per-frame tile decisions.
	internal IReadOnlyList<BackgroundImage> CullTestBackgroundLayers => backgroundLayers;

	internal IReadOnlyList<BackgroundImage> CullTestForegroundLayers => foregroundLayers;

	public Background(Game game)
		: base(game)
	{
		base.DrawOrder = 0;
		scrollspeedchangetimer.Stop();
		glitchTimer.Stop();
		pulseTimer.Stop();
		pulseCooldown.Stop();
		showdoodad = false;
		backgroundLayers = new List<BackgroundImage>();
		foregroundLayers = new List<BackgroundImage>();
		XFade.Stop();
	}

	// Online co-op (card 11.3): the mid-level Background ops the level scripts drive are
	// replicated at these primitives (host-gated inside OnBackgroundOp), so a join peer --
	// whose script never runs -- sees the same scenery beats. Initialize-time setters
	// (SetSpace/SetMars/...) are not hooked: both peers run their own scene Initialize.
	public void SetSpeed(Vector2 speed)
	{
		targetscrollspeed = speed;
		scrollspeedinitial = scrollspeed;
		scrollspeedchangetimer.Reset();
		scrollspeedchangetimer.Start();
		netLastSpeed = speed;
		EvilAliensWeb.Compat.Net.NetSession.OnBackgroundOp(EvilAliensWeb.Compat.Net.NetBackgroundOp.SetSpeed, speed);
	}

	public void QueueSmallEarth()
	{
		if (!showdoodad)
		{
			// Minor "earth in the corner" appearance uses a dedicated small texture
			// (256px) so we don't decode the big hero strip (~1392x1822) just to draw
			// a ~110px dot. scale 0.45 on the 243px disk == the old 730px disk at 0.15.
			doodadname = "GFX/Sprites/earth_small";
			doodad = Content.Load<Texture2D>(doodadname);
			showdoodad = true;
			doodadscale = 0.45f;
			doodadscrollspeed = new Vector2(1f, 1f);
			doodadPos = new Vector2(620f, (float)(-doodad.LogicalHeight()) * doodadscale / 2f);
			doodadcolor = Color.White;
			doodadblendmode = (SpriteBlendMode)1;
			// Milder than the hero earth (small corner planet): slow the stars to ~25%.
			doodadStarSlowdown = 0.25f;
			doodadEnterFromTop = true;
			netLastDoodad = EvilAliensWeb.Compat.Net.NetBackgroundOp.QueueSmallEarth;
			EvilAliensWeb.Compat.Net.NetSession.OnBackgroundOp(EvilAliensWeb.Compat.Net.NetBackgroundOp.QueueSmallEarth, Vector2.Zero);
		}
	}

	public void QueueEarth()
	{
		if (!showdoodad)
		{
			doodadname = "GFX/Sprites/earth";
			doodadcolor = Color.White;
			doodadblendmode = (SpriteBlendMode)1;
			doodad = Content.Load<Texture2D>(doodadname);
			showdoodad = true;
			// Hi-res NASA Blue Marble hero disk: earth.png is now the FULL-res source
			// crop (~1822px disk), cropped to a central vertical strip (tools/earth/
			// build_earth.py), so scale falls 0.8 -> 0.6467 to keep the on-screen size
			// identical (1806*0.6467 == old 730*1.6 == 1168 px) while rendering crisp.
			doodadscale = 0.6467f;
			// X scroll is ZERO: the hero earth only descends vertically, staying
			// horizontally centred so its cropped sides never reach the screen edge.
			doodadscrollspeed = new Vector2(0f, 1.55f);
			doodadcolor = Color.White;
			doodadblendmode = (SpriteBlendMode)1;
			// Hero earth: freeze the starfields so the earth reads as 5x the fastest near
			// ("hero") star -- the "it's closer, so it zooms past" parallax cue (card: "earth
			// animation improvements"). The earth keeps its own descent speed; the stars are
			// what slow. 5x target (at the modifier=1 gameplay baseline): slow = doodadspeed.Y
			// / (5 * maxHeroStarParallax) = 1.55 / (5 * 3.8) ~= 0.082 (DriftingStars caps at 3.8).
			doodadStarSlowdown = 0.082f;
			doodadEnterFromTop = scrollspeed.Y > 0f;
			if (scrollspeed.Y > 0f)
			{
				doodadPos = new Vector2(400f, (float)(-doodad.LogicalHeight()) * doodadscale / 2f);
			}
			else
			{
				doodadPos = new Vector2(400f, 600f + (float)doodad.LogicalHeight() * doodadscale / 2f);
			}
			netLastDoodad = EvilAliensWeb.Compat.Net.NetBackgroundOp.QueueEarth;
			EvilAliensWeb.Compat.Net.NetSession.OnBackgroundOp(EvilAliensWeb.Compat.Net.NetBackgroundOp.QueueEarth, Vector2.Zero);
		}
	}

	public void QueueAndromeda()
	{
		if (!showdoodad)
		{
			doodadcolor = Color.White;
			doodadblendmode = (SpriteBlendMode)1;
			doodadname = "GFX/Sprites/andromeda";
			doodad = Content.Load<Texture2D>(doodadname);
			showdoodad = true;
			// On-screen footprint is pinned to AndromedaDesignWidth (in 800x600 design
			// space) regardless of the texture's pixel resolution, so an HD drop-in (tools/nebula/
			// build_nebula.py output) stays the same size on screen -- more texels = crisper
			// at high-res windows, not bigger. 840 preserves the original 840px@scale-1 look.
			const float AndromedaDesignWidth = 840f;
			doodadscale = AndromedaDesignWidth / (float)doodad.LogicalWidth();
			doodadscrollspeed = new Vector2(1f, 1f);
			// A distant galaxy, not a planet — no star slowdown (also clears a prior earth's value).
			doodadStarSlowdown = 1f;
			if (scrollspeed.Y > 0f)
			{
				doodadPos = new Vector2(400f, (float)(-doodad.LogicalHeight()) * doodadscale / 2f);
			}
			else
			{
				doodadPos = new Vector2(400f, 600f + (float)doodad.LogicalHeight() * doodadscale / 2f);
			}
			netLastDoodad = EvilAliensWeb.Compat.Net.NetBackgroundOp.QueueAndromeda;
			EvilAliensWeb.Compat.Net.NetSession.OnBackgroundOp(EvilAliensWeb.Compat.Net.NetBackgroundOp.QueueAndromeda, Vector2.Zero);
		}
	}

	protected void fadeBackBufferToWhite(float factor)
	{
		factor = MathHelper.Clamp(factor, 0f, 1f);
		int num = Convert.ToInt16(factor * 255f);
		// Stage 10: full-screen fade in 800x600 design space (scaled by RenderScale.Matrix).
		base.SpriteBatch.Draw(blank, new Rectangle(0, 0, 800, 600), new Color(byte.MaxValue, byte.MaxValue, byte.MaxValue, (byte)num));
	}

	protected void fadeBackBufferToBlack(float factor)
	{
		factor = MathHelper.Clamp(factor, 0f, 1f);
		base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
		base.SpriteBatch.Draw(blank, new Rectangle(0, 0, 800, 600), new Color(new Vector4(0f, 0f, 0f, factor)));
	}

	public override void Update(GameTime gameTime)
	{
		timer += gameTime.ElapsedGameTime;
		if (showdoodad)
		{
			doodadPos += doodadscrollspeed * scrollspeed * (float)gameTime.ElapsedGameTime.TotalMilliseconds;
			if (scrollspeed.Y > 0f && ((doodadPos.Y > 600f + (float)doodad.LogicalHeight() * doodadscale / 2f) | (doodadPos.X > 800f + (float)doodad.LogicalWidth() * doodadscale / 2f)))
			{
				showdoodad = false;
			}
			if (scrollspeed.Y < 0f && ((doodadPos.Y < (float)(-doodad.LogicalHeight()) * doodadscale / 2f) | (doodadPos.X > 800f + (float)doodad.LogicalWidth() * doodadscale / 2f)))
			{
				showdoodad = false;
			}
		}
		scrollspeedchangetimer.Update(gameTime);
		if (scrollspeedchangetimer.Active)
		{
			scrollspeed.X = MathHelper.Lerp(scrollspeedinitial.X, targetscrollspeed.X, 1f - scrollspeedchangetimer.Normalized);
			scrollspeed.Y = MathHelper.Lerp(scrollspeedinitial.Y, targetscrollspeed.Y, 1f - scrollspeedchangetimer.Normalized);
		}
		if (scrollspeedchangetimer.Finished)
		{
			scrollspeed = targetscrollspeed;
			scrollspeedchangetimer.Reset();
		}
		// Fold the planet-crossing star-slowdown in LOCALLY, never into the field. The End
		// state leaves scrollspeedmodifier unwritten for its first 3.5s, so multiplying the
		// factor into the field there would compound it geometrically frame-over-frame and
		// slam the stars to a halt in a few frames (then snap back once End starts rewriting
		// the field). A per-frame local keeps the doodad factor applied exactly once.
		UpdateBeltSlowdown(gameTime);
		// Combine the doodad and belt star-slowdowns by taking the STRONGER (smaller) factor, not the
		// product. In Level 1 they're temporally disjoint (the belt gates on the earth leaving via
		// WaitForDoodadEvent), so one is always 1 and min == product. But the Level 1 ATTRACT demo
		// (Demo1) has no such gate -- its earth fly-by can still be crossing when the belt engages --
		// and multiplying would double-slow the stars to a crawl. min() applies whichever slowdown is
		// currently deeper and never stacks them, so the composition is correct on both paths.
		float starSlowdown = MathHelper.Min(DoodadStarSlowdownFactor(), BeltStarSlowdownFactor());
		float effectiveModifier = scrollspeedmodifier * starSlowdown;
		// ?bgfreeze=<designX>: hold every layer still with a tile BOUNDARY parked at that design
		// column (boundaries sit at position.X + k*realsize.X, so position.X = designX mod realsize.X
		// puts one there). The layers scroll at six different speeds, so a tiling artifact can only
		// be screenshotted comparably before/after if it stops moving. position.Y is deliberately
		// left alone -- the marsloop floor sits at 300 by design.
		bool frozen = DebugFlags.BgFreeze.HasValue;
		foreach (BackgroundImage backgroundLayer in backgroundLayers)
		{
			if (frozen)
			{
				backgroundLayer.position.X = MyMath.Mod(DebugFlags.BgFreeze.Value, backgroundLayer.realsize.X);
			}
			else
			{
				backgroundLayer.Move(scrollspeed * (float)gameTime.ElapsedGameTime.TotalMilliseconds * effectiveModifier);
			}
		}
		foreach (BackgroundImage foregroundLayer in foregroundLayers)
		{
			if (frozen)
			{
				foregroundLayer.position.X = MyMath.Mod(DebugFlags.BgFreeze.Value, foregroundLayer.realsize.X);
			}
			else
			{
				foregroundLayer.Move(scrollspeed * (float)gameTime.ElapsedGameTime.TotalMilliseconds * effectiveModifier);
			}
		}
		Vector2 starDelta = frozen
			? Vector2.Zero
			: scrollspeed * (float)gameTime.ElapsedGameTime.TotalMilliseconds * effectiveModifier;
		if (starfield != null)
		{
			starfield.Advance(starDelta);
		}
		// The near stars get the same base scroll delta (each applies its own per-star
		// parallax) plus the elapsed time for their twinkle.
		if (nearStars != null)
		{
			nearStars.Advance(starDelta, (float)gameTime.ElapsedGameTime.TotalMilliseconds);
		}
		UpdateHoloGlitch(gameTime);
		UpdateHoloPulse(gameTime);
		switch (state)
		{
		case BackgroundState.LeavingHyperspace:
			if (timer.TotalMilliseconds > 1.0)
			{
				fadeFactor -= 0.0005f * Convert.ToSingle(gameTime.ElapsedGameTime.TotalMilliseconds);
				if (fadeFactor < 0f)
				{
					fadeFactor = 0f;
				}
				scrollspeedmodifier = 1f + fadeFactor * 10f;
			}
			break;
		case BackgroundState.End:
			if (timer.TotalMilliseconds > 3500.0)
			{
				fadeFactor += 0.0005f * Convert.ToSingle(gameTime.ElapsedGameTime.TotalMilliseconds);
				if (fadeFactor < 0f)
				{
					fadeFactor = 0f;
				}
				scrollspeedmodifier = 1f + fadeFactor * 30f;
			}
			break;
		}
		// Earth fly-by parallax: the planet-crossing slowdown is folded into effectiveModifier
		// at the top of Update (a per-frame local, applied to the star/bg moves), NOT mutated
		// into scrollspeedmodifier here — see the comment there. The earth doodad moves on
		// scrollspeed alone, so only the starfields/bg layers slow, letting the earth read as
		// the fastest, nearest object.
		if (XFade.Active)
		{
			XFade.Update(gameTime);
			if (XFade.Finished && this.OnXFadeFinished != null)
			{
				this.OnXFadeFinished();
			}
		}
	}

	// Star-slowdown envelope for the earth fly-by (cards: "Physics of star background and earth",
	// "earth animation improvements"). Returns the factor (<= 1) to multiply into scrollspeedmodifier
	// so the starfields slow while a planet doodad crosses; 1 means no change. Keyed to the doodad's
	// own on-screen progress (its centre vs the screen edges, accounting for the disk's half-height)
	// so it is robust to any scrollspeed and to both crossing directions. Shape over the crossing:
	//   hold full -> RAPID ramp down to doodadStarSlowdown -> long hold slow -> ~1.6s ramp back to
	// full as it leaves. The ramp durations are WALL-CLOCK seconds (converted to progress via the
	// doodad's crossing speed), so the slow-down stays snappy even though the earth itself drifts
	// across over ~90s -- the near-frozen stars make the slow earth read as the fast, nearest object.
	private float DoodadStarSlowdownFactor()
	{
		if (!showdoodad || doodad == null || doodadStarSlowdown >= 1f)
		{
			return 1f;
		}
		float halfH = (float)doodad.LogicalHeight() * doodadscale * 0.5f;
		// enter/exit edges of the doodad centre across the screen, by crossing direction.
		float enter = doodadEnterFromTop ? (0f - halfH) : (600f + halfH);
		float exit = doodadEnterFromTop ? (600f + halfH) : (0f - halfH);
		float span = exit - enter;
		if (Math.Abs(span) < 0.0001f)
		{
			return 1f;
		}
		float prog = (doodadPos.Y - enter) / span; // 0 = just appearing, 1 = fully gone
		// Convert the wall-clock ramp durations to progress fractions via the doodad's CURRENT
		// crossing speed, so the slow-down is rapid and the speed-up is ~1.6s no matter how
		// slowly the earth actually descends. progress-per-ms = |doodad vertical speed| / span.
		// (During a SetSpeed lerp this uses the instantaneous speed, so the ramp duration is
		// momentarily off; negligible -- the ramps are ~seconds against a ~90s crossing.)
		float progPerMs = Math.Abs(doodadscrollspeed.Y * scrollspeed.Y) / Math.Abs(span);
		float holdIn, rampIn, rampOut;
		if (progPerMs > 1E-07f)
		{
			holdIn = DoodadSlowHoldInMs * progPerMs;   // brief: a sliver of earth shows first
			rampIn = DoodadSlowRampInMs * progPerMs;   // rapid star deceleration on entry
			rampOut = DoodadSlowRampOutMs * progPerMs; // gentler re-acceleration on exit
		}
		else
		{
			// Doodad momentarily not scrolling (scrollspeed ~0): fall back to fixed fractions.
			holdIn = 0.04f;
			rampIn = 0.12f;
			rampOut = 0.18f;
		}
		// Keep each ramp finite (no div-by-zero) and ensure in + out still fit inside [0,1].
		holdIn = MathHelper.Clamp(holdIn, 0f, 0.1f);
		rampIn = MathHelper.Clamp(rampIn, 0.002f, 0.45f);
		rampOut = MathHelper.Clamp(rampOut, 0.002f, 0.45f);
		float t; // 0 = full star speed, 1 = fully slowed
		if (prog < holdIn)
		{
			t = 0f;
		}
		else if (prog < holdIn + rampIn)
		{
			t = (prog - holdIn) / rampIn;
		}
		else if (prog <= 1f - rampOut)
		{
			t = 1f;
		}
		else
		{
			t = (1f - prog) / rampOut;
		}
		t = MathHelper.SmoothStep(0f, 1f, MathHelper.Clamp(t, 0f, 1f));
		return MathHelper.Lerp(1f, doodadStarSlowdown, t);
	}

	// Join-in-progress catch-up (card 45a4e48d). A peer arriving mid-level ran its OWN scene
	// Initialize, so it starts from the level's INITIAL scenery and -- the script being
	// host-only (11.2 sim-split) -- will never run the ops that already fired. Replay them as
	// the same reliable NetBackgroundOp events the live path uses, so the client applies them
	// through the identical GameScene.NetApplyBackgroundOp switch and nothing needs a second
	// code path. Host-only + peer-gated inside OnBackgroundOp; called once, from the EvReady
	// handler (the client's scene-up edge) -- NOT at pairing time, when the joiner has no
	// GameScene yet and its imminent Initialize would clobber the lot.
	//
	// Order matters for the doodad pair only: the kind before its position, because Queue* parks
	// a fresh doodad back at its entry point and SetDoodadPos then moves it to the host's.
	// (Speed goes first for readability, NOT for correctness: SetSpeed only retargets a 1333ms
	// lerp, so scrollspeed -- which is what Queue* reads for its entry/exit edge -- has not moved
	// by the time the doodad op is applied. The joiner's own Initialize already gave it the same
	// scroll direction as the host, which is what actually makes the edge agree.)
	//
	// `emit` is the sink rather than a hard call to NetSession.OnBackgroundOp so the burst can
	// also be captured into a list -- which is what makes the whole catch-up testable as a pure
	// encode->apply function in one tab (GameScene.NetCatchUpSelfTest / eaNetBgTest), with no
	// second peer and no timing.
	internal void NetReplayCatchUp(Action<EvilAliensWeb.Compat.Net.NetBackgroundOp, Vector2> emit)
	{
		if (netLastSpeed.HasValue)
		{
			emit(EvilAliensWeb.Compat.Net.NetBackgroundOp.SetSpeed, netLastSpeed.Value);
		}
		if (netLastAlienBase.HasValue)
		{
			emit(netLastAlienBase.Value, Vector2.Zero);
		}
		if (beltSlowActive)
		{
			emit(EvilAliensWeb.Compat.Net.NetBackgroundOp.EngageBeltSlowdown, Vector2.Zero);
		}
		if (showdoodad && netLastDoodad.HasValue)
		{
			emit(netLastDoodad.Value, Vector2.Zero);
			emit(EvilAliensWeb.Compat.Net.NetBackgroundOp.SetDoodadPos, doodadPos);
		}
	}

	// Catch-up only: place an in-flight doodad where the host has it, so the joiner picks the
	// fly-by up mid-crossing instead of watching it descend again from the top (and running the
	// star-freeze envelope for a whole extra crossing). No-op if nothing is showing -- the
	// preceding Queue* is what decides that, and a client whose own doodad slot was busy
	// simply keeps its own.
	// Debug only (the eaNetBgTest round-trip): put the scenery back to what a peer that just ran
	// its own scene Initialize would hold, so the replayed burst has something real to restore.
	// Reset() covers the doodad, the belt and the latches; targetscrollspeed is cleared on top
	// because Reset() leaves it (it only rewinds the LIVE scrollspeed), and a stale target would
	// make the speed leg of the diff pass without the burst doing anything.
	//
	// What this canNOT wipe is the alien-base tile: Reset() deliberately leaves layer 0 alone and
	// the level's initial texture name isn't recoverable from here, so a SetAlienBaseN in the
	// burst is re-applied but reads identical either way. The self-test prints the ops it
	// replayed precisely so that gap is visible instead of hiding inside a PASS.
	internal void NetTestWipe()
	{
		Reset();
		targetscrollspeed = Vector2.Zero;
		scrollspeedchangetimer.Stop();
	}

	// A hostile peer is on the other end of this once a game is publicly listed, and a NaN would
	// wedge the doodad forever: BOTH exit tests in Update are false for NaN, so showdoodad never
	// clears and DoodadStarSlowdownFactor poisons the star modifier -- a permanently frozen
	// starfield. Cheap to refuse here.
	internal void NetSetDoodadPos(Vector2 pos)
	{
		if (showdoodad && float.IsFinite(pos.X) && float.IsFinite(pos.Y))
		{
			doodadPos = pos;
		}
	}

	// The live catch-up state, for the eaNetBg() verification dump (card 45a4e48d): one
	// parseable line so a JIP joiner's scenery can be DIFFED against the host's instead of
	// eyeballed off a screenshot of something that moves every frame.
	//
	// Deliberately reports the state the ops CONSUME, not the netLast* bookkeeping they replay:
	// printing the latches would make the round-trip self-test a tautology (latch -> wire ->
	// latch), passing even if SetSpeed/SetAlienBaseN never touched the scenery. speed is the
	// lerp TARGET rather than the live scrollspeed because SetSpeed only retargets a 1333ms
	// ramp -- the live value is still mid-flight the instant the burst is applied.
	internal string NetStateLine()
	{
		string doodad = showdoodad
			? (netLastDoodad.HasValue ? netLastDoodad.Value.ToString() : (doodadname ?? "?") + "(nosync)")
				+ "@" + doodadPos.X.ToString("0.#") + "," + doodadPos.Y.ToString("0.#")
			: "-";
		return "speed=" + targetscrollspeed.X.ToString("0.####") + "," + targetscrollspeed.Y.ToString("0.####")
			+ " base=" + NetBaseTextureName()
			+ " belt=" + (beltSlowActive ? "1" : "0")
			+ " doodad=" + doodad;
	}

	// The floor tile layer 0 is actually showing (or switching to) -- what SetAlienBaseN really
	// changes, as opposed to the op we remembered running.
	private string NetBaseTextureName()
	{
		if (backgroundLayers == null || backgroundLayers.Count == 0)
		{
			return "-";
		}
		BackgroundImage layer = backgroundLayers[0];
		string[,] names = layer.new_texturenames ?? layer.texturenames;
		return (names != null && names.GetLength(0) > 0 && names.GetLength(1) > 0)
			? (names[0, 0] ?? "-")
			: "-";
	}

	// Engage the asteroid-belt star-slowdown (Level 1 sideways belt phase). Called from
	// Level1.spawner_OnFinished when the belt scroll speed is set; the near stars ramp DOWN over
	// BeltRampInMs so the fastest star drops below the slowest asteroid. Idempotent.
	//
	// Death-mid-belt is self-correcting: GameEventList.RevertToCheckpoint clears active events
	// WITHOUT terminating them, so a death during the belt drops the AsteroidSpawner's OnFinished
	// (i.e. Disengage never fires for that run). It's harmless because the ONLY checkpoint reachable
	// from mid-belt is the pre-belt one (Level1/Demo1 place their checkpoint before the belt gate),
	// so the belt replays: Engage is called again (fine -- the belt IS active again) and the fresh
	// spawner's OnFinished eventually disengages. INVARIANT: don't place a checkpoint INSIDE the
	// belt, or a death there would strand beltSlowActive = true with no re-engage to correct it.
	public void EngageBeltSlowdown()
	{
		beltSlowActive = true;
		EvilAliensWeb.Compat.Net.NetSession.OnBackgroundOp(EvilAliensWeb.Compat.Net.NetBackgroundOp.EngageBeltSlowdown, Vector2.Zero);
	}

	// Disengage the belt slowdown (Level 1 belt wave finished). Called from the AsteroidSpawner's
	// OnFinished; the stars ramp BACK UP to full speed over BeltRampOutMs. Idempotent. (A death
	// mid-belt can skip this call -- see EngageBeltSlowdown; it's self-correcting.)
	public void DisengageBeltSlowdown()
	{
		beltSlowActive = false;
		EvilAliensWeb.Compat.Net.NetSession.OnBackgroundOp(EvilAliensWeb.Compat.Net.NetBackgroundOp.DisengageBeltSlowdown, Vector2.Zero);
	}

	// Step the belt slowdown envelope each frame: rise to full slow while engaged (BeltRampInMs),
	// fall back to none while disengaged (BeltRampOutMs). Wall-clock driven (no doodad position),
	// so the ramp feel matches the earth's regardless of the belt's long, variable duration.
	private void UpdateBeltSlowdown(GameTime gameTime)
	{
		float dt = (float)gameTime.ElapsedGameTime.TotalMilliseconds;
		if (beltSlowActive)
		{
			beltSlowAmount += dt / BeltRampInMs;
		}
		else
		{
			beltSlowAmount -= dt / BeltRampOutMs;
		}
		beltSlowAmount = MathHelper.Clamp(beltSlowAmount, 0f, 1f);
	}

	// The belt star-slowdown factor (<= 1), combined with the doodad factor via MathHelper.Min in
	// Update (see there). Smoothstep-eased like the doodad envelope so engage/disengage aren't linear
	// jerks.
	private float BeltStarSlowdownFactor()
	{
		if (beltSlowAmount <= 0f)
		{
			return 1f;
		}
		float t = MathHelper.SmoothStep(0f, 1f, beltSlowAmount);
		return MathHelper.Lerp(1f, BeltStarSlowdown, t);
	}

	public void DrawForeground(GameTime gameTime)
	{
		if (XFade.Active && rendertarget != null)
		{
			// Normalized counts DOWN (1 -> 0, fraction of the fade REMAINING), so
			// 1 - Normalized ramps 0 -> 1: the clean background copy progressively
			// COVERS the objects (enemies/bullets/dead ship) — they dissolve out into
			// the untouched background. (rendertarget is populated by Draw, which runs
			// first this frame; the null guard is belt-and-braces for that ordering.)
			float num = 1f - XFade.Normalized;
			base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
			// Stage 10: render-sized RT -> 1:1 identity composite (DrawPresent).
			base.SpriteBatch.DrawPresent(rendertarget, Vector2.Zero, Vector2.Zero, 1f, new Color(new Vector4(1f, 1f, 1f, num)));
		}
		foreach (BackgroundImage foregroundLayer in foregroundLayers)
		{
			foregroundLayer.Draw(base.SpriteBatch, gameTime);
		}
	}

	public override void Draw(GameTime gameTime)
	{
		if (XFade.Active)
		{
			base.SpriteBatch.Flush();
			EnsureRenderTarget();
			base.GraphicsDevice.SetRenderTarget(0, rendertarget);
			// Reset the target to opaque black each frame: it's a PreserveContents RT, so
			// without this the additive starfield accumulates over last frame's pixels and
			// lights up. RGB is fully redrawn below; the alpha is sealed to 1 before the
			// target is composited (see SealAlpha) so the DrawForeground overlay — which
			// the XBLIG's alpha-less Bgr565 RT let cover by tint alpha alone — covers
			// uniformly instead of inheriting the veil/cloud draws' eroded alpha.
			base.GraphicsDevice.Clear(Color.Black);
		}
		if (starfield != null)
		{
			// Render-space, additive, custom-window batch — now through the wrapper (BeginCustom
			// flushes the open design-space batch itself, so no manual Flush needed here).
			starfield.Brightness = DebugToggles.Active ? DebugToggles.StarfieldBrightness : 1f;
			starfield.Draw(base.SpriteBatch);
		}
		// Near drifting stars ON TOP of the far nebula (its own render-space additive wrapper batch).
		// Drawn before the doodad/planet so the planet still occludes them.
		if (nearStars != null)
		{
			nearStars.Brightness = DebugToggles.Active ? DebugToggles.StarfieldBrightness : 1f;
			nearStars.Draw(base.SpriteBatch);
		}
		foreach (BackgroundImage backgroundLayer in backgroundLayers)
		{
			// The holodeck's cyan simulation grid is a see-through overlay -- it should render IN
			// FRONT of a fly-by doodad (a projected planet/nebula "inside" the simulation), not
			// behind it. Both grid layers are held back and drawn after the doodad below (card
			// 02c0e9c0). Every other scene's backgroundLayers has neither field set, so this is a
			// no-op there.
			if (backgroundLayer == holoGrid || backgroundLayer == holoGridFar)
			{
				continue;
			}
			backgroundLayer.Draw(base.SpriteBatch, gameTime);
		}
		base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
		if (showdoodad)
		{
			base.SpriteBatch.BlendMode = doodadblendmode;
			base.SpriteBatch.Draw(doodad, doodadPos, 0f, doodadscale, center: true, doodadcolor);
			base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
		}
		if (holoGridFar != null)
		{
			holoGridFar.Draw(base.SpriteBatch, gameTime);
		}
		if (holoGrid != null)
		{
			holoGrid.Draw(base.SpriteBatch, gameTime);
		}
		DrawHoloPulse();
		float factor = Convert.ToSingle((double)(0.15f + oscilatereach) + Math.Sin((double)oscilatespeed * timer.TotalMilliseconds) * (double)oscilatereach);
		if (!DebugToggles.Active || DebugToggles.BgVeil)
		{
			fadeBackBufferToBlack(factor);
		}
		if (fadeFactor > 0f)
		{
			fadeBackBufferToWhite(fadeFactor);
		}
		if (XFade.Active)
		{
			base.SpriteBatch.Flush();
			// The background just captured includes partial-alpha veil/cloud draws that
			// erode this RGBA8 target's alpha below 1. Seal it opaque before it's reused
			// as the DrawForeground dissolve overlay, so that overlay covers the objects
			// uniformly by its own tint alpha (matching the alpha-less Bgr565 original).
			base.SpriteBatch.SealAlpha(blank, RenderScale.Width, RenderScale.Height);
			base.GraphicsDevice.SetRenderTarget(0, (RenderTarget2D)null);
			base.SpriteBatch.BlendMode = (SpriteBlendMode)0;
			// Stage 10: render-sized RT -> 1:1 identity composite (DrawPresent).
			base.SpriteBatch.DrawPresent(rendertarget, Vector2.Zero, Vector2.Zero, 1f, Color.White);
			base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
		}
	}

	internal void FadeOut()
	{
		timer = default(TimeSpan);
		state = BackgroundState.End;
		fadeFactor = 0f;
	}

	public void SetAlienBase6()
	{
		backgroundLayers[0].new_texturenames = new string[1, 1];
		backgroundLayers[0].new_textures = new Texture2D[1, 1];
		backgroundLayers[0].new_textures[0, 0] = Content.Load<Texture2D>("GFX/Base/756-v8");
		backgroundLayers[0].new_texturenames[0, 0] = "GFX/Base/756-v8";
		backgroundLayers[0].StartSwitch();
		netLastAlienBase = EvilAliensWeb.Compat.Net.NetBackgroundOp.SetAlienBase6;
		EvilAliensWeb.Compat.Net.NetSession.OnBackgroundOp(EvilAliensWeb.Compat.Net.NetBackgroundOp.SetAlienBase6, Vector2.Zero);
	}

	public void SetAlienBase5()
	{
		backgroundLayers[0].new_texturenames = new string[1, 1];
		backgroundLayers[0].new_textures = new Texture2D[1, 1];
		backgroundLayers[0].new_textures[0, 0] = Content.Load<Texture2D>("GFX/Base/756-v6");
		backgroundLayers[0].new_texturenames[0, 0] = "GFX/Base/756-v6";
		backgroundLayers[0].StartSwitch();
		netLastAlienBase = EvilAliensWeb.Compat.Net.NetBackgroundOp.SetAlienBase5;
		EvilAliensWeb.Compat.Net.NetSession.OnBackgroundOp(EvilAliensWeb.Compat.Net.NetBackgroundOp.SetAlienBase5, Vector2.Zero);
	}

	public void SetAlienBase4()
	{
		backgroundLayers[0].new_texturenames = new string[1, 1];
		backgroundLayers[0].new_textures = new Texture2D[1, 1];
		backgroundLayers[0].new_textures[0, 0] = Content.Load<Texture2D>("GFX/Base/756-v4");
		backgroundLayers[0].new_texturenames[0, 0] = "GFX/Base/756-v4";
		backgroundLayers[0].StartSwitch();
		netLastAlienBase = EvilAliensWeb.Compat.Net.NetBackgroundOp.SetAlienBase4;
		EvilAliensWeb.Compat.Net.NetSession.OnBackgroundOp(EvilAliensWeb.Compat.Net.NetBackgroundOp.SetAlienBase4, Vector2.Zero);
	}

	public void SetAlienBase3()
	{
		backgroundLayers[0].new_texturenames = new string[1, 1];
		backgroundLayers[0].new_textures = new Texture2D[1, 1];
		backgroundLayers[0].new_textures[0, 0] = Content.Load<Texture2D>("GFX/Base/756-v3");
		backgroundLayers[0].new_texturenames[0, 0] = "GFX/Base/756-v3";
		backgroundLayers[0].StartSwitch();
		netLastAlienBase = EvilAliensWeb.Compat.Net.NetBackgroundOp.SetAlienBase3;
		EvilAliensWeb.Compat.Net.NetSession.OnBackgroundOp(EvilAliensWeb.Compat.Net.NetBackgroundOp.SetAlienBase3, Vector2.Zero);
	}

	public void SetAlienBase2()
	{
		backgroundLayers[0].new_texturenames = new string[1, 1];
		backgroundLayers[0].new_textures = new Texture2D[1, 1];
		backgroundLayers[0].new_textures[0, 0] = Content.Load<Texture2D>("GFX/Base/756-v5");
		backgroundLayers[0].new_texturenames[0, 0] = "GFX/Base/756-v5";
		backgroundLayers[0].StartSwitch();
		netLastAlienBase = EvilAliensWeb.Compat.Net.NetBackgroundOp.SetAlienBase2;
		EvilAliensWeb.Compat.Net.NetSession.OnBackgroundOp(EvilAliensWeb.Compat.Net.NetBackgroundOp.SetAlienBase2, Vector2.Zero);
	}

	public void SetAlienBase()
	{
		BackgroundImage backgroundImage = new BackgroundImage();
		backgroundLayers.Clear();
		foregroundLayers.Clear();
		isHolodeck = false;
		holoGrid = null;
		holoGridFar = null;
		DisposeStarfield();
		backgroundImage.position = Vector2.Zero;
		backgroundImage.textures = new Texture2D[1, 1];
		backgroundImage.texturenames = new string[1, 1];
		backgroundImage.textures[0, 0] = Content.Load<Texture2D>("GFX/Base/756");
		backgroundImage.texturenames[0, 0] = "GFX/Base/756";
		backgroundImage.size = 1f;
		backgroundImage.realsize.X = (float)backgroundImage.textures[0, 0].LogicalWidth() * backgroundImage.size;
		backgroundImage.realsize.Y = (float)backgroundImage.textures[0, 0].LogicalHeight() * backgroundImage.size;
		backgroundImage.scrollspeedmodifier = 0.66f;
		backgroundLayers.Add(backgroundImage);
		backgroundImage = new BackgroundImage();
		backgroundImage.position = Vector2.Zero;
		backgroundImage.textures = new Texture2D[1, 1];
		backgroundImage.texturenames = new string[1, 1];
		backgroundImage.textures[0, 0] = Content.Load<Texture2D>("GFX/Base/2331-v5");
		backgroundImage.texturenames[0, 0] = "GFX/Base/2331-v5";
		backgroundImage.size = 1f;
		backgroundImage.realsize.X = (float)backgroundImage.textures[0, 0].LogicalWidth() * backgroundImage.size;
		backgroundImage.realsize.Y = (float)backgroundImage.textures[0, 0].LogicalHeight() * backgroundImage.size;
		backgroundImage.scrollspeedmodifier = 0.52f;
		backgroundImage.blendMode = (SpriteBlendMode)2;
		backgroundLayers.Add(backgroundImage);
		backgroundImage = new BackgroundImage();
		backgroundImage.position = new Vector2(400f, 300f);
		backgroundImage.textures = new Texture2D[1, 1];
		backgroundImage.texturenames = new string[1, 1];
		backgroundImage.textures[0, 0] = Content.Load<Texture2D>("GFX/Base/2331-v5");
		backgroundImage.texturenames[0, 0] = "GFX/Base/2331-v5";
		backgroundImage.size = 1f;
		backgroundImage.realsize.X = (float)backgroundImage.textures[0, 0].LogicalWidth() * backgroundImage.size;
		backgroundImage.realsize.Y = (float)backgroundImage.textures[0, 0].LogicalHeight() * backgroundImage.size;
		backgroundImage.scrollspeedmodifier = 0.8f;
		backgroundImage.blendMode = (SpriteBlendMode)2;
		backgroundLayers.Add(backgroundImage);
		scrollspeedreset = new Vector2(0f, 4.5f) / 16.666666f;
		oscilatereach = 0.233f;
		oscilatespeed = 0.0003f;
		Reset();
	}

	public void SetSpace()
	{
		backgroundLayers.Clear();
		foregroundLayers.Clear();
		isHolodeck = false;
		holoGrid = null;
		holoGridFar = null;
		// Stage 13 reskin: replace the three hand-placed Starfield2/tileablestarfield
		// layers with a deterministic, infinite, scrolling grid of overlapping high-res
		// nebula tiles, crossfaded by starwindow.fx. See ProceduralStarfield. The legacy
		// backgroundLayers list stays empty for space; Update/Draw drive `starfield`.
		DisposeStarfield();
		starfield = new ProceduralStarfield();
		starfield.LoadContent(Content, base.GraphicsDevice);
		// Near (foreground) star layer: a handful of individual drifting stars cut from the
		// space_near tiles, each with its own speed / scale / twinkle (see DriftingStars).
		nearStars = new DriftingStars();
		nearStars.LoadContent(Content, base.GraphicsDevice);
		scrollspeedreset = new Vector2(0f, 0.2f) / 16.666666f;
		oscilatereach = 0.1f;
		oscilatespeed = 0.001f;
		Reset();
	}

	public void SetSimpleSpace()
	{
		// Holodeck / trial-simulation chamber. Space here is PROJECTED, not real: the stars
		// stay (a space-combat sim that showed no stars would be dull AND a poor simulation)
		// but are cool-tinted + dimmed so they read as part of the projection, while the grid
		// becomes the hero -- a bright cyan near layer over a dim far layer for depth. A gentle
		// pulse breathes (oscilate*), and Jump() fires deliberate holo-glitches (see Update).
		backgroundLayers.Clear();
		foregroundLayers.Clear();
		DisposeStarfield();
		// simulated stars, far: cool + dim, straight alpha
		BackgroundImage backgroundImage = new BackgroundImage();
		backgroundImage.color = new Color(0.45f, 0.7f, 0.95f, 1f);
		backgroundImage.position = Vector2.Zero;
		backgroundImage.textures = new Texture2D[1, 1];
		backgroundImage.texturenames = new string[1, 1];
		backgroundImage.textures[0, 0] = Content.Load<Texture2D>("GFX/Game/Starfield2");
		backgroundImage.texturenames[0, 0] = "GFX/Game/Starfield2";
		backgroundImage.size = 1.5f;
		backgroundImage.realsize.X = (float)backgroundImage.textures[0, 0].LogicalWidth() * backgroundImage.size;
		backgroundImage.realsize.Y = (float)backgroundImage.textures[0, 0].LogicalHeight() * backgroundImage.size;
		backgroundImage.scrollspeedmodifier = 0.5f;
		backgroundLayers.Add(backgroundImage);
		// simulated stars, near: additive cool glint
		backgroundImage = new BackgroundImage();
		backgroundImage.color = new Color(0.3f, 0.55f, 0.8f, 1f);
		backgroundImage.position = new Vector2(400f, 0f);
		backgroundImage.textures = new Texture2D[1, 1];
		backgroundImage.texturenames = new string[1, 1];
		backgroundImage.textures[0, 0] = Content.Load<Texture2D>("GFX/Game/Starfield2");
		backgroundImage.texturenames[0, 0] = "GFX/Game/Starfield2";
		backgroundImage.size = 2f;
		backgroundImage.realsize.X = (float)backgroundImage.textures[0, 0].LogicalWidth() * backgroundImage.size;
		backgroundImage.realsize.Y = (float)backgroundImage.textures[0, 0].LogicalHeight() * backgroundImage.size;
		backgroundImage.scrollspeedmodifier = 1.2f;
		backgroundImage.blendMode = (SpriteBlendMode)2;
		backgroundLayers.Add(backgroundImage);
		// holo-grid, far: dim, large, slow -> depth
		backgroundImage = new BackgroundImage();
		backgroundImage.color = new Color(0.22f, 0.55f, 0.66f, 0.3f);
		backgroundImage.position = new Vector2(400f, 0f);
		backgroundImage.textures = new Texture2D[1, 1];
		backgroundImage.texturenames = new string[1, 1];
		backgroundImage.textures[0, 0] = Content.Load<Texture2D>("GFX/Tutorial/grid3");
		backgroundImage.texturenames[0, 0] = "GFX/Tutorial/grid3";
		backgroundImage.size = 2.4f;
		backgroundImage.realsize.X = (float)backgroundImage.textures[0, 0].LogicalWidth() * backgroundImage.size;
		backgroundImage.realsize.Y = (float)backgroundImage.textures[0, 0].LogicalHeight() * backgroundImage.size;
		backgroundImage.scrollspeedmodifier = 0.25f;
		backgroundImage.blendMode = (SpriteBlendMode)2;
		backgroundLayers.Add(backgroundImage);
		holoGridFar = backgroundImage;
		// holo-grid, near: cyan hero -> the layer the glitch slips most
		backgroundImage = new BackgroundImage();
		backgroundImage.color = new Color(0.42f, 0.82f, 0.95f, 0.55f);
		backgroundImage.position = new Vector2(400f, 0f);
		backgroundImage.textures = new Texture2D[1, 1];
		backgroundImage.texturenames = new string[1, 1];
		backgroundImage.textures[0, 0] = Content.Load<Texture2D>("GFX/Tutorial/grid3");
		backgroundImage.texturenames[0, 0] = "GFX/Tutorial/grid3";
		backgroundImage.size = 1.5f;
		backgroundImage.realsize.X = (float)backgroundImage.textures[0, 0].LogicalWidth() * backgroundImage.size;
		backgroundImage.realsize.Y = (float)backgroundImage.textures[0, 0].LogicalHeight() * backgroundImage.size;
		backgroundImage.scrollspeedmodifier = 0.5f;
		backgroundImage.blendMode = (SpriteBlendMode)2;
		backgroundLayers.Add(backgroundImage);
		holoGrid = backgroundImage;
		isHolodeck = true;
		pulseActive = false;
		pulseCooldown.Duration = RandomHelper.RandomNextFloat(4000f, 9000f);
		pulseCooldown.Reset();
		pulseCooldown.Start();
		scrollspeedreset = new Vector2(0f, 0.2f) / 16.666666f;
		oscilatereach = 0.06f;
		oscilatespeed = 0.0025f;
		Reset();
	}

	public void Reset()
	{
		XFade.Stop();
		showdoodad = false;
		beltSlowActive = false;
		beltSlowAmount = 0f;
		state = BackgroundState.LeavingHyperspace;
		fadeFactor = 0.998f;
		scrollspeed = scrollspeedreset;
		scrollspeedmodifier = 10f;
		// The JIP catch-up latches (above). Reset() is reached from level entry AND from every
		// scene setter (SetSpace/SetMars/SetAlienBase/...), including the mid-level scene swaps
		// InsaneBossI drives -- and EVERY one of those callers rebuilds the layers and the scroll
		// baseline first. So by the time we get here the tracked ops describe scenery that no
		// longer exists, whichever path arrived; clearing them is right in all cases.
		netLastSpeed = null;
		netLastAlienBase = null;
		netLastDoodad = null;
	}

	// Dispose the procedural starfield (the SpriteBatch it owns) and forget it, so a
	// non-space background falls back to backgroundLayers. Safe to call when null.
	private void DisposeStarfield()
	{
		if (starfield != null)
		{
			starfield.Dispose();
			starfield = null;
		}
		if (nearStars != null)
		{
			nearStars.Dispose();
			nearStars = null;
		}
	}

	internal void SetMars()
	{
		backgroundLayers.Clear();
		foregroundLayers.Clear();
		isHolodeck = false;
		holoGrid = null;
		holoGridFar = null;
		DisposeStarfield();
		BackgroundImage backgroundImage = new BackgroundImage();
		backgroundImage.position = Vector2.Zero;
		backgroundImage.textures = new Texture2D[1, 1];
		backgroundImage.texturenames = new string[1, 1];
		backgroundImage.textures[0, 0] = Content.Load<Texture2D>("GFX/MarsBG/clouds-background");
		backgroundImage.texturenames[0, 0] = "GFX/MarsBG/clouds-background";
		backgroundImage.size = 1f;
		backgroundImage.realsize.X = (float)backgroundImage.textures[0, 0].LogicalWidth() * backgroundImage.size;
		backgroundImage.realsize.Y = (float)backgroundImage.textures[0, 0].LogicalHeight() * backgroundImage.size;
		backgroundImage.scrollspeedmodifier = 0.3f;
		backgroundLayers.Add(backgroundImage);
		// Far hills: THREE parallax slices (far/mid/near ridge), one texture per
		// RIDGES entry in tools/mars/build_marshills.py -- each natively seamless
		// and scrolling at its own depth between the sky (0.3) and the ground
		// (1.0), so the ridges parallax against each other instead of moving as
		// one flat card. Change speeds here AND the note in the tool's docstring.
		string[] hillNames = { "GFX/MarsBG/marshills1", "GFX/MarsBG/marshills2", "GFX/MarsBG/marshills3" };
		float[] hillScrolls = { 0.33f, 0.53f, 0.85f };
		for (int hi = 0; hi < hillNames.Length; hi++)
		{
			backgroundImage = new BackgroundImage();
			backgroundImage.position = Vector2.Zero;
			backgroundImage.textures = new Texture2D[1, 1];
			backgroundImage.texturenames = new string[1, 1];
			backgroundImage.textures[0, 0] = Content.Load<Texture2D>(hillNames[hi]);
			backgroundImage.texturenames[0, 0] = hillNames[hi];
			backgroundImage.size = 1f;
			backgroundImage.realsize.X = (float)backgroundImage.textures[0, 0].LogicalWidth() * backgroundImage.size;
			backgroundImage.realsize.Y = (float)backgroundImage.textures[0, 0].LogicalHeight() * backgroundImage.size;
			backgroundImage.scrollspeedmodifier = hillScrolls[hi];
			backgroundLayers.Add(backgroundImage);
		}
		backgroundImage = new BackgroundImage();
		// HD looping Mars floor (mars-bg-remaster). The 12 `marsloop` tiles are the stitched,
		// seamless, natively-LOOPABLE ground strip (tools/mars/STITCH_ALGORITHM.md), upscaled
		// ~3.238x. They REPLACE the old mars1..6 + horizontal
		// mirror: the strip closes on itself, so mirrorX is OFF (and the old realsize.X*=2 is
		// gone). Drawn at size 1/3.238 so every tile is the SAME on-screen size as the original
		// art, and at position.Y=300 (design) so this half-height (bottom-half) ground sits
		// EXACTLY where the old 600-tall tile's ground did -- no top padding needed. realsize.Y
		// stays the full 600 so the 300-tall band is not repeated vertically.
		backgroundImage.position = new Vector2(0f, 300f);
		backgroundImage.textures = new Texture2D[12, 1];
		backgroundImage.texturenames = new string[12, 1];
		float marsWidth = 0f;
		for (int mi = 0; mi < 12; mi++)
		{
			backgroundImage.texturenames[mi, 0] = "GFX/MarsBG/marsloop" + (mi + 1);
			backgroundImage.textures[mi, 0] = Content.Load<Texture2D>(backgroundImage.texturenames[mi, 0]);
			marsWidth += (float)backgroundImage.textures[mi, 0].LogicalWidth();
		}
		backgroundImage.size = 1f / 3.238f;
		backgroundImage.realsize.X = marsWidth * backgroundImage.size;
		backgroundImage.realsize.Y = 600f;
		backgroundImage.scrollspeedmodifier = 1f;
		backgroundImage.mirrorX = false;
		backgroundLayers.Add(backgroundImage);
		backgroundImage = new BackgroundImage();
		backgroundImage.position = Vector2.Zero;
		backgroundImage.textures = new Texture2D[1, 1];
		backgroundImage.texturenames = new string[1, 1];
		backgroundImage.textures[0, 0] = Content.Load<Texture2D>("GFX/MarsBG/clouds-foreground2");
		backgroundImage.texturenames[0, 0] = "GFX/MarsBG/clouds-foreground2";
		backgroundImage.size = 1f;
		backgroundImage.realsize.X = (float)backgroundImage.textures[0, 0].LogicalWidth() * backgroundImage.size;
		backgroundImage.realsize.Y = (float)backgroundImage.textures[0, 0].LogicalHeight() * backgroundImage.size;
		backgroundImage.scrollspeedmodifier = 2.5f;
		foregroundLayers.Add(backgroundImage);
		scrollspeedreset = new Vector2(-10f, 0f) / 16.666666f;
		oscilatereach = 0.1f;
		oscilatespeed = 5E-05f;
		Reset();
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		if (doodadname != null)
		{
			doodad = Content.Load<Texture2D>(doodadname);
		}
		blank = Content.Load<Texture2D>("GFX/Game/blank");
		foreach (BackgroundImage backgroundLayer in backgroundLayers)
		{
			backgroundLayer.LoadGraphics(Content);
		}
		foreach (BackgroundImage foregroundLayer in foregroundLayers)
		{
			foregroundLayer.LoadGraphics(Content);
		}
		EnsureRenderTarget();
	}

	// Stage 10: the cross-fade (XFade) renders a background into this offscreen target,
	// then blits it over the new background to dissolve between them. Size it to the
	// unified render resolution (RenderScale) so it composites 1:1 with the scene, and
	// use SurfaceFormat.Color (RGBA8) — the original 16-bit format renders nothing on
	// WebGL (same trap Stage 5 hit with the menu targets). Recreated on a size change.
	private void EnsureRenderTarget()
	{
		int w = RenderScale.Width;
		int h = RenderScale.Height;
		if (rendertarget != null && ((Texture2D)rendertarget).Width == w && ((Texture2D)rendertarget).Height == h)
		{
			return;
		}
		if (rendertarget != null)
		{
			((Texture2D)rendertarget).Dispose();
		}
		rendertarget = new RenderTarget2D(base.GraphicsDevice, w, h, false, SurfaceFormat.Color, DepthFormat.None, 0, (RenderTargetUsage)1);
	}

	protected override void UnloadContent()
	{
		base.UnloadContent();
		DisposeStarfield();
		if (rendertarget != null)
		{
			((Texture2D)rendertarget).Dispose();
		}
		rendertarget = null;
	}

	public void CrossFade()
	{
		XFade.Start();
		XFade.Reset();
	}

	public void SetSpaceClassic()
	{
		SetSpace();
		scrollspeedreset = new Vector2(0f, -0.2f) / 16.666666f;
		Reset();
	}

	// Trigger one deliberate "projection hiccup". Called by the simulator levels
	// (TutorialLevel/ClassicAliens). The old behaviour teleported each layer to a random
	// position -- a hard pop that read like a rendering seam; now it kicks off a short,
	// subtle position-only stutter-slip driven over glitchTimer in UpdateHoloGlitch. No
	// brightness flash (a tinted flicker read as a distracting random-colour flash).
	internal void Jump()
	{
		if (!isHolodeck)
		{
			return;
		}
		glitchTimer.Reset();
		glitchTimer.Start();
		glitchSlip = new Vector2(RandomHelper.RandomNextFloat(-7f, 7f), RandomHelper.RandomNextFloat(-3f, 3f));
	}

	// Per-frame: clear last frame's transient slip, then (while a glitch burst is active)
	// apply a small steppy "digital" stutter so it reads as an intentional holographic
	// hiccup rather than a smooth smear. The grid slips most; the rest of the projection
	// (stars, far grid) barely moves, so it doesn't pull the eye off gameplay.
	private void UpdateHoloGlitch(GameTime gameTime)
	{
		if (!isHolodeck)
		{
			return;
		}
		foreach (BackgroundImage layer in backgroundLayers)
		{
			layer.drawOffset = Vector2.Zero;
		}
		glitchTimer.Update(gameTime);
		if (!glitchTimer.Active)
		{
			return;
		}
		float p = 1f - glitchTimer.Normalized;
		float[] slipSteps = new float[3] { 1f, -0.5f, 0.2f };
		Vector2 off = glitchSlip * slipSteps[Math.Min((int)(p * 3f), 2)];
		if (holoGrid != null)
		{
			holoGrid.drawOffset = off;
		}
		foreach (BackgroundImage layer in backgroundLayers)
		{
			if (layer != holoGrid)
			{
				layer.drawOffset = off * 0.15f;
			}
		}
	}

	// Drives the grid light-pulse: while a sweep runs, advance it; otherwise count down the
	// randomised cooldown and kick off the next sweep when it elapses. DrawHoloPulse renders it.
	private void UpdateHoloPulse(GameTime gameTime)
	{
		if (!isHolodeck)
		{
			return;
		}
		if (pulseActive)
		{
			pulseTimer.Update(gameTime);
			if (!pulseTimer.Active)
			{
				pulseActive = false;
				pulseCooldown.Duration = RandomHelper.RandomNextFloat(8000f, 16000f);
				pulseCooldown.Reset();
				pulseCooldown.Start();
			}
		}
		else
		{
			pulseCooldown.Update(gameTime);
			if (!pulseCooldown.Active)
			{
				pulseActive = true;
				pulseTimer.Reset();
				pulseTimer.Start();
			}
		}
	}

	// A soft cyan light band that sweeps top->bottom through the holodeck. Built from a few
	// stacked, centred additive strips (a cheap triangular falloff) so where it passes the
	// grid lines surge brighter; the bloom present-pass softens it further. Drawn in 800x600
	// design space (scaled by RenderScale.Matrix), like the fade overlays.
	private void DrawHoloPulse()
	{
		if (!isHolodeck || !pulseActive)
		{
			return;
		}
		float p = 1f - pulseTimer.Normalized;
		float bandFull = 220f;
		float centerY = MathHelper.Lerp(0f - bandFull, 600f + bandFull, p);
		float envelope = MathHelper.Clamp(Convert.ToSingle(Math.Sin((double)p * Math.PI)), 0f, 1f);
		float peak = 0.5f * envelope;
		int layers = 10;
		base.SpriteBatch.BlendMode = (SpriteBlendMode)2;
		for (int i = 0; i < layers; i++)
		{
			float h = bandFull * (float)(layers - i) / (float)layers;
			base.SpriteBatch.Draw(blank, new Rectangle(0, (int)(centerY - h / 2f), 800, (int)h), new Color(0.5f, 0.9f, 1f, peak / (float)layers));
		}
	}

	public void SetSimpleSpaceClassic()
	{
		SetSimpleSpace();
		scrollspeedreset = new Vector2(0f, -0.2f) / 16.666666f;
		Reset();
	}

	public void QueueEarthSim()
	{
		if (!showdoodad)
		{
			doodadname = "GFX/Sprites/earth";
			doodadcolor = Color.White;
			doodadblendmode = (SpriteBlendMode)1;
			doodad = Content.Load<Texture2D>(doodadname);
			showdoodad = true;
			// Same full-res hero strip as QueueEarth -> scale 0.6467, X scroll 0
			// (centred vertical descent so the cropped strip's sides never show).
			doodadscale = 0.6467f;
			doodadscrollspeed = new Vector2(0f, 1.55f);
			doodadcolor = new Color(0.7f, 0.7f, 0.7f, 1f);
			doodadblendmode = (SpriteBlendMode)2;
			// Holodeck sim-earth (projected, over the grid starfield) is out of scope — no slowdown.
			doodadStarSlowdown = 1f;
			// Not replicable (no wire op, and it shares QueueEarth's texture) -- see netLastDoodad.
			netLastDoodad = null;
			if (scrollspeed.Y > 0f)
			{
				doodadPos = new Vector2(400f, (float)(-doodad.LogicalHeight()) * doodadscale / 2f);
			}
			else
			{
				doodadPos = new Vector2(400f, 600f + (float)doodad.LogicalHeight() * doodadscale / 2f);
			}
		}
	}

	public void SetAlienBaseDark()
	{
		SetAlienBase();
		oscilatereach = 0.5f;
		oscilatespeed = 0f;
		Reset();
	}
}
