using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class CrazyGame : GameScene
{
	private int bullets;

	private bool spawnstar;

	public CrazyGame(Game game)
		: base(game, Levels.CrazyGame)
	{
		base.OnReset += CrazyGame_OnReset;
	}

	private void CrazyGame_OnReset()
	{
		bullets = 0;
		spawnstar = false;
	}

	protected override void PreloadGraphicalContent()
	{
		base.PreloadGraphicalContent();
		ContentManager contentManager = ServiceHelper.Get<IContentManagerService>().ContentManager;
		contentManager.Load<Texture2D>("GFX/Sprites/deathstarsheet2");
	}

	protected override void PopulateEventList()
	{
		MessageEvent messageEvent = new MessageEvent(base.Game, "10", SoundManager.Texts.Nothing);
		eventList.AddEvent(messageEvent, halting: false);
		WaitEvent waitEvent = new WaitEvent(base.Game, 4.5f);
		eventList.AddEvent(waitEvent);
		eventList.AddHalt();
		messageEvent = new MessageEvent(base.Game, "9", SoundManager.Texts.Nothing);
		eventList.AddEvent(messageEvent, halting: false);
		waitEvent = new WaitEvent(base.Game, 4.5f);
		eventList.AddEvent(waitEvent);
		eventList.AddHalt();
		messageEvent = new MessageEvent(base.Game, "8", SoundManager.Texts.Nothing);
		eventList.AddEvent(messageEvent, halting: false);
		waitEvent = new WaitEvent(base.Game, 4.5f);
		eventList.AddEvent(waitEvent);
		eventList.AddHalt();
		messageEvent = new MessageEvent(base.Game, "7", SoundManager.Texts.Nothing);
		eventList.AddEvent(messageEvent, halting: false);
		messageEvent.OnFinished += setspawnstar;
		waitEvent = new WaitEvent(base.Game, 4.5f);
		eventList.AddEvent(waitEvent);
		eventList.AddHalt();
		messageEvent = new MessageEvent(base.Game, "6", SoundManager.Texts.Nothing);
		eventList.AddEvent(messageEvent, halting: false);
		waitEvent = new WaitEvent(base.Game, 4.5f);
		eventList.AddEvent(waitEvent);
		eventList.AddHalt();
		messageEvent = new MessageEvent(base.Game, "5", SoundManager.Texts.Nothing);
		eventList.AddEvent(messageEvent, halting: false);
		waitEvent = new WaitEvent(base.Game, 4.5f);
		eventList.AddEvent(waitEvent);
		eventList.AddHalt();
		messageEvent = new MessageEvent(base.Game, "4", SoundManager.Texts.Nothing);
		eventList.AddEvent(messageEvent, halting: false);
		waitEvent = new WaitEvent(base.Game, 4.5f);
		eventList.AddEvent(waitEvent);
		eventList.AddHalt();
		messageEvent = new MessageEvent(base.Game, "3", SoundManager.Texts.Nothing);
		eventList.AddEvent(messageEvent, halting: false);
		waitEvent = new WaitEvent(base.Game, 4.5f);
		eventList.AddEvent(waitEvent);
		eventList.AddHalt();
		messageEvent = new MessageEvent(base.Game, "2", SoundManager.Texts.Nothing);
		eventList.AddEvent(messageEvent, halting: false);
		waitEvent = new WaitEvent(base.Game, 4.5f);
		eventList.AddEvent(waitEvent);
		eventList.AddHalt();
		messageEvent = new MessageEvent(base.Game, "1", SoundManager.Texts.Nothing);
		eventList.AddEvent(messageEvent, halting: false);
		waitEvent = new WaitEvent(base.Game, 4.5f);
		eventList.AddEvent(waitEvent);
		eventList.AddHalt();
		messageEvent = new MessageEvent(base.Game, "Wave Completed!", SoundManager.Texts.WaveCompleted);
		eventList.AddEvent(messageEvent, halting: true);
		eventList.AddHalt();
		messageEvent.OnFinished += victory;
	}

	private void setspawnstar(GameEvent sender)
	{
		if (Settings.GetInstance().CurrentDifficulty == Settings.DifficultyLevel.Inzane)
		{
			spawnstar = true;
		}
	}

	private void victory(GameEvent sender)
	{
		Victory();
	}

	public override void Initialize()
	{
		setPresence((GamerPresenceMode)14);
		Background.SetAlienBaseDark();
		base.SoundManager.PlayMusic(SoundManager.ClassicForDifficulty());
		base.Initialize();
		Settings.GetInstance().LockDifficulty();
	}

	protected override void UpdateNormal(GameTime gameTime)
	{
		base.UpdateNormal(gameTime);
		if (bullets < (int)(30f * Settings.GetInstance().DifficultyModifier))
		{
			float num;
			float num2;
			switch (RandomHelper.Random.Next(4))
			{
			default:
				return;
			case 0:
				num = -10f;
				num2 = RandomHelper.RandomNextFloat(0f, 600f);
				break;
			case 1:
				num = 810f;
				num2 = RandomHelper.RandomNextFloat(0f, 600f);
				break;
			case 2:
				num = RandomHelper.RandomNextFloat(0f, 800f);
				num2 = -10f;
				break;
			case 3:
				num = RandomHelper.RandomNextFloat(0f, 800f);
				num2 = 610f;
				break;
			}
			Vector2 val = new Vector2(num, num2);
			float num3 = 200f;
			foreach (PlayerShip ship in oracle.GetShips())
			{
				Vector2 val2 = ship.Position - val;
				if ((val2).Length() <= num3)
				{
					return;
				}
			}
			EvilBullet evilBullet = EvilBullet.NewEvilBullet(Collection, base.Game);
			evilBullet.Setup(new Vector2(num, num2), MyMath.VectorToAngle(oracle.GetRandomPlayerPosition() - new Vector2(num, num2)));
			Collection.Add((GameComponent)(object)evilBullet);
			evilBullet.OnDeath += b_OnDeath;
			bullets++;
		}
		if (spawnstar && !Collection.ContainsType<StarMine>())
		{
			StarMine starMine = StarMine.NewStarMine(Collection, base.Game);
			starMine.Setup();
			Collection.Add((GameComponent)(object)starMine);
		}
	}

	private void b_OnDeath(object sender)
	{
		bullets--;
	}
}
