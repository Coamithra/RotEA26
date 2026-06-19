using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class Demo2 : GameScene
{
	private HelpText text;

	private Floor floor;

	public Demo2(Game game)
		: base(game, Levels.Demo2)
	{
		base.OnFinished += Demo2_OnFinished;
		AllowAIFriends = false;
		floor = new Floor(base.Game);
		isDemo = true;
		spawnType = PlayerSpawnType.West;
		text = new HelpText(base.Game);
	}

	public override void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		base.OnComponentRemoved(e);
		if (e.GameComponent == this)
		{
			Collection.Remove((GameComponent)(object)text);
		}
	}

	private void Demo2_OnFinished(object sender, FinishedArgs args)
	{
		Collection.Remove((GameComponent)(object)floor);
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

	public override void Initialize()
	{
		setPresence((GamerPresenceMode)38);
		base.SoundManager.StopMusic();
		Collection.Add((GameComponent)(object)floor);
		Background.SetMars();
		base.Initialize();
		base.spawnPlayerNormally = true;
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
		Collection.Add((GameComponent)(object)text);
	}

	protected override void PreloadGraphicalContent()
	{
		base.PreloadGraphicalContent();
		ContentManager contentManager = ServiceHelper.Get<IContentManagerService>().ContentManager;
		contentManager.Load<Texture2D>("GFX/Sprites/braingoo");
		contentManager.Load<Texture2D>("GFX/Sprites/lazerbottom");
		contentManager.Load<Texture2D>("GFX/Sprites/lazermiddle");
		contentManager.Load<Texture2D>("GFX/Sprites/lazertop");
		contentManager.Load<Texture2D>("GFX/Sprites/ufosheet");
		contentManager.Load<Texture2D>("GFX/Sprites/blooddrop_green");
		contentManager.Load<Texture2D>("GFX/Sprites/spider_sheet2");
		contentManager.Load<Texture2D>("GFX/Sprites/spiderdebris1");
		contentManager.Load<Texture2D>("GFX/Sprites/spiderdebris2");
		contentManager.Load<Texture2D>("GFX/Sprites/spiderjump");
		contentManager.Load<Texture2D>("GFX/Sprites/ufometpootjes");
		contentManager.Load<Texture2D>("GFX/Sprites/wing1");
		contentManager.Load<Texture2D>("GFX/Sprites/shadow");
		contentManager.Load<Texture2D>("GFX/Sprites/mediumship");
		contentManager.Load<Texture2D>("GFX/Sprites/mothershipA");
		contentManager.Load<Texture2D>("GFX/Sprites/mothershipB");
		contentManager.Load<Texture2D>("GFX/Sprites/smallship");
		contentManager.Load<Texture2D>("GFX/Sprites/Smallship_landed");
		contentManager.Load<Texture2D>("GFX/Sprites/Mediumship_landed");
		contentManager.Load<Texture2D>("GFX/Sprites/Mothership_landed");
	}

	protected override void PopulateEventList()
	{
		StationaryWave(8f, 3f, 100f, 0f, 0f, 0f);
		Wait(2f);
		UfoWave(3f, 0f);
		Wait(2f);
		StationaryWave(4f, 2f, 100f, 0f, 0f, 0f);
		Wait(2f);
		UfoWave(4.2f, 0f);
		Wait(2f);
		StationaryWave(4f, 2f, 100f, 0f, 0f, 0f);
		Wait(2f);
		UfoWave(4.2f, 0.8f);
		Wait(2f);
		StationaryWave(4f, 2f, 100f, 0f, 0f, 0f);
		Wait(2f);
		UfoWave(4.2f, 1.8f);
		Wait(2f);
		StationaryWave(4f, 2f, 100f, 0f, 0f, 0f);
		Wait(2f);
		UfoWave(4.2f, 3.5f);
		Wait(6f);
		UfoSpawner ufoSpawner = new UfoSpawner(base.Game, 0f, 0.5f, big: false);
		ufoSpawner.SetupMars();
		eventList.AddEvent(ufoSpawner, halting: false);
		eventList.SetLastEventAsCheckPoint();
		WaitEvent waitEvent = new WaitEvent(base.Game, 0.1f);
		waitEvent.LinkWith(ufoSpawner);
		ufoSpawner = new UfoSpawner(base.Game, 0f, 0.5f, big: false);
		ufoSpawner.SetupMars();
		ufoSpawner.SetupFastEntry();
		waitEvent.LinkWith(ufoSpawner);
		eventList.AddEvent(ufoSpawner, halting: false);
		BonusSpawner bonusSpawner = new BonusSpawner(base.Game, 0f, 0.1f, randomly: true);
		bonusSpawner.SetMars();
		eventList.AddEvent(bonusSpawner, halting: false);
		waitEvent.LinkWith(bonusSpawner);
		StationaryWave(4f, 3f, 96f, 4f, 0f, 0f);
		Wait(4f);
		StationaryWave(1f, 3f, 0f, 1f, 0f, 0f);
		Wait(3f);
		StationaryWave(1f, 3f, 0f, 1f, 0f, 0f);
		WaitEvent waitEvent2 = new WaitEvent(base.Game, 3f);
		eventList.AddEvent(waitEvent2, halting: false);
		waitEvent2.OnFinished += spawnStationaryBoss;
		Wait(5f);
		StationaryWave(1f, 6f, 1f, 1f, 0f, 0f);
		Wait(4f);
		StationaryWave(1f, 3f, 0f, 1f, 0f, 0f);
		Wait(3f);
		StationaryWave(1f, 3f, 0f, 1f, 0f, 0f);
		waitEvent2 = new WaitEvent(base.Game, 3f);
		eventList.AddEvent(waitEvent2, halting: false);
		waitEvent2.OnFinished += spawnStationaryBoss;
		StationaryWave(1f, 3f, 0f, 1f, 0f, 0f);
		Wait(3f);
		StationaryWave(1f, 6f, 1f, 1f, 0f, 0f);
		eventList.AddEvent(waitEvent, halting: false);
		waitEvent2 = new WaitEvent(base.Game, 10f);
		eventList.AddEvent(waitEvent2, halting: true);
		waitEvent2.OnFinished += slowdown;
		eventList.AddHalt();
		ufoSpawner = new UfoSpawner(base.Game, 25f, 2.1f, big: false);
		ufoSpawner.SetupMarsWest();
		eventList.AddEvent(ufoSpawner, halting: false);
		eventList.SetLastEventAsCheckPoint();
		ufoSpawner = new UfoSpawner(base.Game, 25f, 0.15f, big: true);
		ufoSpawner.SetupMarsWest();
		eventList.AddEvent(ufoSpawner, halting: false);
		bonusSpawner = new BonusSpawner(base.Game, 25f, 0.2f, randomly: true);
		eventList.AddEvent(bonusSpawner, halting: false);
		Wait(5f);
		StationaryWave(20f, 0.5f, 0f, 0f, 0f, 1f);
		waitEvent2 = new WaitEvent(base.Game, 6f);
		eventList.AddEvent(waitEvent2, halting: true);
		waitEvent2.OnFinished += speedup;
		eventList.AddHalt();
		waitEvent2 = new WaitEvent(base.Game, 2f);
		eventList.AddEvent(waitEvent2, halting: true);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		MarsBossSpawner marsBossSpawner = new MarsBossSpawner(base.Game);
		Wait(3f);
		StationarySpawner stationarySpawner = new StationarySpawner(base.Game, 560f, 0f, 0.8f);
		stationarySpawner.SetChances(0f, 0f, 0f, 1f);
		marsBossSpawner.LinkWith(stationarySpawner);
		eventList.AddEvent(stationarySpawner, halting: false);
		Wait(5f);
		waitEvent2 = new WaitEvent(base.Game, 3f);
		eventList.AddEvent(waitEvent2, halting: true);
		eventList.AddHalt();
		bonusSpawner = new BonusSpawner(base.Game, 0f, 0.2f, randomly: true);
		bonusSpawner.SetMars();
		marsBossSpawner.LinkWith(bonusSpawner);
		eventList.AddEvent(bonusSpawner, halting: false);
		eventList.AddEvent(marsBossSpawner, halting: true);
		eventList.AddHalt();
		UnlockEvent gameEvent = new UnlockEvent(base.Game, "Mechanical Friends", Unlockables.Items.Friends, AnimatedMessage.UnlockType.cheat, level);
		eventList.AddEvent(gameEvent, halting: true);
		eventList.AddHalt();
		waitEvent2 = new WaitEvent(base.Game, 0.1f);
		eventList.AddEvent(waitEvent2, halting: true);
		eventList.AddHalt();
		waitEvent2.OnFinished += Victory;
	}

	private void Victory(GameEvent sender)
	{
		Victory();
	}

	private void spawnBosses(GameEvent sender)
	{
		MarsBoss marsBoss = MarsBoss.NewMarsBoss(Collection, base.Game);
		marsBoss.Setup(MarsBoss.BossPosition.left);
		Collection.Add((GameComponent)(object)marsBoss);
		marsBoss = MarsBoss.NewMarsBoss(Collection, base.Game);
		marsBoss.Setup(MarsBoss.BossPosition.right);
		Collection.Add((GameComponent)(object)marsBoss);
	}

	private void StationaryWave(float time, float chance, float ufochance, float bigufochance, float brainchance, float spiderchance)
	{
		StationarySpawner stationarySpawner = new StationarySpawner(base.Game, 560f, time, chance);
		stationarySpawner.SetChances(ufochance, bigufochance, brainchance, spiderchance);
		eventList.AddEvent(stationarySpawner, halting: true);
		eventList.AddHalt();
	}

	private void Wait(float time)
	{
		WaitEvent gameEvent = new WaitEvent(base.Game, time);
		eventList.AddEvent(gameEvent, halting: true);
		eventList.AddHalt();
	}

	private void UfoWave(float fastufochance, float normalufochance)
	{
		BonusSpawner bonusSpawner = new BonusSpawner(base.Game, 1f, 0.5f, randomly: false);
		bonusSpawner.SetMars();
		eventList.AddEvent(bonusSpawner, halting: false);
		UfoSpawner ufoSpawner = new UfoSpawner(base.Game, 3f, fastufochance, big: false);
		ufoSpawner.SetupMars();
		ufoSpawner.SetupFastEntry();
		eventList.AddEvent(ufoSpawner, halting: true);
		ufoSpawner = new UfoSpawner(base.Game, 3f, normalufochance, big: false);
		ufoSpawner.SetupMars();
		eventList.AddEvent(ufoSpawner, halting: true);
		eventList.AddHalt();
	}

	private void slowdown(GameEvent sender)
	{
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		Background.SetSpeed(new Vector2(-3f, 0f) / 16.666666f);
	}

	private void speedup(GameEvent sender)
	{
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		Background.SetSpeed(new Vector2(-10f, 0f) / 16.666666f);
	}

	private void spawnStationaryBoss(GameEvent sender)
	{
		StationaryBoss component = StationaryBoss.NewAlien(Collection, base.Game);
		Collection.Add((GameComponent)(object)component);
	}
}
