using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class OwnLevel : GameScene
{
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
		contentManager.Load<Texture2D>("GFX/Sprites/explosionspritepurple");
		contentManager.Load<Texture2D>("GFX/Sprites/faceofdeathspritesheet");
		contentManager.Load<Texture2D>("GFX/Base/black line lalalal");
		contentManager.Load<Texture2D>("GFX/Base/756-v1");
		contentManager.Load<Texture2D>("GFX/Base/756");
		contentManager.Load<Texture2D>("GFX/Base/756-v5");
		contentManager.Load<Texture2D>("GFX/Base/756-v3");
		contentManager.Load<Texture2D>("GFX/Base/756-v4");
		contentManager.Load<Texture2D>("GFX/Base/756-v6");
		contentManager.Load<Texture2D>("GFX/Base/756-v8");
	}

	protected override void PopulateEventList()
	{
		Wait(1f);
		MessageEvent messageEvent = new MessageEvent(base.Game, "Get ready!", SoundManager.Texts.GetReady);
		eventList.AddEvent(messageEvent, halting: false);
		messageEvent.OnFinished += setspeed;
		Wait(2f);
		SkullSpawner gameEvent = new SkullSpawner(base.Game, 0f, 2f, maze: true, bonusonly: false);
		eventList.AddEvent(gameEvent, halting: false);
		eventList.SetLastEventAsCheckPoint();
		StarMineSpawner starMineSpawner = new StarMineSpawner(base.Game, 0f, 0.1f);
		eventList.AddEvent(starMineSpawner, halting: false);
		eventList.MakeConditional(starMineSpawner, Settings.DifficultyLevel.Very_Hard, Settings.DifficultyLevel.Inzane);
		Walls gameEvent2 = new Walls(base.Game, 2);
		eventList.AddEvent(gameEvent2, halting: true);
		eventList.AddHalt();
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
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		Background.SetSpeed(new Vector2(0f, 4.3f * Settings.GetInstance().GetDifficultyValue(Settings.GetInstance().CurrentDifficulty)) / 16.666666f);
	}

	private void victory(GameEvent sender)
	{
		Victory();
	}
}
