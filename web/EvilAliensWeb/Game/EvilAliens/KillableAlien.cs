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
		// Online co-op: tell the other peer this thing was hit. Its copy is a FROZEN puppet, so
		// the only damage it ever sees is the hp field arriving in a world snapshot -- and
		// NetApplyHp deliberately does not touch the blink (an older snapshot must not resurrect
		// one). The blink is 35ms against a 60ms-to-1.2s snapshot turn, so it cannot ride the
		// snapshot at all; it needs its own beat. A no-op with no session or no peer.
		//
		// ONLY WHEN THE THING SURVIVES THE HIT (card f6fc1d97), and the predicate is deliberately
		// `hitpoints > 0` -- THE SAME TERM `isBlinking()` ALREADY CARRIES. That is the whole
		// argument: the host does not draw a blink on its own killing blow, so a beat sent there
		// asked the joiner to draw something no screen in the session was drawing. The beat used
		// to go out for EVERY hit, this one included, so a LETHAL hit told the peer "flash" and
		// then, an EvDeath later, "explode" -- on a one-hit-point enemy (the ordinary small UFO,
		// SetHitPoints(1)) that is every single kill, which is exactly how it was reported. Send
		// side and draw side now agree by construction rather than by two constants matching.
		// SpiderBoss's own EnemyHitFlash emitter already sat in the `else` of its death test; this
		// makes KillableAlien consistent with it.
		//
		// It ALSO covers a hit landing on something already DYING, and that case is live rather
		// than theoretical: SpiderHelperMothership's KilledBy only flags `dying` and never clears
		// `Collides`, so the host keeps hitting it for seconds -- with `hitpoints` already at or
		// below 0, so the host shows nothing, while the joiner's copy (tracked rather than
		// released, `dead` still false, hp still positive from its last snapshot) accepted every
		// beat and flashed. `(hitpoints <= 0) & !dead` -- the shape of the branch below -- would
		// keep sending exactly there.
		//
		// (Precision, because this reasons from `isBlinking()`: Spider and FlyingSpider read the
		// raw `hittimeractive` instead, so they do flash for the one frame between the collision
		// pass and the next TopOfTickFlush. One frame, under their own explosion; FlyingSpider
		// already disagrees with itself, wings on `hittimeractive` and body on `isBlinking`.)
		if (hitpoints > 0)
		{
			EvilAliensWeb.Compat.Net.NetSession.OnGameFx(
				EvilAliensWeb.Compat.Net.NetFxKind.EnemyHitFlash, this);
		}
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
			NoteDeathBegan();
			// (card 1878b321) A DEFERRED death on a CLIENT never reaches the removal seam --
			// the puppet is frozen, so the dying animation/mission KilledBy started cannot run
			// its own Die() -- and the kill claim normally files at that seam. File it here
			// instead, or the host never learns the kill happened at all: the joiner's 50-hp
			// investment in the SpiderHelperMothership left a red, unresponsive zombie while
			// the host's copy flew on untouched. A no-op for ordinary types (IsDead is already
			// true here, and the removal seam still owns their claim), on the host, and offline.
			EvilAliensWeb.Compat.Net.NetSession.OnClientDeferredKill(this);
		}
	}

	// Online co-op (card f62116b5): a KilledBy that returned with the component STILL IN THE
	// WORLD deferred its own removal into a multi-second dying animation -- BattleSkull's 2.5s
	// shrink-and-flicker, the surviving MarsBoss's 5s crash, the FakeBoss's 4s and the BrainBoss's
	// TWENTY-second asplode (durations matter to the puppet layer: NetPuppets.releasedDying).
	// A puppet is frozen for life, so the other peer has to be told NOW to let its copy go and
	// finish dying locally; the host's own EvDeath does not arrive until that animation ENDS.
	//
	// Every other type ends its KilledBy in Die(), so IsDead is already true here and nothing is
	// sent. One branch offline, and this is the only place either kill entry point converges --
	// so a new deferred-death type costs nothing.
	private void NoteDeathBegan()
	{
		if (!IsDead)
		{
			EvilAliensWeb.Compat.Net.NetSession.OnHostDeathBegan(this);
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

	void EvilAliensWeb.Compat.Net.INetKillable.NetReplayUnattributedDeath(ICollidable agent)
	{
		NetReplayUnattributedDeath(agent);
	}

	internal int NetHitPoints => hitpoints;

	// Read by NetFxTest. The hit blink is a private timer read only by Draw, so a beat that
	// quietly stopped starting it would move no counter and show up in no frame a test can take.
	internal bool NetHitBlinking => isBlinking();

	// The client half of the hit beat above: light the puppet up for the same 35ms the host did.
	// Draw-only -- no hp is spent here, because damage is the host's to decide and arrives in the
	// snapshot; this is the flash that damage was ALREADY invisible without.
	//
	// The `hittimer.Active` guard is what makes it idempotent against our own simulation: a
	// client's bullets hit puppets locally (the CollisionHandler.IsActive seam) and run the real
	// HitBy, so for a hit WE observed the host's beat lands on a blink already running and does
	// nothing. That is the same gate HitBy opens with, so a beat can never extend a blink either.
	internal override void NetPlayFx(EvilAliensWeb.Compat.Net.NetFxKind kind)
	{
		if (kind != EvilAliensWeb.Compat.Net.NetFxKind.EnemyHitFlash || dead || hittimer.Active)
		{
			return;
		}
		hittimer.Reset();
		hittimer.Start();
	}

	// Apply a replicated hp value to a frozen client puppet: the HOST IS AUTHORITATIVE IN BOTH
	// DIRECTIONS (card 87310afa). Floors at 1 and refuses a dead/spent puppet, so deaths still
	// arrive exclusively as events/local kills and no snapshot can resurrect one. Recomputes the
	// colorize redden exactly like HitBy so damage tint tracks.
	//
	// IT USED TO REFUSE ANY RAISE, and that was not a free property. A client's bullets run the
	// real HitBy against puppets locally (they are Enabled=false but stay hit-testable via
	// NetPuppets.CollidableOverride -- that is what client-owned kill claims ARE), while the
	// host's own per-entity 35ms hittimer may refuse those same hits: the two peers run
	// independent gates over hit sequences ~100ms apart. Under a downward-only clamp every such
	// over-prediction was permanent, so the client's copy ratcheted below the host's for the rest
	// of the fight -- and since the client kills locally at hitpoints<=0 and files an
	// unconditional EvClaim (HandleClaim -> NetKill bypasses the hittimer: a claim is already a
	// confirmed kill), a boss could be claimed dead while the host's copy still had HP.
	//
	// WHAT THE OLD DIRECTION WAS NOT DOING: it is not what stops two players draining a boss at
	// double rate. That is host authority plus the 35ms gate at the top of HitBy -- the host's
	// boss is ONE real entity and both players' bullets (the peer's re-spawned from the
	// replicated cumulative shot count) contend for that one gate. Card a5c2a39b's closing note
	// credited the clamp; the conclusion held, the mechanism cited did not.
	//
	// THE COST, accepted deliberately: an in-order but ~half-RTT-stale snapshot legitimately does
	// not contain the hits we just landed, so the DRAW-SIDE readers of hp -- this redden, and
	// BattleSkull's Draw-time hue remap -- can nudge back up mid-burst. Cosmetic, and bounded by
	// the snapshot turn. (MarsBoss's fps = Lerp(32,16,HitPointsNormalized) is NOT one of them: it
	// is re-derived at the top of its Update, which a frozen puppet never runs -- that is exactly
	// why MarsBoss opts out of NetFrameLocal and takes the replicated frame instead.) The REORDER
	// case is a different guard and is untouched: NetPuppets refuses an entry older than the last
	// applied seq for this netId (card f5cf7a5c) before ApplySnapshotState ever reaches here.
	//
	// ?nethpraise=0 restores the downward-only clamp verbatim -- the deliberate bug reproduction,
	// and the raise legs' mutation control.
	internal void NetApplyHp(int hp)
	{
		if (dead || hitpoints <= 0)
		{
			return;
		}
		// FLOORED ONLY -- do NOT add a cap at initialhitpoints here, however tempting it looks
		// now that hp can rise. `HitPointsNormalized <= 1` is NOT an invariant this class has:
		// Initialize sets hitpoints = initialhitpoints * DifficultyFactorized(0.5f), which is
		// ABOVE 1 on every tier past the floor, so a scaleWithDifficulty type (Boss, 225) is
		// already over its initial at full health in ordinary single-player. Capping here would
		// cut those types' replicated hp to the unscaled number on every snapshot -- a real
		// desync traded for a cosmetic one that the raise does not actually cause, since both
		// peers share the session difficulty and so compute the same scaled pool.
		hp = (int)MathHelper.Max(hp, 1f);
		if (!EvilAliensWeb.Compat.DebugFlags.NetHpRaise && hp >= hitpoints)
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
		// The host reaches this from HandleClaim when the CLIENT landed the killing blow, so the
		// deferred-death beat has to go out from here too -- see NoteDeathBegan. On a client this
		// is a no-op (OnDeathBegan is host-gated).
		NoteDeathBegan();
	}

	// Replay a death NOBODY landed the killing blow on (cards 4e406eba / 303bfb5b / 13aa596c):
	// the host's copy self-destructed or was killed off-script, told the net layer so
	// (NetSession.NoteSelfDestruct), and the peer must show the same bang.
	//
	// The default IS the type's ordinary death look, which is right for almost everything.
	// Override only where the self-destruct genuinely looks DIFFERENT from being shot — the
	// shipped case is StarMine, whose Asplode() is two big blue bursts and "expl2" while its
	// KilledBy is one small burst and "expl1".
	//
	// `agent` is NetPuppets' scratch Bullet carrying KillerSelf as its slot, so a KilledBy that
	// casts `other` to Bullet still works; nothing is credited, because KillerSelf is not a
	// slot this peer owns and AwardScore only writes owned slots (card af96bcc2).
	internal virtual void NetReplayUnattributedDeath(ICollidable agent)
	{
		NetKill(agent, isComboGenerator: false);
	}
}
