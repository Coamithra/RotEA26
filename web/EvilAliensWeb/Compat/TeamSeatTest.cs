using System.Text;
using EvilAliens;

namespace EvilAliensWeb.Compat;

// Console oracle for TeamChallenge's two seat decisions (card e6927ef8). Invoke with eaTeamSeat()
// from the browser console; `tools/sim/logic_probe` reflects into the same helpers to run the
// identical table headlessly on the desktop CLR.
//
// The bug this guards is a SEATING decision whose consequence is a permanent pause loop:
// GameScene.Update raises pauseRequested on every tick a seated pad device reads
// !InputHandler.PadConnected(i), and the 2008 code seated Keyboard + PadOne flat. Covering that
// live would need four physical gamepads for the interesting cases and, for the failure itself, a
// machine with none -- so the decisions are read as DATA instead, over every pad-connection mask.
// TeamChallenge.ResolvePrimarySeat/ResolvePartnerSeat are pure precisely for this (the
// NetSession.OwnsSlotCore idiom); this drives the REAL functions, never a copy of them.
//
// The assertions are PROPERTIES, not a restatement of the implementation: "never an absent pad",
// "the two seats never hold the same device", "the partner is the LOWEST-INDEXED eligible pad"
// (the index recomputed by bit arithmetic, so a PadTwo/PadThree mix-up in the resolver's switch
// cannot hide behind an identical mix-up here). Part 3 is the negative control -- the pre-card
// policy over the same table, which must FAIL the first property. Per the eaNetScore.test() rule:
// a green tick means nothing unless the same input is shown to break what came before.
internal static class TeamSeatTest
{
	// Every pad-connection mask: bit i set == pad i connected. 16 of them, so the input space of
	// the decision is covered exhaustively rather than sampled.
	private const int MaskCount = 16;

	private static bool Connected(int mask, int pad)
	{
		return (mask & (1 << pad)) != 0;
	}

	// Independent of TeamChallenge.PadIndexOf on purpose (bit scan vs. a switch): the two agreeing
	// is what makes the index mapping tested rather than assumed.
	private static int LowestSetBit(int mask)
	{
		for (int i = 0; i < 4; i++)
		{
			if (Connected(mask, i))
			{
				return i;
			}
		}
		return -1;
	}

	private static int LowestSetBitOtherThan(int mask, int exclude)
	{
		for (int i = 0; i < 4; i++)
		{
			if (Connected(mask, i) && i != exclude)
			{
				return i;
			}
		}
		return -1;
	}

	// The precondition of GameScene.Update's force-pause, stated positively: a seated PAD whose
	// controller is not there re-pauses the world every tick. Mirrors that guard's condition (the
	// PlayerShip.IsAiThreat <-> PlayerShip.CollidesWith idiom) -- if the guard's device set ever
	// changes, this must follow it.
	internal static bool WouldForcePause(ControlDevice seat, int mask)
	{
		int pad = TeamChallenge.PadIndexOf(seat);
		return pad >= 0 && !Connected(mask, pad);
	}

	// Every device that could arrive as the launching device, including the ones that cannot drive
	// a ship (Generic has no PlayerShip.Update case; the puppets belong to the net layer).
	private static readonly ControlDevice[] Starters =
	{
		ControlDevice.Keyboard,
		ControlDevice.PadOne,
		ControlDevice.PadTwo,
		ControlDevice.PadThree,
		ControlDevice.PadFour,
		ControlDevice.Generic,
		ControlDevice.AI,
		ControlDevice.Remote
	};

	private static readonly DebugFlags.TeamPartnerSeat[] Overrides =
	{
		DebugFlags.TeamPartnerSeat.None,
		DebugFlags.TeamPartnerSeat.Ai,
		DebugFlags.TeamPartnerSeat.Pad
	};

	private static string MaskName(int mask)
	{
		if (mask == 0)
		{
			return "no pads";
		}
		StringBuilder sb = new StringBuilder("pads ");
		for (int i = 0; i < 4; i++)
		{
			if (Connected(mask, i))
			{
				sb.Append(i);
			}
		}
		return sb.ToString();
	}

