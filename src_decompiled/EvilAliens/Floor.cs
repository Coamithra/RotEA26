using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

public class Floor : DrawableGameComponent, ICollidable, IComponentWatcher
{
	private struct Shadow
	{
		public float x;

		public float height;

		public float size;
	}

	public const float bottom = 560f;

	private const float top = 250f;

	private Texture2D shadowimage;

	private ComponentBin collection;

	private Floorbottom floorbottom;

	private SpriteBatchWrapper spriteBatch;

	private List<Shadow> shadows;

	private CollisionBox b = new CollisionBox(Vector2.Zero, Vector2.One);

	public float Bottom => 560f;

	public Floor(Game game)
		: base(game)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		floorbottom = new Floorbottom(((GameComponent)this).Game, 560f);
		shadows = new List<Shadow>();
		spriteBatch = ServiceHelper.Get<ISpriteBatchWrapperService>().SpriteBatchWrapper;
		collection = ServiceHelper.Get<IComponentBinService>().ComponentBin;
		((DrawableGameComponent)this).DrawOrder = 2;
	}

	protected override void LoadContent()
	{
		((DrawableGameComponent)this).LoadContent();
		shadowimage = ServiceHelper.Get<IContentManagerService>().ContentManager.Load<Texture2D>("GFX/Sprites/shadow");
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0061: Unknown result type (might be due to invalid IL or missing references)
		//IL_0081: Unknown result type (might be due to invalid IL or missing references)
		Color color = default(Color);
		foreach (Shadow shadow in shadows)
		{
			((Color)(ref color))._002Ector(new Vector4(1f, 1f, 1f, shadow.height));
			spriteBatch.Draw(shadowimage, new Vector2(shadow.x, MathHelper.Lerp(520f, 560f, shadow.height)), 0f, shadow.size * (2f - shadow.height), center: true, color);
		}
		((DrawableGameComponent)this).Draw(gameTime);
	}

	public override void Initialize()
	{
		shadows.Clear();
		collection.Add((GameComponent)(object)floorbottom);
		((DrawableGameComponent)this).Initialize();
	}

	public override void Update(GameTime gameTime)
	{
		((GameComponent)this).Update(gameTime);
		shadows.Clear();
	}

	public bool DetectCollision(ICollidable other)
	{
		if (!(other is AlienDrawableGameComponent) || ((AlienDrawableGameComponent)other).Collides)
		{
			return GetCollisionType().TestCollision(other.GetCollisionType());
		}
		return false;
	}

	public ICollisionType GetCollisionType()
	{
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		b.TopLeft = new Vector2(-500f, 250f);
		b.BottomRight = new Vector2(1300f, 1100f);
		return b;
	}

	public void CollidesWith(ICollidable other)
	{
		if (other is Floorbottom)
		{
			return;
		}
		CollisionBox collisionBox = null;
		if (other.GetCollisionType() is CollisionBox)
		{
			collisionBox = (CollisionBox)other.GetCollisionType();
		}
		if (other.GetCollisionType() is CollisionMultibox)
		{
			CollisionMultibox collisionMultibox = other.GetCollisionType() as CollisionMultibox;
			if (collisionMultibox.Items.Count > 0)
			{
				collisionBox = collisionMultibox.Items[0];
			}
		}
		if (collisionBox != null)
		{
			Shadow item = default(Shadow);
			item.x = collisionBox.Left + (collisionBox.Right - collisionBox.Left) / 2f;
			item.height = (560f - collisionBox.Bottom) / 310f;
			item.height = MathHelper.Clamp(1f - item.height, 0f, 1f);
			item.size = (collisionBox.Right - collisionBox.Left) / ((float)shadowimage.Width * 0.7f);
			shadows.Add(item);
		}
	}

	public void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		if (e.GameComponent == this)
		{
			collection.Remove((GameComponent)(object)floorbottom);
		}
	}

	public void OnComponentAdded(GameComponentCollectionEventArgs e)
	{
	}
}
