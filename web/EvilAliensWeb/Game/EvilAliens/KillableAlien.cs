using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

public abstract class KillableAlien : AlienDrawableGameComponent
{
	private Timer hittimer = new Timer(35f, repeating: false);

	private bool dead;

	private bool isboss;

	private int initialhitpoints;

	private int hitpoints;

	private bool scaling;

	private bool colorize;

	protected bool WasHit;

	protected bool hittimeractive => hittimer.Active;

	protected int HitPoints
	{
		get
		{
			return hitpoints;
		}
		set
		{
			hitpoints = value;
		}
	}

	protected float HitPointsNormalized
	{
		get
		{
			if (initialhitpoints == 0)
			{
				return 1f;
			}
			return (float)hitpoints / (float)initialhitpoints;
		}
	}

	protected bool Colorize
	{
		get
		{
			return colorize;
		}
		set
		{
			colorize = value;
		}
	}

	protected bool IsBoss
	{
		get
		{
			return isboss;
		}
		set
		{
			isboss = value;
		}
	}

	public KillableAlien(Game game)
		: base(game)
	{
		timers.Add(hittimer);
	}

	protected void SetHitPoints(int hitpoints, bool scaleWithDifficulty)
	{
		scaling = scaleWithDifficulty;
		initialhitpoints = hitpoints;
	}

	public override void Initialize()
	{
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		base.Initialize();
		dead = false;
		hittimer.Stop();
		if (colorize)
		{
			color = Color.White;
		}
		float num = 1f;
		if (scaling)
		{
			num = Settings.GetInstance().DifficultyFactorized(0.5f);
		}
		hitpoints = (int)MathHelper.Max(1f, (float)initialhitpoints * num);
	}

	public override void Draw(GameTime gameTime)
	{
		if (isBlinking())
		{
			spriteBatch.lightenEffect.Enable();
		}
		base.Draw(gameTime);
		if (isBlinking())
		{
			spriteBatch.lightenEffect.Disable();
		}
	}

	protected bool isBlinking()
	{
		return hittimer.Active & (hitpoints > 0);
	}

	protected abstract void KilledBy(ICollidable other, bool isComboGenerator);

	public override void CollidesWith(ICollidable other)
	{
		WasHit = false;
		base.CollidesWith(other);
		if (other is IAlienKiller && !(!((IAlienKiller)other).CanHitBosses() & isboss))
		{
			HitBy(other, ((IAlienKiller)other).CausesCombo());
		}
	}

	protected virtual void HitBy(ICollidable other, bool isComboGenerator)
	{
		//IL_006a: Unknown result type (might be due to invalid IL or missing references)
		//IL_006f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		if (hittimer.Active)
		{
			return;
		}
		if (other is Option)
		{
			hitpoints -= 5;
			if (hitpoints < 0)
			{
				hitpoints = 0;
			}
		}
		else
		{
			hitpoints--;
		}
		if (colorize)
		{
			float num = (float)hitpoints / ((float)initialhitpoints / 3f);
			color = new Color(new Vector3(1f, num, num));
		}
		WasHit = true;
		hittimer.Reset();
		hittimer.Start();
		if ((hitpoints <= 0) & !dead)
		{
			// Game juice: every confirmed kill lands a punch — a micro freeze-frame + a tap
			// of screen shake (boss kills a longer stop + real shake). Rate-limited inside
			// Juice so a bomb-cleared wave reads as one impact, not a stutter.
			EvilAliensWeb.Compat.Juice.KillPunch(isboss);
			KilledBy(other, isComboGenerator);
			dead = true;
		}
	}
}
