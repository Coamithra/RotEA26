using System;

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
	// Bare flags are ON; ?menu=0 / ?menu=false turns one back off (handy in saved URLs).
	// Examples:  ?menu   ?menu&noattract   ?level=ClassicAliens   ?level=Level2&noattract
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
			Active = SkipSplash || AutoStart || NoAttract || Level.HasValue;
			if (Active)
			{
				Console.WriteLine("[debug] flags active: skipSplash=" + SkipSplash
					+ " autoStart=" + AutoStart + " noAttract=" + NoAttract
					+ " level=" + (Level.HasValue ? Level.Value.ToString() : "-"));
			}
			else
			{
				Hint();
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