	// The properties slot 0's device must satisfy. Returns null when it holds, else what broke.
	internal static string PrimaryViolation(ControlDevice seat, ControlDevice starter, int mask)
	{
		if (WouldForcePause(seat, mask))
		{
			return "seats an absent pad (would force-pause every tick)";
		}
		if (TeamChallenge.PadIndexOf(seat) < 0 && seat != ControlDevice.Keyboard)
		{
			return "seats " + seat + ", which has no PlayerShip input case -- a ship nobody can steer";
		}
		// A pad that is present AND launched the level keeps its ship; everything else falls back
		// to the keyboard, which is always drivable here.
		int starterPad = TeamChallenge.PadIndexOf(starter);
		bool starterUsable = starterPad >= 0 && Connected(mask, starterPad);
		if (starterUsable && seat != starter)
		{
			return "dropped the launching device " + starter + " (present) in favour of " + seat;
		}
		if (!starterUsable && seat != ControlDevice.Keyboard)
		{
			return "starter " + starter + " cannot drive, so the seat should fall back to Keyboard, not " + seat;
		}
		return null;
	}

	// The properties the partner seat must satisfy.
	internal static string PartnerViolation(ControlDevice seat, ControlDevice primary, int mask, DebugFlags.TeamPartnerSeat forced)
	{
		if (forced == DebugFlags.TeamPartnerSeat.Ai)
		{
			return seat == ControlDevice.AI ? null : "?teampartner=ai must force the bot, got " + seat;
		}
		if (forced == DebugFlags.TeamPartnerSeat.Pad)
		{
			// The bug reproduction: the pre-card seating verbatim, absent pad and all.
			return seat == ControlDevice.PadOne ? null : "?teampartner=pad must seat PadOne verbatim, got " + seat;
		}
		if (WouldForcePause(seat, mask))
		{
			return "seats an absent pad (would force-pause every tick)";
		}
		if (seat == primary)
		{
			return "seats the SAME device as the primary (" + seat + ") -- one player, two ships";
		}
		int want = LowestSetBitOtherThan(mask, TeamChallenge.PadIndexOf(primary));
		if (want < 0)
		{
			return seat == ControlDevice.AI ? null : "no pad is free for a second human, so the bot should fly, not " + seat;
		}
		if (TeamChallenge.PadIndexOf(seat) != want)
		{
			return "a second human is on pad " + want + " but the seat went to " + seat;
		}
		return null;
	}

