using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class Paratrooper : GameScene
{
	private Floor floor;

	private List<ParatrooperBrain> brainzleft = new List<ParatrooperBrain>();

	private List<ParatrooperBrain> brainzright = new List<ParatrooperBrain>();

	private int scoretarget;

	public Paratrooper(Game game)
		: base(game, Levels.Paratrooper)
	{
		floor = new Floor(base.Game);
		base.OnFinished += Paratrooper_OnFinished;
	}

	protected override void PreloadGraphicalContent()
	{
		base.PreloadGraphicalContent();
		ContentManager contentManager = ServiceHelper.Get<IContentManagerService>().ContentManager;
		contentManager.Load<Texture2D>("GFX/Sprites/brainlargetransglow");
		contentManager.Load<Texture2D>("GFX/Sprites/plasmaball2");
		contentManager.Load<Texture2D>("GFX/Sprites/parachute");
	}

	public override void OnComponentAdded(GameComponentCollectionEventArgs e)
	{
		base.OnComponentAdded(e);
		if (e.GameComponent is Bullet)
		{
			Bullet bullet = (Bullet)(object)e.GameComponent;
			bullet.ClampAngle((float)Math.PI * -9f / 10f, -(float)Math.PI / 10f);
		}
		if (e.GameComponent is PlayerShip)
		{
			PlayerShip playerShip = (PlayerShip)(object)e.GameComponent;
			playerShip.AddRangePowerups(8);
		}
	}

	private void Paratrooper_OnFinished(object sender, FinishedArgs args)
	{
		Collection.Remove((GameComponent)(object)floor);
		score.EnableCombos();
		brainzleft.Clear();
		brainzright.Clear();
	}

	protected override void PopulateEventList()
	{
		WaitEvent waitEvent = new WaitEvent(base.Game, 0.1f);
		eventList.AddEvent(waitEvent);
		eventList.AddHalt();
		waitEvent.OnFinished += message;
	}

	private void message(GameEvent sender)
	{
		AnimatedMessage animatedMessage = AnimatedMessage.NewAnimatedMessage(Collection, base.Game);
		animatedMessage.Setup("Target: " + scoretarget + " points", SoundManager.Texts.Nothing, AnimatedMessage.MessageType.starwarsblue);
		Collection.Add((GameComponent)(object)animatedMessage);
	}

	private void victory(GameEvent sender)
	{
		Victory();
	}

	public override void Initialize()
	{
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		setPresence((GamerPresenceMode)14);
		score.DisableCombos();
		Collection.Add((GameComponent)(object)floor);
		Background.SetMars();
		Background.SetSpeed(Vector2.Zero);
		base.SoundManager.PlayMusic(Songs.Bach);
		base.Initialize();
		Settings.GetInstance().LockDifficulty();
		scoretarget = (int)Math.Round(2500f * Settings.GetInstance().DifficultyModifier);
	}

	public override void Update(GameTime gameTime)
	{
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		base.Update(gameTime);
		foreach (PlayerShip ship in oracle.GetShips())
		{
			ship.SetPosition(new Vector2(400f, 500f));
		}
	}

	protected override void UpdateNormal(GameTime gameTime)
	{
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		//IL_0127: Unknown result type (might be due to invalid IL or missing references)
		//IL_0145: Unknown result type (might be due to invalid IL or missing references)
		base.UpdateNormal(gameTime);
		brainzleft.Clear();
		brainzright.Clear();
		foreach (ParatrooperBrain paratrooperBrain in oracle.GetParatrooperBrains())
		{
			if (paratrooperBrain.ReadyToConnect())
			{
				if (paratrooperBrain.Position.X < 400f)
				{
					brainzleft.Add(paratrooperBrain);
				}
				if (paratrooperBrain.Position.X > 400f)
				{
					brainzright.Add(paratrooperBrain);
				}
			}
		}
		if (brainzleft.Count >= 2)
		{
			brainzleft[0].MergeWith(brainzleft[1]);
		}
		if (brainzright.Count >= 2)
		{
			brainzright[0].MergeWith(brainzright[1]);
		}
		brainzleft.Clear();
		brainzright.Clear();
		foreach (ParatrooperBrain paratrooperBrain2 in oracle.GetParatrooperBrains())
		{
			if (paratrooperBrain2.ReadyToConnect2())
			{
				if (paratrooperBrain2.Position.X < 400f)
				{
					brainzleft.Add(paratrooperBrain2);
				}
				if (paratrooperBrain2.Position.X > 400f)
				{
					brainzright.Add(paratrooperBrain2);
				}
			}
		}
		if (brainzleft.Count >= 2)
		{
			brainzleft[0].MergeWith2(brainzleft[1]);
		}
		if (brainzright.Count >= 2)
		{
			brainzright[0].MergeWith2(brainzright[1]);
		}
		if (RandomHelper.RandomFromAverage(6f * Settings.GetInstance().DifficultyModifier, gameTime))
		{
			ParatrooperAlien paratrooperAlien = ParatrooperAlien.NewAlien(Collection, base.Game);
			paratrooperAlien.Setup();
			Collection.Add((GameComponent)(object)paratrooperAlien);
		}
		if (score.PointScore(0) >= (float)scoretarget)
		{
			Victory();
			AnimatedMessage animatedMessage = AnimatedMessage.NewAnimatedMessage(Collection, base.Game);
			animatedMessage.Setup("Wave Completed!", SoundManager.Texts.WaveCompleted, AnimatedMessage.MessageType.starwarsblue);
			Collection.Add((GameComponent)(object)animatedMessage);
		}
	}
}
