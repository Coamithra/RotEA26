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
		NetNoteRates(angle, length, leadValue);
		base.Direction = angle;
		len = length;
		lead = leadValue;
		lazor.MoveTo(base.Position);
		lazor.AimAt(angle);
		lazor.SetLength(length);
		lazor.SetLead(leadValue);
	}

	// ---- Local simulation of the beam between snapshots (card 0108d1fc) -------------------
	//
	// THE BUG: aim, length and lead are STATE EXTRAS, so a frozen puppet's beam only moved when
	// that entity's round-robin turn came up -- once per SnapshotTurnMs, i.e. 60ms at best and
	// several hundred in a busy world. A beam that grows at 0.4 px/ms therefore jumped in ~24px
	// steps instead of extending, which is the reported chop.
	//
	// THE FIX, AND WHY IT NEEDS NO WIRE BYTES: all three quantities are RAMPS, so their rates can
	// be OBSERVED from consecutive snapshots exactly the way NetSession.CaptureBaseState observes
	// an entity's velocity from consecutive positions -- the same idiom, one layer down. The
	// alternative (putting growthspeed on the wire) would also have been WRONG, because
	// `len` is scaled by Settings.DifficultyModifier, which ramps with elapsed play time and adapts
	// on death and so is NOT equal on the two peers. An observed rate is measured on the host's
	// actual beam and carries that scaling for free.
	//
	// IT COVERS ROTATION TOO, which is the card's explicit caveat: Level 1's miniboss SWEEPS its
	// beam (Lazer.ChangeAim), and the angular rate falls out of the same two samples. The angle
	// delta is wrapped into (-PI, PI] so a beam sweeping across the 0/2PI seam does not extrapolate
	// backwards at ~6 rad per turn.
	//
	// BOUNDED, deliberately: extrapolation stops after NetExtrapolateCapMs of silence. A stalled
	// peer must not have its beam grow without limit across the screen -- puppets are collidable,
	// so an invented beam can kill the local player. Same reasoning as the driver's PeerStalled
	// hold and ShipStateBuffer's 250ms cap; the cap is a little over one packet interval, since
	// this only has to bridge BETWEEN turns, never a real outage.
	private const float NetExtrapolateCapMs = 250f;

	private bool netHasRates;
	private float netAngleRate;      // rad/ms
	private float netLenRate;        // px/ms
	private float netLeadRate;       // px/ms
	private float netExtrapolatedMs; // budget SPENT against NetExtrapolateCapMs, not a timestamp
	private float netSinceApplyMs;   // real time since the last NetApplyBeam
	private float netPrevAngle;
	private float netPrevLen;
	private float netPrevLead;
	private bool netHasPrev;

	// Lazer is POOLED (NewLazer -> collection.Recycle<Lazer>), so a recycled instance would
	// otherwise start its new life with the PREVIOUS beam's rates already armed: NetDriveExtras
	// would extrapolate the old beam's aim and growth for up to NetExtrapolateCapMs before the
	// first snapshot of the new life landed, and the first NetNoteRates after the recycle would
	// difference across the gap and derive a nonsense angular rate. That moves a COLLIDABLE
	// hitbox (CollisionType reads len/lead/Direction), so it is not a cosmetic slip. Same
	// recycle trap NetVelocityScan documents on the measurement side.
	private void NetResetExtrapolation()
	{
		netHasRates = false;
		netHasPrev = false;
		netAngleRate = 0f;
		netLenRate = 0f;
		netLeadRate = 0f;
		netExtrapolatedMs = 0f;
		netSinceApplyMs = 0f;
		netPrevAngle = 0f;
		netPrevLen = 0f;
		netPrevLead = 0f;
	}

	private void NetNoteRates(float angle, float length, float leadValue)
	{
		if (netHasPrev && netSinceApplyMs > 0f)
		{
			float dt = netSinceApplyMs;
			netAngleRate = MathHelper.WrapAngle(angle - netPrevAngle) / dt;
			netLenRate = (length - netPrevLen) / dt;
			netLeadRate = (leadValue - netPrevLead) / dt;
			netHasRates = true;
		}
		netPrevAngle = angle;
		netPrevLen = length;
		netPrevLead = leadValue;
		netHasPrev = true;
		netSinceApplyMs = 0f;
		netExtrapolatedMs = 0f;
	}

	internal override void NetDriveExtras(GameTime gameTime)
	{
		base.NetDriveExtras(gameTime);
		float dtMs = (float)gameTime.ElapsedGameTime.TotalMilliseconds;
		netSinceApplyMs += dtMs;
		if (!netHasRates || netExtrapolatedMs >= NetExtrapolateCapMs)
		{
			// Still track the muzzle: base.Position is dead-reckoned by the driver every tick, and
			// the Quad holds its own copy, so without this the beam's origin lags its emitter even
			// when there is nothing to extrapolate.
			lazor.MoveTo(base.Position);
			return;
		}
		float step = MathHelper.Min(dtMs, NetExtrapolateCapMs - netExtrapolatedMs);
		netExtrapolatedMs += step;
		base.Direction += netAngleRate * step;
		// The ramps are monotone on the host (growthspeed is positive and `stopped`/`freed` only
		// ever zero a rate or start the lead one), so a negative observed rate is a sample pair
		// straddling a re-Setup rather than real shrinkage -- clamping at 0 keeps a recycled beam
		// from retracting into itself.
		len = MathHelper.Max(0f, len + MathHelper.Max(0f, netLenRate) * step);
		lead = MathHelper.Max(0f, lead + MathHelper.Max(0f, netLeadRate) * step);
		lazor.MoveTo(base.Position);
		lazor.AimAt(base.Direction);
		lazor.SetLength(len);
		lazor.SetLead(lead);
	}
}
