using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

public abstract class AlienDrawableGameComponent : DrawableGameComponent, ICollidable, IComponentWatcher
{
	public enum InterpolationOptions
	{
		always,
		never,
		as_specified
	}

	public delegate void DeathEvent(object sender);

	protected ScoreVisualiser Score;

	protected float PointValue;

	private bool awarded;

	protected List<Timer> timers = new List<Timer>();

	public Texture2D texture;

	public string texturename;

	public float rotation;

	public float scale = 1f;

	public int rows;

	public int columns;

	public int separatingspace;

	public float curframe;

	public float fps;

	public SpriteEffects spriteEffects;

	public SpriteBlendMode blendMode = (SpriteBlendMode)1;

	protected Color color;

	public InterpolationOptions interpolationOptions = InterpolationOptions.as_specified;

	private bool _collides = true;

	protected InputHandler input;

	protected ComponentBin collection;

	protected SpriteBatchWrapper spriteBatch;

	protected ContentManager content;

	protected SoundManager sound;

	protected Oracle oracle;

	private Vector2 _position = Vector2.Zero;

	private float _minimumSpeed;

	private float _maximumSpeed;

	private float _deceleration;

	private float _acceleration;

	private float _direction;

	private float _speed;

	private bool isdead;

	private CollisionBox collisionBox;

	public abstract ICollisionType CollisionType { get; }

	public bool Collides
	{
		get
		{
			return _collides;
		}
		set
		{
			_collides = value;
		}
	}

	public bool IsDead => isdead;

