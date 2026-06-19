using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;

namespace EvilAliens;

internal class Lazer : AlienDrawableGameComponent
{
	private Quad lazor;

	private Cue soundeffect;

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
			//IL_0007: Unknown result type (might be due to invalid IL or missing references)
			//IL_0018: Unknown result type (might be due to invalid IL or missing references)
			//IL_001d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0022: Unknown result type (might be due to invalid IL or missing references)
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
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0040: Unknown result type (might be due to invalid IL or missing references)
		lazor = new Quad(((GameComponent)this).Game, base.Position, 0f, 16f, 0f, 0f);
		((DrawableGameComponent)this).DrawOrder = 40;
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
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
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
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		SetupSingleShot(position, direction, lead, playSound: true);
	}

	public void SetupSingleShot(Vector2 position, float direction, float lead, bool playSound)
	{
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		issingle = true;
		base.Position = position;
		this.lead = lead;
		len = lead + 0.5f;
		base.Direction = direction;
		lazor.SetProperties(position, direction, len, lead);
		if (playSound)
		{
			sound.PlayCue("lazershotnoloop");
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
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
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
		lazor.Draw();
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
}
