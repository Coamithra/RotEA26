using System;
using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class Option : KillableAlien, IAlienKiller
{
	private int player;

	private float radius;

	private float prevangle;

	private float angle;

	private float targetangle;

	private PlayerShip owner;

	private Timer startuptimer = new Timer(500f, repeating: false);

	private Timer positionshifttimer = new Timer(500f, repeating: false);

	public override ICollisionType CollisionType
	{
		get
		{
			CollisionBox collisionBox = retrieveBoundsFromTexture();
			collisionBox.TopLeft += base.Position;
			collisionBox.BottomRight += base.Position;
			return collisionBox;
		}
	}

	public Option(Game game)
		: base(game)
	{
		LoadAnimation(new AnimationData("GFX/Sprites/option"));
		base.DrawOrder = 22;
		timers.Add(startuptimer);
		timers.Add(positionshifttimer);
		SetHitPoints(2, scaleWithDifficulty: false);
	}

	public override void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		base.OnComponentRemoved(e);
		if (e.GameComponent == owner)
		{
			Die();
		}
	}

	public static Option NewOption(ComponentBin collection, Game game)
	{
		Option option = collection.Recycle<Option>();
		if (option == null)
		{
			option = new Option(game);
		}
		return option;
	}

	public void Setup(PlayerShip owner, float angle, int layer, int player)
	{
		this.player = player;
		radius = 20 + 20 * layer;
		prevangle = angle;
		this.owner = owner;
		this.angle = angle;
		targetangle = angle;
		base.Position = owner.GetPosition();
	}

	// Online co-op (card c5228350): drop this option out of the world with no death FX, cue or
	// score -- the owner simply reports fewer than this peer is flying, which is not a kill.
	// Die() is protected, so PlayerShip.NetSetOptionCounts cannot reach it directly; it is the
	// right primitive anyway (it flags isdead, which Initialize clears when the pool hands the
	// component out again).
	internal void NetDespawn()
	{
		Die();
	}

	public void SetAngle(float angle)
	{
		prevangle = this.angle;
		targetangle = angle;
		positionshifttimer.Reset();
		positionshifttimer.Start();
	}

	public override void Initialize()
	{
		base.Initialize();
		rotation = 0f;
		startuptimer.Start();
	}

	public override void Draw(GameTime gameTime)
	{
		if (oracle.Hue(player) != -1f)
		{
			spriteBatch.colorizeEffect.Enable();
			spriteBatch.colorizeEffect.RangeTarget = new Vector3(180f, 250f, oracle.Hue(player));
		}
		base.Draw(gameTime);
		spriteBatch.colorizeEffect.Disable();
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);
		rotation -= (float)Math.PI * 2f * (float)gameTime.ElapsedGameTime.TotalSeconds / 5f;
		float num = ((!startuptimer.Active) ? radius : ((1f - startuptimer.Normalized) * radius));
		if (positionshifttimer.Active)
		{
			angle = prevangle + MyMath.DifferenceMod(prevangle, targetangle, (float)Math.PI * 2f) * (1f - positionshifttimer.Normalized);
		}
		if (positionshifttimer.Finished)
		{
			angle = targetangle;
			prevangle = angle;
			positionshifttimer.Reset();
		}
		float num2 = MyMath.Mod((float)gameTime.TotalGameTime.TotalSeconds * ((float)owner.OptionLevel * 0.7f + 1f), 1.75f) / 1.75f;
		if (owner.OptionLevel >= 3)
		{
			num *= 1.15f;
		}
		base.Position = MyMath.AngleToVector(angle + num2 * ((float)Math.PI * 2f)) * num + owner.GetPosition();
	}

	protected override void HitBy(ICollidable other, bool isComboGenerator)
	{
	}

	public override void CollidesWith(ICollidable other)
	{
		base.CollidesWith(other);
		if (other is EvilBullet)
		{
			base.HitBy(other, isComboGenerator: false);
		}
		if (other is Lazer && owner.OptionLevel <= 3)
		{
			base.HitBy(other, isComboGenerator: false);
		}
		if (other is UFO || other is Boss || other is Braineroid || other is Asteroid || other is Ball || other is JunkBoss || other is DeathStar || other is ClassicBoss || other is BattleSkull || other is Spider || other is EvilSkull || other is FlyingSpider || other is StarMine || other is PlasmaBall || other is BrainBoss || other is FakeBoss || other is MarsBoss || other is PunchingBag)
		{
			Die();
		}
		if (other is Floorbottom)
		{
			base.Position = new Vector2(base.Position.X, ((Floorbottom)other).Bottom - ((CollisionBox)GetCollisionType()).Height / 2f);
		}
	}

	protected override void KilledBy(ICollidable other, bool isComboGenerator)
	{
		Die();
	}

	public bool CausesCombo()
	{
		return true;
	}

	public bool CanHitBosses()
	{
		return true;
	}

	public int Player()
	{
		return player;
	}
}