	public Vector2 Position
	{
		get
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			return _position;
		}
		set
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0002: Unknown result type (might be due to invalid IL or missing references)
			_position = value;
		}
	}

	protected float Speed
	{
		get
		{
			return _speed;
		}
		set
		{
			_speed = value;
		}
	}

	protected Vector2 SpeedVector
	{
		get
		{
			//IL_000c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0011: Unknown result type (might be due to invalid IL or missing references)
			return _speed * MyMath.AngleToVector(_direction);
		}
		set
		{
			//IL_000e: Unknown result type (might be due to invalid IL or missing references)
			_speed = (value).Length();
			_direction = MyMath.VectorToAngle(value);
		}
	}

	protected float Direction
	{
		get
		{
			return _direction;
		}
		set
		{
			_direction = value;
		}
	}

	protected float Acceleration
	{
		get
		{
			return _acceleration;
		}
		set
		{
			_acceleration = value;
		}
	}

	protected float Deceleration
	{
		get
		{
			return _deceleration;
		}
		set
		{
			_deceleration = value;
		}
	}

	protected float MaxSpeed
	{
		get
		{
			return _maximumSpeed;
		}
		set
		{
			_maximumSpeed = value;
		}
	}

	protected float MinSpeed
	{
		get
		{
			return _minimumSpeed;
		}
		set
		{
			_minimumSpeed = value;
		}
	}

	protected Vector2 DirectionalVector
	{
		get
		{
			//IL_0006: Unknown result type (might be due to invalid IL or missing references)
			return MyMath.AngleToVector(_direction);
		}
		set
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			_direction = MyMath.VectorToAngle(value);
		}
	}

	public event DeathEvent OnDeath;

	public AlienDrawableGameComponent(Game game)
		: base(game)
	{
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		input = ServiceHelper.Get<IInputHandlerService>().InputHandler;
		spriteBatch = ServiceHelper.Get<ISpriteBatchWrapperService>().SpriteBatchWrapper;
		collection = ServiceHelper.Get<IComponentBinService>().ComponentBin;
		content = ServiceHelper.Get<IContentManagerService>().ContentManager;
		sound = ServiceHelper.Get<ISoundManagerService>().SoundManager;
		Score = ServiceHelper.Get<IScoreService>().Score;
		oracle = ServiceHelper.Get<IOracleService>().Oracle;
	}

	protected bool OffScreen(float buffer)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		if (!(Position.X > 800f + buffer) && !(Position.X < 0f - buffer) && !(Position.Y > 600f + buffer))
		{
			return Position.Y < 0f - buffer;
		}
		return true;
	}

	protected void AddTimer(Timer t)
	{
		timers.Add(t);
	}

	protected void Die()
	{
		if (!isdead)
		{
			collection.Remove((GameComponent)(object)this);
			if (this.OnDeath != null)
			{
				this.OnDeath(this);
			}
			isdead = true;
		}
	}

	protected CollisionBox retrieveBoundsFromTexture()
	{
		//IL_008b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0095: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cb: Unknown result type (might be due to invalid IL or missing references)
		if (collisionBox == null)
		{
			collisionBox = new CollisionBox();
		}
		float num = texture.Width;
		num -= (float)((columns - 1) * separatingspace);
		num /= (float)columns;
		float num2 = texture.Height;
		num2 -= (float)((rows - 1) * separatingspace);
		num2 /= (float)rows;
		collisionBox.TopLeft = new Vector2((0f - num * scale) / 2f, (0f - num2 * scale) / 2f) * 0.6f;
		collisionBox.BottomRight = new Vector2(num * scale / 2f, num2 * scale / 2f) * 0.6f;
		return collisionBox;
	}

	public void LoadAnimation(AnimationData animationData)
	{
		//IL_0065: Unknown result type (might be due to invalid IL or missing references)
		//IL_006a: Unknown result type (might be due to invalid IL or missing references)
		texture = content.Load<Texture2D>(animationData.TextureName);
		texturename = animationData.TextureName;
		rows = animationData.rows;
		columns = animationData.columns;
		fps = animationData.fps;
		separatingspace = animationData.separatingspace;
		curframe = 0f;
		color = Color.White;
	}

	protected void Move(Vector2 direction, GameTime gameTime)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		if (direction != Vector2.Zero)
		{
			Move((float?)MyMath.VectorToAngle(direction), gameTime);
		}
		else
		{
			Move((float?)null, gameTime);
		}
	}

	protected void Move(GameTime gameTime)
	{
		Move((float?)null, gameTime);
	}

	protected void Move(float? direction, GameTime gameTime)
	{
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		//IL_0056: Unknown result type (might be due to invalid IL or missing references)
		//IL_0089: Unknown result type (might be due to invalid IL or missing references)
		//IL_008e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0068: Unknown result type (might be due to invalid IL or missing references)
		//IL_007a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Unknown result type (might be due to invalid IL or missing references)
		//IL_0085: Unknown result type (might be due to invalid IL or missing references)
		//IL_0090: Unknown result type (might be due to invalid IL or missing references)
		//IL_0091: Unknown result type (might be due to invalid IL or missing references)
		//IL_0092: Unknown result type (might be due to invalid IL or missing references)
		//IL_0097: Unknown result type (might be due to invalid IL or missing references)
		//IL_0099: Unknown result type (might be due to invalid IL or missing references)
		//IL_009e: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a1: Unknown result type (might be due to invalid IL or missing references)
		float num = Convert.ToSingle(gameTime.ElapsedGameTime.TotalMilliseconds);
		float direction2 = _direction;
		Vector2 val = MyMath.AngleToVector(direction2) * _speed;
		Vector2 val2 = MyMath.AngleToVector(direction2) * -1f * MathHelper.Min(_deceleration * num, _speed);
		Vector2 val3 = ((!direction.HasValue) ? Vector2.Zero : (MyMath.AngleToVector(direction.Value) * (_acceleration + _deceleration) * num));
		Vector2 v = val + val2 + val3;
		_direction = MyMath.VectorToAngle(v);
		_speed = MathHelper.Clamp((v).Length(), _minimumSpeed, _maximumSpeed);
	}

	public override void Update(GameTime gameTime)
	{
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		//IL_005f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0064: Unknown result type (might be due to invalid IL or missing references)
		//IL_0067: Unknown result type (might be due to invalid IL or missing references)
		//IL_006c: Unknown result type (might be due to invalid IL or missing references)
		//IL_006d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		foreach (Timer timer in timers)
		{
			timer.Update(gameTime);
		}
		Vector2 val = MyMath.AngleToVector(_direction) * _speed * Convert.ToSingle(gameTime.ElapsedGameTime.TotalMilliseconds);
		_position += val;
		curframe = (curframe + fps * (float)gameTime.ElapsedGameTime.TotalSeconds) % (float)(rows * columns);
		base.Update(gameTime);
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0077: Unknown result type (might be due to invalid IL or missing references)
		//IL_008a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0090: Unknown result type (might be due to invalid IL or missing references)
		spriteBatch.BlendMode = blendMode;
		if (columns > 1 || rows > 1)
		{
			switch (interpolationOptions)
			{
			case InterpolationOptions.always:
				drawWithInterpolation();
				break;
			case InterpolationOptions.as_specified:
				if (Settings.GetInstance().Interpolate)
				{
					drawWithInterpolation();
				}
				else
				{
					drawWithoutInterpolation();
				}
				break;
			case InterpolationOptions.never:
				drawWithoutInterpolation();
				break;
			}
		}
		else
		{
			spriteBatch.Draw(texture, _position, rotation, scale, center: true, color, spriteEffects);
		}
		base.Draw(gameTime);
	}

	private void drawWithoutInterpolation()
	{
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_006e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0070: Unknown result type (might be due to invalid IL or missing references)
		//IL_0083: Unknown result type (might be due to invalid IL or missing references)
		//IL_0089: Unknown result type (might be due to invalid IL or missing references)
		//IL_0058: Unknown result type (might be due to invalid IL or missing references)
		bool flag = spriteBatch.colorizeEffect.Enabled || spriteBatch.lightenEffect.Enabled;
		Rectangle frameRectangle = getFrameRectangle((int)curframe);
		if (flag)
		{
			spriteBatch.fadeEffect.Enable();
			spriteBatch.fadeEffect.Value = (color).ToVector4();
		}
		spriteBatch.Draw(texture, frameRectangle, Position, rotation, scale, center: true, color, spriteEffects);
		if (flag)
		{
			spriteBatch.fadeEffect.Disable();
		}
	}

	private Rectangle getFrameRectangle(int framenr)
	{
		//IL_0079: Unknown result type (might be due to invalid IL or missing references)
		int num = framenr / columns;
		int num2 = framenr % columns;
		int num3 = texture.Width - (columns - 1) * separatingspace;
		num3 /= columns;
		int num4 = texture.Height - (rows - 1) * separatingspace;
		num4 /= rows;
		Rectangle result = default(Rectangle);
		(result) = new Rectangle(num2 * (num3 + separatingspace), num * (num4 + separatingspace), num3, num4);
		return result;
	}

	private void drawWithInterpolation()
	{
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0052: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		//IL_0073: Expected I4, but got Unknown
		//IL_0148: Unknown result type (might be due to invalid IL or missing references)
		//IL_0165: Unknown result type (might be due to invalid IL or missing references)
		//IL_016a: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a6: Unknown result type (might be due to invalid IL or missing references)
		//IL_01bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_01be: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d1: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_008c: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00be: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00eb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ed: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ff: Unknown result type (might be due to invalid IL or missing references)
		//IL_0102: Unknown result type (might be due to invalid IL or missing references)
		int num = (int)curframe;
		float num2 = curframe % 1f;
		if (!spriteBatch.colorizeEffect.Enabled)
		{
			_ = spriteBatch.lightenEffect.Enabled;
		}
		Rectangle frameRectangle = getFrameRectangle(num);
		Rectangle frameRectangle2 = getFrameRectangle((num + 1) % (rows * columns));
		SpriteBlendMode val = blendMode;
		switch ((int)val)
		{
		case 2:
		{
			Color val2 = default(Color);
			(val2) = new Color(new Vector4(1f, 1f, 1f, 1f - num2));
			Color val3 = default(Color);
			(val3) = new Color(new Vector4(1f, 1f, 1f, num2));
			spriteBatch.Draw(texture, frameRectangle, Position, rotation, scale, center: true, val2, spriteEffects);
			spriteBatch.Draw(texture, frameRectangle2, Position, rotation, scale, center: true, val3, spriteEffects);
			break;
		}
		case 0:
		case 1:
			spriteBatch.interpolateEffect.Enable();
			spriteBatch.interpolateEffect.Offset = new Vector2((float)((frameRectangle2).Left - (frameRectangle).Left), (float)((frameRectangle2).Top - (frameRectangle).Top)) / new Vector2((float)texture.Width, (float)texture.Height);
			spriteBatch.interpolateEffect.Delta = num2;
			spriteBatch.fadeEffect.Enable();
			spriteBatch.fadeEffect.Value = (color).ToVector4();
			spriteBatch.Draw(texture, frameRectangle, Position, rotation, scale, center: true, color, spriteEffects);
			spriteBatch.interpolateEffect.Disable();
			spriteBatch.fadeEffect.Disable();
			break;
		}
	}

	public void AwardScore(bool combo, ICollidable other)
	{
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		if ((!awarded & (PointValue > 0f)) && other is IAlienKiller && ((IAlienKiller)other).Player() >= 0)
		{
			Score.AddScore(PointValue, combo, Position, ((IAlienKiller)other).Player());
			awarded = true;
		}
	}

	public void AwardScoreToAll(bool combo)
	{
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		if (!(!awarded & (PointValue > 0f)))
		{
			return;
		}
		for (int i = 0; i < oracle.Players; i++)
		{
			if (i == 0)
			{
				Score.AddScore(PointValue, combo, Position, i);
			}
			else
			{
				Score.AddScore(PointValue, combo, i);
			}
		}
		awarded = true;
	}

	public override void Initialize()
	{
		isdead = false;
		awarded = false;
		foreach (Timer timer in timers)
		{
			timer.Reset();
		}
		base.Initialize();
	}

	public bool DetectCollision(ICollidable other)
	{
		if (Collides)
		{
			if (other is AlienDrawableGameComponent)
			{
				if (((AlienDrawableGameComponent)other).Collides)
				{
					return CollisionType.TestCollision(((AlienDrawableGameComponent)other).CollisionType);
				}
				return false;
			}
			return CollisionType.TestCollision(other.GetCollisionType());
		}
		return false;
	}

	public virtual void CollidesWith(ICollidable other)
	{
	}

	public ICollisionType GetCollisionType()
	{
		return CollisionType;
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		if (texturename != null)
		{
			texture = content.Load<Texture2D>(texturename);
		}
	}

	public virtual void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		if (e.GameComponent == this)
		{
			this.OnDeath = null;
		}
	}

	public virtual void OnComponentAdded(GameComponentCollectionEventArgs e)
	{
	}
}
