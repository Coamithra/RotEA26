using System.Collections.Generic;
using System.Text;
using EvilAliens;

namespace EvilAliensWeb.Compat;

// Console oracle for the Boss Train's section state (card 4a3b22b7). Invoke with eaBossTrain()
// from the browser console, or `eval BossTrain` under eahl, on a `?level=InsaneBossI` boot.
//
// The bug: InsaneBossI is the only level whose script walks three SECTIONS (space -> Mars ->
// alien base), each of which changes the music, the backdrop and the Mars floor. None of that is
// event-list state, so GameEventList.RevertToCheckpoint does not restore it -- and its walk-back
// lands on the nearest checkpoint AT OR BEFORE the death, which for the alien-base transition
// (script index 33, next checkpoint 36) is checkpoint 24, two sections earlier. Dying in that ~10s
// window replayed Level 2's spider boss on the alien-base backdrop under Level 3's track, and the
// second arrival at the alien base changed no music, because nothing had put it back.
//
// Reaching that window in play means dying inside a ~10s slot after a multi-minute boss run: eight
// full AI soaks (up to 25 deaths each) hit `revert 33 -> 24` but never the case one index later.
// So the decision is read as DATA instead, over the REAL event list of the REAL scene.
//
// Part 1 is a PROPERTY, not a restatement: it forward-walks the script and checks each checkpoint's
// declared section against the section a player arriving there would actually be in. Both sides
// come from records written by the working statements themselves (CheckPointSection beside its
// SetLastEventAsCheckPoint, SectionChangeHere beside its `OnFinished += Go*`), so a map the test
// spelled out for itself cannot agree with itself.
//
// Part 2 drives the REAL RevertToCheckpoint from the REAL alien-base window and reads the section
// and the track back. Part 3 is the negative control per the eaNetScore.test() rule: the same
// input with the re-assert suppressed, i.e. the pre-card code, which MUST fail part 2's assertion
// -- a green tick means nothing unless the same input is shown to break what came before.
internal static class BossTrainTest
{
	// The death position the card is about: one index past the alien-base transition at 33, i.e.
	// the first tick on which a player is IN the alien base and can die there. Derived below from
	// the live script rather than hard-coded, so the suite follows a script edit.
	private static int DeathPosJustAfter(int sectionChangeIndex)
	{
		// progressList leaves `pos` one past the last event it activated, and the transition's own
		// OnFinished advances the list before the handler runs -- so the tick after the alien-base
		// beat sits at index+2.
		return sectionChangeIndex + 2;
	}

