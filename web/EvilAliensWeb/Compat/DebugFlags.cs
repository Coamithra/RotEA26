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
	//   ?noattract     disable the menu's idle -> demo (attract) mode  (alias: ?nodemo).
	//                  Deliberately OUT of `Active` (card af63f958) so an ONLINE JOINER can pass
	//                  it: a menu session rejects the pairing on its own Active bit, and its
	//                  lobby is otherwise yanked into the attract demo mid-navigation.
	//   ?demo=<1|2|3>  pin WHICH attract demo the idle menu drops into (Demo1/Demo2/Demo3),
	//                  which is otherwise an unseeded random roll per launch. NOT the
	//                  off-switch of ?nodemo/?noattract -- those disable attract entirely.
	//   ?seed=<n>      seed the gameplay RNG (RandomHelper) so two runs of the same boot reach
	//                  the same world -- what makes a level-level eahl screenshot A/B measure
	//                  the change rather than the divergence. No flag = unseeded, as shipped.
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
	//   ?nethitstop=1  let hit-stop freeze game time inside an online co-op session again
	//                  (card 68f62e92 refuses every hit-stop there — one peer's world halting
	//                  while the wire streams on is what rewound the other peer's enemies).
	//                  THE DELIBERATE BUG REPRODUCTION, and IN `Active` for that reason.
	//   ?netstaleguard=0  turn the world-snapshot staleness guard OFF, so a reordered or late
	//                  snapshot entry drags a puppet backwards again (card f5cf7a5c). The other
	//                  deliberate bug reproduction, and in `Active` for the same reason. ON by
	//                  default, so `Active` tests its NEGATION.
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
	//                  bloom). Built for iterating on drawing code: the image is
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
	//   ?creditsshot[=1|2|3]  POST-LEVEL TEXT CRAWL: boot straight into CreditsScene set up as
	//                  though that level had just been beaten (3 = the Hard "you have done it"
	//                  crawl + Cast + full credits). Reaching it otherwise means finishing a
	//                  level (or ?level=N&win). Esc/Enter -> menu, as in normal play.
	//     ?crawlpos=<designY>  park the crawl at that scroll position instead of scrolling it,
	//                  so the taper can be screenshot at a chosen point (textpos starts at 650
	//                  and falls; ~560 puts the first line at the screen bottom).
	//     ?crawlskew=<f>  amount of the Star Wars-style perspective taper on the crawl (larger
	//                  at the screen bottom, smaller at the top; 0 = the flat pre-card crawl).
	//                  CLAMPED to what keeps the widest line on screen -- the shipped crawls
	//                  saturate at ~0.08-0.10, so the 0.2 default draws as that and a bigger
	//                  value changes nothing. Applies in normal play too -- the shipped look.
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
		// Deliberately OUT of Active (card af63f958): it unwires one menu hook and alters no
		// gameplay, and an ONLINE JOINER -- whose lobby IS a menu -- must be able to pass it.
		public static bool NoAttract { get; private set; }

		// ?mute: silence BOTH audio subsystems for this boot. They do not share a bus, so it
		// takes two switches (Game1.Initialize applies both): SoundEffect.MasterVolume = 0 for
		// the KNI SFX/speech path, and eaMusic.setMute for the WebAudio music layer. Neither
		// the Options "Music" toggle nor a tab mute covers both -- the former is music-only,
		// the latter is not reachable from a URL, and a two-tab co-op run wants one link that
		// comes up silent on both ends.
		// Deliberately OUT of Active, for the ?noattract reason: it changes no gameplay,
		// difficulty, unlock or fairness, and putting it in would refuse online play for the
		// very runs it exists to make bearable.
		public static bool Mute { get; private set; }

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

		// LEVEL 2 ONLY -- Level2.cs is its only reader, and it replaces that level's script with
		// the ending unlock chain -> Victory() (forcing Hard along the way), to exercise the
		// victory -> credits -> menu handoff without playing the level out. It does NOT work "in
		// any GameScene", which this comment used to claim and which the root CLAUDE.md's
		// `?level=N&win` phrasing inherited -- both corrected by card 3b6c12e7, whose two-peer
		// level-end probes are its main user now (a host wins from its SCRIPT, never from a wire
		// beat, so this is the only route a rig has to a co-op HOST victory). Out of `Active` and
		// nothing rests on that: it is useless without `?level=`, which is in `Active` -- so a
		// menu-lobby pairing carrying it still needs `?netallowdebug` either way.
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

		// ?binlog — ComponentBin lifecycle diagnostics (card 02d9ad67): logs adds diverted by
		// the standing purge filter and world objects frozen by a pause-time add. Pure console
		// output, no behaviour change; deliberately OUT of `Active`.
		public static bool BinLog { get; private set; }

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

		// ---- Bomb detonation ripple (Compat/BombRipple + bombripple.fx, card 5f38ed35) -----
		// All null => the baked BombRipple.Default* consts, so a shipped build with no flags
		// is byte-identical to the tuned look. Pure render/feel, so the whole family stays
		// OUT of `Active` (like every ?holo* and ?slowmotrail) and can never refuse a co-op
		// session.

		// Master scale on the whole effect; 0 = off (the kill switch). ?ripple=
		public static float? Ripple { get; private set; }

		// Peak UV displacement of the wavefront. ?rippleamp=
		public static float? RippleAmp { get; private set; }

		// How far the wavefront travels over its life, in fractions of screen HEIGHT.
		// ?rippleradius=
		public static float? RippleRadius { get; private set; }

		// Seconds one ring lives. ?rippleduration=
		public static float? RippleDuration { get; private set; }

		// Gaussian half-width of the wavefront (same units as the radius). ?ripplewidth=
		public static float? RippleWidth { get; private set; }

		// Exponent on the (1 - t) amplitude decay. ?ripplefalloff=
		public static float? RippleFalloff { get; private set; }

		// Additive caustic glint on the crest. ?ripplerim=
		public static float? RippleRim { get; private set; }

		// Let the MINI blasts (asploding bullets) ripple too. OFF by default -- a dozen at
		// once strobes the frame. ?ripplemini
		public static bool RippleMini { get; private set; }

		// ?ripplephase=<0..1>: park ONE ring at that point in its life and hold it there, so
		// a still screenshot shows the deformation at a known phase (the card's scrub rig --
		// the effect is time-varying, so a timed live screenshot proves nothing). Works on
		// any boot: ?level=Level2&invuln&ripplephase=0.35.
		public static float? RipplePhase { get; private set; }

		// ?ripplecenter=x,y: where the parked ring sits, in 800x600 design coords.
		// null => screen centre.
		public static Vector2? RippleCenter { get; private set; }

		// ?ripplepower=<0..4>: the bomb powerup level the PARKED ring pretends it came
		// from (amplitude and radius scale with it). null => 0, a bare bomb.
		public static float? RipplePower { get; private set; }

		// JS bridge for the live ripple tuner panel (eaRipple in wwwroot/index.html, shown on
		// ?rippletune): overrides the knobs in real time. BombRipple resolves ALL of them in
		// PackedRings rather than baking them in at Fire, so a slider drag retunes the very
		// next frame -- rings already travelling, and the parked screenshot ring, included.
		internal static void SetRippleOverride(float? master, float? amp, float? radius,
			float? duration, float? width, float? falloff, float? rim, float? phase)
		{
			Ripple = master;
			RippleAmp = amp;
			RippleRadius = radius;
			RippleDuration = duration;
			RippleWidth = width;
			RippleFalloff = falloff;
			RippleRim = rim;
			RipplePhase = phase;
		}

		// Park/un-park the screenshot ring on its own, without disturbing the tuning knobs
		// (eaRipple.park(phase) / DebugInput.RipplePark). null => live again.
		internal static void SetRipplePhaseOverride(float? phase)
		{
			RipplePhase = phase;
		}

		// ?respawnphase=<0..1>: park the respawn clock ring at that point in its fill and hold
		// it there (card 37f3a663) -- the ?ripplephase= convention, negative meaning live. The
		// real ring takes ~10 s to fill and pops in the last 220 ms, so no timed screenshot can
		// catch a chosen phase; this is what makes the look verifiable. Applies to the sprite
		// harness (?harness=respawn) and to a live co-op respawn alike.
		public static float? RespawnPhase { get; private set; }

		// Park/un-park it from the console (eaRespawn.park(phase) / DebugInput.RespawnPark).
		internal static void SetRespawnPhaseOverride(float? phase)
		{
			RespawnPhase = phase;
		}

		// ?brainoverlayphase=<0..1>: park EVERY BrainBoss overlay patch at that point in its
		// ping-pong cycle and hold it there (card 9f90978c). The eye rests on frame 0 -- the
		// untouched crop, i.e. closed -- and only opens on a ~15 s random roll, so without this
		// there is no way to screenshot it open on demand; the exhaust pods likewise only run
		// while the boss is venting. Pair with ?harness=brainboss or a live ?brainboss boot.
		// Negative => not parked (the eaRipple/?ripplephase convention).
		public static float? BrainOverlayPhase { get; private set; }

		// ?brainhitflash: force the BrainBoss to draw as if it had just been hit, so the
		// hit-flash brighten can be captured without landing a real shot inside the 35 ms
		// hittimer window. Draw-side only -- no hitpoints change, nothing is damaged.
		public static bool BrainHitFlash { get; private set; }

		// ?skullvolley: make every EvilSkull ("the evil grinning face of death") report each shot
		// of its volley on a `[skull]` line -- shot index, the cap it is counting up to, and the
		// difficulty modifier that cap is derived from (card d8344c17). The volley length is not
		// visible in any frame and no metric moves when it goes wrong, so this is the only
		// observable that can defend the fix; `tools/headless/probes/evilskull_volley.txt` is
		// built on it. Diagnostic only -- reading it changes nothing.
		public static bool SkullVolley { get; private set; }

		// Master multiplier on the trauma-based screen shake (Compat/Juice.cs). 1 = the
		// shipped feel, 0 = off, >1 exaggerates while tuning (?shake=, clamped 0..3).
		// A pure camera/render look, so — like MetalScore/SlowmoTrail — kept OUT of `Active`.
		public static float ShakeAmount { get; private set; } = 1f;

		// Restore the pre-card-68f62e92 behaviour: let a hit-stop freeze game time even
		// inside an online co-op session. THE DELIBERATE BUG REPRODUCTION (the
		// `?teampartner=pad` idiom) — a freeze halts one peer's whole world while the wire
		// keeps streaming, and the other peer's enemies then slide backward when the
		// corrections land (Compat/Juice.cs AddHitStop has the mechanism). It is the negative
		// control NetResetSpawnTest's hit-stop leg needs.
		// IN `Active` even though `?hitstop` is OUT, and the asymmetry is the point: since
		// that card `?hitstop` can no longer degrade a session at all (AddHitStop refuses
		// while NetSession.Active regardless), so this is the one flag whose whole purpose is
		// reintroducing a net-desync bug — it must never reach a public lobby or a listed
		// game. Every legitimate use is a dev `?net=` boot, which is anything-goes.
		public static bool NetHitstop { get; private set; }

		// The world-snapshot staleness guard (card f5cf7a5c), ON by default -- `?netstaleguard=0`
		// turns it OFF, restoring the pre-card behaviour where a reordered or late snapshot entry
		// applies an older position than the one already on screen and drags the puppet backwards.
		//
		// DEFAULTS TRUE, which makes it the odd one out in this file: every other boolean here
		// turns something ON, so `Active` asks "was it set". This one turns a FIX OFF, so `Active`
		// asks the inverse (`!NetSnapshotStaleGuard`) -- and it is in `Active` for the
		// `?nethitstop=1` reason, being a deliberate bug reproduction that must never reach a
		// public lobby. A negative control you cannot run two months later is not a negative
		// control, which is why it ships rather than living inside the test suite.
		public static bool NetSnapshotStaleGuard { get; private set; } = true;

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
		//
		// Card b174b00f gave it a SECOND owner: OwnLevel. There it drops the level's continuous
		// SkullSpawner and StarMineSpawner and keeps the Walls(2) section, so a churn figure can be
		// taken from the walls ALONE -- comparing OwnLevel's full level against a Level-3
		// ?wallsonly run is walls-plus-enemies against walls-alone, and that confound is what made
		// the question unanswerable for two cards. Deliberately the same flag name on both levels,
		// so the walls-only rigs are reached the same way rather than by a reader remembering.
		// NOT the same SHAPE on both, though, and the difference bounds any ratio taken across
		// them: Level3.PopulateWallsOnly loops six sections (variations 1/0/3, twice) and is still
		// running after 180 sim-seconds, while OwnLevel has one Walls(2) and reaches victory at
		// ~60. Both are rates over the ticks they ran, so they compare; the SECTIONS differ, which
		// is exactly the grid-vs-grid question, but do not read the two as equal-length runs.
		public static bool WallsOnly { get; private set; }

		// ?nowalls (card b174b00f) -- the control for the above, currently OwnLevel only: keep the
		// spawners, drop the Walls section. Without it a quiet ?wallsonly reading cannot be told
		// from a rig that event suppression simply broke, because both are just a low number.
		// MEASURED, so do not use a prediction as the sanity criterion: ?nowalls reads ~61 deg/s
		// against walls-only's 229 and the full level's 404. The decision rule is comparative --
		// if BOTH halves come back quiet the rig is what changed and neither number means
		// anything; one quiet and one loud attributes the churn to the loud half.
		public static bool NoWalls { get; private set; }

		// A/B the mip chain (?nomips): WebContentManager.TryLoadDds uploads level 0 only, so every
		// .dds falls back to plain bilinear -- the before/after for card 110153c7, where a tower
		// shaft spends ~10.8 cells of 756-v1 down its length and its far end aliases without mips.
		// Affects load only, so it must be set at boot; toggling it later changes nothing already
		// decoded. Out of Active (a pure render toggle, and it must not make a co-op peer reject us).
		public static bool NoMips { get; private set; }

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

		// Fast-boot Level2 straight to a dense, endless FLYING-spider swarm (skips the whole level).
		// Built for the frame profiler (card 22e655b5): the BACKGROUND flying spider is the only
		// user of the group-flatten render-target round trip (SpriteBatchWrapper.BeginGroupFlatten),
		// so measuring what that costs needs a steady swarm on screen -- which the real level only
		// reaches minutes in. `?flyspiders=fg` runs the FOREGROUND variant instead (same sprites,
		// no flatten): the A/B that separates the render-target cost from the drawing itself.
		// Pair with ?level=Level2 (+ ?invuln). See Level2.PopulateFlyingSpidersOnly.
		public static bool FlySpiders { get; private set; }

		// Set by `?flyspiders=fg` only -- picks the non-flattened foreground variant for the A/B.
		public static bool FlySpidersForeground { get; private set; }

		// How the BACKGROUND (fog) flying spiders get flattened. The three-way A/B of card
		// 9c92962e -- background-vs-foreground was never a flatten A/B at all, because the two
		// variants differ in six things (flatten, Collides, Speed, scale, alpha, DrawOrder), so
		// most of the measured gap was population, not the render-target round trip.
		//   ?flyspiderflatten=swarm  (default, SHIPPED since this card) ONE RT round trip for the
		//                            whole swarm (FlyingSpiderSwarm). Measured ~1 GL call total
		//                            regardless of population vs +1.97 calls PER SPIDER for the
		//                            per-spider bracket, at the identical pinned bench.
		//   ?flyspiderflatten=per    the pre-card path: one RT round trip PER SPIDER. Kept as the
		//                            A/B baseline (also what a non-Level2 scene with no swarm
		//                            driver still uses -- see FlyingSpider.Draw's fallback).
		//   ?flyspiderflatten=0|off  no flatten at all: body+wings drawn straight at fog alpha,
		//                            so the overlaps double-brighten (what the flatten exists to
		//                            stop). Also the "drop it for the fog layer" option's look.
		// Holding population/scale/alpha fixed with ?flyspidercount=, this is the ONLY variable.
		public enum FlySpiderFlattenMode { PerSpider, None, Swarm }

		public static FlySpiderFlattenMode FlySpiderFlatten { get; private set; } =
			FlySpiderFlattenMode.Swarm;

		// ?flyspidercount=<N>: turn the ?flyspiders fast-boot from an endless STREAM into a pinned
		// bench -- spawn exactly N flying spiders once, on a deterministic grid, frozen in X
		// (Speed 0) so none ever crosses off-screen and dies. The swivel/flap timers still tick,
		// so wings flap and bodies bob and the per-frame DRAW work stays representative; only the
		// population stops drifting. That pin is the whole point: the first numbers on this card
		// compared two runs whose spider counts were never equal. N=0 is a legal baseline (an
		// empty Level 2 to subtract). null => the original endless 5.5/s stream.
		// Holding N also costs the FOREGROUND variant its collidability -- bench spiders are
		// forced Collides=false (FlyingSpider.ApplyBenchPlacement), or the player would shoot the
		// population down mid-run and an un-invulned ship could be killed by the grid it is
		// measuring. So a foreground bench is a DRAW-cost rig (GL calls / frame ms) that sits out
		// the collision pass; the background variant it is compared against never collided anyway.
		public static int? FlySpiderCount { get; private set; }

		// Ceiling for ?flyspidercount=. Far above anything the cost curve needs (it was measured
		// at N=0/40/80 and is linear), and low enough that a fat-fingered extra zero is reported
		// as a bad value instead of spending the boot building components on the WASM heap.
		private const int MaxFlySpiderBench = 4096;

		// ?flyspiderbox=<half>: override the group-flatten bounding box half-extent in FlyingSpider
		// .Draw (baked 200 design px, scaled by the spider's `scale`). This is the DISCRIMINATOR
		// between the two candidate costs: a per-CALL / per-FBO-bind cost is flat in the box size,
		// while a FILL cost scales with its area (the RT Clear is whole-RT and the composite quad
		// is the whole box). The baked 200 is generous -- the drawn union needs about 105 (body
		// half-extent ~80x89 design px; the 92x26 wing swings +-90 deg about an origin 82 along it,
		// anchored ~21 off the body centre) -- so if it IS fill, ~3.6x of it is free.
		// NOTE the RT is grow-only and its Clear is whole-RT, so compare box sizes on FRESH page
		// loads: within one session the largest box ever flattened sets every later clear's cost.
		public static float? FlySpiderBox { get; private set; }

		// Fast-boot the Tutorial straight to its FINAL power-up training beat (skips the whole
		// welcome/move/fire/lesson sequence): the eye "punching bag" boss + the PowerUpTrainingEvent
		// where every powerup streams in and a banner explains its powered-up effect. Built to
		// reproduce the R-banner timing bug (the last powerup, powered up almost instantly while the
		// player is mid-combo on the boss, used to rip its banner away before it finished appearing)
		// in seconds instead of playing the whole tutorial. Pair with ?level=Tutorial (+ ?invuln).
		// See TutorialLevel.PopulatePowerUpTrainingOnly.
		public static bool TutorialTraining { get; private set; }

		// Pin the splash channel-flip's reveal variant (?splashvariant=revenged|pure|glasses).
		// SplashScene rolls it ~90/10 (then 50/50 on the two portrait shots) and, since card
		// 57555583, decodes ONLY the winner -- so the two portrait reveals are a 5% branch each
		// and unreachable on demand for a screenshot. null => roll as normal, so a shipped build
		// is unchanged. Out of `Active`: it picks between three splash images and cannot change
		// a shared run.
		public static string SplashVariant { get; private set; }

		// Pin WHICH attract demo the idle menu drops into (?demo=1|2|3 -> Demo1/Demo2/Demo3).
		// MenuScene.mainMenu_DemoSelected rolls RandomHelper.Random.Next(3) off an unseeded
		// Random on every attract launch, so a demo cannot be reached on demand: capturing one
		// demo's preload gaps, or probing them, was a (2/3)^attempts coin flip (card e63601a4).
		// null => roll as normal, so a shipped build is unchanged.
		// NOT the off-switch of ?nodemo/?noattract -- that unwires the idle timeout so no demo
		// ever launches; this only pins which one the roll picks.
		// Out of `Active`: like ?noattract it alters no gameplay, difficulty, unlock or fairness,
		// and ComputeEligible refuses Demo1/2/3 outright so an attract demo can never be
		// advertised to a peer in the first place.
		public static int? DemoPick { get; private set; }

		// ?seed=<n>: make the GAMEPLAY RNG reproducible for this boot (card d937c721).
		// RandomHelper.Random is `new Random()`, so two eahl runs of one level never reach the
		// same world -- a screenshot A/B on any rig with spawners measures that divergence as
		// much as the change under test (measured on ?level=OwnLevel&noattract: mean |diff| 0.2,
		// MAX 210 of 255; ?level=Level3&wallsonly is BISTABLE, 1 run in 6 differing). Both noise
		// floors were larger than the effect being measured on card b7e9b106, which is what this
		// flag exists to end. null => unseeded, so a shipped build is unchanged.
		//
		// Applied at PARSE time (RandomHelper.Reseed), unlike ?mute's apply-in-Game1: RandomHelper
		// is a pure static needing no graphics device, and every host parses before the first
		// tick, so this is the earliest point that catches boot-time draws too.
		//
		// It reaches RandomHelper's stream ONLY -- see Reseed's comment for why Quad's,
		// ShipConnector's, Juice's and SplashScene's own Randoms stay unseeded.
		//
		// OUT of `Active`, deliberately, and the two halves of that both matter:
		//  - It does not HIJACK anything. A seeded boot plays a normal, winnable level with no
		//    unlock, no invulnerability and no foreknowledge -- one valid world instead of
		//    another. The precedent is ?difficulty=, which changes every enemy in the run and is
		//    likewise out.
		//  - `Active` REFUSES ONLINE PLAY (NetSession.HandleHello rejects menu pairing,
		//    NetListing.ComputeEligible refuses to list). The reproducible-world case this flag
		//    exists for is loudest in the netplay desync work, where a two-peer capture pair is
		//    incomparable without it -- putting it in `Active` would forbid exactly that. And it
		//    could not desync a session anyway: co-op here is distributed-authority replication,
		//    NOT lockstep (Compat/Net/CLAUDE.md), so the two peers ALREADY run two different
		//    unseeded streams today.
		// The mitigation for staying out is the unconditional "[debug] ?seed=" line in Parse: no
		// run can be seeded without saying so, whether or not the `Active` dump prints.
		public static int? Seed { get; private set; }

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

		// Post-level text crawl (card bee8f0e0), all three read by CreditsScene.
		//
		// ?creditsshot=<1|2|3> boots straight into the crawl for the level just "beaten"
		// (bare => 1). It hijacks boot => SkipSplash + AutoStart, so it is IN `Active`.
		public static int? CreditsShot { get; private set; }

		// ?crawlpos=<designY> parks the crawl at a scroll position instead of scrolling it --
		// the scrub rig for a taper that is a function of each line's Y. IN `Active`: it
		// stalls the post-level crawl indefinitely, which a co-op peer would share.
		public static float? CrawlPos { get; private set; }

		// ?crawlskew=<f> overrides the crawl's perspective taper; null => the baked
		// CreditsScene.DefaultCrawlSkew, and ?crawlskew=0 restores the flat pre-card crawl.
		// The value is CLAMPED to what keeps the widest line on screen (the shipped crawls
		// saturate around 0.08-0.10), so any value here is safe -- it just stops growing. The
		// `[crawl]` console line reports requested vs effective. Pure render/feel => OUT of
		// `Active`.
		public static float? CrawlSkew { get; private set; }

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

		// ?bgfreeze=<designX> STOPS every background/foreground layer scrolling and parks a tile
		// BOUNDARY of each one at design column <designX>. The Mars/alien-base layers scroll at six
		// different speeds (0.3 / 0.33 / 0.53 / 0.85 / 1.0 / 2.5), so a live screenshot of a tiling
		// artifact can never be reproduced and a before/after pair is meaningless -- the seam has
		// moved. Frozen, every layer's seam stacks in one screen column and the shots are directly
		// comparable. Built for the pad-bleed seams (Trello 4ddcd13f); reach for it for any tiling,
		// wrap-period or parallax-alignment question. null => normal scrolling, which is what a
		// bare ?bgfreeze=false gives; a bare ?bgfreeze (no value) parks at design x=400.
		public static float? BgFreeze { get; private set; }

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

		// ?netfakepeer=<s>: override THIS tab's peer-identity token (card 0b8a300b), the same
		// trick ?netfakehash= plays on the build hash and for the same reason -- both dev tabs
		// share ONE localStorage, so they mint the SAME eaRtc.peerId and a host blocking the
		// joiner would block itself. REQUIRED for any two-tab kick+block test; the loopback rig
		// cannot exercise the ban at all without it.
		// Null/empty = the genuine WebRtcInterop.PeerId(); dev-only, byte-identical when unset.
		public static string NetFakePeerId { get; private set; } = "";

		// ?netfakelisted=<code>: report this game as publicly LISTED under that room code without
		// ever opening a socket (card d1a0559b). NetListing.Tick short-circuits on it, so nothing
		// is registered with the signaling server and no stranger can actually join -- it exists
		// purely so the two places that SURFACE a listing can be screenshot offline: the pause
		// menu's "Listed online -- room XYZAB" line and ScoreVisualiser's corner beacon. Reaching
		// either for real needs a live server plus a game the eligibility predicate accepts, which
		// is not something a headless screenshot run can stand up.
		// Card 0d6ffe70 gave the short-circuit two more jobs, so the fake room behaves like a
		// real one rather than being pinned on: it sets NetListing.CouldList (which is what puts
		// the pause menu's "Online Play" row on screen) and it reports Listed = the live
		// Settings.AllowOnlineJoins, so that row's room toggle is a working control here
		// instead of a dead one. Still no socket and still nothing registered.
		// Any value is legal, so nothing is reported -- the ?netfakepeer=/?netfakehash= class of
		// silent flag. Unlike those two it is UPPER-CASED as well as trimmed, because a real room
		// code is upper case (the server mints them that way) and this has to render like one.
		// Deliberately NOT part of DebugFlags.Active: no session exists, so it cannot alter a
		// shared run.
		// Null/empty = off; byte-identical when unset.
		public static string NetFakeListed { get; private set; } = "";

		// ?netkickshot: park the host's remote-pause KICK menu over a booted level with no peer
		// at all (pair with ?level=<Name>), so its appearance can be screenshot in isolation --
		// the ?gamebrowser fake-entry precedent, named for the ?textshot/?lazershot idiom.
		// Reaching it for real needs two windows AND a peer that holds a pause past the 4s offer
		// delay, which is not a screenshot rig. It is an APPEARANCE harness only: both Kick
		// entries no-op (KickPeer needs a session), so only "Keep Waiting" does anything -- it
		// releases the synthetic freeze and hands the level back. In Active.
		public static bool NetKickShot { get; private set; }

		// ?aiplayer: force the LOCAL player's ship onto the existing PlayerShip AI branch
		// (ControlDevice.AI / DoAIMove/DoAIFire -- the attract-demo behaviour) at level start,
		// so two net tabs can drive themselves unattended (the user-specified 11.1 testing
		// strategy: under distributed authority the wire carries ship STATE, so an AI-driven
		// ship replicates byte-identically to a human one). The controller itself stays
		// Keyboard/pad -- only the Update branch is forced -- so joins, pause and the net
		// layer's "which ship is local" logic are untouched. Remote puppets are never forced.
		public static bool AIPlayer { get; private set; }

		// ?teampartner=ai|pad (card e6927ef8): override how TeamChallenge seats its SECOND slot.
		// Normally (None) TeamChallenge.ResolvePartnerSeat picks the first CONNECTED pad, or
		// ControlDevice.AI when nothing is plugged in -- so the level plays either way.
		//   ai  -- force the auto-pilot AI partner even with a pad connected.
		//   pad -- force ControlDevice.PadOne even with NOTHING connected, i.e. reproduce the
		//          shipped-2008 seating: the only deliberate way to reach GameScene.Update's
		//          disconnected-pad force-pause (the bug this card fixed), and so the negative
		//          control for the fix.
		// In Active -- it changes which devices drive a shared run.
		// This REPLACES ?aiteam (card 9391f95a), which seated ControlDevice.Generic purely so the
		// level could be BENCHED at all; that is obsolete now the no-pad seat drives itself, so
		// ?level=TeamChallenge&aiplayer benches with no special flag.
		public static TeamPartnerSeat TeamPartner { get; private set; } = TeamPartnerSeat.None;

		public enum TeamPartnerSeat
		{
			None,
			Ai,
			Pad
		}

		// ?aibench (card f4d1721f): AI telemetry -- wall contacts (counted even under ?invuln),
		// the heading-reversal jitter rate, fire-decision idleness and the level-script progress
		// + run verdict. Pair with ?aiplayer. Console: eaAiBench(). See Compat/AiBench.cs.
		public static bool AiBench { get; private set; }

		// ?aiff=<2-64> (card f4d1721f): run the game's Update N times per rendered frame with the
		// SAME dt, so an AI soak covers a whole level in a fraction of the wall-clock time without
		// changing the sim it is measuring. Deliberately NOT Settings.Turbo, which scales dt --
		// that changes per-tick physics (and so the very steering behaviour under test).
		// Suppressed inside a net session (both peers must run at one pace) and while a level
		// launch is warming. 0/1 = off.
		public static int AiFastForward { get; private set; }

		// AI steering/targeting knobs (card f4d1721f -- Game/EvilAliens/PlayerShip.cs). Null =>
		// the baked value, so a shipped build is byte-identical. A/B them against the ?aibench
		// counters, then bake a settled value in.
		// NOTE (card c10e3e7f): "the baked value" is no longer always a single const. TWO knobs
		// -- ?aifieldpx and ?aiaim -- resolve through PlayerShip.AiSkillByDifficulty, so their
		// default depends on the tier the fight is being run at. An override here is still
		// ABSOLUTE and wins over the tier row, which is what makes a per-tier A/B possible (and
		// is how that table's values were chosen): pair it with ?difficulty=<tier>.
		//   ?aismooth=<ms>   steering low-pass time constant (the anti-jitter lever)
		//   ?aireact=<ms>    wall look-ahead, in milliseconds of closing travel
		//   ?aigapmargin=<t> tiles a rival gap must beat the committed one by
		//   ?aiscanrows=<n> rows of grid looked at when judging a column (an INT, not a float)
		//   ?aicrosspenalty=<c> cost per blocked column the ship would have to cross
		//   ?aithreatlead=<ms> how far ahead a moving threat is projected
		//   ?aibossbias=<f>  distance discount applied to level-halting bosses when targeting
		//   ?aiaim=<rad>     random error added to every shot's aim angle            [per-tier]
		//                    (JunkBoss excepted -- it always gets exact aim)
		public static float? AiSteerSmoothMs { get; private set; }

		public static float? AiWallReactionMs { get; private set; }

		public static float? AiGapSwitchMargin { get; private set; }

		// int?, not float? -- WallScanRows counts grid ROWS and indexes the scan loop.
		public static int? AiWallScanRows { get; private set; }

		public static float? AiWallCrossPenalty { get; private set; }

		public static float? AiThreatLeadMs { get; private set; }

		public static float? AiPriorityBias { get; private set; }

		public static float? AiAimSpreadRad { get; private set; }

		// The AI's personal-space field around a threat (PlayerShip.ThreatFieldRange /
		// ThreatFieldStrength):
		//   ?aifieldpx=<px>    clearance wanted beyond ANY threat's hull            [per-tier]
		//   ?aifieldsize=<f>   extra clearance per pixel of the threat's own half-extent
		//   ?aifieldfall=<p>   exponent of the (1-t)^p falloff; higher = bites later and harder
		// A big field with a FAST falloff is the point: the bot keeps well clear of something
		// the size of the spider boss, while the outer half of the field stays cheap enough that
		// it can still dive in to shoot and to weave through bullets.
		// ?aismoothurgent=<ms> the smoothing floor used when the push is strong -- half of the
		//                      "damp when calm, fly when not" balance in PlayerShip.DoAIMove.
		public static float? AiSteerSmoothUrgentMs { get; private set; }

		// The two cancellation floors (card ada9e839, both baked 0.2):
		//   ?airepeldelta=<d>   the REPULSION resultant at or below which the repellents have
		//                       argued each other to a standstill and none of them is applied.
		//   ?ainoisefloor=<d>   the WHOLE steer at or below which the ship holds still, applied
		//                       last. Both exist because Move() discards magnitude and thrusts at
		//                       full acceleration along the ANGLE, so a cancelled-to-noise vector
		//                       is a sprint in an arbitrary direction rather than a gentle nudge.
		// `?aipark=` was the pre-card spelling of the second one, and it is GONE rather than
		// renamed: it was baked at 0.95, i.e. ABOVE the 0.8 seek, so it did not floor noise -- it
		// deleted every deliberate destination the bot had. A query still passing it would be
		// asking for the bug back under a name that no longer means the same thing.
		public static float? AiRepelCancelDelta { get; private set; }

		public static float? AiSteerNoiseFloor { get; private set; }

		// ?aiseekdeadzone=<px>  the radius inside which the seek attractor stops pulling -- the
		//                       anti-pingpong mechanism for every deliberate destination, and the
		//                       one knob that has to stay above the ship's 11.3px stopping
		//                       distance (PlayerShip.DefaultSeekArriveDeadzonePx).
		public static float? AiSeekDeadzonePx { get; private set; }

		// ?aiseekpowerup=<w>    the pull toward a POWERUP the bot has chosen to fetch, and
		// ?aiseekapproach=<w>   the pull toward a destination it COMMITS to -- a halting boss's
		//                       standoff point, a partner to dock with, a blastable cluster.
		// ?aipowerupreach=<px>  how far out a powerup exerts its own direct pull, on top of
		//                       being eligible as that chosen target.
		// All card ada9e839 -- see PlayerShip.DefaultSeekPowerupWeight for the history.
		public static float? AiSeekPowerupWeight { get; private set; }

		public static float? AiSeekApproachWeight { get; private set; }

		public static float? AiPowerupReachPx { get; private set; }

		public static float? AiThreatFieldPx { get; private set; }

		public static float? AiThreatFieldSize { get; private set; }

		public static float? AiThreatFieldFalloff { get; private set; }

		// ?aiasteroidscale=<f>  per-type repellent multiplier for ASTEROIDS only (card ada9e839).
		//                       The belt is the one place a dense field of lethal obstacles has to
		//                       out-argue an ordinary powerup detour, and a GLOBAL falloff change
		//                       was already measured and declined elsewhere.
		public static float? AiAsteroidThreatScale { get; private set; }

		// ?aiasteroidrange=<f>  multiplier on the asteroid field's RANGE, and
		// ?aiasteroidfall=<p>   the asteroid field's own falloff exponent (lower = earlier and
		//                       gentler). The shape axes to ?aiasteroidscale='s magnitude axis --
		//                       a taller mountain of the same width shoves the ship out of the
		//                       belt, a wider shallower one leans on it the whole way across.
		public static float? AiAsteroidRangeScale { get; private set; }

		public static float? AiAsteroidFalloff { get; private set; }

		// ?aifieldcurve=classic      restore the 2008 threat-field SHAPE, max*(1-t^2), globally;
		// ?aiasteroidcurve=classic   the same for ASTEROIDS only (wins over the global switch);
		// ?aiasteroidflatpx=<px>     replace the asteroid field's size-scaled range with a flat
		//                            absolute one -- 150 reproduces the 2008 field exactly.
		// The port swapped a PLATEAU for a SPIKE (75% vs 12% strength at half range) and
		// ?aifieldfall= only ever swept the exponent inside the port's family, so the original
		// shape had never been measured. Card e88e21ca.
		public static bool? AiClassicFieldCurve { get; private set; }

		public static bool? AiAsteroidClassicCurve { get; private set; }

		public static float? AiAsteroidFlatRangePx { get; private set; }

		// ?aievade=0  turn OFF EvadeMovingThreat, the closest-approach path, so every threat is
		//             handled by the radial field alone. Card ada9e839's measurement seam -- that
		//             special case was measured under the 0.95 park and never inside the field
		//             composition that replaced it.
		public static bool? AiEvadeMovers { get; private set; }

		// ---- directional repellent shapes (card e425781b) ----
		// ?aicone=0        turn the whole velocity-cone / lane-wedge shape off, leaving the radial
		//                  field alone -- the A/B control for everything this card measures.
		// ?aiwedge=0       keep the cone but drop the asymmetric lane wedge, which is the only way
		//                  to attribute a spider-boss result to one half of the shape.
		// ?ailaneescape=0  turn off the hand-rolled spider lane/sweep escapes, so the wedge can be
		//                  measured against them rather than on top of them.
		public static bool? AiConeShapes { get; private set; }

		public static bool? AiLaneWedge { get; private set; }

		public static bool? AiLaneEscape { get; private set; }

		// ?aiconelead=<ms>     cone length per unit speed, as a time horizon;
		// ?aiconemaxlen=<px>   the ceiling on that length;
		// ?aiconewidth=<px>    how far outside the swept corridor it still pushes;
		// ?aiconetaper=<f>     1 = a true triangle, 0 = a parallel capsule;
		// ?aiconefallalong=<p> the ALONG-axis plateau exponent (1 - t^p);
		// ?aiconefallacross=<p> the ACROSS-axis spike exponent ((1-t)^p);
		// ?aiconescale=<f>     peak magnitude as a multiple of maxSteerStrength.
		public static float? AiConeLeadMs { get; private set; }

		public static float? AiConeMaxLenPx { get; private set; }

		public static float? AiConeWidthPx { get; private set; }

		// ?aiconespread=<f>    scale the across-axis reach with the mover's own half-extent instead
		//                      of using the flat width (0 = off, the shipped default), and
		// ?aiconewidthmin=<px> the floor that scaling clamps to.
		public static float? AiConeSpread { get; private set; }

		public static float? AiConeWidthMinPx { get; private set; }

		public static float? AiConeTaper { get; private set; }

		public static float? AiConeFallAlong { get; private set; }

		public static float? AiConeFallAcross { get; private set; }

		public static float? AiConeScale { get; private set; }

		// ?aiwedgestrength=<f> the wedge's peak magnitude, and
		// ?aiwedgefall=<p>     its own along-axis plateau exponent -- separate from the cone's
		//                      because the wedge spans the play field rather than a cone length.
		public static float? AiLaneWedgeStrength { get; private set; }

		public static float? AiLaneWedgeFallAlong { get; private set; }

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

		// ?netlocal=<1-3> (card 4d904410): queue that many synthetic COUCH joins on this peer,
		// fired a few seconds after the session goes live (NetSession.TickLocalJoinSim). Local
		// co-op joins are gamepad Start presses, which the automated rig cannot produce -- there
		// are no physical pads and eaPress only reaches the keyboard path -- so this is the seam
		// that makes "someone picks up a controller mid-session" testable at all. Pair with
		// ?net=host/join (+ ?aiplayer so the extra ships fly themselves: they are not puppets, so
		// EffectiveController puts them on the AI branch). Shipped builds are unchanged (0 = off).
		public static int NetLocal { get; private set; }

		// ?netdropgrant (card af0eb00a): CLIENT-side -- deliberately drop the FIRST EvSlotGrant
		// the host answers a couch join with, instead of seating it; every later grant in the
		// same session completes normally. That is the one state the host's
		// ExpireUnclaimedGrants path exists for (the client can silently fail to take a grant: its
		// device got seated meanwhile, its scene changed) and the ONLY thing that reaches it --
		// ?netlocal always takes its grant, so without this flag the expiry has no trigger at all
		// and the seat-leak it guards against is untestable. ONE-SHOT since card ee96ea61 (it
		// dropped every grant, so a run could only ever show the DROP half): ?netlocal=2 now
		// covers the drop AND a subsequent successful take in one run. The latch is per SESSION
		// and cleared by NetSession.ResetPerSessionState -- see NetSession.ShouldDropGrant.
		// Shipped builds are unchanged.
		public static bool NetDropGrant { get; private set; }

		// Artificial network impairment (card 40334a8f, plans/net-impairment.md), applied to
		// INBOUND traffic by Compat/Net/NetImpairment so the drop-tolerance paths cards
		// 11.1-11.3 built actually get exercised. ?netlag=<ms> (0-500) delays both lanes;
		// ?netloss=<0-100> drops STREAM-lane packets only (the reliable lane is never dropped
		// or reordered -- that contract is what everything above INetTransport assumes).
		// 0/0 = the wrapper's inline pass-through, so an unimpaired net session behaves exactly
		// as it did before. All three are live-settable from the eaNetSim panel (opt-in: ?netsim
		// on top of the ?net= boot, or eaNetSim.show() from the console) -- these two flags do
		// not need it.
		public static float NetLagMs { get; private set; }

		public static float NetLossPct { get; private set; }

		// ?netjitter=<ms> (0-MaxJitterMs): +/- this many ms on each stream packet's release,
		// which is the only way the stream lane ever actually REORDERS and so the only way
		// ordViol/seqGap tolerance gets tested. The reliable lane's releases are clamped
		// monotone, so jitter can never reorder it.
		// It was PANEL-ONLY until the flag was added, on the reasoning that reaching jitter
		// should mean having the ?netsim panel up. That cost more than it bought: the three
		// knobs are one impairment profile, and two of them being URL-settable while the third
		// was not made a lag/loss/jitter rig unreproducible from a link -- which is the only
		// form a two-window recipe travels in. Panel and console still drive all three.
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

		// ?gamebrowser=fallback: the same boot, but NetGameBrowser.InjectFakeGames also lists two
		// levels with no bundled art -- one in our Levels enum with no carousel slot, one not in
		// the enum at all. That is the only offline way to reach SubMenuOnlineGames.EnsureArt's
		// no-art branch, which is otherwise reachable only from a real stranger's build off the
		// wire (card 0d166364). Split from the bare flag (card 0d166364 follow-up) because the
		// two rigs want opposite things: an APPEARANCE screenshot wants every row to look like a
		// real game, and two rows drawing Mission 1's art under the generic "Mission" title are
		// noise you have to mentally discount. Bare ?gamebrowser is therefore the clean rig again.
		public static bool GameBrowserFallback { get; private set; }

		// ?gamebrowser=thumbs (card e7404647): the same boot, but two of the four fake entries
		// also carry a synthetic ROOM THUMBNAIL, so a single screenshot shows both halves of the
		// carousel's new rule -- prefer the live picture of the host's game, fall back to stock
		// level art when there is none. It is the only offline way to see the thumbnail path at
		// all: a real one needs a listed stranger, a signaling server and its pull schedule.
		// A third value rather than a second flag because it is the same rig with one row of
		// data changed, exactly as `fallback` is.
		public static bool GameBrowserThumbs { get; private set; }

		// ?netjip: the two-window join-in-progress test. Pair with ?level=<Name> (+ ?invuln):
		// the host boots straight into a level, solo, and LISTS it despite the debug boot
		// (NetListing's eligibility normally refuses a DebugFlags.Active / cheating host, so
		// a plain ?level= host could never list). The host prints its room code; a second
		// window joins mid-level via the menu's Join Online Game (or ?net=join&rtc&code=),
		// and both sides' [net] metrics tell the JIP story. In Active.
		public static bool NetJip { get; private set; }

		// ?netallowdebug: let a peer carrying gameplay debug flags take part in a MENU-LOBBY
		// session -- this tab's hello presents as clean (LocalHelloFlags) and its own local
		// DebugActive refusal is skipped (NetSession.HandleHello). The ?netjip bypass in
		// exactly the same shape, for the other pairing route.
		// The case it exists for: `?aiplayer` is in Active, so the ONLY way to bot-drive a ship
		// was the `?net=` direct rig -- which never sends EvLaunch from the ordinary menu
		// (MenuScene only replicates a launch in netMode), so a menu-driven co-op flow with an
		// AI pilot was unreachable. That is precisely the rig for testing the JOINING paths.
		// **It does NOT relax NetListing.ComputeEligible**, deliberately -- a flagged game still
		// refuses to LIST, so this can never advertise a bot-driven game to a stranger off the
		// public browser. Widen that with ?netjip if a test needs it, as a separate decision.
		// Out of Active: a flag whose whole job is to be tolerated in a session would otherwise
		// refuse the session it is enabling.
		public static bool NetAllowDebug { get; private set; }

		// True if any flag that HIJACKS boot/levels is set -- deliberately NOT "any debug flag":
		// pure render/feel/diagnostic toggles stay out (?hitboxes, ?metalscore, ?noattract, ...).
		// This is not just a log line. NetSession (LocalHelloFlags/HandleHello) refuses a menu
		// session when either peer has it, and NetListing.ComputeEligible refuses to list a
		// flagged host -- so putting a flag in this expression DISABLES ONLINE PLAY for that
		// boot. The test for a new flag is "could this change the shared run?", not "is this a
		// debug flag?".
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
				case "mute":
					Mute = IsOn(val);
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
				case "binlog":
					BinLog = IsOn(val);
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
					// The on/off fallback is DELIBERATE, but only for the on/off SPELLINGS: a
					// typo'd number (?shake=1.5O) is not "off", and silently reading it as off
					// turns the effect under test off entirely -- worse than ignoring it, since
					// the run then measures no-shake while labelled as a shake sweep.
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var shk))
					{
						ShakeAmount = (shk < 0f) ? 0f : (shk > 3f) ? 3f : shk;
					}
					else if (IsOn(val) || IsExplicitlyOff(val))
					{
						ShakeAmount = IsOn(val) ? 1f : 0f;
					}
					else
					{
						RejectFlagValue(key, val, "a number 0..3, or on/off", InForce(ShakeAmount));
					}
					break;
				case "hitstop":
					Hitstop = IsOn(val);
					break;
				case "nethitstop":
					NetHitstop = IsOn(val);
					break;
				case "netstaleguard":
					// NOT the bare `IsOn(val)` every other boolean here uses, and the asymmetry is
					// the point: for those a typo silently leaves a DIAGNOSTIC off, which costs
					// nothing, whereas an unrecognised value here would silently turn a shipped FIX
					// off and restore the backward drag. So only an explicit off spelling disables
					// it, and anything else is reported and ignored -- the value-carrying flags'
					// rule, applied to the one boolean that needs it.
					if (IsExplicitlyOff(val))
					{
						NetSnapshotStaleGuard = false;
					}
					else if (!IsOn(val))
					{
						// Names the setting actually IN FORCE, not the shipped default -- a
						// repeated flag (?netstaleguard=0&netstaleguard=nope) keeps the earlier
						// valid value, and a diagnostic that can state the wrong condition is
						// worse than one that states none.
						Console.WriteLine("[debug] unknown ?" + key + "= value '" + val
							+ "' (expected 0/off to disable) -- ignored, the snapshot staleness"
							+ " guard stays " + (NetSnapshotStaleGuard ? "ON" : "OFF"));
					}
					break;
				case "slowmotrail":
					SlowmoTrail = IsOn(val);
					break;
				case "slowmotraildecay":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var smd))
					{
						SlowmoTrailDecay = (smd < 0f) ? 0f : (smd > 0.99f) ? 0.99f : smd;
					}
					else
					{
						RejectFlagValue(key, val, "a number",
							InForce(SlowmoTrailDecay));
					}
					break;
				case "slowmotrailstrength":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var sms))
					{
						SlowmoTrailStrength = (sms < 0f) ? 0f : (sms > 1f) ? 1f : sms;
					}
					else
					{
						RejectFlagValue(key, val, "a number",
							InForce(SlowmoTrailStrength));
					}
					break;
				case "holofilter":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var hf))
					{
						HoloFilter = (hf < 0f) ? 0f : (hf > 2f) ? 2f : hf;
					}
					else
					{
						RejectFlagValue(key, val, "a number",
							InForce(HoloFilter));
					}
					break;
				case "holoburst":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var hb))
					{
						HoloBurst = (hb < 0f) ? 0f : (hb > 2f) ? 2f : hb;
					}
					else
					{
						RejectFlagValue(key, val, "a number",
							InForce(HoloBurst));
					}
					break;
				case "hologreen":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var hg))
					{
						HoloGreen = (hg < 0f) ? 0f : (hg > 1f) ? 1f : hg;
					}
					else
					{
						RejectFlagValue(key, val, "a number",
							InForce(HoloGreen));
					}
					break;
				case "hologreenpulse":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var hgp))
					{
						HoloGreenPulse = (hgp < 0f) ? 0f : (hgp > 1f) ? 1f : hgp;
					}
					else
					{
						RejectFlagValue(key, val, "a number",
							InForce(HoloGreenPulse));
					}
					break;
				case "holostaticrate":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var hsr))
					{
						HoloStaticRate = (hsr < 0f) ? 0f : (hsr > 1f) ? 1f : hsr;
					}
					else
					{
						RejectFlagValue(key, val, "a number",
							InForce(HoloStaticRate));
					}
					break;
				case "ripple":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var rip))
					{
						Ripple = (rip < 0f) ? 0f : (rip > 4f) ? 4f : rip;
					}
					else
					{
						RejectFlagValue(key, val, "a number",
							InForce(Ripple));
					}
					break;
				case "rippleamp":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var ripa))
					{
						RippleAmp = (ripa < 0f) ? 0f : (ripa > 0.5f) ? 0.5f : ripa;
					}
					else
					{
						RejectFlagValue(key, val, "a number",
							InForce(RippleAmp));
					}
					break;
				case "rippleradius":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var ripr) && ripr > 0f)
					{
						RippleRadius = (ripr > 4f) ? 4f : ripr;
					}
					else
					{
						RejectFlagValue(key, val, "a number > 0",
							InForce(RippleRadius));
					}
					break;
				case "rippleduration":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var ripd) && ripd > 0f)
					{
						RippleDuration = (ripd > 10f) ? 10f : ripd;
					}
					else
					{
						RejectFlagValue(key, val, "a number > 0",
							InForce(RippleDuration));
					}
					break;
				case "ripplewidth":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var ripw) && ripw > 0f)
					{
						RippleWidth = (ripw > 2f) ? 2f : ripw;
					}
					else
					{
						RejectFlagValue(key, val, "a number > 0",
							InForce(RippleWidth));
					}
					break;
				case "ripplefalloff":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var ripf) && ripf >= 0f)
					{
						RippleFalloff = (ripf > 8f) ? 8f : ripf;
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(RippleFalloff));
					}
					break;
				case "ripplerim":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var ripm))
					{
						RippleRim = (ripm < 0f) ? 0f : (ripm > 4f) ? 4f : ripm;
					}
					else
					{
						RejectFlagValue(key, val, "a number",
							InForce(RippleRim));
					}
					break;
				case "ripplemini":
					RippleMini = IsOn(val);
					break;
				// NOTE there is deliberately no `case "rippletune"`: like ?holotune,
				// ?walltune and ?spidertune, that flag is read by the JS panel off
				// location.search and never reaches C#. The switch has no `default:`, so an
				// unlisted key is silently ignored -- adding a write-only property here
				// would just be dead code.
				case "ripplephase":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var ripp))
					{
						// A NEGATIVE phase means "not parked", matching eaRipple.park(-1) and
						// the panel's "-1 = live" slider -- clamping it to 0 instead would
						// PARK on the very value a user copies out of the panel to un-park.
						RipplePhase = (ripp < 0f) ? (float?)null : (ripp > 1f) ? 1f : ripp;
					}
					else
					{
						RejectFlagValue(key, val, "a number 0..1, or negative for live",
							InForce(RipplePhase));
					}
					break;
				case "respawnphase":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var respp))
					{
						// Negative = live, exactly as ?ripplephase= above.
						RespawnPhase = (respp < 0f) ? (float?)null : (respp > 1f) ? 1f : respp;
					}
					else
					{
						RejectFlagValue(key, val, "a number 0..1, or negative for live",
							InForce(RespawnPhase));
					}
					break;
				case "ripplepower":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var ripw2) && ripw2 >= 0f)
					{
						RipplePower = (ripw2 > 4f) ? 4f : ripw2;
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0 (a bomb powerup level 0..4)",
							InForce(RipplePower));
					}
					break;
				case "ripplecenter":
					ParseRippleCenter(key, val);
					break;
				case "brainoverlayphase":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var bop))
					{
						BrainOverlayPhase = (bop < 0f) ? (float?)null : (bop > 1f) ? 1f : bop;
					}
					else
					{
						RejectFlagValue(key, val, "a number 0..1, or negative for live",
							InForce(BrainOverlayPhase));
					}
					break;
				case "brainhitflash":
					BrainHitFlash = IsOn(val);
					break;
				case "skullvolley":
					SkullVolley = IsOn(val);
					break;
				case "blastactive":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var ba))
					{
						BlastActiveAlpha = (ba < 0f) ? 0f : (ba > 1f) ? 1f : ba;
					}
					else
					{
						RejectFlagValue(key, val, "a number",
							InForce(BlastActiveAlpha));
					}
					break;
				case "blasthit":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var bh) && bh > 0f)
					{
						BlastHitFactor = bh;
					}
					else
					{
						RejectFlagValue(key, val, "a number > 0",
							InForce(BlastHitFactor));
					}
					break;
				case "reticlesize":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var rs) && rs > 0f)
					{
						ReticleSize = rs;
					}
					else
					{
						RejectFlagValue(key, val, "a number > 0",
							InForce(ReticleSize));
					}
					break;
				case "blastloop":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var bl) && bl > 0f)
					{
						BlastLoopSeconds = bl;
					}
					else
					{
						RejectFlagValue(key, val, "a number > 0",
							InForce(BlastLoopSeconds));
					}
					break;
				case "lazerchargescale":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var lcs) && lcs > 0f)
					{
						LazerChargeScale = lcs;
					}
					else
					{
						RejectFlagValue(key, val, "a number > 0",
							InForce(LazerChargeScale));
					}
					break;
				case "lazercapscale":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var lcap) && lcap >= 0f)
					{
						LazerCapScale = lcap;
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(LazerCapScale));
					}
					break;
				case "lazerarcs":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var larc) && larc >= 0f)
					{
						LazerArcRate = larc;
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(LazerArcRate));
					}
					break;
				case "lazertendrilspeed":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var lts) && lts >= 0f)
					{
						LazerTendrilSpeed = lts;
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(LazerTendrilSpeed));
					}
					break;
				case "lazerarclife":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var lal) && lal > 0f)
					{
						LazerArcLife = lal;
					}
					else
					{
						RejectFlagValue(key, val, "a number > 0",
							InForce(LazerArcLife));
					}
					break;
				case "connectorbolts":
					if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cbolts) && cbolts >= 0)
					{
						ConnectorBoltCount = cbolts;
					}
					else
					{
						RejectFlagValue(key, val, "an integer >= 0",
							InForce(ConnectorBoltCount));
					}
					break;
				case "connectorarcs":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var carc) && carc >= 0f)
					{
						ConnectorArcRate = carc;
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(ConnectorArcRate));
					}
					break;
				case "connectorjitter":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var cjit) && cjit >= 0f)
					{
						ConnectorJitter = cjit;
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(ConnectorJitter));
					}
					break;
				case "connectorpulse":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var cpul) && cpul >= 0f)
					{
						ConnectorPulse = cpul;
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(ConnectorPulse));
					}
					break;
				case "connectorglow":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var cglo) && cglo >= 0f)
					{
						ConnectorGlow = cglo;
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(ConnectorGlow));
					}
					break;
				case "walltowers":
					WallTowers = IsOn(val);
					break;
				case "wallsonly":
					WallsOnly = IsOn(val);
					break;
				case "nowalls":
					NoWalls = IsOn(val);
					break;
				case "walltrace":
					WallTrace = IsOn(val);
					break;
				case "nomips":
					NoMips = IsOn(val);
					break;
				case "wallpoptest":
					WallPopTest = IsOn(val);
					break;
				case "wall3dbands":
					if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var w3b) && w3b >= 1 && w3b <= 64)
					{
						Wall3DBands = w3b;
					}
					else
					{
						RejectFlagValue(key, val, "an integer 1..64",
							InForce(Wall3DBands));
					}
					break;
				case "walldepth":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var wd) && wd > 0f && wd < 1f)
					{
						WallDepth = wd;
					}
					else
					{
						RejectFlagValue(key, val, "a number strictly between 0 and 1",
							InForce(WallDepth));
					}
					break;
				case "wallfog":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var wf) && wf >= 0f)
					{
						WallFog = wf;
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(WallFog));
					}
					break;
				case "wallfogcolor":
					if (TryParseHexColor(val, out var wfc))
					{
						WallFogColor = wfc;
					}
					else
					{
						// A colour, so the in-force value is quoted back as the hex a reader would
						// put in the URL rather than as a Color.ToString().
						RejectFlagValue(key, val, "a hex colour like #4080c8",
							WallFogColor.HasValue
								? "#" + WallFogColor.Value.R.ToString("X2", CultureInfo.InvariantCulture)
									+ WallFogColor.Value.G.ToString("X2", CultureInfo.InvariantCulture)
									+ WallFogColor.Value.B.ToString("X2", CultureInfo.InvariantCulture)
								: "the shipped default");
					}
					break;
				case "wallsidedark":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var wsd) && wsd >= 0f)
					{
						WallSideDark = wsd;
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(WallSideDark));
					}
					break;
				case "wallsidetile":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var wst) && wst > 0f && wst <= 32f)
					{
						WallSideTile = wst;
					}
					else
					{
						RejectFlagValue(key, val, "a number > 0 and <= 32",
							InForce(WallSideTile));
					}
					break;
				case "wallfacelight":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var wfl) && wfl >= 0f)
					{
						WallFaceLight = wfl;
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(WallFaceLight));
					}
					break;
				case "wallfaceangle":
					// Signed: any azimuth.
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var wfa))
					{
						WallFaceAngle = wfa;
					}
					else
					{
						RejectFlagValue(key, val, "a number",
							InForce(WallFaceAngle));
					}
					break;
				case "walltoplift":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var wtl) && wtl >= 0f)
					{
						WallTopLift = wtl;
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(WallTopLift));
					}
					break;
				case "wallwisps":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var ww) && ww >= 0f)
					{
						WallWisps = ww;
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(WallWisps));
					}
					break;
				case "wallwispspeed":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var wws) && wws >= 0f)
					{
						WallWispSpeed = wws;
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(WallWispSpeed));
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
					else
					{
						// Reported like its ?flyspidercount=/?flyspiderbox= siblings: a typo'd
						// value would otherwise run at the baked DefaultSizeFactor while the
						// session believes it is looking at the size under test.
						Console.WriteLine("[debug] unknown ?flyspiderscale= value '" + val
							+ "' (expected a number > 0) -- ignored, staying on "
							+ (FlySpiderScale ?? EvilAliens.FlyingSpider.DefaultSizeFactor)
								.ToString(CultureInfo.InvariantCulture));
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
					else
					{
						// Names the TIERS rather than "a difficulty": the numeric spelling this
						// case deliberately refuses is the obvious thing to reach for next.
						RejectFlagValue(key, val, "a tier name (Easy/Medium/Hard/Very_Hard/Inzane), not a number",
							WebcamDifficulty.HasValue ? WebcamDifficulty.Value.ToString() : "the level's own tier");
					}
					break;
				case "wchearts":
					if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var wch) && wch > 0)
					{
						WebcamHearts = wch;
					}
					else
					{
						RejectFlagValue(key, val, "an integer > 0",
							InForce(WebcamHearts));
					}
					break;
				case "wckills":
					if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var wck) && wck > 0)
					{
						WebcamKills = wck;
					}
					else
					{
						RejectFlagValue(key, val, "an integer > 0",
							InForce(WebcamKills));
					}
					break;
				case "wcsaucers":
					if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var wcs) && wcs > 0)
					{
						WebcamSaucers = wcs;
					}
					else
					{
						RejectFlagValue(key, val, "an integer > 0",
							InForce(WebcamSaucers));
					}
					break;
				case "wcsaucerspeed":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var wcss) && wcss > 0f)
					{
						WebcamSaucerSpeed = wcss;
					}
					else
					{
						RejectFlagValue(key, val, "a number > 0",
							InForce(WebcamSaucerSpeed));
					}
					break;
				case "wcplasmaspeed":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var wcps) && wcps > 0f)
					{
						WebcamPlasmaSpeed = wcps;
					}
					else
					{
						RejectFlagValue(key, val, "a number > 0",
							InForce(WebcamPlasmaSpeed));
					}
					break;
				case "wcspawn":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var wcsp) && wcsp > 0f)
					{
						WebcamSpawnInterval = wcsp;
					}
					else
					{
						RejectFlagValue(key, val, "a number > 0",
							InForce(WebcamSpawnInterval));
					}
					break;
				case "wcarm":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var wcar) && wcar > 0f)
					{
						WebcamArmDelay = wcar;
					}
					else
					{
						RejectFlagValue(key, val, "a number > 0",
							InForce(WebcamArmDelay));
					}
					break;
				case "wccharge":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var wcch) && wcch > 0f)
					{
						WebcamChargeTime = wcch;
					}
					else
					{
						RejectFlagValue(key, val, "a number > 0",
							InForce(WebcamChargeTime));
					}
					break;
				case "wcminemax":
					if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var wcmm) && wcmm > 0)
					{
						WebcamMineMax = wcmm;
					}
					else
					{
						RejectFlagValue(key, val, "an integer > 0",
							InForce(WebcamMineMax));
					}
					break;
				case "wcminespawn":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var wcms) && wcms > 0f)
					{
						WebcamMineSpawn = wcms;
					}
					else
					{
						RejectFlagValue(key, val, "a number > 0",
							InForce(WebcamMineSpawn));
					}
					break;
				case "wcminelife":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var wcml) && wcml > 0f)
					{
						WebcamMineLife = wcml;
					}
					else
					{
						RejectFlagValue(key, val, "a number > 0",
							InForce(WebcamMineLife));
					}
					break;
				case "wcmothership":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var wcmo) && wcmo >= 0f)
					{
						WebcamMothership = wcmo;
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(WebcamMothership));
					}
					break;
				case "wcmothershipdir":
					{
						string d = val == null ? "" : val.Trim().ToLowerInvariant();
						if (d == "vertical" || d == "horizontal")
						{
							WebcamMothershipDir = d;
						}
						else
						{
							// A misspelled direction is the exact silent-variant trap this sweep is
							// about: the run reads as "forced vertical" while the choreography rolls
							// its usual ~60/40.
							RejectFlagValue(key, val, "vertical or horizontal",
								WebcamMothershipDir ?? "the random orientation roll");
						}
					}
					break;
				case "wcmothershipfreeze":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var wcmf) && wcmf >= 0f)
					{
						WebcamMothershipFreeze = wcmf;
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(WebcamMothershipFreeze));
					}
					break;
				case "wchitleeway":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var wchl) && wchl >= 0f)
					{
						WebcamHitLeeway = wchl;
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(WebcamHitLeeway));
					}
					break;
				case "wcavoid":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var wcav) && wcav >= 0f)
					{
						WebcamAvoid = wcav;
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(WebcamAvoid));
					}
					break;
				case "wcreturndelay":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var wcrd) && wcrd >= 0f)
					{
						WebcamReturnDelay = wcrd;
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(WebcamReturnDelay));
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
					else
					{
						RejectFlagValue(key, val, "a number",
							InForce(HueStart));
					}
					break;
				case "hueend":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var he))
					{
						HueEnd = he;
					}
					else
					{
						RejectFlagValue(key, val, "a number",
							InForce(HueEnd));
					}
					break;
				case "huetarget":
				case "hue":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var ht))
					{
						HueTarget = ht;
					}
					else
					{
						RejectFlagValue(key, val, "a number",
							InForce(HueTarget));
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
					else
					{
						RejectFlagValue(key, val, "a number > 0",
							InForce(HueLoopSeconds));
					}
					break;
				case "spiderhelpercycles":
					if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var shc) && shc >= 1)
					{
						SpiderHelperCycles = shc;
					}
					else
					{
						RejectFlagValue(key, val, "an integer >= 1",
							InForce(SpiderHelperCycles));
					}
					break;
				case "spiderhelperhp":
					if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var shhp) && shhp >= 1)
					{
						SpiderHelperHitPoints = shhp;
					}
					else
					{
						RejectFlagValue(key, val, "an integer >= 1",
							InForce(SpiderHelperHitPoints));
					}
					break;
				case "spiderhelperhovery":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var shy))
					{
						SpiderHelperHoverY = shy;
					}
					else
					{
						RejectFlagValue(key, val, "a number",
							InForce(SpiderHelperHoverY));
					}
					break;
				case "spiderhelperspeed":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var shs) && shs > 0f)
					{
						SpiderHelperSpeed = shs;
					}
					else
					{
						RejectFlagValue(key, val, "a number > 0",
							InForce(SpiderHelperSpeed));
					}
					break;
				case "spiderhelperwindup":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var shw) && shw >= 0f)
					{
						SpiderHelperWindupSeconds = shw;
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(SpiderHelperWindupSeconds));
					}
					break;
				case "spiderhelperfire":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var shf) && shf > 0f)
					{
						SpiderHelperFireSeconds = shf;
					}
					else
					{
						RejectFlagValue(key, val, "a number > 0",
							InForce(SpiderHelperFireSeconds));
					}
					break;
				case "spiderhelperlead":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var shl) && shl >= 0f)
					{
						SpiderHelperFireLead = shl;
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(SpiderHelperFireLead));
					}
					break;
				case "spiderhelperenterpower":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var shep) && shep > 0f)
					{
						SpiderHelperEnterPower = shep;
					}
					else
					{
						RejectFlagValue(key, val, "a number > 0",
							InForce(SpiderHelperEnterPower));
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
					else
					{
						RejectFlagValue(key, val, "a tier name (Easy/Medium/Hard/Very_Hard/Inzane), not a number",
							Difficulty.HasValue ? Difficulty.Value.ToString() : "the saved menu difficulty");
					}
					break;
				case "spiderbosshp":
					if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sbhp) && sbhp > 0)
					{
						SpiderBossHp = sbhp;
					}
					else
					{
						RejectFlagValue(key, val, "an integer > 0",
							InForce(SpiderBossHp));
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
				case "teampartner":
					// Bare ?teampartner (no value) means the AI partner -- the case someone
					// reaching for this flag wants. An off spelling resolves to None (the normal
					// connected-pad-then-AI resolution) rather than silently forcing the AI.
					// An unrecognised value is REPORTED and ignored, for the ?flyspiderflatten
					// reason: a typo would otherwise quietly run the other arm of the A/B while
					// the run is labelled as the variant under test.
					if (val == null || val.Trim().Length == 0 || val.Trim().ToLowerInvariant() == "ai")
					{
						TeamPartner = TeamPartnerSeat.Ai;
					}
					else if (val.Trim().ToLowerInvariant() == "pad")
					{
						TeamPartner = TeamPartnerSeat.Pad;
					}
					else if (IsExplicitlyOff(val))
					{
						TeamPartner = TeamPartnerSeat.None;
					}
					else
					{
						Console.WriteLine("[debug] unknown ?teampartner= value '" + val
							+ "' (expected ai/pad) -- ignored, seats resolve normally");
					}
					break;
				case "aibench":
					AiBench = IsOn(val);
					break;
				case "aismooth":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var aism) && aism >= 0f)
					{
						AiSteerSmoothMs = MathHelper.Min(aism, 1000f);
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(AiSteerSmoothMs ?? EvilAliens.PlayerShip.DefaultSteerSmoothMs));
					}
					break;
				case "aireact":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var aire) && aire >= 0f)
					{
						AiWallReactionMs = MathHelper.Min(aire, 3000f);
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(AiWallReactionMs ?? EvilAliens.PlayerShip.DefaultWallReactionMs));
					}
					break;
				case "aiaim":
					// Radians, applied as RandomNextFloat(-aiaim, +aiaim) -- so this is the HALF
					// width of the error arc and Pi (a full turn of spread) is a genuinely random
					// shot. Capped there rather than lower because "fires in a random direction" is
					// a legitimate skill FLOOR to A/B a tier row against, not a nonsense value.
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var aiaim) && aiaim >= 0f)
					{
						AiAimSpreadRad = MathHelper.Min(aiaim, MathHelper.Pi);
					}
					else
					{
						// Per-tier (PlayerShip.AimSpread resolves to Skill.AimRad): with no override
						// standing there is no single number to name, and the tier is not settled at
						// parse time, so say which TABLE is in force rather than guess a row.
						RejectFlagValue(key, val, "a number >= 0",
							AiAimSpreadRad.HasValue ? InForce(AiAimSpreadRad.Value) : "the per-tier skill row");
					}
					break;
				case "aigapmargin":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var aigm) && aigm >= 0f)
					{
						AiGapSwitchMargin = MathHelper.Min(aigm, 20f);
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(AiGapSwitchMargin ?? EvilAliens.PlayerShip.DefaultGapSwitchMargin));
					}
					break;
				case "aiscanrows":
					// Parsed as an INT: WallScanRows counts grid rows, so `4.7` is not a
					// value this knob has -- reject it rather than silently truncating to 4
					// and reporting a sweep that never moved.
					// 0 is DELIBERATELY allowed: it makes DistanceToBlockedRow report "nothing
					// blocked" always, i.e. a bot that does not look ahead at all -- the same
					// kind of skill FLOOR ?aiaim=Pi is, and the negative control a look-ahead
					// sweep wants at one end. The 64 ceiling is a COST bound, not a semantic
					// one: the scan runs per column per tick, and the deepest shipped grid is
					// 179 rows, so this does not reach the bottom of var3 by design.
					if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var aisr) && aisr >= 0)
					{
						AiWallScanRows = MathHelper.Clamp(aisr, 0, 64);
					}
					else
					{
						RejectFlagValue(key, val, "an integer >= 0",
							InForce(AiWallScanRows ?? EvilAliens.PlayerShip.DefaultWallScanRows));
					}
					break;
				case "aicrosspenalty":
					// Cost per blocked column crossed, against WallRowWeight's 8 per row of
					// clearance -- so the cap is where crossing dominates clearance absolutely.
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var aicp) && aicp >= 0f)
					{
						AiWallCrossPenalty = MathHelper.Min(aicp, 100f);
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(AiWallCrossPenalty ?? EvilAliens.PlayerShip.DefaultWallCrossPenalty));
					}
					break;
				case "aithreatlead":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var aitl) && aitl >= 0f)
					{
						AiThreatLeadMs = MathHelper.Min(aitl, 3000f);
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(AiThreatLeadMs ?? EvilAliens.PlayerShip.DefaultThreatLeadMs));
					}
					break;
				case "aismoothurgent":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var aisu) && aisu >= 0f)
					{
						AiSteerSmoothUrgentMs = MathHelper.Min(aisu, 1000f);
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(AiSteerSmoothUrgentMs ?? EvilAliens.PlayerShip.DefaultSteerSmoothUrgentMs));
					}
					break;
				case "airepeldelta":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var airpd) && airpd >= 0f)
					{
						AiRepelCancelDelta = MathHelper.Min(airpd, 20f);
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(AiRepelCancelDelta ?? EvilAliens.PlayerShip.DefaultRepulseCancelDelta));
					}
					break;
				case "ainoisefloor":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var ainf) && ainf >= 0f)
					{
						AiSteerNoiseFloor = MathHelper.Min(ainf, 20f);
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(AiSteerNoiseFloor ?? EvilAliens.PlayerShip.DefaultSteerNoiseFloor));
					}
					break;
				case "aievade":
					if (IsOn(val) || IsExplicitlyOff(val))
					{
						AiEvadeMovers = IsOn(val);
					}
					else
					{
						// A typo here would leave the evade path ON while the run is LABELLED as
						// having it off -- i.e. a measurement seam quietly measuring the other arm.
						RejectFlagValue(key, val, "on/off", (AiEvadeMovers ?? true) ? "on" : "off");
					}
					break;
				case "aicone":
				case "aiwedge":
				case "ailaneescape":
					if (IsOn(val) || IsExplicitlyOff(val))
					{
						if (key == "aicone")
						{
							AiConeShapes = IsOn(val);
						}
						else if (key == "aiwedge")
						{
							AiLaneWedge = IsOn(val);
						}
						else
						{
							AiLaneEscape = IsOn(val);
						}
					}
					else
					{
						// Same hazard as ?aievade=: a typo would leave the shape ON while the run
						// is LABELLED as having it off, i.e. a measurement seam quietly measuring
						// the other arm.
						RejectFlagValue(key, val, "on/off",
							((key == "aicone" ? AiConeShapes : (key == "aiwedge" ? AiLaneWedge : AiLaneEscape)) ?? true) ? "on" : "off");
					}
					break;
				case "aiconelead":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var aicl) && aicl >= 0f)
					{
						AiConeLeadMs = MathHelper.Min(aicl, 5000f);
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(AiConeLeadMs ?? EvilAliens.PlayerShip.DefaultConeLeadMs));
					}
					break;
				case "aiconemaxlen":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var aicml) && aicml >= 0f)
					{
						AiConeMaxLenPx = MathHelper.Min(aicml, 4000f);
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(AiConeMaxLenPx ?? EvilAliens.PlayerShip.DefaultConeMaxLenPx));
					}
					break;
				case "aiconewidth":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var aicw) && aicw > 0f)
					{
						AiConeWidthPx = MathHelper.Min(aicw, 2000f);
					}
					else
					{
						RejectFlagValue(key, val, "a number > 0",
							InForce(AiConeWidthPx ?? EvilAliens.PlayerShip.DefaultConeWidthPx));
					}
					break;
				case "aiconespread":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var aicsp) && aicsp >= 0f)
					{
						AiConeSpread = MathHelper.Min(aicsp, 200f);
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(AiConeSpread ?? EvilAliens.PlayerShip.DefaultConeSpread));
					}
					break;
				case "aiconewidthmin":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var aicwm) && aicwm >= 0f)
					{
						AiConeWidthMinPx = MathHelper.Min(aicwm, 2000f);
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(AiConeWidthMinPx ?? EvilAliens.PlayerShip.DefaultConeWidthMinPx));
					}
					break;
				case "aiconetaper":
					// REFUSED above 1 rather than clamped: 1 is the top of the taper's real range (a true
					// triangle), not a guard rail far outside anything anyone would type, so silently
					// clamping ?aiconetaper=2 would measure 1 under a run labelled 2.
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var aict) && aict >= 0f && aict <= 1f)
					{
						AiConeTaper = aict;
					}
					else
					{
						RejectFlagValue(key, val, "a number 0..1",
							InForce(AiConeTaper ?? EvilAliens.PlayerShip.DefaultConeTaper));
					}
					break;
				case "aiconefallalong":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var aicfa) && aicfa >= 0f)
					{
						AiConeFallAlong = MathHelper.Min(aicfa, 20f);
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(AiConeFallAlong ?? EvilAliens.PlayerShip.DefaultConeFallAlong));
					}
					break;
				case "aiconefallacross":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var aicfc) && aicfc >= 0f)
					{
						AiConeFallAcross = MathHelper.Min(aicfc, 20f);
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(AiConeFallAcross ?? EvilAliens.PlayerShip.DefaultConeFallAcross));
					}
					break;
				case "aiconescale":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var aics) && aics >= 0f)
					{
						AiConeScale = MathHelper.Min(aics, 20f);
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(AiConeScale ?? EvilAliens.PlayerShip.DefaultConeScale));
					}
					break;
				case "aiwedgestrength":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var aiws) && aiws >= 0f)
					{
						AiLaneWedgeStrength = MathHelper.Min(aiws, 100f);
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(AiLaneWedgeStrength ?? EvilAliens.PlayerShip.DefaultLaneWedgeStrength));
					}
					break;
				case "aiwedgefall":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var aiwf) && aiwf >= 0f)
					{
						AiLaneWedgeFallAlong = MathHelper.Min(aiwf, 20f);
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(AiLaneWedgeFallAlong ?? EvilAliens.PlayerShip.DefaultLaneWedgeFallAlong));
					}
					break;
				case "aifieldcurve":
				case "aiasteroidcurve":
					if (val == "classic" || val == "port")
					{
						if (key == "aifieldcurve")
						{
							AiClassicFieldCurve = val == "classic";
						}
						else
						{
							AiAsteroidClassicCurve = val == "classic";
						}
					}
					else
					{
						RejectFlagValue(key, val, "classic/port",
							((key == "aifieldcurve" ? AiClassicFieldCurve : AiAsteroidClassicCurve) ?? false) ? "classic" : "port");
					}
					break;
				case "aiasteroidflatpx":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var aiafp) && aiafp > 0f)
					{
						AiAsteroidFlatRangePx = MathHelper.Min(aiafp, 2000f);
					}
					else
					{
						RejectFlagValue(key, val, "a number > 0", InForce(AiAsteroidFlatRangePx));
					}
					break;
				case "aiasteroidrange":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var aiar) && aiar > 0f)
					{
						AiAsteroidRangeScale = MathHelper.Min(aiar, 20f);
					}
					else
					{
						RejectFlagValue(key, val, "a number > 0",
							InForce(AiAsteroidRangeScale ?? EvilAliens.PlayerShip.DefaultAsteroidRangeScale));
					}
					break;
				case "aiasteroidfall":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var aiaf) && aiaf >= 0f)
					{
						AiAsteroidFalloff = MathHelper.Min(aiaf, 20f);
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(AiAsteroidFalloff ?? EvilAliens.PlayerShip.DefaultAsteroidFalloff));
					}
					break;
				case "aiasteroidscale":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var aiast) && aiast >= 0f)
					{
						AiAsteroidThreatScale = MathHelper.Min(aiast, 20f);
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(AiAsteroidThreatScale ?? EvilAliens.PlayerShip.DefaultAsteroidThreatScale));
					}
					break;
				case "aiseekdeadzone":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var aisdz) && aisdz >= 0f)
					{
						AiSeekDeadzonePx = MathHelper.Min(aisdz, 400f);
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(AiSeekDeadzonePx ?? EvilAliens.PlayerShip.DefaultSeekArriveDeadzonePx));
					}
					break;
				case "aiseekpowerup":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var aisp) && aisp >= 0f)
					{
						AiSeekPowerupWeight = MathHelper.Min(aisp, 20f);
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(AiSeekPowerupWeight ?? EvilAliens.PlayerShip.DefaultSeekPowerupWeight));
					}
					break;
				case "aiseekapproach":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var aisa) && aisa >= 0f)
					{
						AiSeekApproachWeight = MathHelper.Min(aisa, 20f);
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(AiSeekApproachWeight ?? EvilAliens.PlayerShip.DefaultSeekApproachWeight));
					}
					break;
				case "aipowerupreach":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var aipr) && aipr >= 0f)
					{
						AiPowerupReachPx = MathHelper.Min(aipr, 1000f);
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(AiPowerupReachPx ?? EvilAliens.PlayerShip.DefaultPowerupReachPx));
					}
					break;
				case "aifieldpx":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var aifp) && aifp >= 0f)
					{
						AiThreatFieldPx = MathHelper.Min(aifp, 800f);
					}
					else
					{
						// Per-tier, like ?aiaim above (PlayerShip.ThreatFieldBasePx => Skill.FieldPx).
						RejectFlagValue(key, val, "a number >= 0",
							AiThreatFieldPx.HasValue ? InForce(AiThreatFieldPx.Value) : "the per-tier skill row");
					}
					break;
				case "aifieldsize":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var aifs) && aifs >= 0f)
					{
						AiThreatFieldSize = MathHelper.Min(aifs, 10f);
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(AiThreatFieldSize ?? EvilAliens.PlayerShip.DefaultThreatFieldSizeScale));
					}
					break;
				case "aifieldfall":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var aiff2) && aiff2 > 0f)
					{
						AiThreatFieldFalloff = MathHelper.Min(aiff2, 12f);
					}
					else
					{
						// STRICTLY > 0 (it is the exponent of a (1-t)^p falloff), so 0 is rejected
						// here where it is a legitimate floor for most of the family.
						RejectFlagValue(key, val, "a number > 0",
							InForce(AiThreatFieldFalloff ?? EvilAliens.PlayerShip.DefaultThreatFieldFalloff));
					}
					break;
				case "aibossbias":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var aibb) && aibb > 0f)
					{
						AiPriorityBias = MathHelper.Min(aibb, 1f);
					}
					else
					{
						RejectFlagValue(key, val, "a number > 0",
							InForce(AiPriorityBias ?? EvilAliens.PlayerShip.DefaultPriorityTargetBias));
					}
					break;
				case "aiff":
					if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var aiff))
					{
						AiFastForward = (int)MathHelper.Clamp(aiff, 0, 64);
					}
					else
					{
						// Not nullable: AiFastForward is 0 (off) until a flag sets it, so the value in
						// force is simply the current one. No range predicate either -- it clamps
						// 0..64 like the rest of the family clamps its ceilings -- so only an
						// unparseable value reaches here (?aiff=-1 and ?aiff=99999 are accepted and
						// clamped, deliberately unlike ?flyspidercount's rejected ceiling).
						RejectFlagValue(key, val, "an integer", InForce(AiFastForward));
					}
					break;
				case "netscript":
					NetScript = IsOn(val);
					break;
				case "aifriends":
					if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var aif))
					{
						AiFriends = (int)MathHelper.Clamp(aif, 0, 3);
					}
					else
					{
						RejectFlagValue(key, val, "an integer",
							InForce(AiFriends));
					}
					break;
				case "netlocal":
					if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var nloc))
					{
						NetLocal = (int)MathHelper.Clamp(nloc, 0, 3);
					}
					else
					{
						RejectFlagValue(key, val, "an integer",
							InForce(NetLocal));
					}
					break;
				case "netdropgrant":
					NetDropGrant = IsOn(val);
					break;
				case "gamebrowser":
					// Bare ?gamebrowser is the appearance rig (real-looking entries only).
					// ?gamebrowser=fallback adds the two unmapped entries; =thumbs gives two
					// entries a room thumbnail (card e7404647). An unrecognised value
					// is REPORTED and treated as bare, for the ?teampartner reason: a typo would
					// otherwise silently run the appearance rig while the run is labelled as the
					// fallback one, and the missing entries look exactly like the bug.
					if (val != null && val.Trim().ToLowerInvariant() == "fallback")
					{
						GameBrowser = true;
						GameBrowserFallback = true;
						GameBrowserThumbs = false;
					}
					else if (val != null && val.Trim().ToLowerInvariant() == "thumbs")
					{
						GameBrowser = true;
						GameBrowserFallback = false;
						GameBrowserThumbs = true;
					}
					else if (IsOn(val))
					{
						GameBrowser = true;
						GameBrowserFallback = false;
						GameBrowserThumbs = false;
					}
					else if (IsExplicitlyOff(val))
					{
						GameBrowser = false;
						GameBrowserFallback = false;
						GameBrowserThumbs = false;
					}
					else
					{
						// GameBrowserFallback/Thumbs are deliberately NOT written here: a
						// repeated flag (?gamebrowser=fallback&gamebrowser=falback) keeps the
						// earlier VALID value, per the ?flyspiderflatten convention, and the
						// message names what is actually in force rather than what the typo
						// would have set.
						GameBrowser = true;
						Console.WriteLine("[debug] unknown ?gamebrowser= value '" + val
							+ "' (expected fallback or thumbs) -- ignored, listing "
							+ (GameBrowserFallback
								? "the unmapped entries too"
								: "the real-looking entries only")
							+ (GameBrowserThumbs ? " with thumbnails" : ""));
					}
					if (GameBrowser)
					{
						SkipSplash = true;
						AutoStart = true;
					}
					break;
				case "netjip":
					NetJip = IsOn(val);
					break;
				case "netallowdebug":
					NetAllowDebug = IsOn(val);
					break;
				case "netfakepeer":
					if (!string.IsNullOrEmpty(val))
					{
						NetFakePeerId = val.Trim();
					}
					break;
				case "netfakelisted":
					if (!string.IsNullOrEmpty(val))
					{
						NetFakeListed = val.Trim().ToUpperInvariant();
					}
					break;
				case "netkickshot":
					NetKickShot = IsOn(val);
					break;
				case "netlag":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var nlag) && nlag >= 0f)
					{
						NetLagMs = MathHelper.Clamp(nlag, 0f, Net.NetImpairment.MaxLagMs);
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(NetLagMs));
					}
					break;
				case "netloss":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var nloss) && nloss >= 0f)
					{
						NetLossPct = MathHelper.Clamp(nloss, 0f, Net.NetImpairment.MaxLossPct);
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(NetLossPct));
					}
					break;
				case "netjitter":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var njit) && njit >= 0f)
					{
						NetJitterMs = MathHelper.Clamp(njit, 0f, Net.NetImpairment.MaxJitterMs);
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0",
							InForce(NetJitterMs));
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
				case "flyspiders":
					// Value-carrying: `fg`/`foreground` picks the un-flattened foreground variant;
					// `bg`/`background` and a bare ?flyspiders pick the group-flatten one under
					// test. An unrecognised value is NOT silently ignored -- swallowing it would
					// boot the whole of Level 2 with no hint why the fast-boot did nothing.
					{
						bool fg = string.Equals(val, "fg", StringComparison.OrdinalIgnoreCase)
							|| string.Equals(val, "foreground", StringComparison.OrdinalIgnoreCase);
						bool bg = string.Equals(val, "bg", StringComparison.OrdinalIgnoreCase)
							|| string.Equals(val, "background", StringComparison.OrdinalIgnoreCase);
						FlySpiders = IsOn(val) || fg || bg;
						FlySpidersForeground = fg;
						if (!FlySpiders)
						{
							Console.WriteLine("[debug] unknown ?flyspiders= value '" + val
								+ "' (expected fg/bg or a bare ?flyspiders) -- ignored");
						}
					}
					break;
				case "flyspiderflatten":
					// Value-carrying like ?flyspiders: an unrecognised value is reported, never
					// silently swallowed -- a typo would otherwise measure the DEFAULT path while
					// the run is labelled as the variant under test, which is the exact class of
					// mistake this card exists to correct.
					if (string.Equals(val, "swarm", StringComparison.OrdinalIgnoreCase))
					{
						FlySpiderFlatten = FlySpiderFlattenMode.Swarm;
					}
					else if (string.Equals(val, "per", StringComparison.OrdinalIgnoreCase)
						|| string.Equals(val, "perspider", StringComparison.OrdinalIgnoreCase))
					{
						FlySpiderFlatten = FlySpiderFlattenMode.PerSpider;
					}
					else if (IsExplicitlyOff(val))
					{
						FlySpiderFlatten = FlySpiderFlattenMode.None;
					}
					else
					{
						// Every rejection message below names the setting that is actually IN
						// FORCE, read back off the property rather than written as a literal --
						// a repeated flag (?flyspiderbox=250&flyspiderbox=xx) keeps the earlier
						// valid value, and a diagnostic that can state the wrong condition is
						// worse than one that states none.
						Console.WriteLine("[debug] unknown ?flyspiderflatten= value '" + val
							+ "' (expected per/0/swarm) -- ignored, staying on " + FlySpiderFlatten);
					}
					break;
				case "flyspidercount":
					// Reported, never swallowed -- same reason as ?flyspiderflatten= above, and it
					// bites harder here: a typo'd N silently leaves the endless STREAM running,
					// so the run has no pinned population at all while being labelled a bench.
					// The upper bound is rejected for the same reason rather than clamped: one
					// extra zero would otherwise spend the run building a million components on
					// the WASM heap, which reads as a hung boot, not as a mislabelled bench.
					if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fsc)
						&& fsc >= 0 && fsc <= MaxFlySpiderBench)
					{
						FlySpiderCount = fsc;
					}
					else
					{
						Console.WriteLine("[debug] unknown ?flyspidercount= value '" + val
							+ "' (expected an integer 0.." + MaxFlySpiderBench + ") -- ignored, staying on "
							+ (FlySpiderCount.HasValue
								? "the pinned bench of " + FlySpiderCount.Value
								: "the endless stream"));
					}
					break;
				case "flyspiderbox":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var fsb) && fsb > 0f)
					{
						FlySpiderBox = fsb;
					}
					else
					{
						Console.WriteLine("[debug] unknown ?flyspiderbox= value '" + val
							+ "' (expected a number > 0) -- ignored, staying on "
							+ EvilAliens.FlyingSpider.FlattenBoxHalfDesign.ToString(CultureInfo.InvariantCulture));
					}
					break;
				case "splashvariant":
					// Reported, never swallowed -- same reason as ?flyspiderflatten= above: a typo
					// would silently leave the RANDOM roll in force while the capture is labelled
					// as a pinned variant, i.e. a screenshot of whatever came up.
					if (string.Equals(val, "revenged", StringComparison.OrdinalIgnoreCase)
						|| string.Equals(val, "pure", StringComparison.OrdinalIgnoreCase)
						|| string.Equals(val, "glasses", StringComparison.OrdinalIgnoreCase))
					{
						SplashVariant = val.ToLowerInvariant();
					}
					else
					{
						Console.WriteLine("[debug] unknown ?splashvariant= value '" + val
							+ "' (expected revenged/pure/glasses) -- ignored, staying on "
							+ (SplashVariant ?? "the random roll"));
					}
					break;
				case "demo":
					// Reported, never swallowed -- same reason as ?splashvariant= above: a typo
					// would silently leave the RANDOM roll in force while the capture (or probe)
					// is labelled as a pinned demo, i.e. evidence about whichever demo came up.
					if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dp)
						&& dp >= 1 && dp <= 3)
					{
						DemoPick = dp;
					}
					else
					{
						RejectFlagValue(key, val, "1, 2 or 3",
							DemoPick.HasValue ? InForce(DemoPick) : "the random roll");
					}
					break;
				case "seed":
					// Any int is a legal seed, negatives included -- so this flag has no range
					// predicate and does NOT belong in logic_probe's rejection SWEEP (whose
					// shared shape is "a negative is clamped or refused"); it has its own leg
					// there instead. Only an unparseable value reaches the diagnostic, and it
					// must be REPORTED for the usual reason turned up a notch: a run labelled
					// ?seed=... but silently unseeded is an A/B measuring the very noise the
					// flag was added to remove, and the numbers would look like a real effect.
					if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sd))
					{
						Seed = sd;
						EvilAliens.RandomHelper.Reseed(sd);
					}
					else
					{
						RejectFlagValue(key, val, "an integer",
							Seed.HasValue ? InForce(Seed) : "an unseeded Random (the shipped default)");
					}
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
				case "creditsshot":
					// Bare => level 1 (and "=1" reads the same either way).
					if (val == null || val.Trim().Length == 0)
					{
						CreditsShot = 1;
					}
					else if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var csl)
						&& csl >= 1 && csl <= 3)
					{
						CreditsShot = csl;
					}
					else
					{
						RejectFlagValue(key, val, "1, 2 or 3 (the level just beaten)",
							CreditsShot.HasValue ? InForce(CreditsShot) : "a normal boot");
					}
					if (CreditsShot.HasValue)
					{
						SkipSplash = true;
						AutoStart = true;
					}
					break;
				case "crawlpos":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var cwp))
					{
						CrawlPos = cwp;
					}
					else
					{
						// Any design Y is meaningful here (the crawl scrolls from 650 down
						// through negative values), so the only refusal is unparseable.
						RejectFlagValue(key, val, "a design-space Y",
							CrawlPos.HasValue ? InForce(CrawlPos) : "a scrolling crawl");
					}
					break;
				case "crawlskew":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var cws) && cws >= 0f)
					{
						CrawlSkew = cws;
					}
					else
					{
						RejectFlagValue(key, val, "a number >= 0 (0 = no taper)",
							InForce(CrawlSkew));
					}
					break;
				case "castbrainscale":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var cbs) && cbs > 0f)
					{
						CastBrainScale = cbs;
					}
					else
					{
						RejectFlagValue(key, val, "a number > 0",
							InForce(CastBrainScale));
					}
					break;
				case "castbrainfps":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var cbf) && cbf > 0f)
					{
						CastBrainFps = cbf;
					}
					else
					{
						RejectFlagValue(key, val, "a number > 0",
							InForce(CastBrainFps));
					}
					break;
				case "spiderloop":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var spl) && spl > 0f)
					{
						SpiderLoopSeconds = spl;
					}
					else
					{
						RejectFlagValue(key, val, "a number > 0",
							InForce(SpiderLoopSeconds));
					}
					break;
				case "spiderjumpframe":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var spjf))
					{
						SpiderJumpFrame = spjf;
					}
					else
					{
						RejectFlagValue(key, val, "a number",
							InForce(SpiderJumpFrame));
					}
					break;
				case "spiderlandframe":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var splf))
					{
						SpiderLandFrame = splf;
					}
					else
					{
						RejectFlagValue(key, val, "a number",
							InForce(SpiderLandFrame));
					}
					break;
				case "spiderjumpx":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var spjx))
					{
						SpiderJumpX = spjx;
					}
					else
					{
						RejectFlagValue(key, val, "a number",
							InForce(SpiderJumpX));
					}
					break;
				case "spidershadowx":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var spsx))
					{
						SpiderShadowX = spsx;
					}
					else
					{
						RejectFlagValue(key, val, "a number",
							InForce(SpiderShadowX));
					}
					break;
				case "spidershadowy":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var spsy))
					{
						SpiderShadowY = spsy;
					}
					else
					{
						RejectFlagValue(key, val, "a number",
							InForce(SpiderShadowY));
					}
					break;
				case "spidershadowscale":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var spss) && spss > 0f)
					{
						SpiderShadowScale = spss;
					}
					else
					{
						RejectFlagValue(key, val, "a number > 0",
							InForce(SpiderShadowScale));
					}
					break;
				case "spiderairx":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var spax))
					{
						SpiderAirX = spax;
					}
					else
					{
						RejectFlagValue(key, val, "a number",
							InForce(SpiderAirX));
					}
					break;
				case "spiderairy":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var spay))
					{
						SpiderAirY = spay;
					}
					else
					{
						RejectFlagValue(key, val, "a number",
							InForce(SpiderAirY));
					}
					break;
				case "spiderphase":
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var spph))
					{
						SpiderPhase = ((spph % 1f) + 1f) % 1f;
					}
					else
					{
						// Any real number is legal -- it wraps into [0,1) -- so only an unparseable
						// one lands here.
						RejectFlagValue(key, val, "a number", InForce(SpiderPhase));
					}
					break;
				case "bgfreeze":
					// Numeric value = freeze there; bare ?bgfreeze = freeze at design x=400 (mid-screen);
					// ?bgfreeze=false = off, per this file's on/off convention. Parse BEFORE IsOn so "0"
					// reads as the column 0, not as "off" -- =false is the only way to disable it.
					// IsFinite because NumberStyles.Float accepts NaN/Infinity, and a NaN would ride
					// through MyMath.Mod into every layer's position.X and wedge the background.
					// Like ?shake, the on/off fallback covers the on/off SPELLINGS only: a typo'd
					// column (?bgfreeze=40O) is not "off", and swallowing it leaves the background
					// SCROLLING while the run is labelled as a frozen-phase capture -- which is
					// precisely the artifact hunt this flag exists for.
					if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var bgf)
						&& float.IsFinite(bgf))
					{
						BgFreeze = bgf;
					}
					else if (IsOn(val) || IsExplicitlyOff(val))
					{
						BgFreeze = IsOn(val) ? 400f : (float?)null;
					}
					else
					{
						RejectFlagValue(key, val, "a finite number, or on/off", InForce(BgFreeze));
					}
					break;
				case "harness":
						// The object name itself is the value (?harness=Spider). A bare ?harness with
						// no value is meaningless (no object) -- and swallowing it boots the WHOLE
						// GAME instead, which is the loudest version of this card's failure mode,
						// so say so. An unknown NAME is a different matter and stays the harness
						// scene's own business (HarnessRegistry reports it, with the valid list).
						if (!string.IsNullOrEmpty(val))
						{
							Harness = val.Trim();
							SkipSplash = true;
							AutoStart = true;
						}
						else
						{
							RejectFlagValue(key, val, "an object name, e.g. ?harness=spider",
								Harness ?? "no harness (a normal boot)");
						}
						break;
					case "frame":
						if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fr))
						{
							HarnessFrame = fr;
						}
						else
						{
							RejectFlagValue(key, val, "an integer",
								InForce(HarnessFrame));
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
						else
						{
							RejectFlagValue(key, val, "a number > 0",
								InForce(HarnessScale));
						}
						break;
					case "rot":
					case "rotation":
						if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var rt))
						{
							HarnessRot = rt;
						}
						else
						{
							RejectFlagValue(key, val, "a number",
								InForce(HarnessRot));
						}
						break;
					case "fps":
					case "animfps":
						if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var afps) && afps > 0f)
						{
							HarnessFps = afps;
						}
						else
						{
							RejectFlagValue(key, val, "a number > 0",
								InForce(HarnessFps));
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
					// `val` is NULL for a bare ?level (no '='), and this used to dereference it:
					// the NRE took the headless host down outright, and in the browser
					// Index.razor.cs caught it as one "flag read failed" line, silently dropping
					// EVERY remaining flag in the query -- the same class of silent miscarriage
					// this file's rejection convention exists to end.
					if (!string.IsNullOrEmpty(val) && !char.IsDigit(val[0]) && val[0] != '+' && val[0] != '-'
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
			// ?noattract is deliberately OUT (card af63f958): its ONE effect is leaving the main
			// menu's idle timeout unwired, which alters no gameplay, difficulty, unlock or fairness
			// -- and a menu-session joiner is rejected on its own Active bit (NetSession.HandleHello),
			// so keeping it in here made "don't yank my lobby into the attract demo" unaskable for
			// exactly the peer that needs it most. Knock-on: a ?noattract game now LISTS publicly
			// and no longer sets the hello debug bit. Both are intended -- ComputeEligible still
			// refuses Demo1/2/3, so it can never advertise an attract demo.
			// ?unlockall is reachable on the LIVE site (this whole method parses the real URL
			// query in Release), and since card 36db5d75 it stops Achievements and Unlockables
			// saving at all -- which is what makes its own "session-only" promise true, but also
			// means a session booted with it keeps NO progress: no hiscores, no level completion,
			// no awardments. Silent progress loss deserves a line of its own, above the flag dump.
			if (UnlockAll)
			{
				Console.WriteLine("[debug] ?unlockall is on -- everything is unlocked for this "
					+ "session ONLY, and NOTHING will be saved (no hiscores, no level completion, "
					+ "no awardments). Reload without the flag to keep progress.");
			}
			// Its own line, ABOVE the Active dump and not gated on it: ?seed is deliberately out
			// of `Active` (see the property), so the dump may never print -- and a run whose
			// world is pinned must say so in the log, or a capture taken from it cannot be told
			// apart from a normal one after the fact.
			if (Seed.HasValue)
			{
				Console.WriteLine("[debug] ?seed=" + Seed.Value.ToString(CultureInfo.InvariantCulture)
					+ " -- the gameplay RNG (RandomHelper) is seeded, so two runs of this boot "
					+ "reach the same world. Reload without the flag for normal random play.");
			}
			Active = SkipSplash || AutoStart || Level.HasValue || UnlockAll || Invuln || LoadLog || Harness != null || Bulletshot || Lazershot || Textshot || CastBrain || CastShow || CreditsShot.HasValue || CrawlPos.HasValue || TexViewer || WallsOnly || NoWalls || BrainBoss || TutorialTraining || FlySpiders || NetRole != NetRole.None || AIPlayer || TeamPartner != TeamPartnerSeat.None || NetScript || GameBrowser || NetJip || NetKickShot || NetHitstop || !NetSnapshotStaleGuard || AiFastForward > 1;
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
							+ (NoWalls ? " nowalls" : "")
							+ (BrainBoss ? " brainboss" : "")
							+ (TutorialTraining ? " tutorialtraining" : "")
							// Same class (card 3b6c12e7): ?win replaces Level 2's script with the ending
							// unlock chain AND forces Hard, so a run that reached the credits in a
							// minute has to be able to say why.
							+ (Win ? " win" : "")
							// Prints only when set: it decides which attract demo a capture or probe
							// actually measured. Note the whole line is gated on `Active`, which
							// DemoPick is deliberately out of -- so a bare ?demo=2 confirms nothing
							// and the probes see this only because they also pass ?loadlog.
							+ (DemoPick.HasValue ? " demo=" + DemoPick.Value.ToString(CultureInfo.InvariantCulture) : "")
							+ (FlySpiders ? (FlySpidersForeground ? " flyspiders=fg" : " flyspiders") : "")
							+ (NetRole != NetRole.None ? " net=" + NetRole.ToString().ToLowerInvariant() + " room=" + NetRoom : "")
							+ (AIPlayer ? " aiplayer" : "")
								+ (TeamPartner != TeamPartnerSeat.None ? " teampartner=" + TeamPartner.ToString().ToLowerInvariant() : "")
								+ (AiBench ? " aibench" : "")
								+ (AiFastForward > 1 ? " aiff=" + AiFastForward : "")
						+ (NetScript ? " netscript" : "")
						+ (NetLocal > 0 ? " netlocal=" + NetLocal : "")
						+ (NetDropGrant ? " netdropgrant" : "")
						// Prints only when the guard is OFF, i.e. only on the deliberate bug repro. It is
						// the one flag in this dump whose ABSENCE is the normal state, so a run that
						// reordered a snapshot on purpose has to be tellable from one that did not.
						+ (!NetSnapshotStaleGuard ? " netstaleguard=0" : "")
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
		// An unusable component is REPORTED per axis rather than as one verdict for the pair, so
		// "?pos=400,3O0" says the Y was dropped and the X stood -- a single message could only
		// say one or the other, and the half that landed is the confusing half.
		private static void ParsePos(string val)
		{
			string[] parts = (val ?? "").Split(',');
			bool haveX = parts.Length >= 1 && parts[0].Length > 0;
			bool haveY = parts.Length >= 2 && parts[1].Length > 0;
			if (haveX && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
			{
				HarnessX = x;
			}
			else if (haveX)
			{
				RejectFlagValue("pos", parts[0], "a number for x in ?pos=x,y", InForce(HarnessX));
			}
			if (haveY && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
			{
				HarnessY = y;
			}
			else if (haveY)
			{
				RejectFlagValue("pos", parts[1], "a number for y in ?pos=x,y", InForce(HarnessY));
			}
		}

		// Parse "?ripplecenter=x,y" (design coords) for the parked screenshot ring. Unlike
		// ?pos= this is a single Vector2, so a half-valid value would place the ring somewhere
		// nobody asked for -- BOTH axes must parse or the flag is rejected as a whole and the
		// centre stays where it was (screen centre unless a previous ?ripplecenter set it).
		private static void ParseRippleCenter(string key, string val)
		{
			string[] parts = (val ?? "").Split(',');
			if (parts.Length >= 2
				&& float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var rcx)
				&& float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var rcy))
			{
				RippleCenter = new Vector2(rcx, rcy);
				return;
			}
			RejectFlagValue(key, val, "two numbers as ?ripplecenter=x,y in design coords",
				RippleCenter.HasValue
					? InForce(RippleCenter.Value.X) + "," + InForce(RippleCenter.Value.Y)
					: "the screen centre");
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

		// Two call sites: an empty/absent query, and the end of Parse when nothing in the
		// `Active` expression ended up set -- which INCLUDES a boot carrying only out-of-Active
		// flags (?noattract, ?hitboxes, ?shake=, ?wallfog=, ...), so it does not say "no debug
		// flags". For an online joiner this line is the useful verdict: flag-clean, so the
		// menu-session pairing will not reject itself.
		private static void Hint()
		{
			Console.WriteLine("[debug] no boot-hijacking debug flags. URL options: ?menu  ?noattract  "
				+ "?level=<Name>  ?skipsplash  (see Compat/DebugFlags.cs)");
		}

		// THE REJECTION DIAGNOSTIC the value-carrying flags in this file route through, in the
		// wording card 6eb8dc9e settled for ?flyspider* and card 48b7c6b1 took to the ?ai* knobs:
		//   [debug] unknown ?aireact= value '420x' (expected a number >= 0) -- ignored, staying on 420
		// A tuning flag's failure mode is not a wrong picture, it is a run that measures the
		// DEFAULT path while carrying the label of the variant under test -- so a silently-ignored
		// value reads as "the knob did nothing", which is the wrong conclusion recorded as a
		// result. Card 4e401005 swept the remaining families (?wall*, ?wc*, ?holo*, ?spider*,
		// ?lazer*, ?connector*, ?hue*, ?net*, the harness params, ...); adding a new
		// value-carrying case means adding its `else` here too.
		//
		// `inForce` must be the setting ACTUALLY left standing, not the baked default: a repeated
		// flag (?aireact=420&aireact=xx) keeps the earlier valid value, and a diagnostic that can
		// state the wrong condition is worse than one that states none. Hence the InForce()
		// overloads below rather than a literal at the call site.
		//
		// WHAT REACHES IT: only a value the guard cannot use AT ALL -- an unparseable one, or one
		// the range predicate refuses (typically a negative). An out-of-RANGE value is CLAMPED
		// silently across almost the whole file and stays that way; ?flyspidercount is the one
		// deliberate exception, for a reason specific to it (one fat-fingered zero there spends
		// the boot building a million components). The ?flyspider* sites also keep their own
		// inline WriteLines: most of their wording is pinned by tools/sim/logic_probe, and
		// rerouting them buys nothing.
		//
		// STILL DELIBERATELY SILENT, so a reader does not conclude more than holds: the on/off
		// booleans (IsOn/IsExplicitlyOff have their own convention), and the free-form identity
		// STRINGS (?netfakepeer=, ?netfakehash=, ?bg=, ?room=, ?code=, ?signal=) where any value
		// is legal, an empty one is not a typo class, and there is no "expected" to state.
		// A handful of sites report from somewhere other than a plain `else`, and each has its
		// reason written where it sits: ?shake and ?bgfreeze take a number OR an on/off spelling,
		// so only a value that is neither reaches the diagnostic (reading a typo'd number as
		// "off" was the worse bug -- it turned the very effect under test off); ?pos reports per
		// AXIS; ?level keeps its own older wording; the ?flyspider*, ?net, ?teampartner,
		// ?splashvariant and ?gamebrowser sites keep inline WriteLines. NOTE ?gamebrowser is on
		// that list rather than among the silent booleans above: it WAS a plain on/off flag and
		// became value-carrying (=fallback) in card 0d166364's follow-up.
		private static void RejectFlagValue(string flag, string val, string expected, string inForce)
		{
			Console.WriteLine("[debug] unknown ?" + flag + "= value '" + val + "' (expected "
				+ expected + ") -- ignored, staying on " + inForce);
		}

		// Invariant-culture rendering of an in-force number for RejectFlagValue. InvariantGlobalization
		// is on project-wide, but these are diagnostics quoted back into a URL, so say it explicitly.
		private static string InForce(float v) => v.ToString(CultureInfo.InvariantCulture);

		private static string InForce(int v) => v.ToString(CultureInfo.InvariantCulture);

		// The nullable overrides -- the majority. Null means no override stands, and the value in
		// force is then the consumer's own baked constant, which lives in the game class rather
		// than here (Wall.DefaultSideTile, HoloSim's consts, WebcamLevel.Tunings[], ...). Naming a
		// number we would have to guess at is exactly the failure the "in force" rule exists to
		// prevent, so say WHICH setting stands instead -- the ?aiaim "per-tier skill row" call.
		private static string InForce(float? v) =>
			v.HasValue ? v.Value.ToString(CultureInfo.InvariantCulture) : "the shipped default";

		private static string InForce(int? v) =>
			v.HasValue ? v.Value.ToString(CultureInfo.InvariantCulture) : "the shipped default";

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

		// The complement of IsOn for the VALUE-CARRYING flags, which must tell "the author wrote
		// off" apart from "the author wrote something we don't understand" -- !IsOn() conflates
		// the two and would silently run a typo'd variant on the default path. Note a BARE flag is
		// not explicitly off (null => false), which is the whole difference from !IsOn().
		private static bool IsExplicitlyOff(string val)
		{
			if (val == null)
			{
				return false;
			}
			switch (val.Trim().ToLowerInvariant())
			{
			case "0":
			case "false":
			case "no":
			case "off":
				return true;
			default:
				return false;
			}
		}
	}
}
