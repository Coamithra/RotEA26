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
	// They exist to ATTRIBUTE this level's AI heading churn. OwnLevel measures 254-477 deg/s
	// against Level 3's ~70, and that 4-7x gap was read as a wall-nav defect -- but the ~70 comes
	// from Level3.PopulateWallsOnly, which by its own comment spawns "nothing else", while the
	// 254-477 is the WHOLE level: Walls(2) running concurrently with a continuous SkullSpawner and
	// (Very_Hard+) a StarMineSpawner. So it compares walls-alone against walls-plus-a-sustained-
	// enemy-stream, and no amount of care with the wall grid can settle it. ?wallsonly here makes
	// the two rigs the same rig -- which is why it reuses Level 3's flag name rather than minting
	// an OwnLevel-specific one.
	//
	// ?nowalls is the POSITIVE CONTROL and is not optional: a quiet ?wallsonly reading on its own
	// cannot distinguish "the walls are innocent" from "suppressing events broke the rig". If the
	// walls really are innocent, ?nowalls stays high and ?wallsonly drops. If BOTH go quiet, the
	// rig is what changed and neither number means anything.
	protected override void PopulateEventList()
	{
		bool wallsOnly = EvilAliensWeb.Compat.DebugFlags.WallsOnly;
		bool noWalls = EvilAliensWeb.Compat.DebugFlags.NoWalls;

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
