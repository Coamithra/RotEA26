using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class InsaneBossI : GameScene
{
	// The Boss Train is the one level that walks through all three of the game's SECTIONS in a
	// single run -- space (Level 1's bosses), Mars (Level 2's), the alien base (Level 3's) -- and
	// each transition mutates state that lives OUTSIDE the event list: the track, the backdrop and
	// the Mars floor. A checkpoint revert restores none of it, and GameEventList.RevertToCheckpoint
	// walks back to the nearest checkpoint at or before the death, which is not necessarily in the
	// section you died in. Card 4a3b22b7: the alien-base transition sits at script index 33 while
	// the next checkpoint is 36, so dying in that ~10s window rewound the script to the SPIDER BOSS
	// (checkpoint 24) while the backdrop and music stayed on the alien base -- Level 2's set-piece
	// replayed with Level 3's scenery and track, and arriving at "level 3's bosses" the second time
	// produced no music change at all, because nothing had ever put it back.
	//
	// So the section is state, not an edge: each Go* handler asks for a section, ApplySection is
	// idempotent, and every checkpoint declares which section it belongs to and re-asserts it on
	// entry. That covers the forward pass (a no-op -- you are already in that section) and the
	// revert (the actual fix) with one rule instead of a per-boundary patch.
	//
	// ONE CAVEAT, and it bounds where a future checkpoint may go: a checkpoint on a
	// DIFFICULTY-CONDITIONAL event does not re-assert on the tiers that filter it out.
	// GameEventList.progressList tests the difficulty range BEFORE it tests `checkpoints`, so a
	// skipped event fires no OnCheckPointReached -- while RevertToCheckpoint's walk-back tests only
	// `checkpoints` and can still land `pos` on it. The one such checkpoint here (the Hard+ gate in
	// front of the BrainBoss) is harmless because both its neighbours are AlienBase too, so the
	// section is right either way. Putting a CONDITIONAL checkpoint on a section BOUNDARY would be
	// the unsafe case, and it would fail silently on the low tiers only.
	internal enum Section
	{
		Space,
		Mars,
		AlienBase
	}

	private Section section;

	// Which section each checkpoint belongs to, built alongside the script so the two cannot drift
	// -- a checkpoint added without a section is a build-time omission the oracle reports, not a
	// silent fallthrough.
	private readonly Dictionary<GameEvent, Section> checkpointSections = new Dictionary<GameEvent, Section>();

	private Floor f;

	public InsaneBossI(Game game)
		: base(game, Levels.InsaneBossI)
	{
		f = new Floor(base.Game);
		base.OnFinished += InsaneBossI_OnFinished;
		eventList.OnCheckPointReached += InsaneBossI_OnCheckPointReached;
	}

	private void InsaneBossI_OnCheckPointReached(GameEventList sender, GameEvent checkpoint)
	{
		if (DebugSuppressSectionReassert)
		{
			return;
		}
		if (checkpointSections.TryGetValue(checkpoint, out var want))
		{
			ApplySection(want);
		}
	}

	// Idempotent by design: the forward pass hits every checkpoint too, and re-running a section's
	// setters there would restart the track (PlayMusic is not deduped C#-side) and rebuild the
	// backdrop layers for nothing. `section` is what was last APPLIED, so the guard is also what
	// makes the revert case fire -- coming back to Mars from the alien base is a real change.
	private void ApplySection(Section want)
	{
		if (section == want)
		{
			return;
		}
		section = want;
		switch (want)
		{
		case Section.Space:
			base.SoundManager.PlayMusic(Songs.Level1);
			Background.SetSpace();
			Collection.Remove((GameComponent)(object)f);
			spawnType = PlayerSpawnType.South;
			break;
		case Section.Mars:
			base.SoundManager.PlayMusic(Songs.Level2);
			Background.SetMars();
			Collection.Add((GameComponent)(object)f);
			spawnType = PlayerSpawnType.West;
			break;
		case Section.AlienBase:
			base.SoundManager.PlayMusic(Songs.Level3);
			Background.SetAlienBase();
			Collection.Remove((GameComponent)(object)f);
			spawnType = PlayerSpawnType.South;
			break;
		}
	}

	// Declare the section the checkpoint just added belongs to. Called immediately after
	// eventList.SetLastEventAsCheckPoint() so the two read as one statement at the call site.
	private void CheckPointSection(Section s)
	{
		int index = eventList.BenchCount - 1;
		checkpointSections[eventList.EventAt(index)] = s;
		checkpointSectionAt[index] = s;
	}

	// ---- Oracle surface (Compat/BossTrainTest.cs, card 4a3b22b7) -------------------------------
	// Two parallel records of what CheckPointSection and the Go* wiring declared, keyed by script
	// INDEX so the oracle can compare the declarations against a forward walk of the same script.
	// They are written by the same statements that do the real work, never restated -- a map the
	// test spelled out itself would agree with itself and prove nothing.
	private readonly Dictionary<int, Section> checkpointSectionAt = new Dictionary<int, Section>();

	private readonly Dictionary<int, Section> sectionChangeAt = new Dictionary<int, Section>();

	// Record that the event just added drives the level into `s` when it finishes. Called beside
	// each `OnFinished += Go*`, for the same reason CheckPointSection sits beside its checkpoint.
	private void SectionChangeHere(Section s)
	{
		sectionChangeAt[eventList.BenchCount - 1] = s;
	}

	internal IReadOnlyDictionary<int, Section> DebugCheckpointSections => checkpointSectionAt;

	internal IReadOnlyDictionary<int, Section> DebugSectionChanges => sectionChangeAt;

	internal string DebugSection => section.ToString();

	internal GameEventList DebugEventList => eventList;

	// The negative control's switch: with the re-assert suppressed, RevertToCheckpoint behaves
	// exactly as it did before this card, so the oracle can show the same input breaking the old
	// code rather than only ticking green against the new.
	internal bool DebugSuppressSectionReassert;

	internal void DebugApplySection(string name)
	{
		if (Enum.TryParse<Section>(name, out var s))
		{
			ApplySection(s);
			return;
		}
		// Reported, never swallowed -- the file-wide value-carrying-flag convention. A silent
		// no-op here would surface only as the oracle's generic "could not park the level".
		Console.WriteLine("[bosstrain] unknown section '" + name + "' (expected one of "
			+ string.Join(", ", Enum.GetNames(typeof(Section))) + ") -- ignored");
	}

	private void InsaneBossI_OnFinished(object sender, FinishedArgs args)
	{
		Collection.Remove((GameComponent)(object)f);
	}

	public override void Initialize()
	{
		base.Initialize();
		setPresence((GamerPresenceMode)34);
		switch (Settings.GetInstance().CurrentDifficulty)
		{
		case Settings.DifficultyLevel.Hard:
			score.Lives = 5;
			break;
		case Settings.DifficultyLevel.Very_Hard:
			score.Lives = 5;
			break;
		case Settings.DifficultyLevel.Inzane:
			score.Lives = 1;
			break;
		}
		// Force the opening section rather than ApplySection(Space): the level scene is a re-added
		// singleton, so `section` still holds whatever the LAST play ended on and the idempotence
		// guard would skip the setters on a replay. Everything after this goes through
		// ApplySection.
		section = Section.Space;
		spawnType = PlayerSpawnType.South;
		Background.SetSpace();
		// Not in the pre-card code, and a no-op on a fresh entry -- but without it "section ==
		// Space" would not actually imply "no floor" after a replay, and the whole point of the
		// field is that it names the state that IS in force.
		Collection.Remove((GameComponent)(object)f);
		base.SoundManager.PlayMusic(Songs.Level1);
		Settings.GetInstance().LockDifficulty();
	}

	protected override void PreloadGraphicalContent()
	{
		base.PreloadGraphicalContent();
		ContentManager contentManager = ServiceHelper.Get<IContentManagerService>().ContentManager;
		contentManager.Load<Texture2D>("GFX/Sprites/andromeda");
		contentManager.Load<Texture2D>("GFX/Sprites/blooddrop");
		contentManager.Load<Texture2D>("GFX/Sprites/braingoo");
		contentManager.Load<Texture2D>("GFX/Sprites/earth");
		contentManager.Load<Texture2D>("GFX/Sprites/eye_idle");
		contentManager.Load<Texture2D>("GFX/Sprites/eye_attract");
		contentManager.Load<Texture2D>("GFX/Sprites/lazermiddle");
		contentManager.Load<Texture2D>("GFX/Sprites/ufosheet");
		contentManager.Load<Texture2D>("GFX/Sprites/mediumship");
		contentManager.Load<Texture2D>("GFX/Sprites/mothershipA");
		contentManager.Load<Texture2D>("GFX/Sprites/mothershipB");
		contentManager.Load<Texture2D>("GFX/Sprites/smallship");
		contentManager.Load<Texture2D>("GFX/Sprites/blooddrop_green");
		contentManager.Load<Texture2D>("GFX/Sprites/spider_sheet2");
		contentManager.Load<Texture2D>("GFX/Sprites/spiderdebris1");
		contentManager.Load<Texture2D>("GFX/Sprites/spiderdebris2");
		contentManager.Load<Texture2D>("GFX/Sprites/spiderdebris3");
		contentManager.Load<Texture2D>("GFX/Sprites/spiderjump");
		contentManager.Load<Texture2D>("GFX/Sprites/ufometpootjes");
		contentManager.Load<Texture2D>("GFX/Sprites/wing1");
		contentManager.Load<Texture2D>("GFX/Sprites/shadow");
		contentManager.Load<Texture2D>("GFX/Sprites/brainbosshd");
		contentManager.Load<Texture2D>("GFX/Sprites/brainbossaura");
		contentManager.Load<Texture2D>("GFX/alienboss/alienboss");
		contentManager.Load<Texture2D>("GFX/Sprites/deathstarsheet2");
		contentManager.Load<Texture2D>("GFX/Sprites/explosionpurple");
		contentManager.Load<Texture2D>("GFX/Sprites/faceofdeathspritesheet");
		contentManager.Load<Texture2D>("GFX/Sprites/plasmaball2");
		contentManager.Load<Texture2D>("GFX/Spider/spiderfly");
		contentManager.Load<Texture2D>("GFX/Spider/spiderjump");
		contentManager.Load<Texture2D>("GFX/Spider/spiderland");
		contentManager.Load<Texture2D>("GFX/Spider/spiderstand");
	}

	protected override void PopulateEventList()
	{
		WaitEvent waitEvent = new WaitEvent(base.Game, 0.1f);
		eventList.AddEvent(waitEvent);
		eventList.AddHalt();
		waitEvent.OnFinished += GoSpace;
		SectionChangeHere(Section.Space);
		MessageEvent messageEvent = new MessageEvent(base.Game, "Warning!", SoundManager.Texts.Warning, 2.5f);
		messageEvent.SetupAsWarning(4.712389f);
		eventList.AddEvent(messageEvent, halting: true);
		eventList.AddHalt();
		BossSpawner bossSpawner = new BossSpawner(base.Game);
		BonusSpawner bonusSpawner = new BonusSpawner(base.Game, 0f, 0.05f, randomly: false);
		eventList.AddEvent(bonusSpawner, halting: false);
		bossSpawner.LinkWith(bonusSpawner);
		UfoSpawner ufoSpawner = new UfoSpawner(base.Game, 0f, 2f, big: false);
		eventList.AddEvent(ufoSpawner, halting: false);
		bossSpawner.LinkWith(ufoSpawner);
		ufoSpawner = new UfoSpawner(base.Game, 0f, 0.33f, big: true);
		eventList.AddEvent(ufoSpawner, halting: false);
		bossSpawner.LinkWith(ufoSpawner);
		eventList.AddEvent(bossSpawner);
		eventList.AddHalt();
		Wait(5f);
		messageEvent = new MessageEvent(base.Game, "Warning!", SoundManager.Texts.Warning, 3.2f);
		messageEvent.SetupAsWarning(4.712389f);
		eventList.AddEvent(messageEvent, halting: true);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		CheckPointSection(Section.Space);
		JunkBossSpawner junkBossSpawner = new JunkBossSpawner(base.Game);
		ufoSpawner = new UfoSpawner(base.Game, 0f, 0.12f, big: false);
		eventList.AddEvent(ufoSpawner, halting: false);
		junkBossSpawner.LinkWith(ufoSpawner);
		bonusSpawner = new BonusSpawner(base.Game, 0f, 0.046f, randomly: true);
		eventList.AddEvent(bonusSpawner, halting: false);
		junkBossSpawner.LinkWith(bonusSpawner);
		ufoSpawner = new UfoSpawner(base.Game, 0f, 0.053f, big: true);
		eventList.AddEvent(ufoSpawner, halting: false);
		junkBossSpawner.LinkWith(ufoSpawner);
		eventList.AddEvent(junkBossSpawner, halting: true);
		eventList.AddHalt();
		waitEvent = Wait(5f);
		waitEvent.OnFinished += GoMars;
		SectionChangeHere(Section.Mars);
		Wait(3f);
		messageEvent = new MessageEvent(base.Game, "Warning!", SoundManager.Texts.Warning);
		messageEvent.SetupAsWarning((float)Math.PI / 8f);
		eventList.AddEvent(messageEvent, halting: true);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		CheckPointSection(Section.Mars);
		MarsBossSpawner marsBossSpawner = new MarsBossSpawner(base.Game);
		Wait(3f);
		StationarySpawner stationarySpawner = new StationarySpawner(base.Game, 560f, 0f, 0.8f);
		stationarySpawner.SetChances(0f, 0f, 0f, 1f);
		marsBossSpawner.LinkWith(stationarySpawner);
		eventList.AddEvent(stationarySpawner, halting: false);
		Wait(5f);
		messageEvent = new MessageEvent(base.Game, "Warning!", SoundManager.Texts.Warning);
		messageEvent.SetupAsWarning(3.7699115f);
		eventList.AddEvent(messageEvent, halting: false);
		Wait(3f);
		bonusSpawner = new BonusSpawner(base.Game, 0f, 0.2f, randomly: true);
		bonusSpawner.SetMars();
		marsBossSpawner.LinkWith(bonusSpawner);
		eventList.AddEvent(bonusSpawner, halting: false);
		eventList.AddEvent(marsBossSpawner, halting: true);
		eventList.AddHalt();
		Wait(6.5f);
		Wait(5f);
		messageEvent = new MessageEvent(base.Game, "Danger!", SoundManager.Texts.Danger);
		eventList.AddEvent(messageEvent);
		messageEvent.SetupAsWarning(0f);
		eventList.SetLastEventAsCheckPoint();
		CheckPointSection(Section.Mars);
		waitEvent = Wait(4f);
		waitEvent.OnFinished += halt;
		SpiderBossEvent spiderBossEvent = new SpiderBossEvent(base.Game);
		eventList.AddEvent(spiderBossEvent, halting: false);
		Wait(8f);
		ufoSpawner = new UfoSpawner(base.Game, 0f, 2f, big: false);
		eventList.AddEvent(ufoSpawner, halting: false);
		spiderBossEvent.LinkWith(ufoSpawner);
		ufoSpawner = new UfoSpawner(base.Game, 0f, 0.2f, big: true);
		eventList.AddEvent(ufoSpawner, halting: false);
		spiderBossEvent.LinkWith(ufoSpawner);
		bonusSpawner = new BonusSpawner(base.Game, 0f, 0.08f, randomly: false);
		eventList.AddEvent(bonusSpawner, halting: false);
		spiderBossEvent.LinkWith(bonusSpawner);
		ufoSpawner = new UfoSpawner(base.Game, 0f, 0.15f, big: true);
		eventList.AddEvent(ufoSpawner, halting: true);
		ufoSpawner.DoNotScale();
		spiderBossEvent.LinkWith(ufoSpawner);
		eventList.AddHalt();
		Wait(2f);
		waitEvent = Wait(5f);
		waitEvent.OnFinished += GoAlienBase;
		SectionChangeHere(Section.AlienBase);
		Wait(5f);
		StarMineSpawner starMineSpawner = new StarMineSpawner(base.Game, 5f, 0.7f);
		eventList.AddEvent(starMineSpawner, halting: false);
		messageEvent = new MessageEvent(base.Game, "Danger!", SoundManager.Texts.Danger);
		messageEvent.SetupAsWarning(-(float)Math.PI / 2f);
		eventList.AddEvent(messageEvent, halting: false);
		eventList.SetLastEventAsCheckPoint();
		CheckPointSection(Section.AlienBase);
		Wait(5f);
		SkullSpawner skullSpawner = new SkullSpawner(base.Game, 0f, 0.1f, maze: false, bonusonly: true);
		eventList.AddEvent(skullSpawner, halting: false);
		starMineSpawner = new StarMineSpawner(base.Game, 0f, 0.75f);
		eventList.AddEvent(starMineSpawner, halting: false);
		junkBossSpawner = new JunkBossSpawner(base.Game);
		junkBossSpawner.SetBase();
		eventList.AddEvent(junkBossSpawner);
		eventList.AddHalt();
		junkBossSpawner.LinkWith(skullSpawner);
		junkBossSpawner.LinkWith(starMineSpawner);
		Wait(5f);
		messageEvent = new MessageEvent(base.Game, "Danger!", SoundManager.Texts.Danger);
		messageEvent.SetupAsWarning(-(float)Math.PI / 2f);
		eventList.AddEvent(messageEvent, halting: false);
		eventList.SetLastEventAsCheckPoint();
		CheckPointSection(Section.AlienBase);
		Wait(3f);
		FakeBossSpawner fakeBossSpawner = new FakeBossSpawner(base.Game);
		eventList.AddEvent(fakeBossSpawner);
		eventList.AddHalt();
		Wait(5f);
		messageEvent = new MessageEvent(base.Game, "Warning!", SoundManager.Texts.Warning);
		messageEvent.SetupAsWarning(-(float)Math.PI / 2f);
		eventList.AddEvent(messageEvent, halting: false);
		eventList.MakeConditional(messageEvent, Settings.DifficultyLevel.Hard, Settings.DifficultyLevel.Inzane);
		eventList.SetLastEventAsCheckPoint();
		CheckPointSection(Section.AlienBase);
		waitEvent = Wait(5f);
		eventList.MakeConditional(waitEvent, Settings.DifficultyLevel.Hard, Settings.DifficultyLevel.Inzane);
		BrainBossSpawner brainBossSpawner = new BrainBossSpawner(base.Game, challenge: true);
		eventList.AddEvent(brainBossSpawner);
		eventList.MakeConditional(brainBossSpawner, Settings.DifficultyLevel.Hard, Settings.DifficultyLevel.Inzane);
		eventList.AddHalt();
		waitEvent = new WaitEvent(base.Game, 0.1f);
		eventList.AddEvent(waitEvent);
		eventList.AddHalt();
		waitEvent.OnFinished += Victory;
	}

	// Online co-op (card ca4fd94f): this is the one level whose script swaps the whole backdrop
	// mid-run, so a join peer -- whose event list never runs -- gets the swap off the wire. The
	// base call switches the backdrop; this mirrors the rest of the matching Go* handler.
	//
	// Two things the handlers do are deliberately NOT mirrored. PlayMusic already replicates as
	// its own EvMusic beat, so re-firing it here would fight that. Collection.Purge<Ball> is
	// host-authoritative: the host's purge broadcasts an EvDeath per removal and the client's
	// puppets die from those -- purging locally would strand their ids.
	//
	// spawnType is not mirrored either, so a client respawning inside the Mars section enters
	// from the south rather than the west. Local and cosmetic (it only picks the entry point in
	// SpawnPlayer/SpawnAllPlayers, and the ship's real position replicates), but it is a known
	// difference rather than a fix.
	internal override void NetApplySceneChange(EvilAliensWeb.Compat.Net.NetBackgroundOp op)
	{
		base.NetApplySceneChange(op);
		if (op == EvilAliensWeb.Compat.Net.NetBackgroundOp.SetSceneMars)
		{
			Collection.Add((GameComponent)(object)f);
		}
		else
		{
			Collection.Remove((GameComponent)(object)f);
		}
	}

	// The Floor mirror above, as state -- so eaNetBgTest actually covers it and a two-window
	// eaNetBg() diff can see it. It is read from the live collection rather than a bool this class
	// keeps, because the thing worth checking is that the FLOOR is there, not that we remember
	// adding it (this scene owns the only Floor in play).
	protected override string NetSceneChangeState()
	{
		// Membership alone is not enough, for the reason ComponentBin.TryAdd spells out: a Remove
		// is QUEUED to the death list and the component is still in the collection until the next
		// flush, so "is the floor there" has to mean live NEXT tick. Without this the self-test's
		// own wipe reads as not having happened and the floor leg passes vacuously.
		bool live = Collection.ContainsType<Floor>() && !Collection.DEBUGdeathlistcontains((GameComponent)(object)f);
		return "floor=" + (live ? "1" : "0");
	}

	internal override void NetSceneChangeTestWipe()
	{
		// A fresh joiner ran its own Initialize, which does not add the floor -- only GoMars does.
		Collection.Remove((GameComponent)(object)f);
	}

	private void halt(GameEvent sender)
	{
		Background.SetSpeed(new Vector2(-0.2f, 0f) / 16.666666f);
	}

	private void Victory(GameEvent sender)
	{
		Victory();
	}

	private void GoAlienBase(GameEvent sender)
	{
		ApplySection(Section.AlienBase);
	}

	private void GoSpace(GameEvent sender)
	{
		// The `backgroundchanged` guard this replaces was the same idea in miniature -- "only put
		// space back if something else moved us off it". ApplySection's own guard says that for
		// every section, so on the opening pass (already Space, set by Initialize) this is a no-op
		// exactly as before, and it stays correct if a revert ever lands here from further on.
		ApplySection(Section.Space);
	}

	private WaitEvent Wait(float seconds)
	{
		WaitEvent waitEvent = new WaitEvent(base.Game, seconds);
		eventList.AddEvent(waitEvent);
		eventList.AddHalt();
		return waitEvent;
	}

	private void GoMars(GameEvent sender)
	{
		ApplySection(Section.Mars);
		// NOT part of ApplySection: this clears the Level-1 boss's leftover Balls as the script
		// crosses the boundary ONCE. It is host-authoritative (see NetApplySceneChange above), and
		// a checkpoint re-assert has no Balls to clear anyway -- there are none in the Mars or
		// alien-base sections.
		Collection.Purge<Ball>();
	}
}
