using System;
using System.Globalization;

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
	//   ?hitstop=0     disable the hit-stop freeze frames (kill micro-stop, player death,
	//                  boss kill — Compat/Juice.cs). ON by default.
	//   ?metalscore=0  disable the chrome-sheen (metal.fx) on the in-game score + "Press Start"
	//                  text (it is ON by default) to A/B the plain flattened drop shadow
	//   ?slowmotrail=0 disable the cinematic slow-motion ghost-trail post-process (ON by default;
	//                  reverts the 1up slowmo to the plain time-scale + bloom look). Tune the look
	//                  with ?slowmotraildecay=<0..0.99> (ghost persistence) and
	//                  ?slowmotrailstrength=<0..1> (how strongly trails mix over the live frame).
	//                  See it on demand without grinding a 1up: console eaSlowmo() in a level.
	//   ?bulletshot    BULLET SHOWCASE: boot straight onto a frozen reference tableau --
	//                  the player ship + a UFO cluster + both bullet types on the starfield,
	//                  drawn by the real pipeline. A composed cousin of ?harness, built for
	//                  redrawing the bullet sprites (see Compat/BulletShowcaseScene.cs).
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

		// Force the Invulnerability cheat ON at boot (so playtesting a level doesn't keep
		// dying). Applied in Game1.startScreen_OnFinished after Settings has loaded.
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

		// Master multiplier on the trauma-based screen shake (Compat/Juice.cs). 1 = the
		// shipped feel, 0 = off, >1 exaggerates while tuning (?shake=, clamped 0..3).
		// A pure camera/render look, so — like MetalScore/SlowmoTrail — kept OUT of `Active`.
		public static float ShakeAmount { get; private set; } = 1f;

		// Hit-stop freeze frames (Compat/Juice.cs): the per-kill micro-stop + the longer
		// player-death/boss-kill stops. ON by default; ?hitstop=0 disables. Technically a
		// (tiny) gameplay-time effect, but like the shake it's a feel toggle — OUT of `Active`.
		public static bool Hitstop { get; private set; } = true;

		// Route the in-game score / "Player X — Press Start" text through the chrome-sheen
		// effect (metal.fx) instead of the plain flattened drop-shadow draw. ON by default
		// (the card author kept the chrome look); ?metalscore=0 / =false disables it to A/B
		// the plain flatten. Does NOT alter the boot path — purely a render look, so it is
		// deliberately left OUT of `Active` (a clean boot stays "no debug flags").
		public static bool MetalScore { get; private set; } = true;

		// Blast (bomb) tuning knobs for the sprite-harness lifetime visualiser (?harness=blast).
		// All null/default => Blast.cs uses its baked-in constants, so a shipped build is unchanged.
		//   ?blastactive=<0..1>  the blast stops dealing damage once its fade alpha drops below this
		//                        (default 0.5 — collide while at least half-opaque; higher = shorter
		//                        active window, so "dangerous" tracks "clearly visible").
		//   ?blasthit=<f>        fraction of the visible radius that deals damage (default 0.8).
		//   ?blastloop=<sec>     viz only: seconds for one spawn->fade sweep in the harness (default 3).
		public static float? BlastActiveAlpha { get; private set; }

		public static float? BlastHitFactor { get; private set; }

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
		//   ?lazerarcs=<n>         cap on the number of electric tendrils crackling off the beam.
		//   ?lazerarclife=<sec>    lifespan of one tendril before it fades + respawns elsewhere
		//                          (shortlived = "pop up all over"; long = the old "sits forever").
		public static float? LazerChargeScale { get; private set; }

		public static float? LazerCapScale { get; private set; }

		public static int? LazerArcCount { get; private set; }

		public static float? LazerArcLife { get; private set; }

		// Laser showcase scene (Compat/LazerShowcaseScene.cs): the chargeup swarm + a full-grown
		// beam side by side on the starfield, ANIMATING (unlike the frozen ?harness/?bulletshot),
		// so the tendrils / chargeup / caps can be watched while tuning. Opt in with ?lazershot;
		// non-null => SkipSplash + AutoStart and the boot routes into the showcase.
		public static bool Lazershot { get; private set; }

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

		// SpiderBoss "helper mothership" feel knobs (Game/EvilAliens/SpiderHelperMothership.cs +
		// the trigger in SpiderBoss.cs). When the Level2 spider boss goes un-damaged for
		// SpiderHelperIdleSeconds, a mothership slides in showing just its underside at the top,
		// halts dead-centre, fires a Lazer straight DOWN for SpiderHelperFireSeconds (which hits the
		// boss on a fly-by), then leaves east. It is "fake killable": flashes/reddens but never dies
		// in time. All have shipping defaults, so a plain boot is unchanged; these only let the feel
		// be tuned live. See the flags:
		//   ?spiderhelperidle=<sec>    seconds of no boss damage before help arrives (default 30)
		//   ?spiderhelperhovery=<y>    sprite-centre Y; more negative pushes the ship up so less of
		//                              it shows (default 10 => the belly + lower spikes hang in, dome cut)
		//   ?spiderhelperspeed=<f>     horizontal design-px/ms fly speed (default 0.3)
		//   ?spiderhelperfire=<sec>    how long the downward laser holds (default 4.5)
		//   ?spiderhelperlead=<px>     gap from sprite centre down to the beam's start = its belly
		//                              (default 150)
		public static float SpiderHelperIdleSeconds { get; private set; } = 30f;

		public static float SpiderHelperHoverY { get; private set; } = 10f;

		public static float SpiderHelperSpeed { get; private set; } = 0.3f;

		public static float SpiderHelperFireSeconds { get; private set; } = 4.5f;

		public static float SpiderHelperFireLead { get; private set; } = 150f;

		// Fast-boot Level2 straight to the spider-boss fight (skips the whole level) so the helper
		// mothership + boss interaction can be watched in seconds. Pair with ?level=Level2 (+ ?invuln,
		// ?spiderhelperidle=<small>). A pure test shortcut, like ?win. See Level2.PopulateEventList.
		public static bool SpiderBoss { get; private set; }

		// Fast-boot Level2 straight to a continuous pure-spider GROUND wave (skips the whole level)
		// so the animation-driven jump can be watched + dialed in REAL play -- the ?harness=spiderjump
		// sim's arc is illustrative, this is the live Spider.Update path. Pair with ?level=Level2
		// (+ ?invuln, + the ?spiderjumpframe=/?spiderjumpx=/?spiderlandframe=/?spidershadow* knobs).
		// A pure test shortcut, like ?spiderboss. See Level2.PopulateEventList / PopulateSpidersOnly.
		public static bool Spiders { get; private set; }

		// Cast "Brain Spawn" viewer (?castbrain): boot into the end-credits Cast screen parked
		// on the braineroid entry, reusing HarnessScene. Non-null => SkipSplash + AutoStart and
		// the boot routes into the harness in cast-brain mode instead of the menu/a level.
		public static bool CastBrain { get; private set; }

		// On-screen scale + animation fps of the cast "Brain Spawn" specimen. null => the baked
		// defaults in CastDisplayer, so a shipped build is unchanged; these override for by-eye
		// tuning via ?castbrain (the blast/colorize-tuner pattern). ?castbrainscale= / ?castbrainfps=
		public static float? CastBrainScale { get; private set; }

		public static float? CastBrainFps { get; private set; }

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
		public static EvilAliens.Settings.DifficultyLevel? WebcamDifficulty { get; private set; }

		public static int? WebcamHearts { get; private set; }

		public static int? WebcamKills { get; private set; }

		public static int? WebcamSaucers { get; private set; }

		public static float? WebcamSaucerSpeed { get; private set; }

		public static float? WebcamPlasmaSpeed { get; private set; }

		// Spider jump-cycle tuning knobs for the sprite-harness visualiser (?harness=spiderjump).
		// The grounded Mars Spider's whole rear-up -> launch -> arc -> land cycle is otherwise only
		// reachable by driving a live level; the harness LOOPS a self-contained sim of it (see
		// Spider.HarnessApplyPhase) so the shadow, the jump-start X and the land-anim resume frame can
		// be aligned by eye. ALL null/default => the sim uses its baked-in reference values and,
		// crucially, LIVE gameplay is byte-identical (these knobs are only ever read while the harness
		// is up). ?spiderloop= ?spiderjumpframe= ?spiderlandframe= ?spiderjumpx= ?spidershadowx=
		// ?spidershadowy= ?spidershadowscale=  (see the Parse cases below for units).
		public static float SpiderLoopSeconds { get; private set; } = 6f;

		public static float? SpiderJumpFrame { get; private set; }

		public static float? SpiderLandFrame { get; private set; }

		public static float? SpiderJumpX { get; private set; }

		public static float SpiderShadowX { get; private set; }

		public static float SpiderShadowY { get; private set; }

		public static float SpiderShadowScale { get; private set; } = 1f;

		// ?spiderphase=<0..1> FREEZES the jump sim at that fraction of one cycle (instead of looping)
		// so a screenshot of a specific beat -- e.g. the airborne apex -- is deterministic, the same
		// "reliable still" the harness gives for a frozen frame. null => the cycle loops.
		public static float? SpiderPhase { get; private set; }

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
					if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var larc) && larc >= 0)
					{
						LazerArcCount = larc;
					}
					break;
				case "lazerarclife":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var lal) && lal > 0f)
					{
						LazerArcLife = lal;
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
				case "spiderhelperidle":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var shi) && shi >= 0f)
					{
						SpiderHelperIdleSeconds = shi;
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
				case "spiderboss":
					SpiderBoss = IsOn(val);
					break;
				case "spiders":
					Spiders = IsOn(val);
					break;
				case "castbrain":
					CastBrain = IsOn(val);
					if (CastBrain)
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
			Active = SkipSplash || AutoStart || NoAttract || Level.HasValue || UnlockAll || Invuln || LoadLog || Harness != null || Bulletshot || Lazershot || CastBrain;
			if (Active)
			{
				Console.WriteLine("[debug] flags active: skipSplash=" + SkipSplash
					+ " autoStart=" + AutoStart + " noAttract=" + NoAttract
					+ " level=" + (Level.HasValue ? Level.Value.ToString() : "-")
					+ " unlockAll=" + UnlockAll + " invuln=" + Invuln + " loadLog=" + LoadLog
						+ " metalScore=" + MetalScore
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
