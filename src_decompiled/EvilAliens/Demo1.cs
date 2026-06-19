using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class Demo1 : GameScene
{
	private HelpText text;

	public Demo1(Game game)
		: base(game, Levels.Demo1)
	{
		AllowAIFriends = false;
		text = new HelpText(((GameComponent)this).Game);
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
		Background.SetSpace();
		base.SoundManager.StopMusic();
		base.Initialize();
		Settings.GetInstance().LockDifficulty(Settings.DifficultyLevel.Hard);
		base.spawnPlayerNormally = true;
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
		Collection.Add((GameComponent)(object)text);
	}

	protected override void PreloadGraphicalContent()
	{
		base.PreloadGraphicalContent();
		ContentManager contentManager = ServiceHelper.Get<IContentManagerService>().ContentManager;
		contentManager.Load<Texture2D>("GFX/Sprites/andromeda");
		contentManager.Load<Texture2D>("GFX/Sprites/Asteroid2");
		contentManager.Load<Texture2D>("GFX/Sprites/blooddrop");
		contentManager.Load<Texture2D>("GFX/Sprites/braingoo");
		contentManager.Load<Texture2D>("GFX/Sprites/brainlargetransglow");
		contentManager.Load<Texture2D>("GFX/Sprites/earth");
		contentManager.Load<Texture2D>("GFX/Sprites/eye");
		contentManager.Load<Texture2D>("GFX/Sprites/lazerbottom");
		contentManager.Load<Texture2D>("GFX/Sprites/lazermiddle");
		contentManager.Load<Texture2D>("GFX/Sprites/lazertop");
		contentManager.Load<Texture2D>("GFX/Sprites/ufosheet");
		contentManager.Load<Texture2D>("GFX/Sprites/mediumship");
		contentManager.Load<Texture2D>("GFX/Sprites/mothershipA");
		contentManager.Load<Texture2D>("GFX/Sprites/mothershipB");
		contentManager.Load<Texture2D>("GFX/Sprites/smallship");
	}

	protected override void PopulateEventList()
	{
		WaitEvent waitEvent = new WaitEvent(((GameComponent)this).Game, 0.01f);
		eventList.AddEvent(waitEvent, halting: false);
		waitEvent.OnFinished += waitevent_OnFinished3;
		UfoFormationSpawner gameEvent = new UfoFormationSpawner(((GameComponent)this).Game, 6);
		eventList.AddEvent(gameEvent, halting: true);
		eventList.SetLastEventAsCheckPoint();
		eventList.AddHalt();
		gameEvent = new UfoFormationSpawner(((GameComponent)this).Game, 1);
		eventList.AddEvent(gameEvent, halting: false);
		BonusSpawner gameEvent2 = new BonusSpawner(((GameComponent)this).Game, 20f, 0.1f, randomly: false);
		eventList.AddEvent(gameEvent2, halting: false);
		UfoSpawner gameEvent3 = new UfoSpawner(((GameComponent)this).Game, 20f, 1f, big: false);
		eventList.AddEvent(gameEvent3, halting: true);
		eventList.AddHalt();
		gameEvent3 = new UfoSpawner(((GameComponent)this).Game, 5f, 0.1f, big: false);
		eventList.AddEvent(gameEvent3, halting: true);
		eventList.AddHalt();
		gameEvent = new UfoFormationSpawner(((GameComponent)this).Game, 12);
		gameEvent2 = new BonusSpawner(((GameComponent)this).Game, 10f, 0.2f, randomly: false);
		eventList.AddEvent(gameEvent2, halting: false);
		eventList.SetLastEventAsCheckPoint();
		gameEvent.LinkWith(gameEvent2);
		gameEvent3 = new UfoSpawner(((GameComponent)this).Game, 0f, 1.33f, big: false);
		gameEvent3.SetupThreeDirectional();
		eventList.AddEvent(gameEvent3, halting: false);
		gameEvent.LinkWith(gameEvent3);
		eventList.AddEvent(gameEvent, halting: true);
		eventList.AddHalt();
		gameEvent3 = new UfoSpawner(((GameComponent)this).Game, 5f, 1.5f, big: false);
		eventList.AddEvent(gameEvent3, halting: true);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		gameEvent3.OnFinished += spawner_OnFinished;
		AsteroidSpawner gameEvent4 = new AsteroidSpawner(((GameComponent)this).Game, 42f, 4f, startWithBig: true);
		eventList.AddEvent(gameEvent4, halting: true);
		gameEvent2 = new BonusSpawner(((GameComponent)this).Game, 10f, 0.3f, randomly: false);
		eventList.AddEvent(gameEvent2, halting: false);
		gameEvent3 = new UfoSpawner(((GameComponent)this).Game, 10f, 5f, big: false);
		eventList.AddEvent(gameEvent3, halting: true);
		eventList.AddHalt();
		waitEvent = new WaitEvent(((GameComponent)this).Game, 2.5f);
		eventList.AddEvent(waitEvent, halting: true);
		eventList.AddHalt();
		waitEvent.OnFinished += waitevent_OnFinished;
		BrainSpawner brainSpawner = new BrainSpawner(((GameComponent)this).Game, 15f, 0.15f, wrapping: true);
		eventList.AddEvent(brainSpawner, halting: true);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		brainSpawner.OnFinished += message_OnFinished;
		brainSpawner = new BrainSpawner(((GameComponent)this).Game, 15f, 0.15f, wrapping: true);
		eventList.AddEvent(brainSpawner, halting: true);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		gameEvent2 = new BonusSpawner(((GameComponent)this).Game, 40f, 0.15f, randomly: false);
		eventList.AddEvent(gameEvent2, halting: false);
		gameEvent3 = new UfoSpawner(((GameComponent)this).Game, 40f, 1.3f, big: false);
		eventList.AddEvent(gameEvent3, halting: true);
		brainSpawner = new BrainSpawner(((GameComponent)this).Game, 30f, 0.06f, wrapping: true);
		eventList.AddEvent(brainSpawner, halting: true);
		eventList.AddHalt();
		gameEvent3 = new UfoSpawner(((GameComponent)this).Game, 10f, 1.5f, big: false);
		eventList.AddEvent(gameEvent3, halting: true);
		eventList.AddHalt();
		gameEvent3 = new UfoSpawner(((GameComponent)this).Game, 10f, 3f, big: false);
		eventList.AddEvent(gameEvent3, halting: false);
		eventList.SetLastEventAsCheckPoint();
		gameEvent2 = new BonusSpawner(((GameComponent)this).Game, 10f, 0.3f, randomly: false);
		eventList.AddEvent(gameEvent2, halting: false);
		gameEvent3 = new UfoSpawner(((GameComponent)this).Game, 10f, 0.33f, big: true);
		eventList.AddEvent(gameEvent3, halting: true);
		eventList.AddHalt();
		gameEvent2 = new BonusSpawner(((GameComponent)this).Game, 24f, 0.1f, randomly: false);
		eventList.AddEvent(gameEvent2, halting: false);
		gameEvent3 = new UfoSpawner(((GameComponent)this).Game, 24f, 3f, big: false);
		eventList.AddEvent(gameEvent3, halting: true);
		gameEvent3 = new UfoSpawner(((GameComponent)this).Game, 24f, 0.5f, big: true);
		eventList.AddEvent(gameEvent3, halting: true);
		eventList.AddHalt();
		waitEvent = new WaitEvent(((GameComponent)this).Game, 0.1f);
		waitEvent.OnFinished += message_OnFinished2;
		eventList.AddEvent(waitEvent, halting: true);
		gameEvent3 = new UfoSpawner(((GameComponent)this).Game, 6f, 2f, big: false);
		eventList.AddEvent(gameEvent3, halting: true);
		eventList.SetLastEventAsCheckPoint();
		gameEvent3 = new UfoSpawner(((GameComponent)this).Game, 6f, 0.4f, big: true);
		eventList.AddEvent(gameEvent3, halting: true);
		eventList.AddHalt();
		BossSpawner bossSpawner = new BossSpawner(((GameComponent)this).Game);
		gameEvent2 = new BonusSpawner(((GameComponent)this).Game, 0f, 0.05f, randomly: false);
		eventList.AddEvent(gameEvent2, halting: false);
		bossSpawner.LinkWith(gameEvent2);
		gameEvent3 = new UfoSpawner(((GameComponent)this).Game, 0f, 2f, big: false);
		eventList.AddEvent(gameEvent3, halting: false);
		bossSpawner.LinkWith(gameEvent3);
		gameEvent3 = new UfoSpawner(((GameComponent)this).Game, 0f, 0.33f, big: true);
		eventList.AddEvent(gameEvent3, halting: false);
		bossSpawner.LinkWith(gameEvent3);
		eventList.AddEvent(bossSpawner, halting: true);
		eventList.AddHalt();
		gameEvent3 = new UfoSpawner(((GameComponent)this).Game, 35f, 4f, big: false);
		gameEvent3.SetupThreeDirectional();
		eventList.AddEvent(gameEvent3, halting: true);
		eventList.SetLastEventAsCheckPoint();
		gameEvent2 = new BonusSpawner(((GameComponent)this).Game, 35f, 0.125f, randomly: false);
		eventList.AddEvent(gameEvent2, halting: false);
		gameEvent3 = new UfoSpawner(((GameComponent)this).Game, 35f, 0.66f, big: true);
		eventList.AddEvent(gameEvent3, halting: true);
		eventList.AddHalt();
		gameEvent3 = new UfoSpawner(((GameComponent)this).Game, 10f, 2.25f, big: false);
		eventList.AddEvent(gameEvent3, halting: true);
		eventList.AddHalt();
		waitEvent = new WaitEvent(((GameComponent)this).Game, 5f);
		eventList.AddEvent(waitEvent, halting: true);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		waitEvent.OnFinished += waitevent_OnFinished2;
		gameEvent2 = new BonusSpawner(((GameComponent)this).Game, 10f, 0.5f, randomly: false);
		eventList.AddEvent(gameEvent2, halting: false);
		JunkBossSpawner junkBossSpawner = new JunkBossSpawner(((GameComponent)this).Game);
		gameEvent3 = new UfoSpawner(((GameComponent)this).Game, 0f, 0.12f, big: false);
		eventList.AddEvent(gameEvent3, halting: false);
		junkBossSpawner.LinkWith(gameEvent3);
		gameEvent2 = new BonusSpawner(((GameComponent)this).Game, 0f, 0.046f, randomly: true);
		eventList.AddEvent(gameEvent2, halting: false);
		junkBossSpawner.LinkWith(gameEvent2);
		gameEvent3 = new UfoSpawner(((GameComponent)this).Game, 0f, 0.053f, big: true);
		eventList.AddEvent(gameEvent3, halting: false);
		junkBossSpawner.LinkWith(gameEvent3);
		eventList.AddEvent(junkBossSpawner, halting: true);
		eventList.AddHalt();
		UnlockEvent gameEvent5 = new UnlockEvent(((GameComponent)this).Game, "Mechanical Friends", Unlockables.Items.Friends, AnimatedMessage.UnlockType.cheat, level);
		eventList.AddEvent(gameEvent5, halting: true);
		eventList.AddHalt();
		waitEvent = new WaitEvent(((GameComponent)this).Game, 1f);
		eventList.AddEvent(waitEvent, halting: true);
		eventList.AddHalt();
		waitEvent.OnFinished += jbspawner_OnFinished;
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

	private void message_OnFinished(GameEvent sender)
	{
		Background.QueueAndromeda();
	}

	private void message_OnFinished2(GameEvent sender)
	{
		Background.QueueSmallEarth();
	}

	private void waitevent_OnFinished(GameEvent sender)
	{
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		Background.SetSpeed(new Vector2(0f, 0.2f) / 16.666666f);
	}

	private void waitevent_OnFinished2(GameEvent sender)
	{
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		Background.SetSpeed(new Vector2(0f, 7.6f) / 16.666666f);
	}

	private void waitevent_OnFinished3(GameEvent sender)
	{
		Background.QueueEarth();
	}

	private void spawner_OnFinished(GameEvent sender)
	{
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		Background.SetSpeed(new Vector2(0.25f, 0.6f) / 16.666666f);
	}

	private void demo_OnFinished(GameEvent sender)
	{
		SpawnAllPlayers(invulnerable: true);
		base.spawnPlayerNormally = true;
	}

	private void jbspawner_OnFinished(GameEvent sender)
	{
		Victory();
	}
}
