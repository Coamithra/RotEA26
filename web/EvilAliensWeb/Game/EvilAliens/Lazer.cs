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
}
