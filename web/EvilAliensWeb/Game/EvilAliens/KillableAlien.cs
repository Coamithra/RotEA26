using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

public abstract class KillableAlien : AlienDrawableGameComponent, EvilAliensWeb.Compat.Net.INetKillable
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
			// Online co-op: record WHO landed the killing blow before the death cascades into
			// component removal — the removal seam turns the note into a kill claim (client)
			// or an attributed death event (host). A single branch when no session is up.
			EvilAliensWeb.Compat.Net.NetSession.NoteKill(this, other);
			KilledBy(other, isComboGenerator);
			dead = true;
		}
	}

	// ---- Online co-op replication seams (Compat/Net, card 11.2) --------------------------

	// This is the type the net layer's four `is KillableAlien` tests were asking about, so it
	// answers the INetEntity discriminant with itself (card 25ad0659 step 2c-ii).
	private protected override EvilAliensWeb.Compat.Net.INetKillable NetKillableSelf => this;

	int EvilAliensWeb.Compat.Net.INetKillable.NetHitPoints => NetHitPoints;

	void EvilAliensWeb.Compat.Net.INetKillable.NetApplyHp(int hp)
	{
		NetApplyHp(hp);
	}

	void EvilAliensWeb.Compat.Net.INetKillable.NetKill(ICollidable killer, bool isComboGenerator)
	{
		NetKill(killer, isComboGenerator);
	}

	internal int NetHitPoints => hitpoints;

	// Apply a replicated hp value to a frozen client puppet. Only ever lowers (local hits
	// already landed must not be resurrected by an older snapshot) and floors at 1 — deaths
	// arrive exclusively as events/local kills, never by snapshot. Recomputes the colorize
	// redden exactly like HitBy so damage tint tracks.
	internal void NetApplyHp(int hp)
	{
		if (dead || hitpoints <= 0)
		{
			return;
		}
		hp = (int)MathHelper.Max(hp, 1f);
		if (hp >= hitpoints)
		{
			return;
		}
		hitpoints = hp;
		if (colorize)
		{
			float num = (float)hitpoints / ((float)initialhitpoints / 3f);
			color = new Color(new Vector3(1f, num, num));
		}
	}

	// Forced kill through the REAL per-type death path (explosion FX, sounds, AwardScore to
	// the killer's slot, authoritative child spawns, Die). Used by NetSession for honored
	// kill claims (host) and attributed remote deaths (client). Bypasses the hittimer gate —
	// a claim is already a confirmed kill.
	internal void NetKill(ICollidable killer, bool isComboGenerator)
	{
		if (dead || IsDead)
		{
			return;
		}
		hitpoints = 0;
		WasHit = true;
		EvilAliensWeb.Compat.Juice.KillPunch(isboss);
		KilledBy(killer, isComboGenerator);
		dead = true;
	}
}
