using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class Parachute : KillableAlien
{
	private Timer appeartimer = new Timer(300f, repeating: false);

	private Timer disappeartimer = new Timer(100f, repeating: false);

	private ParatrooperBrain owner;

	public override ICollisionType CollisionType
	{
		get
		{
			//IL_0009: Unknown result type (might be due to invalid IL or missing references)
			//IL_000f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0014: Unknown result type (might be due to invalid IL or missing references)
			//IL_0020: Unknown result type (might be due to invalid IL or missing references)
			//IL_0026: Unknown result type (might be due to invalid IL or missing references)
			//IL_002b: Unknown result type (might be due to invalid IL or missing references)
			CollisionBox collisionBox = retrieveBoundsFromTexture();
			collisionBox.TopLeft += base.Position;
			collisionBox.BottomRight += base.Position;
			return collisionBox;
		}
	}

	public Parachute(Game game)
		: base(game)
	{
		LoadAnimation(new AnimationData("GFX/Sprites/parachute"));
		((DrawableGameComponent)this).DrawOrder = 20;
		PointValue = 15f;
	}

	public override void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		base.OnComponentRemoved(e);
		if (e.GameComponent == owner)
		{
			owner = null;
			Remove();
		}
	}

	public static Parachute NewAlien(ComponentBin collection, Game game)
	{
		Parachute parachute = collection.Recycle<Parachute>();
		if (parachute == null)
		{
			parachute = new Parachute(game);
		}
		return parachute;
	}

	public void Setup(ParatrooperBrain owner)
	{
		this.owner = owner;
	}

	public override void Initialize()
	{
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		base.Initialize();
		appeartimer.Reset();
		appeartimer.Start();
		disappeartimer.Stop();
		disappeartimer.Reset();
		scale = 0.001f;
		color = Color.White;
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		float num = (1f - disappeartimer.Normalized) * 10f;
		base.Position += new Vector2(num, 0f);
		base.Draw(gameTime);
		base.Position -= new Vector2(num, 0f);
	}

	public override void Update(GameTime gameTime)
	{
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		base.Update(gameTime);
		appeartimer.Update(gameTime);
		disappeartimer.Update(gameTime);
		color = new Color(new Vector4(1f, 1f, 1f, disappeartimer.Normalized));
		scale = MathHelper.Lerp(0.25f, 0f, appeartimer.Normalized);
		if (disappeartimer.Finished)
		{
			Die();
		}
	}

	public override void CollidesWith(ICollidable other)
	{
		base.CollidesWith(other);
	}

	protected override void KilledBy(ICollidable other, bool isComboGenerator)
	{
		disappeartimer.Start();
		AwardScore(combo: false, other);
	}

	internal void Remove()
	{
		disappeartimer.Start();
	}
}
