using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class BraineroidsLevel : GameScene
{
	private bool fired;

	private int wave = 1;

	private Timer ufotimer = new Timer(15000f, repeating: true);

	public BraineroidsLevel(Game game)
		: base(game, Levels.Braineroids)
	{
		base.OnReset += BraineroidsLevel_OnReset;
	}

	private void BraineroidsLevel_OnReset()
	{
		wave = 1;
		ufotimer.Reset();
		ufotimer.Start();
	}

	protected override void PopulateEventList()
	{
		WaitEvent waitEvent = new WaitEvent(((GameComponent)this).Game, 2f);
		waitEvent.OnFinished += wait_OnFinished;
		eventList.AddEvent(waitEvent, halting: true);
		eventList.AddHalt();
	}

	protected override void PreloadGraphicalContent()
	{
		base.PreloadGraphicalContent();
		ContentManager contentManager = ServiceHelper.Get<IContentManagerService>().ContentManager;
		contentManager.Load<Texture2D>("GFX/Sprites/blooddrop");
		contentManager.Load<Texture2D>("GFX/Sprites/braingoo");
		contentManager.Load<Texture2D>("GFX/Sprites/brainlargetransglow");
		contentManager.Load<Texture2D>("GFX/Sprites/mediumship");
	}

	public override void Initialize()
	{
		setPresence((GamerPresenceMode)14);
		wave = 1;
		ufotimer.Reset();
		ufotimer.Start();
		fired = false;
		Background.SetSpace();
		base.SoundManager.PlayMusic(Songs.Classic);
		base.Initialize();
		Settings.GetInstance().LockDifficulty();
	}

	protected override void UpdateNormal(GameTime gameTime)
	{
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		base.UpdateNormal(gameTime);
		ufotimer.Update(gameTime);
		if (ufotimer.Finished)
		{
			UFO uFO = UFO.NewUFO(Collection, ((GameComponent)this).Game);
			uFO.Setup(new Vector2(RandomHelper.RandomNextFloat(0f, 800f), -40f), isBig: false, EnemyBehaviour.classic);
			Collection.Add((GameComponent)(object)uFO);
			uFO.SetAsBonus();
		}
		if (fired && !Collection.ContainsType<Braineroid>())
		{
			eventList.RevertToCheckpoint();
			fired = false;
		}
	}

	private void wait_OnFinished(GameEvent sender)
	{
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0144: Unknown result type (might be due to invalid IL or missing references)
		fired = true;
		UFO uFO = UFO.NewUFO(Collection, ((GameComponent)this).Game);
		uFO.Setup(new Vector2(RandomHelper.RandomNextFloat(0f, 800f), -115f), isBig: true, EnemyBehaviour.classic);
		Collection.Add((GameComponent)(object)uFO);
		uFO.SetAsBonus();
		if (wave == (int)(8f * Settings.GetInstance().GetDifficultyValue(Settings.GetInstance().CurrentDifficulty)) + 1)
		{
			Victory();
			return;
		}
		Vector2 position = default(Vector2);
		for (int i = 0; i < wave * 2; i++)
		{
			Braineroid braineroid = Braineroid.NewBraineroid(Collection, ((GameComponent)this).Game);
			float num = 200f;
			switch (RandomHelper.Random.Next(1, 4))
			{
			case 1:
				((Vector2)(ref position))._002Ector(0f - num, RandomHelper.RandomNextFloat(0f, 600f));
				break;
			case 2:
				((Vector2)(ref position))._002Ector(800f + num, RandomHelper.RandomNextFloat(0f, 600f));
				break;
			case 3:
				((Vector2)(ref position))._002Ector(RandomHelper.RandomNextFloat(0f, 800f), 0f - num);
				break;
			case 4:
				((Vector2)(ref position))._002Ector(RandomHelper.RandomNextFloat(0f, 800f), 600f + num);
				break;
			default:
				((Vector2)(ref position))._002Ector(400f, 300f);
				break;
			}
			braineroid.Setup(position, BrainSize.huge, 0f, wrapping: true);
			Collection.Add((GameComponent)(object)braineroid);
		}
		wave++;
	}
}
