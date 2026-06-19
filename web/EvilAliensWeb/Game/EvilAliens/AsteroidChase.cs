using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class AsteroidChase : GameScene
{
	public AsteroidChase(Game game)
		: base(game, Levels.SpaceDodge)
	{
	}

	protected override void PreloadGraphicalContent()
	{
		base.PreloadGraphicalContent();
		ContentManager contentManager = ServiceHelper.Get<IContentManagerService>().ContentManager;
		contentManager.Load<Texture2D>("GFX/Sprites/Asteroid2");
		contentManager.Load<Texture2D>("GFX/Sprites/ufosheet");
		contentManager.Load<Texture2D>("GFX/Sprites/smallship");
	}

	protected override void PopulateEventList()
	{
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		WaitEvent waitEvent = new WaitEvent(base.Game, 2f);
		waitEvent.OnFinished += wait_OnFinished;
		eventList.AddEvent(waitEvent, halting: true);
		eventList.AddHalt();
		MessageEvent messageEvent = new MessageEvent(base.Game, "Warning!", SoundManager.Texts.Warning, 2.5f);
		messageEvent.SetupAsWarning(MyMath.VectorToAngle(new Vector2(-800f, -600f)));
		eventList.AddEvent(messageEvent, halting: true);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
		AsteroidSpawner gameEvent = new AsteroidSpawner(base.Game, 60f, 5f, startWithBig: true);
		eventList.AddEvent(gameEvent, halting: true);
		BonusSpawner bonusSpawner = new BonusSpawner(base.Game, 60f, 1f, randomly: false);
		bonusSpawner.SetMars();
		eventList.AddEvent(bonusSpawner, halting: false);
		UfoSpawner ufoSpawner = new UfoSpawner(base.Game, 60f, 8f, big: false);
		eventList.AddEvent(ufoSpawner, halting: true);
		ufoSpawner.SetupAsteroidChase();
		eventList.AddHalt();
		waitEvent = new WaitEvent(base.Game, 4f);
		waitEvent.OnFinished += win;
		eventList.AddEvent(waitEvent, halting: true);
		eventList.AddHalt();
	}

	public override void Initialize()
	{
		setPresence((GamerPresenceMode)14);
		Background.SetSpace();
		base.SoundManager.PlayMusic(Songs.Classic);
		base.Initialize();
		Settings.GetInstance().LockDifficulty(Settings.GetInstance().CurrentDifficulty);
	}

	private void win(GameEvent sender)
	{
		Victory();
	}

	private void wait_OnFinished(GameEvent sender)
	{
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		Background.SetSpeed(new Vector2(0.3f, 0.72f));
	}
}
