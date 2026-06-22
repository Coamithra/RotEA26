using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class Demo3 : GameScene
{
	private HelpText text;

	public Demo3(Game game)
		: base(game, Levels.Demo3)
	{
		AllowAIFriends = false;
		text = new HelpText(base.Game);
		isDemo = true;
	}

	public override void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		base.OnComponentRemoved(e);
		if (e.GameComponent == this)
		{
			Collection.Remove((GameComponent)(object)text);
		}
	}

	public override void Initialize()
	{
		setPresence((GamerPresenceMode)38);
		base.SoundManager.StopMusic();
		Background.SetAlienBase();
		base.Initialize();
		Settings.GetInstance().LockDifficulty(Settings.DifficultyLevel.Hard);
		float num = RandomHelper.RandomNextFloat(0f, 100f);
		if (num <= 20f)
		{
			oracle.AddPlayer(ControlDevice.AI);
			oracle.AddPlayer(ControlDevice.AI);
			oracle.AddPlayer(ControlDevice.AI);
		}
		else if (num <= 60f)
		{
			oracle.AddPlayer(ControlDevice.AI);
		}
		score.Lives = -1;
		base.spawnPlayerNormally = true;
		Collection.Add((GameComponent)(object)text);
	}

	protected override void PreloadGraphicalContent()
	{
		base.PreloadGraphicalContent();
		ContentManager contentManager = ServiceHelper.Get<IContentManagerService>().ContentManager;
		contentManager.Load<Texture2D>("GFX/Sprites/blooddrop");
		contentManager.Load<Texture2D>("GFX/Sprites/brainlargetransglow");
		contentManager.Load<Texture2D>("GFX/Sprites/cablesback");
		contentManager.Load<Texture2D>("GFX/Sprites/cablesfront");
		contentManager.Load<Texture2D>("GFX/alienboss/alienboss");
		contentManager.Load<Texture2D>("GFX/Sprites/deathstarsheet2");
		contentManager.Load<Texture2D>("GFX/Sprites/explosionpurple");
		contentManager.Load<Texture2D>("GFX/Sprites/explosionspritepurple");
		contentManager.Load<Texture2D>("GFX/Sprites/eye_idle");
		contentManager.Load<Texture2D>("GFX/Sprites/faceofdeathspritesheet");
		contentManager.Load<Texture2D>("GFX/Sprites/plasmaball2");
		contentManager.Load<Texture2D>("GFX/Sprites/braingoo");
		contentManager.Load<Texture2D>("GFX/Sprites/lazermiddle");
		contentManager.Load<Texture2D>("GFX/Sprites/ufosheet");
		contentManager.Load<Texture2D>("GFX/Sprites/mediumship");
		contentManager.Load<Texture2D>("GFX/Sprites/smallship");
		contentManager.Load<Texture2D>("GFX/Base/black line lalalal");
		contentManager.Load<Texture2D>("GFX/Base/756-v1");
		contentManager.Load<Texture2D>("GFX/Base/756");
		contentManager.Load<Texture2D>("GFX/Base/756-v5");
		contentManager.Load<Texture2D>("GFX/Base/756-v3");
		contentManager.Load<Texture2D>("GFX/Base/756-v4");
		contentManager.Load<Texture2D>("GFX/Base/756-v6");
		contentManager.Load<Texture2D>("GFX/Base/756-v8");
	}

	public override void Update(GameTime gameTime)
	{
		bool flag = false;
		flag |= base.InputHandler.Pressed(MyKeys.Enter) || base.InputHandler.Pressed(MyKeys.Esc);
		for (int i = 0; i < 4; i++)
		{
			flag |= base.InputHandler.PadPressed(PadKeys.Start, i);
			flag |= base.InputHandler.PadPressed(PadKeys.Back, i);
			flag |= base.InputHandler.PadPressed(PadKeys.A, i);
			flag |= base.InputHandler.PadPressed(PadKeys.B, i);
			flag |= base.InputHandler.PadPressed(PadKeys.LTRT, i);
		}
		if (flag)
		{
			Terminate(FinishedMode.exit);
		}
		else
		{
			base.Update(gameTime);
		}
	}

	protected override void PopulateEventList()
	{
		Wait(1f);
		Walls walls = new Walls(base.Game, 0);
		eventList.AddEvent(walls, halting: true);
		eventList.SetLastEventAsCheckPoint();
		SkullSpawner gameEvent = new SkullSpawner(base.Game, 0f, 1f, maze: true, bonusonly: false);
		eventList.AddEvent(gameEvent, halting: false);
		walls.LinkWith(gameEvent);
		BattleSkullEvent gameEvent2 = new BattleSkullEvent(base.Game, 0f, 0.5f);
		eventList.AddEvent(gameEvent2, halting: false);
		eventList.AddHalt();
		walls.LinkWith(gameEvent2);
		Wait(4f);
		gameEvent = new SkullSpawner(base.Game, 0f, 3f, maze: true, bonusonly: false);
		eventList.AddEvent(gameEvent, halting: false);
		eventList.SetLastEventAsCheckPoint();
		walls = new Walls(base.Game, 1);
		eventList.AddEvent(walls, halting: true);
		eventList.AddHalt();
		UnlockEvent gameEvent3 = new UnlockEvent(base.Game, "Mechanical Friends", Unlockables.Items.Friends, AnimatedMessage.UnlockType.cheat, level);
		eventList.AddEvent(gameEvent3, halting: true);
		eventList.AddHalt();
		WaitEvent waitEvent = new WaitEvent(base.Game, 0.1f);
		eventList.AddEvent(waitEvent, halting: true);
		eventList.AddHalt();
		waitEvent.OnFinished += Victory;
	}

	private void Victory(GameEvent sender)
	{
		Victory();
	}

	private void waitevent_OnFinished(GameEvent sender)
	{
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		Background.SetSpeed(new Vector2(0f, 0.2f) / 16.666666f);
	}

	private void skullspawner_OnFinished(GameEvent sender)
	{
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		Background.SetSpeed(new Vector2(0f, 4.5f) / 16.666666f);
	}

	private void Wait(float time)
	{
		WaitEvent gameEvent = new WaitEvent(base.Game, time);
		eventList.AddEvent(gameEvent, halting: true);
		eventList.AddHalt();
	}
}
