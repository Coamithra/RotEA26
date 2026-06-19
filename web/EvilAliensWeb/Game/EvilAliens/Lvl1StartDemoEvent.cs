using System;
using Microsoft.Xna.Framework;

namespace EvilAliens;

public class Lvl1StartDemoEvent : GameEvent
{
	private enum demostate
	{
		wait,
		createufos,
		wait2,
		createbullets,
		wait3,
		done
	}

	private Timer timer = new Timer(0f, repeating: false);

	private demostate state;

	private int ufoscreated;

	private int bulletscreated;

	public Lvl1StartDemoEvent(Game game)
		: base(game, 0f)
	{
	}

	public override void Reset()
	{
		timer.Duration = 10f;
		timer.Reset();
		timer.Start();
		base.Reset();
		state = demostate.wait;
		ufoscreated = 0;
		bulletscreated = 0;
	}

	public override void Update(GameTime gameTime)
	{
		//IL_009a: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f1: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d8: Unknown result type (might be due to invalid IL or missing references)
		timer.Update(gameTime);
		switch (state)
		{
		case demostate.wait:
			if (timer.Finished)
			{
				timer.Duration = 300f;
				timer.Reset();
				timer.Start();
				state = demostate.createufos;
				UFO uFO2 = UFO.NewUFO(collectionHelper, game);
				uFO2.Setup(new Vector2(RandomHelper.RandomNextFloat(0f, 800f), 648f), isBig: false, EnemyBehaviour.normal);
				uFO2.SetAsBonus();
				collectionHelper.Add((GameComponent)(object)uFO2);
			}
			break;
		case demostate.createufos:
			if (timer.Finished)
			{
				UFO uFO = UFO.NewUFO(collectionHelper, game);
				uFO.Setup(new Vector2(RandomHelper.RandomNextFloat(0f, 800f), 648f), isBig: false, EnemyBehaviour.normal);
				collectionHelper.Add((GameComponent)(object)uFO);
				ufoscreated++;
				timer.Reset();
				timer.Start();
			}
			if (ufoscreated == 20)
			{
				timer.Duration = 100f;
				timer.Reset();
				timer.Start();
				state = demostate.wait2;
			}
			break;
		case demostate.wait2:
			if (timer.Finished)
			{
				timer.Duration = 33f;
				timer.Reset();
				timer.Start();
				state = demostate.createbullets;
			}
			break;
		case demostate.createbullets:
			if (timer.Finished)
			{
				Bullet bullet = Bullet.NewBullet(collectionHelper, game);
				bullet.Setup(new Vector2(400f, 799f), RandomHelper.RandomNextFloat((float)Math.PI * -3f / 4f, -(float)Math.PI / 4f), 3000f, 0);
				bullet.SetBouncing(100);
				bullet.SetAsploding(5000f);
				collectionHelper.Add((GameComponent)(object)bullet);
				bulletscreated++;
				timer.Reset();
				timer.Start();
			}
			if (bulletscreated == 70)
			{
				timer.Duration = 2000f;
				timer.Reset();
				timer.Start();
				state = demostate.wait3;
			}
			break;
		case demostate.wait3:
			if (timer.Finished)
			{
				timer.Duration = 80f;
				timer.Reset();
				timer.Start();
				state = demostate.done;
			}
			break;
		case demostate.done:
			Terminate();
			break;
		}
		base.Update(gameTime);
	}
}
