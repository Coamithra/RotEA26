using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;

namespace EvilAliens;

internal class Lazer : AlienDrawableGameComponent
{
	private Quad lazor;

	private SoundEffectInstance soundeffect;

	private bool stopped;

	private float growthspeed;

	public float len;

	public float lead;

	private bool freed;

	public AlienDrawableGameComponent owner;

	private CollisionLine line;

	private Timer smallshottimer;

	private bool issingle;

	public override ICollisionType CollisionType
	{
		get
		{
			line.Origin = base.Position + lead * MyMath.AngleToVector(base.Direction);
			line.Length = len - lead;
			line.Direction = base.Direction;
			return line;
		}
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		lazor.LoadContent();
	}

	public Lazer(Game game)
		: base(game)
	{
		lazor = new Quad(base.Game, base.Position, 0f, 16f, 0f, 0f);
		base.DrawOrder = 40;
		line = new CollisionLine(Vector2.Zero, Vector2.Zero);
		smallshottimer = new Timer(100f, repeating: false);
		timers.Add(smallshottimer);
		smallshottimer.Stop();
	}

	public override void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		base.OnComponentRemoved(e);
		if ((e.GameComponent == this) & !issingle)
		{
			sound.Stop(soundeffect);
		}
	}

	public static Lazer NewLazer(ComponentBin collection, Game game)
	{
		Lazer lazer = collection.Recycle<Lazer>();
		if (lazer == null)
		{
			lazer = new Lazer(game);
		}
		return lazer;
	}

	public void Setup(Vector2 position, float direction, AlienDrawableGameComponent owner, float lead)
	{
		issingle = false;
		this.owner = owner;
		base.Position = position;
		this.lead = lead;
		len = lead + 0.5f;
		base.Direction = direction;
		lazor.SetProperties(position, direction, len, lead);
		soundeffect = sound.Play("lazershot");
	}

	public void SetupSingleShot(Vector2 position, float direction, float lead)
	{
		SetupSingleShot(position, direction, lead, playSound: true);
	}

	public void SetupSingleShot(Vector2 position, float direction, float lead, bool playSound)
	{
		issingle = true;
		base.Position = position;
		this.lead = lead;
		len = lead + 0.5f;
		base.Direction = direction;
		lazor.SetProperties(position, direction, len, lead);
		if (playSound)
		{
			sound.PlayCue("lazershotnoloop");
			// Online co-op (card c146422f, "the junkboss' laser makes no sound for p2"): the beam
			// replicates as its own puppet, but LazerDescriptor builds it with playSound:false --
			// a puppet is not the shooter. That is right for the CONSTRUCTION and wrong for the
			// event, so the report rides its own beat, emitted HERE at the host's real firing
			// moment. Not off the beam's EvSpawn: NetIdRegistry.ReplayLive re-sends EvSpawn for
			// the whole live set when a peer joins in progress, and the puppet layer cannot tell
			// that from a fresh spawn -- the joiner would be met by every live beam at once.
			EvilAliensWeb.Compat.Net.NetSession.OnGameFx(
				EvilAliensWeb.Compat.Net.NetFxKind.EnemyLazerFire, null);
		}
		smallshottimer.Reset();
		smallshottimer.Start();
	}

	public void ChangeAim(float positiondelta)
	{
		base.Direction += positiondelta;
		lazor.AimAt(base.Direction);
	}

	// Online co-op (card c1a38ef9): tell this beam the CONSTANT angular rate its owner sweeps it
	// at, so the host can put that rate on the wire and a client puppet can turn the beam every
	// tick instead of stepping it once per snapshot turn. Called by the sweeper (Boss) right
	// after Setup; a beam nobody sweeps leaves it at Initialize's 0.
	public void SetSweepRate(float radPerMs)
	{
		netSweepRadPerMs = radPerMs;
	}

	public void MoveTo(Vector2 position)
	{
		base.Position = position;
		lazor.MoveTo(position);
	}

	public override void Initialize()
	{
		stopped = false;
		growthspeed = 0.4f;
		freed = false;
		NetResetExtrapolation();
		base.Initialize();
	}

	public override void Draw(GameTime gameTime)
	{
		lazor.Draw((float)gameTime.TotalGameTime.TotalSeconds);
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);
		if (issingle & smallshottimer.Finished)
		{
			Free();
		}
		if (!stopped)
		{
			len += growthspeed * (float)gameTime.ElapsedGameTime.TotalMilliseconds * Settings.GetInstance().DifficultyModifier;
			lazor.SetLength(len);
		}
		else
		{
			lazor.SetLength(len + RandomHelper.RandomNextFloat(-5f, 5f));
		}
		if (freed)
		{
			lead += growthspeed * (float)gameTime.ElapsedGameTime.TotalMilliseconds * Settings.GetInstance().DifficultyModifier;
			lazor.SetLead(lead);
			if ((lead > 1200f) | (lead > len))
			{
				collection.Remove((GameComponent)(object)this);
			}
		}
	}

	public override void CollidesWith(ICollidable other)
	{
		base.CollidesWith(other);
		if (other is Floorbottom)
		{
			stopped = true;
		}
		if (other is SpiderBoss)
		{
			Die();
		}
	}

	public void Free()
	{
		if (!freed & !issingle)
		{
			sound.Stop(soundeffect);
		}
		freed = true;
	}

	// ---- Online co-op replication seams (Compat/Net, card 11.2) --------------------------
	// A frozen client puppet never runs Update, so the beam's aim + growth are replicated each
	// snapshot. The aim (base.Direction) MUST be re-asserted here: the puppet driver sets
	// NetSpeedVector = observed velocity just before ApplyStateExtra, and SpeedVector's setter
	// rewrites base.Direction from that (near-zero) velocity -- so the beam angle only survives
	// because NetApplyBeam runs last and restores it.

	internal float NetAngle => base.Direction;

	internal float NetLen => len;

	internal float NetLead => lead;

	// Push the host's beam aim/length/lead into BOTH the collision line fields (Direction/len/
	// lead, read by CollisionType) and the drawn Quad geometry, WITHOUT resetting the FX tendril
	// pool (uses MoveTo/AimAt/SetLength/SetLead rather than SetProperties, which would ResetArcs
	// every snapshot and kill the crackle). base.Position is the driver-dead-reckoned muzzle.
	internal void NetApplyBeam(float angle, float length, float leadValue)
	{
		base.Direction = angle;
		len = length;
		lead = leadValue;
		lazor.MoveTo(base.Position);
		lazor.AimAt(angle);
		lazor.SetLength(length);
		lazor.SetLead(leadValue);
	}

	// ---- Local simulation of the beam between snapshots (cards 0108d1fc / c1a38ef9) -------
	//
	// THE BUG: aim, length and lead are STATE EXTRAS, so a frozen puppet's beam only moved when
	// that entity's round-robin turn came up -- once per SnapshotTurnMs, i.e. 60ms at best and
	// several hundred in a busy world. A beam that grows at 0.4 px/ms therefore jumped in ~24px
	// steps instead of extending, which is the reported chop.
	//
	// THE FIX: the host SENDS the three rates beside the three values, and NetDriveExtras
	// integrates them on real dt. Card 0108d1fc first shipped an ESTIMATOR here -- the rates
	// differenced out of consecutive applies, the CaptureBaseState observed-velocity idiom one
	// layer down -- and card c1a38ef9 replaced it, on the ruling that protocol changes are cheap
	// and a design must not be bent around avoiding wire bytes. The rates are exact numbers on the
	// host (`growthspeed`, and the sweeper's own constant), so sending one beats estimating it:
	// an estimator needs two samples before it knows anything at all (so the FIRST turn of every
	// beam's life was always unsmoothed), and it re-derives a fresh figure from each pair, which
	// under stream-lane reorder is a rate computed across a negative interval.
	//
	// THE DIFFICULTY-MODIFIER OBJECTION IS ANSWERED BY SCALING AT SEND TIME, and it is worth
	// recording because the estimator's header cited it as the reason a sent rate would be WRONG:
	// `len` grows by `growthspeed * DifficultyModifier`, and that modifier ramps with elapsed play
	// time and adapts on death, so it genuinely is NOT equal on the two peers. NetLenRate below
	// therefore reports the PRODUCT -- the host's real px/ms -- and the client never applies a
	// modifier of its own. A sent `growthspeed` alone would have been the wrong number; the sent
	// RATE is the right one.
	//
	// IT COVERS ROTATION TOO, which is the card's explicit caveat: Level 1's miniboss SWEEPS its
	// beam, at a constant Boss.LazerSweepRadPerMs the Boss hands over via SetSweepRate. A beam
	// nobody sweeps reports 0 and its aim simply holds -- which an estimator could only ever
	// approach, never state.
	//
	// BOUNDED, deliberately, and unchanged from the estimator: integration stops after
	// NetExtrapolateCapMs of silence. A stalled peer must not have its beam grow without limit
	// across the screen -- puppets are collidable, so an invented beam can kill the local player.
	// Same reasoning as the driver's PeerStalled hold and ShipStateBuffer's 250ms cap; the cap is
	// a little over one packet interval, since this only has to bridge BETWEEN turns, never a
	// real outage.
	private const float NetExtrapolateCapMs = 250f;

	// HOST side: the constant angular rate the owner sweeps this beam at, in rad/ms. Set by the
	// sweeper right after Setup (Boss); 0 for every other emitter, which is the truth -- nothing
	// else calls ChangeAim.
	private float netSweepRadPerMs;

	// CLIENT side: the three rates the host reported, and the budget spent integrating them.
	private bool netHasRates;
	private float netAngleRate;      // rad/ms
	private float netLenRate;        // px/ms, DifficultyModifier already applied by the host
	private float netLeadRate;       // px/ms, ditto
	private float netExtrapolatedMs; // budget SPENT against NetExtrapolateCapMs, not a timestamp

	// Lazer is POOLED (NewLazer -> collection.Recycle<Lazer>), so a recycled instance would
	// otherwise start its new life with the PREVIOUS beam's rates already armed: NetDriveExtras
	// would integrate the old beam's aim and growth for up to NetExtrapolateCapMs before the
	// first snapshot of the new life landed. That moves a COLLIDABLE hitbox (CollisionType reads
	// len/lead/Direction), so it is not a cosmetic slip. It clears the HOST-side sweep rate for
	// the mirror-image reason: a recycled beam whose new owner does not sweep must not inherit
	// the last one's sweep and report it on the wire. Same recycle trap NetVelocityScan
	// documents on the measurement side.
	private void NetResetExtrapolation()
	{
		netHasRates = false;
		netAngleRate = 0f;
		netLenRate = 0f;
		netLeadRate = 0f;
		netExtrapolatedMs = 0f;
		netSweepRadPerMs = 0f;
	}

	// ---- host readbacks for LazerDescriptor's state extras --------------------------------
	//
	// The growth rates are read STRAIGHT off Update's own expressions, including the two gates:
	// `stopped` (the beam has hit the floor and stops extending) zeroes the length rate, and
	// `freed` (the emitter has let go and the beam's tail is catching up) is what STARTS the lead
	// one. So the pair describes what the beam is doing right now, and a client that integrates
	// them stops extending at the same moment the host does rather than a turn later.
	internal float NetLenRate =>
		stopped ? 0f : growthspeed * Settings.GetInstance().DifficultyModifier;

	internal float NetLeadRate =>
		freed ? growthspeed * Settings.GetInstance().DifficultyModifier : 0f;

	internal float NetAngleRate => netSweepRadPerMs;

	// Puppet side. Assigned rather than eased, unlike the wasp's amplitude: these are step
	// functions (a beam stops or is freed at an instant) and easing across such a step would
	// leave the client's beam growing after the host's had stopped -- on a collidable hitbox.
	// The continuous quantity (the beam's own length) is what the correction blend smooths.
	internal void NetApplyRates(float lenRate, float leadRate, float angleRate)
	{
		netLenRate = lenRate;
		netLeadRate = leadRate;
		netAngleRate = angleRate;
		netHasRates = true;
		netExtrapolatedMs = 0f;
	}

	internal override void NetDriveExtras(GameTime gameTime)
	{
		base.NetDriveExtras(gameTime);
		float dtMs = (float)gameTime.ElapsedGameTime.TotalMilliseconds;
		if (!netHasRates || netExtrapolatedMs >= NetExtrapolateCapMs)
		{
			// Still track the muzzle: base.Position is dead-reckoned by the driver every tick, and
			// the Quad holds its own copy, so without this the beam's origin lags its emitter even
			// when there is nothing to integrate.
			lazor.MoveTo(base.Position);
			return;
		}
		float step = MathHelper.Min(dtMs, NetExtrapolateCapMs - netExtrapolatedMs);
		netExtrapolatedMs += step;
		base.Direction += netAngleRate * step;
		// The length/lead rates are non-negative on the host by construction (growthspeed is
		// positive and both gates only ever zero a rate or start one), so the floors below guard
		// the wire rather than the game: these arrive as bytes from a stranger's build over the
		// public game browser, and a negative rate would retract a collidable beam into itself.
		len = MathHelper.Max(0f, len + MathHelper.Max(0f, netLenRate) * step);
		lead = MathHelper.Max(0f, lead + MathHelper.Max(0f, netLeadRate) * step);
		lazor.MoveTo(base.Position);
		lazor.AimAt(base.Direction);
		lazor.SetLength(len);
		lazor.SetLead(lead);
	}
}
