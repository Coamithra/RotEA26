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
		MessageEvent gameEvent = new MessageEvent(((GameComponent)this).Game, "10", SoundManager.Texts.Nothing);
		eventList.AddEvent(gameEvent, halting: false);
		WaitEvent gameEvent2 = new WaitEvent(((GameComponent)this).Game, 4.5f);
		eventList.AddEvent(gameEvent2);
		eventList.AddHalt();
		gameEvent = new MessageEvent(((GameComponent)this).Game, "9", SoundManager.Texts.Nothing);
		eventList.AddEvent(gameEvent, halting: false);
		gameEvent2 = new WaitEvent(((GameComponent)this).Game, 4.5f);
		eventList.AddEvent(gameEvent2);
		eventList.AddHalt();
		gameEvent = new MessageEvent(((GameComponent)this).Game, "8", SoundManager.Texts.Nothing);
		eventList.AddEvent(gameEvent, halting: false);
		gameEvent2 = new WaitEvent(((GameComponent)this).Game, 4.5f);
		eventList.AddEvent(gameEvent2);
		eventList.AddHalt();
		gameEvent = new MessageEvent(((GameComponent)this).Game, "7", SoundManager.Texts.Nothing);
		eventList.AddEvent(gameEvent, halting: false);
		gameEvent.OnFinished += setspawnstar;
		gameEvent2 = new WaitEvent(((GameComponent)this).Game, 4.5f);
		eventList.AddEvent(gameEvent2);
		eventList.AddHalt();
		gameEvent = new MessageEvent(((GameComponent)this).Game, "6", SoundManager.Texts.Nothing);
		eventList.AddEvent(gameEvent, halting: false);
		gameEvent2 = new WaitEvent(((GameComponent)this).Game, 4.5f);
		eventList.AddEvent(gameEvent2);
		eventList.AddHalt();
		gameEvent = new MessageEvent(((GameComponent)this).Game, "5", SoundManager.Texts.Nothing);
		eventList.AddEvent(gameEvent, halting: false);
		gameEvent2 = new WaitEvent(((GameComponent)this).Game, 4.5f);
		eventList.AddEvent(gameEvent2);
		eventList.AddHalt();
		gameEvent = new MessageEvent(((GameComponent)this).Game, "4", SoundManager.Texts.Nothing);
		eventList.AddEvent(gameEvent, halting: false);
		gameEvent2 = new WaitEvent(((GameComponent)this).Game, 4.5f);
		eventList.AddEvent(gameEvent2);
		eventList.AddHalt();
		gameEvent = new MessageEvent(((GameComponent)this).Game, "3", SoundManager.Texts.Nothing);
		eventList.AddEvent(gameEvent, halting: false);
		gameEvent2 = new WaitEvent(((GameComponent)this).Game, 4.5f);
		eventList.AddEvent(gameEvent2);
		eventList.AddHalt();
		gameEvent = new MessageEvent(((GameComponent)this).Game, "2", SoundManager.Texts.Nothing);
		eventList.AddEvent(gameEvent, halting: false);
		gameEvent2 = new WaitEvent(((GameComponent)this).Game, 4.5f);
		eventList.AddEvent(gameEvent2);
		eventList.AddHalt();
		gameEvent = new MessageEvent(((GameComponent)this).Game, "1", SoundManager.Texts.Nothing);
		eventList.AddEvent(gameEvent, halting: false);
		gameEvent2 = new WaitEvent(((GameComponent)this).Game, 4.5f);
		eventList.AddEvent(gameEvent2);
		eventList.AddHalt();
		gameEvent = new MessageEvent(((GameComponent)this).Game, "Wave Completed!", SoundManager.Texts.WaveCompleted);
		eventList.AddEvent(gameEvent, halting: true);
		eventList.AddHalt();
		gameEvent.OnFinished += victory;
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
		base.SoundManager.PlayMusic(Songs.Classic);
		base.Initialize();
		Settings.GetInstance().LockDifficulty();
	}

	protected override void UpdateNormal(GameTime gameTime)
	{
		//IL_00d4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00da: Unknown result type (might be due to invalid IL or missing references)
		//IL_00df: Unknown result type (might be due to invalid IL or missing references)
		//IL_0120: Unknown result type (might be due to invalid IL or missing references)
		//IL_012b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0132: Unknown result type (might be due to invalid IL or missing references)
		//IL_0137: Unknown result type (might be due to invalid IL or missing references)
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
			Vector2 val = default(Vector2);
			((Vector2)(ref val))._002Ector(num, num2);
			float num3 = 200f;
			foreach (PlayerShip ship in oracle.GetShips())
			{
				Vector2 val2 = ship.Position - val;
				if (((Vector2)(ref val2)).Length() <= num3)
				{
					return;
				}
			}
			EvilBullet evilBullet = EvilBullet.NewEvilBullet(Collection, ((GameComponent)this).Game);
			evilBullet.Setup(new Vector2(num, num2), MyMath.VectorToAngle(oracle.GetRandomPlayerPosition() - new Vector2(num, num2)));
			Collection.Add((GameComponent)(object)evilBullet);
			evilBullet.OnDeath += b_OnDeath;
			bullets++;
		}
		if (spawnstar && !Collection.ContainsType<StarMine>())
		{
			StarMine starMine = StarMine.NewStarMine(Collection, ((GameComponent)this).Game);
			starMine.Setup();
			Collection.Add((GameComponent)(object)starMine);
		}
	}

	private void b_OnDeath(object sender)
	{
		bullets--;
	}
}