	public static string Run()
	{
		StringBuilder sb = new StringBuilder();
		int pass = 0;
		int fail = 0;
		void Check(string name, bool ok, string detail)
		{
			if (ok)
			{
				pass++;
			}
			else
			{
				fail++;
			}
			sb.Append(ok ? "PASS " : "FAIL ").Append(name);
			if (detail != null)
			{
				sb.Append("  ").Append(detail);
			}
			sb.Append('\n');
		}

		// ---- 1. slot 0, every launching device x every pad mask ----------------------
		sb.Append("[teamseat] 1. primary seat (real TeamChallenge.ResolvePrimarySeat), ")
			.Append(Starters.Length).Append(" starters x 16 pad masks\n");
		foreach (ControlDevice starter in Starters)
		{
			int bad = 0;
			string first = null;
			for (int mask = 0; mask < MaskCount; mask++)
			{
				int m = mask;
				ControlDevice seat = TeamChallenge.ResolvePrimarySeat(starter, (int pad) => Connected(m, pad));
				string why = PrimaryViolation(seat, starter, mask);
				if (why != null)
				{
					bad++;
					if (first == null)
					{
						first = MaskName(mask) + " -> " + seat + ": " + why;
					}
				}
			}
			Check("starter " + starter, bad == 0, bad == 0 ? "16/16 drivable and never an absent pad" : bad + " bad; " + first);
		}

		// ---- 2. slot 1, every primary x every pad mask x every override ---------------
		sb.Append("[teamseat] 2. partner seat (real TeamChallenge.ResolvePartnerSeat)\n");
		foreach (DebugFlags.TeamPartnerSeat forced in Overrides)
		{
			int bad = 0;
			int loops = 0;
			int humans = 0;
			string first = null;
			foreach (ControlDevice primary in new[] { ControlDevice.Keyboard, ControlDevice.PadOne, ControlDevice.PadTwo })
			{
				for (int mask = 0; mask < MaskCount; mask++)
				{
					int m = mask;
					ControlDevice seat = TeamChallenge.ResolvePartnerSeat(primary, (int pad) => Connected(m, pad), forced);
					if (WouldForcePause(seat, mask))
					{
						loops++;
					}
					if (TeamChallenge.PadIndexOf(seat) >= 0 && !WouldForcePause(seat, mask))
					{
						humans++;
					}
					string why = PartnerViolation(seat, primary, mask, forced);
					if (why != null)
					{
						bad++;
						if (first == null)
						{
							first = "primary " + primary + ", " + MaskName(mask) + " -> " + seat + ": " + why;
						}
					}
				}
			}
			string what = "teampartner=" + forced.ToString().ToLowerInvariant();
			Check("properties " + what, bad == 0, bad == 0 ? "48/48 cases hold" : bad + " violated; " + first);
			// A resolver that always returned AI would satisfy every property above except this
			// one: a real second pad must actually get the seat.
			if (forced == DebugFlags.TeamPartnerSeat.None)
			{
				Check("seats real humans " + what, humans > 0, humans + " of 48 cases seat a present pad (a bot-only resolver would read 0)");
			}
			// Only the bug-reproduction override may seat a device that is not there.
			int wantLoops = (forced == DebugFlags.TeamPartnerSeat.Pad) ? 24 : 0;
			Check("force-pause seats " + what, loops == wantLoops,
				loops + "/48 seat an absent pad (expected " + wantLoops + ")");
		}

		// ---- 3. negative control: the pre-card policy over the same table -------------
		// The 2008 policy took no arguments at all: Keyboard in slot 0, PadOne in slot 1.
		sb.Append("[teamseat] 3. negative control -- the pre-card seating (Keyboard + PadOne)\n");
		int oldLoops = 0;
		int oldUnsteerable = 0;
		foreach (ControlDevice starter in new[] { ControlDevice.Keyboard, ControlDevice.PadOne })
		{
			for (int mask = 0; mask < MaskCount; mask++)
			{
				if (WouldForcePause(ControlDevice.PadOne, mask))
				{
					oldLoops++;
				}
				// ... and a pad-only player got a Keyboard ship they never asked for.
				if (starter != ControlDevice.Keyboard)
				{
					oldUnsteerable++;
				}
			}
		}
		Check("old partner seat pause-loops", oldLoops == 16, oldLoops == 16
			? "16/32 cases (every mask without pad 0, both starters) force-pause every tick -- the bug, reproduced"
			: "expected 16, got " + oldLoops + " (has WouldForcePause drifted from GameScene.Update?)");
		Check("old primary seat ignored the starter", oldUnsteerable == 16,
			"a pad-launched run always got a Keyboard ship in slot 0 (" + oldUnsteerable + "/32 cases)");

		// ---- live state ---------------------------------------------------------------
		// What THIS machine would seat right now, plus the seated roster if a level is up, so the
		// browser pass is one call. Info only; the services are absent outside a booted game, and
		// parts 1-3 above need none of them.
		sb.Append("[teamseat] 4. live state\n");
		InputHandler input = ServiceHelper.Get<IInputHandlerService>()?.InputHandler;
		if (input == null)
		{
			sb.Append("  info  no input service (not booted) -- the pure parts above still ran\n");
		}
		else
		{
			int liveMask = 0;
			for (int i = 0; i < 4; i++)
			{
				if (input.PadConnected(i))
				{
					liveMask |= 1 << i;
				}
			}
			ControlDevice livePrimary = TeamChallenge.ResolvePrimarySeat(ControlDevice.Keyboard, input.PadConnected);
			sb.Append("  info  ").Append(MaskName(liveMask)).Append(" connected; flag=")
				.Append(DebugFlags.TeamPartner.ToString().ToLowerInvariant())
				.Append("; a keyboard-launched run would seat ").Append(livePrimary).Append(" + ")
				.Append(TeamChallenge.ResolvePartnerSeat(livePrimary, input.PadConnected, DebugFlags.TeamPartner))
				.Append('\n');
		}
		Oracle oracle = ServiceHelper.Get<IOracleService>()?.Oracle;
		StringBuilder roster = new StringBuilder();
		if (oracle != null)
		{
			for (int i = 0; i < Oracle.MaxPlayers; i++)
			{
				if (oracle.IsSeated(i))
				{
					if (roster.Length > 0)
					{
						roster.Append(',');
					}
					roster.Append(i).Append(':').Append(oracle.Controller(i));
				}
			}
		}
		sb.Append("  info  roster=").Append(roster.Length > 0 ? roster.ToString() : "-").Append('\n');

		sb.Append("[teamseat] ").Append(fail == 0 ? "ALL PASS" : "FAILURES").Append(" -- ")
			.Append(pass).Append(" passed, ").Append(fail).Append(" failed\n");
		return sb.ToString();
	}
}
