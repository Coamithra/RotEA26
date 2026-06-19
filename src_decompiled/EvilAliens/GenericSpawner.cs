using Microsoft.Xna.Framework;

namespace EvilAliens;

public abstract class GenericSpawner : GameEvent
{
	private float hitsPerSecond;

	private Timer timer = new Timer(0f, repeating: true);

	private bool random;

	private bool scaleSpawns;

	private bool scaleSpawnsMultiplayer;

	public GenericSpawner(Game game, float lifetime, float firesPerSecond, bool randomly, bool scaleSpawns)
		: base(game, lifetime)
	{
		if (randomly)
		{
			hitsPerSecond = firesPerSecond;
			random = true;
		}
		else
		{
			hitsPerSecond = firesPerSecond;
			random = false;
		}
		this.scaleSpawns = scaleSpawns;
	}

	public GenericSpawner(Game game, float lifetime, float firesPerSecond)
		: base(game, lifetime)
	{
		hitsPerSecond = firesPerSecond;
		random = true;
		scaleSpawns = true;
	}

	protected void SetRandomSpawn(bool value)
	{
		random = value;
	}

	protected void SetScaleSpawns(bool value)
	{
		scaleSpawns = value;
	}

	protected void SetScaleWithMultiplayer(bool value)
	{
		scaleSpawnsMultiplayer = value;
	}

	public override void Reset()
	{
		base.Reset();
		if (!random)
		{
			RecalculateTimer();
			timer.Reset();
			timer.Stop();
		}
	}

	public void SetHitsPerSecond(float amount)
	{
		hitsPerSecond = amount;
		if (!random)
		{
			RecalculateTimer();
		}
	}

	private void RecalculateTimer()
	{
		float num = hitsPerSecond;
		if (scaleSpawns)
		{
			num *= Settings.GetInstance().DifficultyModifier;
		}
		if (scaleSpawnsMultiplayer)
		{
			num *= Settings.GetInstance().MultiPlayerDifficultyModifier(Oracle.LiveShips);
		}
		timer.Duration = 1000f / num;
	}

	protected abstract void DoEvent(GameTime gameTime);

	public override void Update(GameTime gameTime)
	{
		if (random)
		{
			float num = hitsPerSecond * (float)gameTime.ElapsedGameTime.TotalSeconds;
			if (scaleSpawns)
			{
				num *= Settings.GetInstance().DifficultyModifier;
			}
			if (scaleSpawnsMultiplayer)
			{
				num *= Settings.GetInstance().MultiPlayerDifficultyModifier(Oracle.LiveShips);
			}
			while (num >= 1f)
			{
				DoEvent(gameTime);
				num -= 1f;
			}
			if (RandomHelper.RandomNextFloat(0f, 1f) <= num)
			{
				DoEvent(gameTime);
			}
		}
		else if (timer.Active)
		{
			timer.Update(gameTime);
			if (timer.Finished)
			{
				RecalculateTimer();
				DoEvent(gameTime);
				timer.Reset();
			}
		}
		else
		{
			DoEvent(gameTime);
			timer.Start();
		}
		base.Update(gameTime);
	}
}
