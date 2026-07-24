using System.Text;
using EvilAliens;

namespace EvilAliensWeb.Compat;

// Console oracle for TeamChallenge's partner seat (card e6927ef8). Invoke with eaTeamSeat()
// from the browser console.
//
// The bug this guards is a SEATING decision whose consequence is a permanent pause loop:
// GameScene.Update raises pauseRequested on every tick a seated pad device reads
// !InputHandler.PadConnected(i), and the 2008 code seated ControlDevice.PadOne unconditionally.
// Verifying the fix by playing it needs four physical gamepads to cover the interesting cases
// and, for the failure, a machine with none -- so the decision is read as DATA instead, over
// every pad-connection mask. TeamChallenge.ResolvePartnerSeat is pure precisely for this (the
// NetSession.OwnsSlotCore idiom); this drives the REAL function, not a restatement of it.
//
// Three parts:
//  1. the invariant that kills the bug -- the resolved device is never an ABSENT pad;
//  2. the preference order -- a connected pad always beats the AI, lowest index first, and the
//     two ?teampartner overrides do what they say;
//  3. the NEGATIVE CONTROL -- the old always-PadOne policy run over the same table, which MUST
//     violate part 1 in every mask where pad 0 is missing. Without it a green tick proves
//     nothing about the bug being fixed (the eaNetScore.test / eaNetCombo.test rule).
internal static class TeamSeatTest
{
	// Every pad-connection mask: bit i set == pad i connected. 16 of them, so this is
	// exhaustive rather than sampled -- the whole input space of the decision.
	private const int MaskCount = 16;

	private static bool Connected(int mask, int pad)
	{
		return (mask & (1 << pad)) != 0;
	}

	private static bool IsPad(ControlDevice d)
	{
		return d == ControlDevice.PadOne || d == ControlDevice.PadTwo || d == ControlDevice.PadThree || d == ControlDevice.PadFour;
	}

	private static int PadIndex(ControlDevice d)
	{
		return d switch
		{
			ControlDevice.PadOne => 0,
			ControlDevice.PadTwo => 1,
			ControlDevice.PadThree => 2,
			ControlDevice.PadFour => 3,
			_ => -1
		};
	}

	// The precondition of GameScene.Update's force-pause, stated positively: a seated PAD whose
	// controller is not there re-pauses the world every tick. Mirrors that guard's condition
	// (the PlayerShip.IsAiThreat <-> PlayerShip.CollidesWith idiom) -- if the guard's device set
	// ever changes, this must follow it.
	private static bool WouldForcePause(ControlDevice seat, int mask)
	{
		return IsPad(seat) && !Connected(mask, PadIndex(seat));
	}

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

	// The seat the rules say this mask/override deserves. ?teampartner=ai is unconditional;
	// ?teampartner=pad only differs from None when there is no pad to prefer (it is the
	// bug-reproduction, so it seats PadOne anyway).
	private static ControlDevice Expected(int mask, DebugFlags.TeamPartnerSeat forced)
	{
		if (forced == DebugFlags.TeamPartnerSeat.Ai)
		{
			return ControlDevice.AI;
		}
		for (int i = 0; i < 4; i++)
		{
			if (Connected(mask, i))
			{
				return i switch
				{
					0 => ControlDevice.PadOne,
					1 => ControlDevice.PadTwo,
					2 => ControlDevice.PadThree,
					_ => ControlDevice.PadFour
				};
			}
		}
		return (forced == DebugFlags.TeamPartnerSeat.Pad) ? ControlDevice.PadOne : ControlDevice.AI;
	}

	private static readonly DebugFlags.TeamPartnerSeat[] Overrides =
	{
		DebugFlags.TeamPartnerSeat.None,
		DebugFlags.TeamPartnerSeat.Ai,
		DebugFlags.TeamPartnerSeat.Pad
	};

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