	internal static string Run()
	{
		StringBuilder sb = new StringBuilder();
		if (!(GameScene.NetActiveScene is InsaneBossI level))
		{
			return "[bosstrain] no InsaneBossI scene is live -- boot ?level=InsaneBossI first.";
		}
		GameEventList list = level.DebugEventList;
		Dictionary<int, InsaneBossI.Section> checkpoints = level.DebugCheckpointSections;
		Dictionary<int, InsaneBossI.Section> changes = level.DebugSectionChanges;
		int fails = 0;

		sb.Append("[bosstrain] script events=").Append(list.BenchCount)
			.Append(" checkpoints=").Append(checkpoints.Count)
			.Append(" sectionChanges=").Append(changes.Count).Append('\n');

		// ---- Part 1: every checkpoint declares the section a player arriving there is in ----
		// Walk the script from 0, carrying the section each recorded change puts us into, and
		// compare at each checkpoint index.
		InsaneBossI.Section walked = InsaneBossI.Section.Space;   // Initialize's opening section
		List<int> checkpointIndices = list.DebugCheckpointIndices();
		for (int i = 0; i < list.BenchCount; i++)
		{
			if (changes.TryGetValue(i, out var into))
			{
				walked = into;
			}
			if (!checkpoints.TryGetValue(i, out var declared))
			{
				// A checkpoint with no declared section would silently keep whatever section the
				// death left behind -- exactly the pre-card behaviour, for that one checkpoint.
				if (checkpointIndices.Contains(i))
				{
					sb.Append("  FAIL checkpoint ").Append(i)
						.Append(" declares no section (add a CheckPointSection beside its SetLastEventAsCheckPoint)\n");
					fails++;
				}
				continue;
			}
			bool ok = declared == walked;
			if (!ok)
			{
				fails++;
			}
			sb.Append(ok ? "  ok   " : "  FAIL ").Append("checkpoint ").Append(i)
				.Append(" declares ").Append(declared)
				.Append(", forward walk says ").Append(walked).Append('\n');
		}
		// Positive control: the walk must actually have MOVED, or every checkpoint agreeing on
		// Space would pass vacuously.
		if (walked != InsaneBossI.Section.AlienBase)
		{
			sb.Append("  FAIL forward walk ended in ").Append(walked)
				.Append(", expected AlienBase -- the script's section changes were not seen\n");
			fails++;
		}

		// ---- Part 2: the revert leg, through the real RevertToCheckpoint ----
		if (!TryAlienBaseChangeIndex(changes, out int alienBaseAt))
		{
			sb.Append("  FAIL the script records no AlienBase section change\n");
			return Verdict(sb, fails + 1);
		}
		int deathPos = DeathPosJustAfter(alienBaseAt);
		fails += RevertLeg(sb, level, list, deathPos, suppress: false,
			expected: InsaneBossI.Section.Mars, label: "fixed");

		// ---- Part 3: the same input against the pre-card behaviour (negative control) ----
		int controlFails = RevertLeg(sb, level, list, deathPos, suppress: true,
			expected: InsaneBossI.Section.Mars, label: "pre-card");
		if (controlFails == 0)
		{
			sb.Append("  FAIL negative control PASSED -- suppressing the re-assert changed nothing, ")
				.Append("so part 2 proves nothing\n");
			fails++;
		}
		else
		{
			sb.Append("  ok   negative control fails as it must (pre-card code leaves the section stranded)\n");
		}

		return Verdict(sb, fails);
	}

	private static bool TryAlienBaseChangeIndex(Dictionary<int, InsaneBossI.Section> changes, out int index)
	{
		index = -1;
		foreach (KeyValuePair<int, InsaneBossI.Section> kv in changes)
		{
			if (kv.Value == InsaneBossI.Section.AlienBase && kv.Key > index)
			{
				index = kv.Key;
			}
		}
		return index >= 0;
	}

	// Park the level in the alien base, put the script at `deathPos`, then run the REAL
	// RevertToCheckpoint and read back where the level ended up. Returns the failure count.
	private static int RevertLeg(StringBuilder sb, InsaneBossI level, GameEventList list,
		int deathPos, bool suppress, InsaneBossI.Section expected, string label)
	{
		int fails = 0;
		level.DebugSuppressSectionReassert = false;
		level.DebugApplySection("AlienBase");
		string preSection = level.DebugSection;
		string preSong = SongName();
		if (preSection != "AlienBase")
		{
			sb.Append("  FAIL [").Append(label).Append("] could not park the level in AlienBase (got ")
				.Append(preSection).Append(") -- the leg would be vacuous\n");
			return fails + 1;
		}

		level.DebugSuppressSectionReassert = suppress;
		list.DebugSetPos(deathPos);
		list.RevertToCheckpoint();
		level.DebugSuppressSectionReassert = false;

		string postSection = level.DebugSection;
		string postSong = SongName();
		bool ok = postSection == expected.ToString();
		if (!ok)
		{
			fails++;
		}
		sb.Append(ok ? "  ok   " : "  FAIL ").Append('[').Append(label).Append("] death at pos ")
			.Append(deathPos).Append(": ").Append(preSection).Append('/').Append(preSong)
			.Append(" -> ").Append(postSection).Append('/').Append(postSong)
			.Append(" (expected ").Append(expected).Append(")\n");
		return fails;
	}

	private static string SongName()
	{
		int song = ServiceHelper.Get<ISoundManagerService>().SoundManager.NetCurrentSong;
		return song < 0 ? "none" : ((Songs)song).ToString();
	}

	private static string Verdict(StringBuilder sb, int fails)
	{
		sb.Append("[bosstrain] ").Append(fails == 0 ? "PASS" : "FAIL")
			.Append(" (").Append(fails).Append(" failure(s))");
		return sb.ToString();
	}
}
