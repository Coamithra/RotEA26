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
	//   ?metalscore=0  disable the chrome-sheen (metal.fx) on the in-game score + "Press Start"
	//                  text (it is ON by default) to A/B the plain flattened drop shadow
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
	//   With ?harness=blast the harness LOOPS the blast through its lifetime and overlays the
	//   real collision ring + a live readout, for tuning the bomb's fade/active window:
	//     ?blastactive=<0..1> fade-alpha floor below which the blast stops dealing damage (def 0.5)
	//     ?blasthit=<f>       fraction of the visible radius that deals damage (default 0.8)
	//     ?blastloop=<sec>    seconds for one spawn->fade sweep in the viz (default 3)
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

		public static float BlastLoopSeconds { get; private set; } = 3f;

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
case "bulletshot":
						Bulletshot = IsOn(val);
						if (Bulletshot)
						{
							SkipSplash = true;
							AutoStart = true;
						}
						break;
					case "level":
					if (Enum.TryParse<EvilAliens.Levels>(val, ignoreCase: true, out var lvl))
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
			Active = SkipSplash || AutoStart || NoAttract || Level.HasValue || UnlockAll || Invuln || LoadLog || Harness != null || Bulletshot;
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