		// ---- 1 + 2. the real resolver over every mask x override ---------------------
		sb.Append("[teamseat] 1. resolved seat, all 16 pad masks x 3 overrides (real TeamChallenge.ResolvePartnerSeat)\n");
		foreach (DebugFlags.TeamPartnerSeat forced in Overrides)
		{
			int loops = 0;
			int wrongPreference = 0;
			string firstLoop = null;
			string firstPreference = null;
			for (int mask = 0; mask < MaskCount; mask++)
			{
				int m = mask;
				ControlDevice seat = TeamChallenge.ResolvePartnerSeat((int pad) => Connected(m, pad), forced);
				if (WouldForcePause(seat, mask))
				{
					loops++;
					if (firstLoop == null)
					{
						firstLoop = "e.g. " + MaskName(mask) + " -> " + seat;
					}
				}
				// Preference: with a pad plugged in the partner is the LOWEST-numbered one, and
				// the pad INDEX must map to the matching device (PlayerShip reads GamePad slot i
				// from the device, so a PadTwo/PadThree mix-up silently reads the wrong pad).
				// This leg restates the rule rather than deriving it independently -- its value is
				// catching a resolver that never seats a human at all (which would sail through
				// part 1) or gets that index mapping wrong. Part 1 and the negative control below
				// are what carry the property-based weight.
				ControlDevice expected = Expected(mask, forced);
				if (seat != expected)
				{
					wrongPreference++;
					if (firstPreference == null)
					{
						firstPreference = "e.g. " + MaskName(mask) + " -> " + seat + ", expected " + expected;
					}
				}
			}
			string what = "teampartner=" + forced.ToString().ToLowerInvariant();
			// ?teampartner=pad is the deliberate exception: it exists to reach the force-pause.
			if (forced == DebugFlags.TeamPartnerSeat.Pad)
			{
				Check("bug-repro reachable  " + what, loops == 1, loops == 1 ? "seats an absent pad in the no-pad mask only, as intended" : "expected exactly 1 force-pause mask (no pads), got " + loops);
			}
			else
			{
				Check("no force-pause seat  " + what, loops == 0, loops == 0 ? "16/16 masks seat a device that is present" : loops + " mask(s) seat an ABSENT pad; " + firstLoop);
			}
			Check("preference order     " + what, wrongPreference == 0, wrongPreference == 0 ? null : wrongPreference + " mask(s) resolved unexpectedly; " + firstPreference);
		}

		// ---- 3. negative control: the OLD policy over the same table -----------------
		// A green suite above means nothing unless the same input is shown to BREAK the code
		// this card replaced. The 2008 policy took no arguments at all: always PadOne.
		sb.Append("[teamseat] 2. negative control -- the pre-card policy (always PadOne)\n");
		int oldLoops = 0;
		for (int mask = 0; mask < MaskCount; mask++)
		{
			if (WouldForcePause(ControlDevice.PadOne, mask))
			{
				oldLoops++;
			}
		}
		Check("old policy pause-loops", oldLoops == 8, oldLoops == 8
			? "8/16 masks (every one without pad 0) force-pause every tick -- the bug, reproduced"
			: "expected 8 force-pause masks, got " + oldLoops + " (has WouldForcePause drifted from GameScene.Update?)");

		// ---- live state ---------------------------------------------------------------
		// What THIS machine would seat right now, plus the seated roster if a level is up, so
		// the browser pass is one call. Info only -- a rig with no pads is the normal case.
		sb.Append("[teamseat] 3. live state\n");
		IInputHandlerService input = ServiceHelper.Get<IInputHandlerService>();
		int liveMask = 0;
		for (int i = 0; i < 4; i++)
		{
			if (input.InputHandler.PadConnected(i))
			{
				liveMask |= 1 << i;
			}
		}
		sb.Append("  info  ").Append(MaskName(liveMask)).Append(" connected; flag=")
			.Append(DebugFlags.TeamPartner.ToString().ToLowerInvariant()).Append("; would seat ")
			.Append(TeamChallenge.ResolvePartnerSeat(input.InputHandler.PadConnected, DebugFlags.TeamPartner))
			.Append('\n');
		Oracle oracle = ServiceHelper.Get<IOracleService>().Oracle;
		StringBuilder roster = new StringBuilder();
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
		sb.Append("  info  roster=").Append(roster.Length > 0 ? roster.ToString() : "-").Append('\n');

		sb.Append("[teamseat] ").Append(fail == 0 ? "ALL PASS" : "FAILURES").Append(" -- ")
			.Append(pass).Append(" passed, ").Append(fail).Append(" failed\n");
		return sb.ToString();
	}
}
