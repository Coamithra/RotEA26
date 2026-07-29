using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class OwnLevel : GameScene
{
	// How long ?nowalls holds the level open. The walls are this level's only halting event, so
	// dropping them would otherwise run straight to victory in a tenth of a second. Long enough to
	// outlast any eaAiBench soak: the metric that rig exists to produce is a RATE (turn deg/s
	// averaged over ticks), so the run never has to FINISH, it has to LAST.
	private const float NoWallsHoldSeconds = 600f;

	public OwnLevel(Game game)
		: base(game, Levels.OwnLevel)
	{
	}

	public override void Initialize()
	{
		setPresence((GamerPresenceMode)14);
		base.SoundManager.PlayMusic(Songs.Level3);
		Background.SetAlienBase();
		base.Initialize();
		Settings.GetInstance().LockDifficulty();
		base.spawnPlayerNormally = true;
	}

	protected override void PreloadGraphicalContent()
	{
		base.PreloadGraphicalContent();
		ContentManager contentManager = ServiceHelper.Get<IContentManagerService>().ContentManager;
		contentManager.Load<Texture2D>("GFX/Sprites/deathstarsheet2");
		contentManager.Load<Texture2D>("GFX/Sprites/explosionpurple");
		contentManager.Load<Texture2D>("GFX/Sprites/faceofdeathspritesheet");
		contentManager.Load<Texture2D>("GFX/Base/black_line_lalalal");
		contentManager.Load<Texture2D>("GFX/Base/756-v1");
		contentManager.Load<Texture2D>("GFX/Base/756");
		contentManager.Load<Texture2D>("GFX/Base/756-v5");
		contentManager.Load<Texture2D>("GFX/Base/756-v3");
		contentManager.Load<Texture2D>("GFX/Base/756-v4");
		contentManager.Load<Texture2D>("GFX/Base/756-v6");
		contentManager.Load<Texture2D>("GFX/Base/756-v8");
		// Warm the tower BasicEffect GL program on the loading screen — OwnLevel spawns Walls too. See
		// Level3.PreloadGraphicalContent for the full why (Trello 3e81fdcd); idempotent across the tower
		// scenes, so it only compiles once per session.
		SpriteBatch.WarmGeometry3D();
	}

	// DEBUG (card b174b00f): ?wallsonly drops this level's two spawners and keeps the walls;
	// ?nowalls does the opposite. Neither set = the shipped level, unchanged.
	//
	// They exist to ATTRIBUTE this level's AI heading churn, and they have DONE so -- the answer
	// is below, because a reader who stops at the setup leaves with the hypothesis that lost.
	//
	// The setup: OwnLevel measures 254-477 deg/s where Level 3's wall sections measure far less,
	// and that gap was read first as a wall-nav defect and then as NOT one. Both readings were
	// unsafe, because the Level-3 figure comes from Level3.PopulateWallsOnly (which by its own
	// comment spawns "nothing else") while OwnLevel's is the WHOLE level: Walls(2) running
	// concurrently with a continuous SkullSpawner and a Very_Hard+ StarMineSpawner. Walls-alone
	// against walls-plus-a-sustained-enemy-stream settles nothing either way. ?wallsonly reaches
	// OwnLevel's walls alone -- reusing Level 3's flag name rather than minting an OwnLevel one --
	// and ?nowalls is the control that keeps a quiet reading honest (both halves quiet would mean
	// the suppression broke the rig, not that the walls are innocent).
	//
	// THE RESULT (eahl, Very_Hard, N=6, no ?invuln): walls only 229 deg/s, spawners only 61, full
	// level 404, Level 3 walls only 29. So this grid alone churns ~7.9x Level 3's grid alone: the
	// churn IS the walls, the enemy stream contributes about a seventh of it, and the two are
	// superadditive. Full numbers and the rig caveats: web/EvilAliensWeb/CLAUDE.md, the OwnLevel
	// row of the challenge-level completion matrix.
	protected override void PopulateEventList()
	{
		bool wallsOnly = EvilAliensWeb.Compat.DebugFlags.WallsOnly;
		bool noWalls = EvilAliensWeb.Compat.DebugFlags.NoWalls;
		if (wallsOnly && noWalls)
		{
			// The two are complements, so together they would suppress EVERYTHING: a silent,
			// clean-looking empty level reporting turn=0deg/s -- a bench run measuring nothing
			// while carrying a label, which is the failure this whole card is about. ?wallsonly
			// wins because it is the primary rig; ?nowalls is its control.
			System.Console.WriteLine("[debug] ?wallsonly and ?nowalls are complements and cannot both apply"
				+ " -- ignoring ?nowalls, running OwnLevel walls-only");
			noWalls = false;
		}

		Wait(1f);
		MessageEvent messageEvent = new MessageEvent(base.Game, "Get ready!", SoundManager.Texts.GetReady);
		eventList.AddEvent(messageEvent, halting: false);
		// setspeed runs in EVERY variant: it is the 4.3x wall-section scroll, i.e. the thing that
		// makes an OwnLevel figure comparable with a Level-3 one in the first place.
		messageEvent.OnFinished += setspeed;
		Wait(2f);
		if (!wallsOnly)
		{
			SkullSpawner gameEvent = new SkullSpawner(base.Game, 0f, 2f, maze: true, bonusonly: false);
			eventList.AddEvent(gameEvent, halting: false);
			eventList.SetLastEventAsCheckPoint();
			StarMineSpawner starMineSpawner = new StarMineSpawner(base.Game, 0f, 0.1f);
			eventList.AddEvent(starMineSpawner, halting: false);
			eventList.MakeConditional(starMineSpawner, Settings.DifficultyLevel.Very_Hard, Settings.DifficultyLevel.Inzane);
		}
		if (noWalls)
		{
			WaitEvent hold = new WaitEvent(base.Game, NoWallsHoldSeconds);
			eventList.AddEvent(hold, halting: true);
			eventList.AddHalt();
		}
		else
		{
			Walls gameEvent2 = new Walls(base.Game, 2);
			eventList.AddEvent(gameEvent2, halting: true);
			if (wallsOnly)
			{
				// The spawner that normally carries the checkpoint is gone, and a death on a
				// Lives = -1 challenge reverts to the last one -- with none set that is the top of
				// the level, so every death would replay the intro and re-measure the approach
				// rather than the wall.
				eventList.SetLastEventAsCheckPoint();
			}
			eventList.AddHalt();
		}
		WaitEvent waitEvent = new WaitEvent(base.Game, 0.1f);
		eventList.AddEvent(waitEvent, halting: true);
		eventList.AddHalt();
		waitEvent.OnFinished += victory;
	}

	private WaitEvent Wait(float time)
	{
		WaitEvent waitEvent = new WaitEvent(base.Game, time);
		eventList.AddEvent(waitEvent, halting: true);
		eventList.AddHalt();
		return waitEvent;
	}

	private void setspeed(GameEvent sender)
	{
		Background.SetSpeed(new Vector2(0f, 4.3f * Settings.GetInstance().GetDifficultyValue(Settings.GetInstance().CurrentDifficulty)) / 16.666666f);
	}

	private void victory(GameEvent sender)
	{
		Victory();
	}
}
