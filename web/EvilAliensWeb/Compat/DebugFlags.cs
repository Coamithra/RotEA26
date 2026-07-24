using System;
using System.Globalization;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat
{
	// Web-port debug shortcuts, opt-in via the URL query string. They exist because the
	// normal boot (splash -> "Press Start" -> menu -> 20s idle drops into an attract demo)
	// is painful to drive in the preview/headless renderer, where focus + real key presses
	// are unreliable. Parsed ONCE at boot from window.location.search (see index.html's
	// getDebugQuery + Index.razor.cs). No params == normal boot, so a shipped build is
	// unaffected unless someone deliberately appends ?... to the URL.
	//
	// Supported flags (combine with '&'):
	//   ?menu          go straight to the main menu (skip splash + auto-"Press Start")
	//   ?skipsplash    skip only the splash sequence (still shows Press Start)
	//   ?autostart     auto-press Start on the Press Start screen
	//   ?noattract     disable the menu's idle -> demo (attract) mode  (alias: ?nodemo)
	//   ?level=<Name>  boot straight into a level, bypassing the menu entirely
	//                  (<Name> is a Levels enum value, case-insensitive: Level1, Level2,
	//                   Level3, ClassicAliens, SpaceDodge, Braineroids, Tutorial, ...)
	//   ?unlockall     reveal every gated menu option (Cheats, all challenges, Level 2/3,
	//                  Challenges/Awardments) so the whole menu can be walked through;
	//                  session-only (not saved), so a normal reload reverts it  (alias: ?unlock)
	//   ?shake=<f>     scale the trauma-based screen shake (Compat/Juice.cs): 0 = off,
	//                  1 = default, up to 3 to exaggerate while tuning. Pure camera look.
	//   ?hitstop=1     re-enable the automatic per-kill/boss-kill hit-stop freeze frame
	//                  (Compat/Juice.cs KillPunch). OFF by default — it read as a stutter,
	//                  not juice (Trello bd5efd9d). Player-death hit-stop and the
	//                  eaHitstop() console/JS hook are unaffected by this flag.
	//   ?metalscore    re-enable the chrome-sheen (metal.fx) on the in-game score + "Press Start"
	//                  text (OFF by default since card 37c4ccca — the chrome's dark mid-band reads
	//                  crunchy on the tiny HUD glyphs) to A/B against the plain flattened drop shadow
	//   ?reticlesize=<designpx>  aiming-reticle size in 800x600 design px (default 30). Scaled by
	//                  the letterbox to pick a cursor rung, so it holds its size at any window size.
	//   ?hitboxes      draw every collidable's collision shape over the game, colour-coded by
	//                  kind (box/circle/line). OFF by default; also toggleable via eaHitboxes()
	//   ?slowmotrail=0 disable the cinematic slow-motion ghost-trail post-process (ON by default;
	//                  reverts the 1up slowmo to the plain time-scale + bloom look). Tune the look
	//                  with ?slowmotraildecay=<0..0.99> (ghost persistence) and
	//                  ?slowmotrailstrength=<0..1> (how strongly trails mix over the live frame).
	//                  See it on demand without grinding a 1up: console eaSlowmo() in a level.
	//   ?holofilter=<f> scale the simulator levels' (Tutorial + ClassicAliens) fullscreen
	//                  holo-sim filter (scanlines + edge cyan cast; Compat/HoloSim + holosim.fx):
	//                  0 = the WHOLE filter off (green included), 1 = default, up to ~2.
	//                  Companions: ?holoburst=<f> scales the channel-surf glitch spikes
	//                  (activate/terminate + holodeck Jump() hiccups); ?hologreen=<0..1> the
	//                  monochrome phosphor-green pull; ?hologreenpulse=<0..1> the slow
	//                  green<->true-colour pulse depth; ?holostaticrate=<per-sec> how often the
	//                  random hiccup glitches fire. LIVE PANEL: eaHolo sliders auto-show on
	//                  ?level=Tutorial / ?level=ClassicAliens / a bare ?holotune.
	//                  Pure render looks — like MetalScore/SlowmoTrail, all kept OUT of `Active`.
	//   ?bulletshot    BULLET SHOWCASE: boot straight onto a frozen reference tableau --
	//                  the player ship + a UFO cluster + both bullet types on the starfield,
	//                  drawn by the real pipeline. A composed cousin of ?harness, built for
	//                  redrawing the bullet sprites (see Compat/BulletShowcaseScene.cs).
	//   ?textshot      TEXT SHOWCASE: a FROZEN grid of the flattened HUD text (DrawShadowString)
	//                  -- score digits / Combo! / the POWER UP! pop at its live animation phases,
	//                  plain AND chrome rows -- so one screenshot judges the text rendering
	//                  (see Compat/TextShowcaseScene.cs; card 37c4ccca).
	//   ?harness=<Obj> SPRITE HARNESS: boot straight onto a space background showing ONE
	//                  game object (an enemy/boss/projectile), FROZEN on a frame, drawn by
	//                  the real in-game Draw path (same SpriteBatchWrapper / RenderScale /
	//                  bloom / gamma). Built for iterating on drawing code: the image is
	//                  identical every frame, so a screenshot at any moment is reliable -- no
	//                  fighting game timing. <Obj> is a HarnessRegistry name (see that file or
	//                  harness.html), case-insensitive, e.g. Spider / UFO / Asteroid / DeathStar.
	//                  Companion flags (only meaningful with ?harness):
	//     ?frame=<n>   freeze on animation frame n (default 0)
	//     ?play        let the animation play in place instead of freezing (alias ?animate)
	//     ?bg=<name>   backdrop: space (default) / spaceclassic / holodeck / mars / base
	//     ?pos=<x,y>   object position in 800x600 design space (default 400,300 = centre)
	//     ?objscale=<f> multiply the object's natural draw scale (default 1; alias ?size)
	//     ?rot=<deg>   object rotation in degrees (default 0; alias ?rotation)
	//     ?fps=<n>     override the played animation's fps (alias ?animfps; only with ?play). Turning
	//                  it real low lets the frame-interpolation shader carry the motion between frames,
	//                  e.g. ?harness=eyeattract&play&fps=2 shows the eye boss's attract sheet tween.
	//   With ?harness=blast the harness LOOPS the blast through its lifetime and overlays the
	//   real collision ring + a live readout, for tuning the bomb's fade/active window:
	//     ?blastactive=<0..1> fade-alpha floor below which the blast stops dealing damage (def 0.5)
	//     ?blasthit=<f>       fraction of the visible radius that deals damage (default 0.8)
	//     ?blastloop=<sec>    seconds for one spawn->fade sweep in the viz (default 3)
	//   ?flyspiderscale=<f>  multiply the flying-spider size (both fg 1.0 + bg 0.67 base scales;
	//                  null => FlyingSpider.DefaultSizeFactor). Applies in play AND the harness,
	//                  so ?harness=flyingspider&play&flyspiderscale=0.8 previews it frozen/looping.
	//   With ?harness=battleskull (the level-3 alienboss "lightbulb" boss) the harness can
	//   override the hue-remap colorize so the recolour band + target can be tuned by eye:
	//     ?huestart=<deg>  hue-band Minimum (in-game -10)   ?hueend=<deg> hue-band Maximum (10)
	//     ?huetarget=<deg> / ?hue=<deg>  pin the target hue (0..360; default = HP-based)
	//     ?huecycle        auto-sweep the target 0..360 (?hueloop=<sec> sets the period, def 6)
	//   ?castbrain     CAST "BRAIN SPAWN" VIEWER: boot straight onto the end-credits Cast screen,
	//                  parked on the "Brain Spawn" entry (the one that now shows the animated
	//                  brainanimated sheet + glow, not the old brainlargetransglow). The real cast
	//                  is only reachable after beating Level 3 on Hard, so this is how to see/tune
	//                  it. Reuses the sprite-harness scene (Esc -> menu). Tuning knobs:
	//     ?castbrainscale=<f>  on-screen size of the cast brain (default baked in CastDisplayer)
	//     ?castbrainfps=<f>    animation speed (the cast draws frames by hand, no interpolation,
	//                          so it plays faster than the in-game 0.4 fps; default baked in)
	//   With ?harness=spiderjump the harness LOOPS the Mars jumping-spider's whole crawl -> launch
	//   -> arc -> land cycle (shadow + jump-X/ground markers + a readout) so its alignment values
	//   can be tuned by eye. The spider crosses the screen over one loop, jumping at jumpX with the
	//   entry frame back-calculated so the jump beat lines up:
	//     ?spiderjumpframe=<f> the ground-anim frame the spider launches on (the jump beat)
	//     ?spiderlandframe=<f> the frame it snaps to on touchdown (live default 44)
	//     ?spiderjumpx=<x>     design-space X it launches at (marker line; default 400)
	//     ?spidershadowx/y=<d> shadow offset from the spider centre / ground baseline
	//     ?spidershadowscale=<f> multiply the shadow's on-ground size (default 1)
	//     ?spiderloop=<sec>    seconds for one crawl->jump->land sweep (default 6)
	//     ?spiderphase=<0..1>  FREEZE the cycle at that fraction (deterministic apex screenshot)
	// Bare flags are ON; ?menu=0 / ?menu=false turns one back off (handy in saved URLs).
	// Examples:  ?menu   ?menu&noattract   ?level=ClassicAliens   ?level=Level2&noattract
	//            ?harness=Spider&frame=2   ?harness=DeathStar&play   ?harness=UFO&pos=300,260
	public static class DebugFlags
	{
		// Skip the studio/meme splash sequence and land on the Press Start screen.
		public static bool SkipSplash { get; private set; }

		// Auto-"Press Start" so the Press Start screen advances itself to the menu.
		public static bool AutoStart { get; private set; }

		// Don't wire the main menu's idle timeout to the demo/attract launcher.
		public static bool NoAttract { get; private set; }

		// If set, boot directly into this level (implies SkipSplash + AutoStart).
		public static EvilAliens.Levels? Level { get; private set; }

		// Unlock every gated menu option (session-only) so the full menu can be explored.
		public static bool UnlockAll { get; private set; }

		// Force invulnerability ON for the session (so playtesting a level doesn't keep dying).
		// SESSION-ONLY, like ?unlockall -- it must never write into Settings.Invulnerability
		// (that field gets persisted to localStorage by later saves, which used to leave a
		// player permanently invulnerable after a single ?invuln test session). Read directly
		// at the two damage gates instead (PlayerShip.CollidesWith, WebcamLevel.PlayerHit).
		public static bool Invuln { get; private set; }

		// TEMP DEBUG (repro only): in any GameScene, jump straight to Victory() once the
		// level reaches Normal play, to exercise the victory -> credits -> brag -> menu
		// handoff without playing the whole level. Combine with ?level=Level2. REMOVE.
		public static bool Win { get; private set; }

		// Sprite harness (see the header comment + HarnessScene/HarnessRegistry). Harness is
		// the registry name of the object to show; non-null => SkipSplash + AutoStart and the
		// boot routes into HarnessScene instead of the menu/a level.
		public static string Harness { get; private set; }

		// Animation frame to freeze on (default 0). Ignored when HarnessPlay is set.
		public static int HarnessFrame { get; private set; }

		// Let the object's animation play in place instead of freezing on a single frame.
		public static bool HarnessPlay { get; private set; }

		// Which Background setup to use behind the object (default "space").
		public static string HarnessBg { get; private set; } = "space";

		// Object position in 800x600 design space (null => centre, 400,300).
		public static float? HarnessX { get; private set; }

		public static float? HarnessY { get; private set; }

		// Multiplier on the object's natural draw scale (default 1).
		public static float HarnessScale { get; private set; } = 1f;

		// Object rotation in degrees (default 0).
		public static float HarnessRot { get; private set; }

		// Override the played animation's frames-per-second in the harness (?fps=<n>, alias ?animfps).
		// null => the sheet's authored fps. Turning it real low makes the frame-interpolation shader
		// carry all the visible motion between frames — e.g. ?harness=eyeattract&play&fps=2 shows the
		// eye boss's rotating/attract sheet tween smoothly rather than step. Only meaningful with ?play.
		public static float? HarnessFps { get; private set; }

		// Bullet showcase scene (Compat/BulletShowcaseScene.cs): a frozen reference tableau
		// (player ship + a UFO cluster + both bullet types on the starfield) drawn through the
		// real pipeline, for redrawing the bullet sprites. Like ?harness but COMPOSED of several
		// objects; non-null => SkipSplash + AutoStart and the boot routes into the showcase.
		public static bool Bulletshot { get; private set; }

		// Record every texture decode (time + size), flag ones that load outside a
		// level's preload phase, and accumulate a self-improving preload manifest in
		// localStorage. See Compat/LoadProfiler.cs. Off => zero overhead, no writes
		// (a shipped build never appends to the list). Does NOT alter the boot path.
		public static bool LoadLog { get; private set; }

		// Cinematic slow-motion motion-trail post-process (Game1.ApplySlowmoTrail). While the
		// 1up-powerup slowmo is active, the scene is fed through a feedback buffer so moving
		// objects smear into fading "ghost" trails (movie bullet-time look). ON by default;
		// ?slowmotrail=0 / =false A/Bs it off (reverts to the plain time-scale + bloom slowmo).
		// Like MetalScore it is a pure render look, deliberately OUT of `Active`.
		public static bool SlowmoTrail { get; private set; } = true;

		// Per-frame feedback retention of the trail buffer (0..1, higher = longer ghosts).
		// null => the baked-in default (Game1), so a shipped build is unchanged. ?slowmotraildecay=
		public static float? SlowmoTrailDecay { get; private set; }

		// How strongly the ghost trail is mixed back over the crisp current frame (0..1).
		// null => the baked-in default (Game1). ?slowmotrailstrength=
		public static float? SlowmoTrailStrength { get; private set; }

		// Scale on the tutorial's fullscreen holo-sim filter (Compat/HoloSim + holosim.fx):
		// 0 disables the whole filter (green pull included), null => the baked
		// HoloSim.DefaultIntensity. ?holofilter=
		// A pure render look, kept OUT of `Active` like SlowmoTrail (all ?holo* are).
		public static float? HoloFilter { get; private set; }

		// Scale on the holo-sim's channel-surf glitch spikes (activate/terminate + the
		// holodeck Jump() hiccups). null => HoloSim.DefaultBurstScale. ?holoburst=
		public static float? HoloBurst { get; private set; }

		// Monochrome phosphor-green pull (0 = true colour, 1 = full green terminal).
		// null => HoloSim.DefaultGreenPull. ?hologreen=
		public static float? HoloGreen { get; private set; }

		// Depth of the slow green<->true-colour pulse (0 = steady green, 1 = swings all
		// the way back to true colour). null => HoloSim.DefaultGreenPulse. ?hologreenpulse=
		public static float? HoloGreenPulse { get; private set; }

		// Average rate (per SECOND) of the simulator levels' random glitch hiccups
		// (Background.Jump + small burst). null => HoloSim.DefaultHiccupRate. ?holostaticrate=
		public static float? HoloStaticRate { get; private set; }

		// JS bridge for the live holo-sim tuner panel (eaHolo in wwwroot/index.html, shown on
		// ?level=Tutorial / ?level=ClassicAliens / a bare ?holotune): overrides the five knobs
		// in real time — HoloSim reads them every frame, so a slider drag retints the next frame.
		internal static void SetHoloOverride(float? green, float? greenPulse, float? burst, float? staticRate, float? filter)
		{
			HoloGreen = green;
			HoloGreenPulse = greenPulse;
			HoloBurst = burst;
			HoloStaticRate = staticRate;
			HoloFilter = filter;
		}

		// Master multiplier on the trauma-based screen shake (Compat/Juice.cs). 1 = the
		// shipped feel, 0 = off, >1 exaggerates while tuning (?shake=, clamped 0..3).
		// A pure camera/render look, so — like MetalScore/SlowmoTrail — kept OUT of `Active`.
		public static float ShakeAmount { get; private set; } = 1f;

		// Gates ONLY the automatic per-kill/boss-kill hit-stop freeze frame fired from
		// Juice.KillPunch (KillableAlien.HitBy) — NOT player-death hit-stop (PlayerShip
		// calls Juice.AddHitStop directly) and NOT the eaHitstop() console/JS hook, both
		// of which always fire. OFF by default (Trello bd5efd9d — the per-kill freeze read
		// as the game stuttering, not juice); ?hitstop=1 re-enables it for A/B. The kill's
		// screen-shake trauma is unaffected either way. A feel toggle — OUT of `Active`.
		public static bool Hitstop { get; private set; }

		// Route the in-game score / "Player X — Press Start" text through the chrome-sheen
		// effect (metal.fx) instead of the plain flattened drop-shadow draw. ON by default
		// (card 16dad393 restored the Stage-13 chrome-on-score default that card 37c4ccca had
		// turned off — the user asked for the chrome sheen back, including the score's
		// event-driven glint sweep on a leading-digit rollover). The chrome darkens the
		// mid-band, so the metal path draws a touch more solid (0.7 vs the plain 0.55). Set
		// ?metalscore=0 to A/B the plain flatten. Does NOT alter the boot path — purely a
		// render look, so it is deliberately left OUT of `Active` (a clean boot stays "no
		// debug flags").
		public static bool MetalScore { get; private set; } = true;

		// Draw every live collidable's collision shape over the frame, colour-coded by kind
		// (box -> rectangle, circle -> ring, line -> segment) so a sprite whose DRAW is offset
		// from its Position/hitbox shows the drift. OFF by default; enable with ?hitboxes (URL)
		// or eaHitboxes(true) (console). A pure overlay — deliberately left OUT of `Active`, so a
		// clean boot stays "no debug flags". Drawn by HitboxOverlay from Game1.DrawInner.
		public static bool ShowHitboxes { get; private set; }

		// Runtime toggle for the console bridge (Compat/DebugInput.Hitboxes -> eaHitboxes()).
		internal static void SetShowHitboxes(bool on)
		{
			ShowHitboxes = on;
		}

		// Blast (bomb) tuning knobs for the sprite-harness lifetime visualiser (?harness=blast).
		// All null/default => Blast.cs uses its baked-in constants, so a shipped build is unchanged.
		//   ?blastactive=<0..1>  the blast stops dealing damage once its fade alpha drops below this
		//                        (default 0.5 — collide while at least half-opaque; higher = shorter
		//                        active window, so "dangerous" tracks "clearly visible").
		//   ?blasthit=<f>        fraction of the visible radius that deals damage (default 0.8).
		//   ?blastloop=<sec>     viz only: seconds for one spawn->fade sweep in the harness (default 3).
		public static float? BlastActiveAlpha { get; private set; }

		public static float? BlastHitFactor { get; private set; }

		// Aiming-reticle size, in DESIGN px (800x600 space) -- MousePointer scales it by the
		// letterbox to pick a cursor rung, so the reticle holds its size relative to the play
		// field at any window size. null => MousePointer.DefaultReticleDesignPx (30). A pure
		// look/feel knob, so — like MetalScore — deliberately kept OUT of `Active`.
		//   ?reticlesize=<designpx>  e.g. 26 = the original XBLIG art's size, 40 = chunky.
		public static float? ReticleSize { get; private set; }

		// Flying-spider size multiplier (Trello: "make the flying spiders slightly smaller").
		// The reared-up HD stance (FlyingSpider loops spider_sheet2 frames 22..30) draws taller
		// and a touch wider than the OG 1x4 crawl sheet the original used, so it reads bigger than
		// the XBLIG. This multiplies BOTH the foreground (1.0) and background (0.67) base scales.
		// null => FlyingSpider.cs uses its baked DefaultSizeFactor, so a shipped build is unchanged.
		// Tune by eye with ?flyspiderscale=<f> (e.g. ?harness=flyingspider&play&flyspiderscale=0.8,
		// or ?level=Level2&flyspiderscale=0.8). The sprite and its box hitbox (sized off the frame
		// via DrawScale) shrink together, so collision keeps tracking the visible size.
		public static float? FlySpiderScale { get; private set; }

		public static float BlastLoopSeconds { get; private set; } = 3f;

		// Laser FX tuning knobs (Trello: "improve laser animation"). The beam + chargeup are
		// drawn by Quad.cs / LazerGenerator.cs; these override the eye-tuned magic numbers so
		// the look can be A/B'd live (see the ?lazershot showcase). ALL null => the baked
		// defaults ship unchanged.
		//   ?lazerchargescale=<f>  multiply the chargeup swarm's per-particle scale (the GFX/Menu/star
		//                          sparkle is soft + full-frame, so at the stock scale the rays vanish
		//                          sub-pixel -> "too subtle"; bump it to make the charge visible).
		//   ?lazercapscale=<f>     size of the beam's rounded END-CAPS vs the core width (default 1;
		//                          the caps round off the otherwise flat/"chopped" beam ends + hide
		//                          the core/flare seam).
		//   ?lazerarcs=<f>         average number of electric tendrils SPAWNED PER SECOND off the beam
		//                          (stochastic, so they pop up out of sync -- was a fixed count).
		//   ?lazertendrilspeed=<f> max |drift| a spawned tendril travels along the beam (design px/sec;
		//                          each tendril gets a random signed speed up to this).
		//   ?lazerarclife=<sec>    overrides the MEAN tendril lifespan (range = mean +/-33%); default
		//                          range is a random 0.25..0.5s per tendril.
		public static float? LazerChargeScale { get; private set; }

		public static float? LazerCapScale { get; private set; }

		public static float? LazerArcRate { get; private set; }

		public static float? LazerTendrilSpeed { get; private set; }

		public static float? LazerArcLife { get; private set; }

		// Ship-connector lightning knobs (Trello "ship connector too static"). The multiplayer
		// docking connector (ShipConnector, the twin-orb GFX/Sprites/connector sprite held between
		// two linked ships) used to be a single frozen sprite; it now pulses and crackles live
		// electric bolts between its two orbs (same fractal-bolt technique as the Quad laser).
		// ALL null => the baked ShipConnector.Default* consts ship unchanged.
		//   ?connectorbolts=<n>   number of continuously-writhing main bolts spanning the two orbs.
		//   ?connectorarcs=<f>    average short crackle tendrils SPAWNED PER SECOND off the link
		//                         (stochastic, like the laser's ?lazerarcs).
		//   ?connectorjitter=<f>  multiplies the bolt zig-zag amplitude (0 = straight, taut arc).
		//   ?connectorpulse=<f>   breathe frequency (Hz) of the base sprite + orb blooms.
		//   ?connectorglow=<f>    orb-bloom intensity/size vs the baked default (0 = no extra blooms).
		public static float? ConnectorArcRate { get; private set; }

		public static int? ConnectorBoltCount { get; private set; }

		public static float? ConnectorJitter { get; private set; }

		public static float? ConnectorPulse { get; private set; }

		public static float? ConnectorGlow { get; private set; }

		// Runtime setter for the live connector-tuner slider panel (Compat/DebugInput.SetConnector ->
		// eaConnector() in index.html, shown on ?level=TeamChallenge / a bare ?connectortune). Lets the
		// five knobs be dragged in real time; ShipConnector reads them every Draw. Same effect as the
		// ?connector* URL flags, live.
		internal static void SetConnectorOverride(int? boltCount, float? arcRate, float? jitter, float? pulse, float? glow)
		{
			ConnectorBoltCount = boltCount;
			ConnectorArcRate = arcRate;
			ConnectorJitter = jitter;
			ConnectorPulse = pulse;
			ConnectorGlow = glow;
		}

		// Runtime setter for the live laser-tuner slider panel (Compat/DebugInput.SetLazer ->
		// eaLazer() in index.html, shown on the ?lazershot showcase). Lets the four knobs be
		// dragged in real time instead of reloading with new ?lazer* flags each nudge. Quad.cs
		// reads CapScale every Draw + ArcRate/TendrilSpeed at each tendril spawn, and LazerGenerator
		// reads the peak charge scale every Draw — so a set here is picked up almost immediately
		// (the showcase swarm/tendrils re-roll continuously). Same effect as the
		// ?lazerchargescale/?lazercapscale/?lazerarcs/?lazertendrilspeed URL flags, live.
		internal static void SetLazerOverride(float? chargeScale, float? capScale, float? arcRate, float? tendrilSpeed)
		{
			LazerChargeScale = chargeScale;
			LazerCapScale = capScale;
			LazerArcRate = arcRate;
			LazerTendrilSpeed = tendrilSpeed;
		}

		// Level-3 wall TOWER knobs (Trello d59266cc / a66fc73e, plans/walls-3d-towers.md +
		// plans/spike-wall3d.md). Wall.Draw extrudes each collidable block downward into a REAL 3D
		// shaft standing on the alien-base ground, so the walls read as towers rising out of the fog.
		// ALL null => the baked Wall.Default* consts ship unchanged; ?walltowers=0 restores the old
		// flat look exactly.
		//   ?walltowers=0        kill switch -- skip the tower + wisp passes entirely.
		//   ?walldepth=<f>       perspective depth factor of the tower BASE (default 0.66, which is
		//                        the alien-base ground layer's scrollspeedmodifier -- that match is
		//                        what glues the bases to the scrolling floor; change it and they slide).
		//   ?wallfog=<0..1>      how fogged the shaft is at its base. This is REAL distance fog
		//                        (BasicEffect.FogEnabled), so it lerps TO the haze colour rather than
		//                        multiplying by it -- see Wall.DefaultFogColor.
		//   ?wallfogcolor=<hex>  the haze colour shafts dissolve into (rrggbb; sampled from the
		//                        alien-base ground + its additive fog layers).
		//   ?wallsidedark=<f>    brightness of the shaft at its cap (1 = as bright as the top face).
		//   ?wallsidetile=<f>    how densely the sheet tiles DOWN a shaft, as a multiple of the top
		//                        face's own texel density (1 = a side texel is the same world size as
		//                        a top-face one, i.e. the shaft's true height in block footprints --
		//                        2.70 cells at the shipped numbers). Baked at 4: honest scale reads
		//                        short, because a steeply foreshortened shaft compresses most of its
		//                        length into its far few pixels. See Wall.DefaultSideTile.
		//   ?wallfacelight=<0..1> per-face shading contrast, so tower CORNERS read (0 = flat-shaded).
		//                        Vertical faces darken, horizontal ones don't; each wall quad is one
		//                        face, so this is just its vertex colour.
		//   ?wallfaceangle=<deg> light azimuth, screen space (0 = from +x, 90 = from +y; 225 = upper left).
		//   ?walltoplift=<f>     lift the tower TOPS above the gameplay plane, as a fraction of depth
		//                        (0 = flush). Cosmetic only -- the hitbox does not move with it.
		//   ?wall3dbands=<n>     dissolve cuts down a side face (default 4) -- NOT the strip count,
		//                        which is this plus one per cell crossing of ?wallsidetile. Geometry
		//                        and fog are exact at 1; the cuts only resolve the smoothstep bottom
		//                        dissolve, which rides per-vertex alpha.
		//   ?wallwisps=<0..1>    alpha of the additive fog wisps drawn across the shafts (0 = off).
		//   ?wallwispspeed=<f>   the wisps' scroll modifier vs the wall (default 0.8 = the near fog
		//                        background layer, which sits inside the shaft's 0.66..1.0 depth band).
		public static bool WallTowers { get; private set; } = true;

		public static float? WallDepth { get; private set; }

		public static float? WallFog { get; private set; }

		public static Color? WallFogColor { get; private set; }

		public static float? WallSideDark { get; private set; }

		public static float? WallSideTile { get; private set; }

		public static float? WallFaceLight { get; private set; }

		public static float? WallFaceAngle { get; private set; }

		public static float? WallTopLift { get; private set; }

		public static int? Wall3DBands { get; private set; }

		public static float? WallWisps { get; private set; }

		public static float? WallWispSpeed { get; private set; }

		// Runtime setter for the live wall-tower slider panel (Compat/DebugInput.SetWalls ->
		// eaWalls() in index.html, shown on ?level=Level3&wallsonly / a bare ?walltune). Wall.Draw
		// re-reads every knob each frame, so a drag re-projects the towers on the next Draw. Same
		// effect as the ?wall* URL flags, live. `towers` doubles as the kill switch.
		internal static void SetWallsOverride(bool towers, float? depth, float? fog, float? sideDark, float? sideTile, float? faceLight, float? faceAngle, float? topLift, float? bands, float? wisps, float? wispSpeed)
		{
			WallTowers = towers;
			WallDepth = depth;
			WallFog = fog;
			WallSideDark = sideDark;
			WallSideTile = sideTile;
			WallFaceLight = faceLight;
			WallFaceAngle = faceAngle;
			WallTopLift = topLift;
			Wall3DBands = (bands.HasValue && bands.Value >= 1f) ? (int?)(int)bands.Value : null;
			WallWisps = wisps;
			WallWispSpeed = wispSpeed;
		}

		// Fast-boot Level3 straight to a looping walls section (?level=Level3&wallsonly) -- mirrors
		// ?spiderboss for Level2. Skips the whole wave sequence so the towers can be watched without
		// minutes of play per iteration. Pair with ?invuln. See Level3.PopulateWallsOnly.
		public static bool WallsOnly { get; private set; }

		// TEMP diagnostic (?walltrace): Wall.Draw logs, per wall instance, the first frame its top
		// faces appear vs the first frame its shafts appear (with Position.Y), plus a coarse sample of
		// (posY, topFaces, shaftQuads) as it enters -- to pin down the reported "top slides in before
		// its pillar" at a segment start. Out of Active; remove once diagnosed.
		public static bool WallTrace { get; private set; }

		// TEMP diagnostic (?wallpoptest): boot Level3 into a chain of ten SMALL (~2-screen) wall
		// sections from Content/Levels/poptest0..9.txt, and drop the scroll speed to ~10% once the
		// SECOND section loads, so the entry "pop" is slow and unmistakable and it's obvious whether
		// it tracks position (geometry) or a one-off load/cache hitch. See Level3.PopulateWallPopTest.
		public static bool WallPopTest { get; private set; }

		// Laser showcase scene (Compat/LazerShowcaseScene.cs): the chargeup swarm + a full-grown
		// beam side by side on the starfield, ANIMATING (unlike the frozen ?harness/?bulletshot),
		// so the tendrils / chargeup / caps can be watched while tuning. Opt in with ?lazershot;
		// non-null => SkipSplash + AutoStart and the boot routes into the showcase.
		public static bool Lazershot { get; private set; }

		// Flattened-text showcase scene (Compat/TextShowcaseScene.cs): a FROZEN reference grid of
		// the DrawShadowString HUD text — score digits / Combo! / the "POWER UP!" pop at its exact
		// live animation phases, plain AND chrome — on the space background, nothing Update-driven,
		// so ONE screenshot at any moment shows the whole matrix pixel-reliably (card 37c4ccca).
		// Opt in with ?textshot; => SkipSplash + AutoStart and the boot routes into the showcase.
		public static bool Textshot { get; private set; }

		// Colorize (hue-remap) tuning knobs for the alienboss "lightbulb" boss in the sprite
		// harness (?harness=battleskull). The BattleSkull recolours a band of the alienboss
		// sprite's hues [Minimum,Maximum] toward a Target hue (all in degrees); in-game that
		// band is (-10,10) and the Target sweeps with HP (100=green full HP -> 0=red dead).
		// These override those numbers so the band + target can be tuned by eye. ALL null/off
		// => BattleSkull uses its baked-in values, so a shipped build is byte-identical, and
		// they only ever apply while the harness is up (BattleSkull gates on Harness != null).
		//   ?huestart=<deg>  overrides the hue-band Minimum  (in-game -10)
		//   ?hueend=<deg>    overrides the hue-band Maximum  (in-game  10)
		//   ?huetarget=<deg> pins the Target hue (0..360). Without it the Target sweeps with the
		//                    ?hue scrub / ?huecycle, or rests at the full-HP value (100).
		//   ?hue=<deg>       one-shot scrub: pin the Target to this exact hue (alias of huetarget)
		//   ?huecycle        auto-sweep the Target 0..360 so a screenshot shows any point of the
		//                    range (alias ?huesweep); ?hueloop=<sec> sets the sweep period (def 6)
		public static float? HueStart { get; private set; }

		public static float? HueEnd { get; private set; }

		public static float? HueTarget { get; private set; }

		public static bool HueCycle { get; private set; }

		public static float HueLoopSeconds { get; private set; } = 6f;

		// Runtime setter for the live slider panel (Compat/DebugInput.SetHue -> eaHue() in
		// index.html, shown on the ?harness=battleskull page). Lets the band/target be dragged
		// in real time instead of reloading with new ?hue* flags each nudge. HarnessColorize.Apply
		// reads these properties every frame, so a set here is picked up on the very next Draw.
		// `target == null` => the Target tracks HP (the shipped default); a non-null value pins it.
		internal static void SetHueOverride(float? start, float? end, float? target, bool cycle, float loop)
		{
			HueStart = start;
			HueEnd = end;
			HueTarget = target;
			HueCycle = cycle;
			if (loop > 0f)
			{
				HueLoopSeconds = loop;
			}
		}

		// SpiderBoss "helper mothership" feel knobs (Game/EvilAliens/SpiderHelperMothership.cs +
		// the trigger in SpiderBoss.cs). Every N completed jump->fly->land CYCLES (N is set ONCE at the
		// fight's start from the difficulty modifier -- baselines Easy/Medium 1, Hard 2, Very_Hard 3,
		// Inzane 4; a ramped-in or higher-tier fight locks a bigger interval), a mothership EASES in from
		// the left showing just its underside at the top, halts dead-centre, WINDS UP a converging
		// spark swarm (like a medium UFO charging its laser) for SpiderHelperWindupSeconds, fires a
		// Lazer for SpiderHelperFireSeconds, then EASES out east and exits right. WHERE the beam aims is
		// keyed on the difficulty TIER: Easy/Medium at the standing spider, Hard straight down,
		// Very_Hard/Inzane AT THE PLAYER (the "helper" turns hazard up top). A little top-left warning
		// arrow announces it. Enter/exit speed is a difficulty-scaled fraction of the twin-MarsBoss
		// traverse speed (Easy ~1/5 .. Inzane ~4/5); ?spiderhelperspeed overrides it with a raw px/ms
		// value. It is now KILLABLE (SpiderHelperHitPoints): destroy it and it still fires its laser,
		// then crash-lands off the bottom-right in a burst of explosions instead of flying off. All have
		// shipping defaults, so a plain boot is unchanged; these only let the feel be tuned live. Flags:
		//   ?spiderhelpercycles=<n>    FIXED boss cycles between helper visits (no ramp), overriding the
		//                              difficulty scaling (which baselines Easy/Medium 1, Hard 2,
		//                              Very_Hard 3, Inzane 4). Pair with ?difficulty= to test a tier.
		//   ?spiderhelperhp=<n>        base hitpoints before it's destroyed (default 50, then
		//                              difficulty-scaled by DifficultyFactorized(0.7))
		//   ?spiderhelperhovery=<y>    sprite-centre Y; more negative pushes the ship up so less of
		//                              it shows (default 10 => the belly + lower spikes hang in, dome cut)
		//   ?spiderhelperspeed=<f>     RAW avg enter/exit design-px/ms, overriding the difficulty
		//                              scaling (default: unset => Easy ~0.15 .. Medium ~0.28 .. Inzane ~0.6)
		//   ?spiderhelperwindup=<sec>  charge-swarm duration before the beam fires (default 2.5)
		//   ?spiderhelperfire=<sec>    how long the laser holds if it hasn't caught the boss (default 4.5)
		//   ?spiderhelperlead=<px>     muzzle offset: sprite centre -> beam origin, along the aim
		//                              (default 100 = MarsBoss's lazer offset, same sprite; lower =
		//                              beam emerges higher up the body)
		//   ?spiderhelperenterpower=<p> ease-out-to-rest exponent for the fly-in (default: unset =>
		//                              the baked DefaultEnterPower 2; >=1; higher = punchier start
		//                              but still glides to a smooth stop)
		public static int? SpiderHelperCycles { get; private set; }

		public static int? SpiderHelperHitPoints { get; private set; }

		public static float SpiderHelperHoverY { get; private set; } = 10f;

		public static float? SpiderHelperSpeed { get; private set; }

		public static float SpiderHelperWindupSeconds { get; private set; } = 2.5f;

		public static float SpiderHelperFireSeconds { get; private set; } = 4.5f;

		public static float SpiderHelperFireLead { get; private set; } = 100f;

		public static float? SpiderHelperEnterPower { get; private set; }

		// Fast-boot Level2 straight to the spider-boss fight (skips the whole level) so the helper
		// mothership + boss interaction can be watched in seconds. Pair with ?level=Level2 (+ ?invuln,
		// ?spiderhelperidle=<small>). A pure test shortcut, like ?win. See Level2.PopulateEventList.
		public static bool SpiderBoss { get; private set; }

		// Fast-boot Level2 straight to the TWIN-mothership (MarsBoss) fight -- like ?spiderboss but for
		// the twins. See Level2.PopulateMarsBossOnly.
		public static bool MarsBoss { get; private set; }

		// Fast-boot Level3 straight to the REAL BrainBoss fight (the big-brain finale) -- like
		// ?spiderboss but for Level 3. Spawns the brain UNCONDITIONALLY (any difficulty), skipping
		// the whole wave sequence, so the brain-boss animated overlays + hit SFX can be verified
		// without grinding the level or being on Hard+. Pair with ?level=Level3 (+ ?invuln,
		// ?difficulty=Hard). See Level3.PopulateBrainBossOnly.
		public static bool BrainBoss { get; private set; }

		// ?difficulty=<Easy|Medium|Hard|Very_Hard|Inzane>: pin the difficulty at boot (applied before
		// any level Initialize runs). The helper's glide speed + aim are difficulty-scaled, so this
		// makes the spider-boss test deterministic. Null => the saved/menu-chosen difficulty is used.
		public static EvilAliens.Settings.DifficultyLevel? Difficulty { get; private set; }

		// ?spiderbosshp=<n>: override the SpiderBoss hitpoint pool (default is ~5*difficulty). Set it
		// high (e.g. 100) so the boss survives many helper cycles without reloading. Null => shipped HP.
		public static int? SpiderBossHp { get; private set; }

		// Fast-boot Level2 straight to a continuous pure-spider GROUND wave (skips the whole level)
		// so the animation-driven jump can be watched + dialed in REAL play -- the ?harness=spiderjump
		// sim's arc is illustrative, this is the live Spider.Update path. Pair with ?level=Level2
		// (+ ?invuln, + the ?spiderjumpframe=/?spiderjumpx=/?spiderlandframe=/?spidershadow* knobs).
		// A pure test shortcut, like ?spiderboss. See Level2.PopulateEventList / PopulateSpidersOnly.
		public static bool Spiders { get; private set; }

		// Fast-boot the Tutorial straight to its FINAL power-up training beat (skips the whole
		// welcome/move/fire/lesson sequence): the eye "punching bag" boss + the PowerUpTrainingEvent
		// where every powerup streams in and a banner explains its powered-up effect. Built to
		// reproduce the R-banner timing bug (the last powerup, powered up almost instantly while the
		// player is mid-combo on the boss, used to rip its banner away before it finished appearing)
		// in seconds instead of playing the whole tutorial. Pair with ?level=Tutorial (+ ?invuln).
		// See TutorialLevel.PopulatePowerUpTrainingOnly.
		public static bool TutorialTraining { get; private set; }

		// Cast "Brain Spawn" viewer (?castbrain): boot into the end-credits Cast screen parked
		// on the braineroid entry, reusing HarnessScene. Non-null => SkipSplash + AutoStart and
		// the boot routes into the harness in cast-brain mode instead of the menu/a level.
		public static bool CastBrain { get; private set; }

		// Full end-credits Cast viewer (?cast): boot the WHOLE CastDisplayer state machine
		// (not the brain-locked showcase) through HarnessScene, so every cast member can be
		// stepped through with Enter. The real Cast screen is only reachable after beating
		// Level 3 on Hard; this is the way to eyeball it (e.g. the per-member frame
		// interpolation). True => SkipSplash + AutoStart, routes into the harness.
		public static bool CastShow { get; private set; }

		// On-screen scale + animation fps of the cast "Brain Spawn" specimen. null => the baked
		// defaults in CastDisplayer, so a shipped build is unchanged; these override for by-eye
		// tuning via ?castbrain (the blast/colorize-tuner pattern). ?castbrainscale= / ?castbrainfps=
		public static float? CastBrainScale { get; private set; }

		public static float? CastBrainFps { get; private set; }

		// Texture-format viewer (?texviewer): boot straight into TexViewerScene, which flips each
		// sprite's RAW (PNG-decoded) vs DXT (BC3) version through the real GPU pipeline so a
		// per-sprite dxt/raw/png decision can be made and saved to tools/textures/textures.config
		// (see plans/texviewer.md + the Trello card). Needs the previews built once by
		// tools/textures/build_texviewer.py. Like ?castbrain it hijacks boot => SkipSplash +
		// AutoStart, and it's IN Active (below) since it takes over the whole boot.
		public static bool TexViewer { get; private set; }

		// Webcam "I Made This!" difficulty tuning knobs (WebcamLevel). The webcam challenge
		// now has a per-difficulty tuning table (hearts / kills-to-win / saucer cap / saucer
		// speed / plasma speed, Easy..Inzane); these knobs A/B those numbers live so the feel
		// can be dialled in by eye, then baked back into WebcamLevel.Tunings. ALL null/off =>
		// the shipped table is used, so a normal build is unchanged.
		//   ?wcdiff=<Easy|Medium|Hard|Very_Hard|Inzane>  force the webcam's difficulty (so any
		//                 tier can be tuned without unlocking it in the menu; case-insensitive,
		//                 spaces or underscores). Pair with ?level=WebcamAliens.
		//   ?wchearts=<int>      override starting hearts for the active run
		//   ?wckills=<int>       override kills-to-win
		//   ?wcsaucers=<int>     override the max simultaneous-saucer cap
		//   ?wcsaucerspeed=<f>   multiply the active tier's saucer-speed multiplier
		//   ?wcplasmaspeed=<f>   multiply the active tier's plasma-speed multiplier
		//   ?wctune              show the LIVE stepper panel (index.html) while the webcam
		//                        level is up: +/- every knob in real time, no reload —
		//                        the panel's readout prints the bake-ready Tunings[] row
		public static EvilAliens.Settings.DifficultyLevel? WebcamDifficulty { get; private set; }

		public static int? WebcamHearts { get; private set; }

		public static int? WebcamKills { get; private set; }

		public static int? WebcamSaucers { get; private set; }

		public static float? WebcamSaucerSpeed { get; private set; }

		public static float? WebcamPlasmaSpeed { get; private set; }

		// Cadence overrides as ABSOLUTE milliseconds (the difficulty-modifier divisor was
		// removed — each tier authors these directly in WebcamLevel.Tunings). ?wcspawn= sets
		// the gap between saucer spawns; ?wcarm= sets the wander time before a saucer starts
		// charging (its fire cadence — bigger = fires less often); ?wccharge= sets the
		// blink-charge windup before the orb releases. null => the tier's authored ms, so a
		// normal build is unchanged. e.g. ?wcarm=5000 = a 5s arm delay.
		public static float? WebcamSpawnInterval { get; private set; }

		public static float? WebcamArmDelay { get; private set; }

		public static float? WebcamChargeTime { get; private set; }

		// F2 DeathStar-mine hazard overrides (absolute, like the cadence flags; null => the
		// tier's authored value). ?wcminemax= = simultaneous mine cap; ?wcminespawn= = ms gap
		// between mine spawns; ?wcminelife= = ms a mine wanders before it leaves.
		public static int? WebcamMineMax { get; private set; }

		public static float? WebcamMineSpawn { get; private set; }

		public static float? WebcamMineLife { get; private set; }

		// F1 screen-bisecting mothership. ?wcmothership=<ms> = gap between bisect events
		// (0 disables); ?wcmothershipdir=vertical|horizontal forces the orientation for
		// testing (null => the random vertical-mostly mix). Both null => tier default.
		public static float? WebcamMothership { get; private set; }

		public static string WebcamMothershipDir { get; private set; }

		// ?wcmothershipfreeze=<ms>: halt a webcam mothership's choreography at this elapsed-ms
		// phase (0..~6200) so a frozen frame can be captured — e.g. ~3600 for the beam mid-fire,
		// to check its centring. null => normal real-time play. Debug-only; kept out of Active.
		public static float? WebcamMothershipFreeze { get; private set; }

		// ?wchitleeway=<ms>: how long the player mask must STEADILY overlap a bad hazard (plasma
		// orb / mothership beam / mine) before it costs a life — the anti-cam-glitch / late-dodge
		// grace. null => WebcamLevel's baked HitLeewayMs (~100ms). 0 = instant (old behavior).
		public static float? WebcamHitLeeway { get; private set; }

		// ?wcavoid=<f>: strength of the webcam saucers' player-avoidance/orbit steering
		// ("fly around the player"). 0 disables it (pure random wander), 1 = the baked
		// default feel, >1 more evasive. null => WebcamUfo's baked DefaultAvoidStrength,
		// so a normal build is unchanged. Lets the "won't hit me if I sit still" feel be
		// A/B'd by eye without a rebuild.
		public static float? WebcamAvoid { get; private set; }

		// ?wcreturndelay=<ms>: how long a webcam saucer holds off-screen after firing before
		// it loops back into the field (WebcamUfo's flee -> return dwell). 0 = loop back
		// immediately (the old instant U-turn), higher = a longer beat away. null =>
		// WebcamUfo's baked ReturnDelayMs, so a normal build is unchanged.
		public static float? WebcamReturnDelay { get; private set; }

		// ?wctune: show the LIVE webcam-tuning stepper panel (index.html, outside #app)
		// while the webcam level is up. The panel drives SetWebcamTuneOverride below via
		// Compat/DebugInput.SetWcTune, so the five Tunings[] knobs can be nudged in real
		// time (mid-play or paused) instead of reloading with new ?wc* flags each change.
		// OFF by default and — like the other tuner panels — kept OUT of `Active`, so a
		// shipped build never shows it and boots byte-identical.
		public static bool WebcamTune { get; private set; }

		// Runtime (live-panel) overrides for the five webcam tuning knobs. Unlike the
		// ?wcsaucerspeed/?wcplasmaspeed URL flags (MULTIPLIERS on the tier baseline),
		// these are ABSOLUTE final values — the panel shows/edits exactly what would be
		// baked into WebcamLevel.Tunings[]. All null => no runtime override.
		public static int? WebcamTuneHearts { get; private set; }

		public static int? WebcamTuneKills { get; private set; }

		public static int? WebcamTuneSaucers { get; private set; }

		public static float? WebcamTuneSaucerSpeed { get; private set; }

		public static float? WebcamTunePlasmaSpeed { get; private set; }

		public static float? WebcamTuneSpawnInterval { get; private set; }

		public static float? WebcamTuneArmDelay { get; private set; }

		public static float? WebcamTuneChargeTime { get; private set; }

		public static int? WebcamTuneMineMax { get; private set; }

		public static float? WebcamTuneMineSpawn { get; private set; }

		// Bumped on every SetWebcamTuneOverride/ClearWebcamTuneOverride so WebcamLevel can
		// re-resolve its tuning the tick after a panel edit (a cheap int compare per Update).
		public static int WebcamTuneVersion { get; private set; }

		// Runtime setter for the live webcam tuner panel (Compat/DebugInput.SetWcTune ->
		// eaWcTune in index.html). The panel always sends the full ten-knob state.
		internal static void SetWebcamTuneOverride(int hearts, int kills, int saucers, float saucerSpeed, float plasmaSpeed, float spawnInterval, float armDelay, float chargeTime, int mineMax, float mineSpawn)
		{
			WebcamTuneHearts = hearts > 0 ? hearts : (int?)null;
			WebcamTuneKills = kills > 0 ? kills : (int?)null;
			WebcamTuneSaucers = saucers > 0 ? saucers : (int?)null;
			WebcamTuneSaucerSpeed = saucerSpeed > 0f ? saucerSpeed : (float?)null;
			WebcamTunePlasmaSpeed = plasmaSpeed > 0f ? plasmaSpeed : (float?)null;
			WebcamTuneSpawnInterval = spawnInterval > 0f ? spawnInterval : (float?)null;
			WebcamTuneArmDelay = armDelay > 0f ? armDelay : (float?)null;
			WebcamTuneChargeTime = chargeTime > 0f ? chargeTime : (float?)null;
			WebcamTuneMineMax = mineMax > 0 ? mineMax : (int?)null;
			WebcamTuneMineSpawn = mineSpawn > 0f ? mineSpawn : (float?)null;
			WebcamTuneVersion++;
		}

		// Drop all runtime overrides (the panel's "Reset to tier" button): the level falls
		// back to its shipped Tunings[] row + any ?wc* URL flags, and re-seeds the panel.
		internal static void ClearWebcamTuneOverride()
		{
			WebcamTuneHearts = null;
			WebcamTuneKills = null;
			WebcamTuneSaucers = null;
			WebcamTuneSaucerSpeed = null;
			WebcamTunePlasmaSpeed = null;
			WebcamTuneSpawnInterval = null;
			WebcamTuneArmDelay = null;
			WebcamTuneChargeTime = null;
			WebcamTuneMineMax = null;
			WebcamTuneMineSpawn = null;
			WebcamTuneVersion++;
		}

		// Mars jumping-spider alignment knobs, shared by LIVE play (Spider.cs), the sprite-harness
		// visualiser (?harness=spiderjump -> Spider.HarnessApplyPhase) AND the live tuner slider panel
		// (SetSpiderOverride below / eaSpider in index.html). They dial the shadow, the launch X, the
		// land-anim resume frame, the launch-beat frame, and the flying-sprite air offset. The frame
		// knobs are nullable (null => the baked Spider.cs consts DefaultJumpFrame/LandFrame); the
		// shadow + air knobs carry the DIALED shipped defaults directly (shadow (37,4) x0.95, air
		// (14,1)), so a plain boot casts the tuned shadow. ?spiderloop= (viz only) ?spiderjumpframe=
		// ?spiderlandframe= ?spiderjumpx= ?spidershadowx=/y=/scale= ?spiderairx=/y= (units: Parse below).
		public static float SpiderLoopSeconds { get; private set; } = 6f;

		public static float? SpiderJumpFrame { get; private set; }

		public static float? SpiderLandFrame { get; private set; }

		public static float? SpiderJumpX { get; private set; }

		// Shadow nudge (design px, +y down) + size x, applied to the generic Floor shadow via the
		// spider's ShadowOffset/ShadowSize. These are the DIALED shipped defaults (not identity), so a
		// plain boot casts the tuned shadow; the panel/URL flags override them.
		public static float SpiderShadowX { get; private set; } = 37f;

		public static float SpiderShadowY { get; private set; } = 4f;

		public static float SpiderShadowScale { get; private set; } = 0.95f;

		// Flying-sprite (spiderjump) draw offset (design px, +y down; negative y lifts it) so the
		// airborne pose lines up with the ground rear-up/land poses at the launch + landing transitions
		// ("start y of flying mode"). DIALED shipped defaults (14, 1) by eye; ?spiderairx=/y= + the
		// tuner panel override them.
		public static float SpiderAirX { get; private set; } = 14f;

		public static float SpiderAirY { get; private set; } = 1f;

		// ?spiderphase=<0..1> FREEZES the jump sim at that fraction of one cycle (instead of looping)
		// so a screenshot of a specific beat -- e.g. the airborne apex -- is deterministic, the same
		// "reliable still" the harness gives for a frozen frame. null => the cycle loops.
		public static float? SpiderPhase { get; private set; }

		// Runtime setter for the live spider-tuner slider panel (Compat/DebugInput.SetSpider ->
		// eaSpider() in index.html, shown on ?harness=spiderjump / ?level=Level2&spiders / ?spidertune).
		// Lets the six alignment knobs be dragged in real time instead of reloading with new ?spider*
		// flags each nudge -- same effect as the ?spiderjumpframe/?spiderlandframe/?spiderjumpx/
		// ?spidershadowx/y/scale URL flags, live. The ?harness=spiderjump sim (Spider.HarnessApplyPhase)
		// + shadow overlay read these every frame, so a drag re-aligns on the very next Draw; in the
		// ?spiders LIVE wave they're read at each Spider.Initialize, so a change takes effect on the
		// NEXT spider spawned (jumpframe/jumpx/shadow) -- landframe is read live on touchdown. `jumpX`
		// null => launch X stays RANDOM per spider (the shipped behaviour); a value pins it.
		internal static void SetSpiderOverride(float jumpFrame, float landFrame, float? jumpX, float shadowX, float shadowY, float shadowScale, float airX, float airY, float? phase)
		{
			SpiderJumpFrame = jumpFrame;
			SpiderLandFrame = landFrame;
			SpiderJumpX = jumpX;
			SpiderShadowX = shadowX;
			SpiderShadowY = shadowY;
			SpiderShadowScale = shadowScale > 0f ? shadowScale : 1f;
			SpiderAirX = airX;
			SpiderAirY = airY;
			// null => the harness LOOPS the cycle; a value FREEZES it there (the panel's scrub, so the
			// user can park on the last ground frame before launch and nudge the air offset to match).
			SpiderPhase = phase;
		}

		// Online co-op (Stage 11, plans/stage11-online-coop.md). ?net=host / ?net=join opts a
		// session into the co-op net layer (Compat/Net/NetSession); no ?net flag = None = the
		// net layer is never constructed, so a plain boot is byte-identical single-player (the
		// hard invariant). ?room=<name> picks the loopback room -- BroadcastChannel name
		// "eanet-<room>" -- so parallel test pairs don't cross-talk (default "dev").
		public static NetRole NetRole { get; private set; } = NetRole.None;

		public static string NetRoom { get; private set; } = "dev";

		// ?netlog: verbose per-message net logging (every spawn/death/blast event). The
		// 5-second "[net] ..." metrics summary is always on while a session is active.
		public static bool NetLog { get; private set; }

		// Card 11.4 dev flags for the REAL transport without the menu flow: ?rtc makes a
		// ?net=host/join boot use WebRtcTransport (signaling + real DataChannels) instead
		// of the BroadcastChannel loopback; the host tab prints its room code to the
		// console and the join tab passes it back via ?code=. ?signal= overrides the
		// signaling server URL (a local `uvicorn main:app --port 8091` rig uses
		// ?signal=ws://localhost:8091/ws). The menu-driven lobby always uses WebRTC and
		// needs none of these.
		public const string DefaultSignalUrl = "wss://notzelda.haraldmaassen.com/rotea/ws";

		public static bool NetRtc { get; private set; }

		public static string NetSignal { get; private set; } = DefaultSignalUrl;

		public static string NetCode { get; private set; } = "";

		// ?netfakehash=<s>: override THIS tab's build-hash fingerprint so two dev tabs disagree,
		// driving the real peerHash-mismatch -> reject flow (RejectBuild -> "update required")
		// on the BroadcastChannel dev rig -- both tabs otherwise read 'dev' and never mismatch.
		// The purpose-built two-tab verification for the reject handshake + its teardown grace.
		// Null/empty = the genuine WebRtcInterop.BuildHash(); dev-only, byte-identical when unset.
		public static string NetFakeBuildHash { get; private set; } = "";

		// ?aiplayer: force the LOCAL player's ship onto the existing PlayerShip AI branch
		// (ControlDevice.AI / DoAIMove/DoAIFire -- the attract-demo behaviour) at level start,
		// so two net tabs can drive themselves unattended (the user-specified 11.1 testing
		// strategy: under distributed authority the wire carries ship STATE, so an AI-driven
		// ship replicates byte-identically to a human one). The controller itself stays
		// Keyboard/pad -- only the Update branch is forced -- so joins, pause and the net
		// layer's "which ship is local" logic are untouched. Remote puppets are never forced.
		public static bool AIPlayer { get; private set; }

		// ?netscript (card 11.3): replace the booted level's event list with a compressed
		// ~60s script that fires every replicated beat type (message, warning, background
		// ops, checkpoints, music switch, victory) -- the purpose-built two-tab
		// verification for level-script replication. Pair with ?level=Level1&net=host/join.
		public static bool NetScript { get; private set; }

		// ?aifriends=<0-3> (coverage-gaps follow-up): seed Settings.Friends at a ?level= direct
		// boot so the "Mechanical Friends" AI helper ships auto-join without walking the cheats
		// menu -- the purpose-built seam for two-tab verification of host-authoritative AI-friend
		// replication (Compat/Net/NetSession.Friends). Applied in Game1.LaunchLevelDirect; shipped
		// builds are unchanged (0 = off, and it only takes effect on a debug ?level= boot).
		public static int AiFriends { get; private set; }

		// Artificial network impairment (card 40334a8f, plans/net-impairment.md), applied to
		// INBOUND traffic by Compat/Net/NetImpairment so the drop-tolerance paths cards
		// 11.1-11.3 built actually get exercised. ?netlag=<ms> (0-500) delays both lanes;
		// ?netloss=<0-100> drops STREAM-lane packets only (the reliable lane is never dropped
		// or reordered -- that contract is what everything above INetTransport assumes).
		// 0/0 = the wrapper's inline pass-through, so an unimpaired net session behaves exactly
		// as it did before. All three are live-settable from the eaNetSim panel.
		public static float NetLagMs { get; private set; }

		public static float NetLossPct { get; private set; }

		// Jitter is deliberately PANEL-ONLY (no URL flag): +/- this many ms on each stream
		// packet's release, which is the only way the stream lane ever actually REORDERS and so
		// the only way ordViol/seqGap tolerance gets tested. The reliable lane's releases are
		// clamped monotone, so jitter can never reorder it.
		public static float NetJitterMs { get; private set; }

		// Runtime setter for the live impairment panel (Compat/DebugInput.SetNetSim ->
		// eaNetSim in index.html). The panel always sends the full three-knob state.
		internal static void SetNetSimOverride(float lagMs, float lossPct, float jitterMs)
		{
			// float.IsNaN guards first: MathHelper.Clamp passes NaN straight through, and a NaN
			// lag would cast to a garbage long release time inside NetImpairment.
			NetLagMs = float.IsNaN(lagMs) ? 0f : MathHelper.Clamp(lagMs, 0f, Net.NetImpairment.MaxLagMs);
			NetLossPct = float.IsNaN(lossPct) ? 0f : MathHelper.Clamp(lossPct, 0f, Net.NetImpairment.MaxLossPct);
			NetJitterMs = float.IsNaN(jitterMs) ? 0f : MathHelper.Clamp(jitterMs, 0f, Net.NetImpairment.MaxJitterMs);
		}

		// Public game browser (card 2001fbd8). ?gamebrowser boots STRAIGHT to the
		// "Join Online Game" carousel populated with injected FAKE entries (no server,
		// no WebRTC) so the carousel's appearance -- level art, difficulty/players/ping/
		// code info text, scroll feel -- can be screenshot in isolation, the ?textshot /
		// ?gamebrowser pattern. Hijacks boot => SkipSplash + AutoStart, and is in Active.
		public static bool GameBrowser { get; private set; }

		// ?netjip: the two-window join-in-progress test. Pair with ?level=<Name> (+ ?invuln):
		// the host boots straight into a level, solo, and LISTS it despite the debug boot
		// (NetListing's eligibility normally refuses a DebugFlags.Active / cheating host, so
		// a plain ?level= host could never list). The host prints its room code; a second
		// window joins mid-level via the menu's Join Online Game (or ?net=join&rtc&code=),
		// and both sides' [net] metrics tell the JIP story. In Active.
		public static bool NetJip { get; private set; }

		// True if any debug flag is active (i.e. the boot path was altered).
		public static bool Active { get; private set; }

		public static void Parse(string query)
		{
			if (string.IsNullOrEmpty(query))
			{
				Hint();
				return;
			}
			if (query[0] == '?')
			{
				query = query.Substring(1);
			}
			foreach (string part in query.Split('&'))
			{
				if (part.Length == 0)
				{
					continue;
				}
				int eq = part.IndexOf('=');
				string key = (eq < 0 ? part : part.Substring(0, eq)).Trim().ToLowerInvariant();
				string val = eq < 0 ? null : Uri.UnescapeDataString(part.Substring(eq + 1));
				switch (key)
				{
				case "menu":
					if (IsOn(val))
					{
						SkipSplash = true;
						AutoStart = true;
					}
					break;
				case "skipsplash":
					SkipSplash = IsOn(val);
					break;
				case "autostart":
					AutoStart = IsOn(val);
					break;
				case "noattract":
				case "nodemo":
					NoAttract = IsOn(val);
					break;
				case "unlockall":
				case "unlock":
					UnlockAll = IsOn(val);
					break;
				case "invuln":
				case "invulnerability":
				case "god":
					Invuln = IsOn(val);
					break;
				case "win":
					Win = IsOn(val);
					break;
				case "loadlog":
				case "profileloads":
					LoadLog = IsOn(val);
					break;
				case "metalscore":
					MetalScore = IsOn(val);
					break;
				case "hitboxes":
				case "hitbox":
					ShowHitboxes = IsOn(val);
					break;
				case "shake":
				case "screenshake":
					// Bare ?shake / =true keeps the default 1; a number scales it (0 = off).
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var shk))
					{
						ShakeAmount = (shk < 0f) ? 0f : (shk > 3f) ? 3f : shk;
					}
					else
					{
						ShakeAmount = IsOn(val) ? 1f : 0f;
					}
					break;
				case "hitstop":
					Hitstop = IsOn(val);
					break;
				case "slowmotrail":
					SlowmoTrail = IsOn(val);
					break;
				case "slowmotraildecay":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var smd))
					{
						SlowmoTrailDecay = (smd < 0f) ? 0f : (smd > 0.99f) ? 0.99f : smd;
					}
					break;
				case "slowmotrailstrength":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var sms))
					{
						SlowmoTrailStrength = (sms < 0f) ? 0f : (sms > 1f) ? 1f : sms;
					}
					break;
				case "holofilter":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var hf))
					{
						HoloFilter = (hf < 0f) ? 0f : (hf > 2f) ? 2f : hf;
					}
					break;
				case "holoburst":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var hb))
					{
						HoloBurst = (hb < 0f) ? 0f : (hb > 2f) ? 2f : hb;
					}
					break;
				case "hologreen":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var hg))
					{
						HoloGreen = (hg < 0f) ? 0f : (hg > 1f) ? 1f : hg;
					}
					break;
				case "hologreenpulse":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var hgp))
					{
						HoloGreenPulse = (hgp < 0f) ? 0f : (hgp > 1f) ? 1f : hgp;
					}
					break;
				case "holostaticrate":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var hsr))
					{
						HoloStaticRate = (hsr < 0f) ? 0f : (hsr > 1f) ? 1f : hsr;
					}
					break;
				case "blastactive":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var ba))
					{
						BlastActiveAlpha = (ba < 0f) ? 0f : (ba > 1f) ? 1f : ba;
					}
					break;
				case "blasthit":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var bh) && bh > 0f)
					{
						BlastHitFactor = bh;
					}
					break;
				case "reticlesize":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var rs) && rs > 0f)
					{
						ReticleSize = rs;
					}
					break;
				case "blastloop":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var bl) && bl > 0f)
					{
						BlastLoopSeconds = bl;
					}
					break;
				case "lazerchargescale":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var lcs) && lcs > 0f)
					{
						LazerChargeScale = lcs;
					}
					break;
				case "lazercapscale":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var lcap) && lcap >= 0f)
					{
						LazerCapScale = lcap;
					}
					break;
				case "lazerarcs":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var larc) && larc >= 0f)
					{
						LazerArcRate = larc;
					}
					break;
				case "lazertendrilspeed":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var lts) && lts >= 0f)
					{
						LazerTendrilSpeed = lts;
					}
					break;
				case "lazerarclife":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var lal) && lal > 0f)
					{
						LazerArcLife = lal;
					}
					break;
				case "connectorbolts":
					if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cbolts) && cbolts >= 0)
					{
						ConnectorBoltCount = cbolts;
					}
					break;
				case "connectorarcs":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var carc) && carc >= 0f)
					{
						ConnectorArcRate = carc;
					}
					break;
				case "connectorjitter":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var cjit) && cjit >= 0f)
					{
						ConnectorJitter = cjit;
					}
					break;
				case "connectorpulse":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var cpul) && cpul >= 0f)
					{
						ConnectorPulse = cpul;
					}
					break;
				case "connectorglow":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var cglo) && cglo >= 0f)
					{
						ConnectorGlow = cglo;
					}
					break;
				case "walltowers":
					WallTowers = IsOn(val);
					break;
				case "wallsonly":
					WallsOnly = IsOn(val);
					break;
				case "walltrace":
					WallTrace = IsOn(val);
					break;
				case "wallpoptest":
					WallPopTest = IsOn(val);
					break;
				case "wall3dbands":
					if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var w3b) && w3b >= 1 && w3b <= 64)
					{
						Wall3DBands = w3b;
					}
					break;
				case "walldepth":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var wd) && wd > 0f && wd < 1f)
					{
						WallDepth = wd;
					}
					break;
				case "wallfog":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var wf) && wf >= 0f)
					{
						WallFog = wf;
					}
					break;
				case "wallfogcolor":
					if (TryParseHexColor(val, out var wfc))
					{
						WallFogColor = wfc;
					}
					break;
				case "wallsidedark":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var wsd) && wsd >= 0f)
					{
						WallSideDark = wsd;
					}
					break;
				case "wallsidetile":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var wst) && wst > 0f && wst <= 32f)
					{
						WallSideTile = wst;
					}
					break;
				case "wallfacelight":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var wfl) && wfl >= 0f)
					{
						WallFaceLight = wfl;
					}
					break;
				case "wallfaceangle":
					// Signed: any azimuth.
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var wfa))
					{
						WallFaceAngle = wfa;
					}
					break;
				case "walltoplift":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var wtl) && wtl >= 0f)
					{
						WallTopLift = wtl;
					}
					break;
				case "wallwisps":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var ww) && ww >= 0f)
					{
						WallWisps = ww;
					}
					break;
				case "wallwispspeed":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var wws) && wws >= 0f)
					{
						WallWispSpeed = wws;
					}
					break;
				case "lazershot":
					Lazershot = IsOn(val);
					if (Lazershot)
					{
						SkipSplash = true;
						AutoStart = true;
					}
					break;
				case "textshot":
					Textshot = IsOn(val);
					if (Textshot)
					{
						SkipSplash = true;
						AutoStart = true;
					}
					break;
				case "flyspiderscale":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var fss) && fss > 0f)
					{
						FlySpiderScale = fss;
					}
					break;
				case "wcdiff":
				case "webcamdiff":
					// Like ?level=, reject numeric input: Enum.TryParse would take "2"
					// -> (DifficultyLevel)2 (Hard) by ordinal, which IsDefined then passes.
					if (!string.IsNullOrEmpty(val) && !char.IsDigit(val.Trim()[0])
						&& val.Trim()[0] != '+' && val.Trim()[0] != '-'
						&& Enum.TryParse<EvilAliens.Settings.DifficultyLevel>(val.Trim().Replace(' ', '_'), ignoreCase: true, out var wcd)
						&& Enum.IsDefined(typeof(EvilAliens.Settings.DifficultyLevel), wcd))
					{
						WebcamDifficulty = wcd;
					}
					break;
				case "wchearts":
					if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var wch) && wch > 0)
					{
						WebcamHearts = wch;
					}
					break;
				case "wckills":
					if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var wck) && wck > 0)
					{
						WebcamKills = wck;
					}
					break;
				case "wcsaucers":
					if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var wcs) && wcs > 0)
					{
						WebcamSaucers = wcs;
					}
					break;
				case "wcsaucerspeed":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var wcss) && wcss > 0f)
					{
						WebcamSaucerSpeed = wcss;
					}
					break;
				case "wcplasmaspeed":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var wcps) && wcps > 0f)
					{
						WebcamPlasmaSpeed = wcps;
					}
					break;
				case "wcspawn":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var wcsp) && wcsp > 0f)
					{
						WebcamSpawnInterval = wcsp;
					}
					break;
				case "wcarm":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var wcar) && wcar > 0f)
					{
						WebcamArmDelay = wcar;
					}
					break;
				case "wccharge":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var wcch) && wcch > 0f)
					{
						WebcamChargeTime = wcch;
					}
					break;
				case "wcminemax":
					if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var wcmm) && wcmm > 0)
					{
						WebcamMineMax = wcmm;
					}
					break;
				case "wcminespawn":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var wcms) && wcms > 0f)
					{
						WebcamMineSpawn = wcms;
					}
					break;
				case "wcminelife":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var wcml) && wcml > 0f)
					{
						WebcamMineLife = wcml;
					}
					break;
				case "wcmothership":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var wcmo) && wcmo >= 0f)
					{
						WebcamMothership = wcmo;
					}
					break;
				case "wcmothershipdir":
					if (!string.IsNullOrEmpty(val))
					{
						string d = val.Trim().ToLowerInvariant();
						if (d == "vertical" || d == "horizontal")
						{
							WebcamMothershipDir = d;
						}
					}
					break;
				case "wcmothershipfreeze":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var wcmf) && wcmf >= 0f)
					{
						WebcamMothershipFreeze = wcmf;
					}
					break;
				case "wchitleeway":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var wchl) && wchl >= 0f)
					{
						WebcamHitLeeway = wchl;
					}
					break;
				case "wcavoid":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var wcav) && wcav >= 0f)
					{
						WebcamAvoid = wcav;
					}
					break;
				case "wcreturndelay":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var wcrd) && wcrd >= 0f)
					{
						WebcamReturnDelay = wcrd;
					}
					break;
				case "wctune":
					WebcamTune = IsOn(val);
					break;
				case "huestart":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var hs))
					{
						HueStart = hs;
					}
					break;
				case "hueend":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var he))
					{
						HueEnd = he;
					}
					break;
				case "huetarget":
				case "hue":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var ht))
					{
						HueTarget = ht;
					}
					break;
				case "huecycle":
				case "huesweep":
					HueCycle = IsOn(val);
					break;
				case "hueloop":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var hl) && hl > 0f)
					{
						HueLoopSeconds = hl;
					}
					break;
				case "spiderhelpercycles":
					if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var shc) && shc >= 1)
					{
						SpiderHelperCycles = shc;
					}
					break;
				case "spiderhelperhp":
					if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var shhp) && shhp >= 1)
					{
						SpiderHelperHitPoints = shhp;
					}
					break;
				case "spiderhelperhovery":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var shy))
					{
						SpiderHelperHoverY = shy;
					}
					break;
				case "spiderhelperspeed":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var shs) && shs > 0f)
					{
						SpiderHelperSpeed = shs;
					}
					break;
				case "spiderhelperwindup":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var shw) && shw >= 0f)
					{
						SpiderHelperWindupSeconds = shw;
					}
					break;
				case "spiderhelperfire":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var shf) && shf > 0f)
					{
						SpiderHelperFireSeconds = shf;
					}
					break;
				case "spiderhelperlead":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var shl) && shl >= 0f)
					{
						SpiderHelperFireLead = shl;
					}
					break;
				case "spiderhelperenterpower":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var shep) && shep > 0f)
					{
						SpiderHelperEnterPower = shep;
					}
					break;
				case "difficulty":
					// Reject numeric input (like ?wcdiff): Enum.TryParse would take "2" -> (DifficultyLevel)2
					// by ordinal, which IsDefined then passes -- we want the named tiers only.
					if (!string.IsNullOrEmpty(val) && !char.IsDigit(val.Trim()[0])
						&& val.Trim()[0] != '+' && val.Trim()[0] != '-'
						&& Enum.TryParse<EvilAliens.Settings.DifficultyLevel>(val.Trim().Replace(' ', '_'), ignoreCase: true, out var diff)
						&& Enum.IsDefined(typeof(EvilAliens.Settings.DifficultyLevel), diff))
					{
						Difficulty = diff;
					}
					break;
				case "spiderbosshp":
					if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sbhp) && sbhp > 0)
					{
						SpiderBossHp = sbhp;
					}
					break;
				case "net":
					if (!string.IsNullOrEmpty(val))
					{
						switch (val.Trim().ToLowerInvariant())
						{
						case "host":
							NetRole = NetRole.Host;
							break;
						case "join":
						case "client":
							NetRole = NetRole.Join;
							break;
						default:
							Console.WriteLine("[net] unknown ?net= value '" + val + "' (use host or join)");
							break;
						}
					}
					break;
				case "room":
				case "netroom":
					if (!string.IsNullOrEmpty(val))
					{
						// Sanitize to a safe channel-name fragment (alnum + dash, lowercase).
						string room = "";
						foreach (char c in val.Trim().ToLowerInvariant())
						{
							if (char.IsLetterOrDigit(c) || c == '-')
							{
								room += c;
							}
						}
						if (room.Length > 0)
						{
							NetRoom = room.Length > 32 ? room.Substring(0, 32) : room;
						}
					}
					break;
				case "netlog":
					NetLog = IsOn(val);
					break;
				case "rtc":
					NetRtc = IsOn(val);
					break;
				case "signal":
					if (!string.IsNullOrEmpty(val))
					{
						NetSignal = val.Trim();
					}
					break;
				case "code":
					if (!string.IsNullOrEmpty(val))
					{
						NetCode = val.Trim().ToUpperInvariant();
					}
					break;
				case "netfakehash":
					if (!string.IsNullOrEmpty(val))
					{
						NetFakeBuildHash = val.Trim();
					}
					break;
				case "aiplayer":
					AIPlayer = IsOn(val);
					break;
				case "netscript":
					NetScript = IsOn(val);
					break;
				case "aifriends":
					if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var aif))
					{
						AiFriends = (int)MathHelper.Clamp(aif, 0, 3);
					}
					break;
				case "gamebrowser":
					GameBrowser = IsOn(val);
					if (GameBrowser)
					{
						SkipSplash = true;
						AutoStart = true;
					}
					break;
				case "netjip":
					NetJip = IsOn(val);
					break;
				case "netlag":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var nlag) && nlag >= 0f)
					{
						NetLagMs = MathHelper.Clamp(nlag, 0f, Net.NetImpairment.MaxLagMs);
					}
					break;
				case "netloss":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var nloss) && nloss >= 0f)
					{
						NetLossPct = MathHelper.Clamp(nloss, 0f, Net.NetImpairment.MaxLossPct);
					}
					break;
				case "spiderboss":
					SpiderBoss = IsOn(val);
					break;
				case "marsboss":
					MarsBoss = IsOn(val);
					break;
				case "brainboss":
					BrainBoss = IsOn(val);
					break;
				case "spiders":
					Spiders = IsOn(val);
					break;
				case "tutorialtraining":
					TutorialTraining = IsOn(val);
					break;
				case "castbrain":
					CastBrain = IsOn(val);
					if (CastBrain)
					{
						SkipSplash = true;
						AutoStart = true;
					}
					break;
				case "cast":
					CastShow = IsOn(val);
					if (CastShow)
					{
						SkipSplash = true;
						AutoStart = true;
					}
					break;
				case "texviewer":
					TexViewer = IsOn(val);
					if (TexViewer)
					{
						SkipSplash = true;
						AutoStart = true;
					}
					break;
				case "castbrainscale":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var cbs) && cbs > 0f)
					{
						CastBrainScale = cbs;
					}
					break;
				case "castbrainfps":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var cbf) && cbf > 0f)
					{
						CastBrainFps = cbf;
					}
					break;
				case "spiderloop":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var spl) && spl > 0f)
					{
						SpiderLoopSeconds = spl;
					}
					break;
				case "spiderjumpframe":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var spjf))
					{
						SpiderJumpFrame = spjf;
					}
					break;
				case "spiderlandframe":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var splf))
					{
						SpiderLandFrame = splf;
					}
					break;
				case "spiderjumpx":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var spjx))
					{
						SpiderJumpX = spjx;
					}
					break;
				case "spidershadowx":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var spsx))
					{
						SpiderShadowX = spsx;
					}
					break;
				case "spidershadowy":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var spsy))
					{
						SpiderShadowY = spsy;
					}
					break;
				case "spidershadowscale":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var spss) && spss > 0f)
					{
						SpiderShadowScale = spss;
					}
					break;
				case "spiderairx":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var spax))
					{
						SpiderAirX = spax;
					}
					break;
				case "spiderairy":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var spay))
					{
						SpiderAirY = spay;
					}
					break;
				case "spiderphase":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var spph))
					{
						SpiderPhase = ((spph % 1f) + 1f) % 1f;					}
					break;
				case "harness":
						// The object name itself is the value (?harness=Spider). A bare ?harness
						// with no value is meaningless (no object), so ignore it.
						if (!string.IsNullOrEmpty(val))
						{
							Harness = val.Trim();
							SkipSplash = true;
							AutoStart = true;
						}
						break;
					case "frame":
						if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fr))
						{
							HarnessFrame = fr;
						}
						break;
					case "play":
					case "animate":
						HarnessPlay = IsOn(val);
						break;
					case "bg":
					case "background":
						if (!string.IsNullOrEmpty(val))
						{
							HarnessBg = val.Trim().ToLowerInvariant();
						}
						break;
					case "pos":
						ParsePos(val);
						break;
					case "objscale":
					case "size":
						if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var sc) && sc > 0f)
						{
							HarnessScale = sc;
						}
						break;
					case "rot":
					case "rotation":
						if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var rt))
						{
							HarnessRot = rt;
						}
						break;
					case "fps":
					case "animfps":
						if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var afps) && afps > 0f)
						{
							HarnessFps = afps;
						}
						break;
						case "bulletshot":
						Bulletshot = IsOn(val);
						if (Bulletshot)
						{
							SkipSplash = true;
							AutoStart = true;
						}
						break;
					case "level":
					// Enum.TryParse also accepts numeric strings ("999" -> (Levels)999) and
					// undefined values; require a real defined member so an invalid ?level=
					// falls into the unknown-level branch instead of booting a bogus level.
					if (val.Length > 0 && !char.IsDigit(val[0]) && val[0] != '+' && val[0] != '-'
						&& Enum.TryParse<EvilAliens.Levels>(val, ignoreCase: true, out var lvl)
						&& Enum.IsDefined(typeof(EvilAliens.Levels), lvl))
					{
						Level = lvl;
						SkipSplash = true;
						AutoStart = true;
					}
					else
					{
						Console.WriteLine("[debug] unknown level '" + val + "' (ignored); valid: "
							+ string.Join(", ", Enum.GetNames(typeof(EvilAliens.Levels))));
					}
					break;
				}
			}
			// The level fast-boots belong here (not with the render/feel toggles that stay OUT): they
			// REPLACE a level's whole event list, and `?brainboss` alone -- reaching Level 3 from the
			// menu rather than via ?level= -- would otherwise hijack the level with nothing in the log.
			Active = SkipSplash || AutoStart || NoAttract || Level.HasValue || UnlockAll || Invuln || LoadLog || Harness != null || Bulletshot || Lazershot || Textshot || CastBrain || CastShow || TexViewer || WallsOnly || BrainBoss || TutorialTraining || NetRole != NetRole.None || AIPlayer || NetScript || GameBrowser || NetJip;
			if (Active)
			{
				Console.WriteLine("[debug] flags active: skipSplash=" + SkipSplash
					+ " autoStart=" + AutoStart + " noAttract=" + NoAttract
					+ " level=" + (Level.HasValue ? Level.Value.ToString() : "-")
					+ " unlockAll=" + UnlockAll + " invuln=" + Invuln + " loadLog=" + LoadLog
						+ " metalScore=" + MetalScore
							// Level fast-boots print only when set: they REPLACE a level's whole event list,
							// so "why is this level not playing normally" needs an answer in the log.
							+ (WallsOnly ? " wallsonly" : "")
							+ (BrainBoss ? " brainboss" : "")
							+ (TutorialTraining ? " tutorialtraining" : "")
							+ (NetRole != NetRole.None ? " net=" + NetRole.ToString().ToLowerInvariant() + " room=" + NetRoom : "")
							+ (AIPlayer ? " aiplayer" : "")
						+ (NetScript ? " netscript" : "")
						+ (Harness != null
							? " harness=" + Harness + " frame=" + HarnessFrame + (HarnessPlay ? " play" : "") + " bg=" + HarnessBg
							: ""));
			}
			else
			{
				Hint();
			}
		}

		// Parse a "?pos=x,y" value into HarnessX/HarnessY (800x600 design space). Either
		// component may be omitted ("400," / ",300") to override just one axis; the missing
		// one falls back to centre in HarnessScene.
		private static void ParsePos(string val)
		{
			if (string.IsNullOrEmpty(val))
			{
				return;
			}
			string[] parts = val.Split(',');
			if (parts.Length >= 1 && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
			{
				HarnessX = x;
			}
			if (parts.Length >= 2 && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
			{
				HarnessY = y;
			}
		}

		// Parse a "?wallfogcolor=rrggbb" value (a leading '#' or '0x' is tolerated) into an
		// opaque Color. The alpha channel is never taken from the string -- the wall slices
		// carry their own dissolve alpha, so a colour here only ever names a hue.
		private static bool TryParseHexColor(string val, out Color color)
		{
			color = default(Color);
			if (string.IsNullOrEmpty(val))
			{
				return false;
			}
			string hex = val.Trim();
			if (hex.StartsWith("#", StringComparison.Ordinal))
			{
				hex = hex.Substring(1);
			}
			else if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
			{
				hex = hex.Substring(2);
			}
			if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
			{
				return false;
			}
			color = new Color((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
			return true;
		}

		private static void Hint()
		{
			Console.WriteLine("[debug] no debug flags. URL options: ?menu  ?noattract  "
				+ "?level=<Name>  ?skipsplash  (see Compat/DebugFlags.cs)");
		}

		// A bare flag (?menu) or =1/=true/=yes/=on means ON; =0/=false/=no/=off means OFF.
		private static bool IsOn(string val)
		{
			if (val == null)
			{
				return true;
			}
			switch (val.Trim().ToLowerInvariant())
			{
			case "":
			case "1":
			case "true":
			case "yes":
			case "on":
				return true;
			default:
				return false;
			}
		}
	}
}
