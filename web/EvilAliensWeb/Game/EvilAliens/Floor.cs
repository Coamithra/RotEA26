using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using EvilAliensWeb.Compat;

namespace EvilAliens;

public class Floor : DrawableGameComponent, ICollidable, IComponentWatcher
{
	private struct Shadow
	{
		public float x;

		public float height;

		public float size;

		// Extra y nudge along the floor line, from the caster's ShadowOffset.Y (0 for
		// everything except a tuned landed Mars UFO).
		public float yoff;
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
		floorbottom = new Floorbottom(base.Game, 560f);
		shadows = new List<Shadow>();
		spriteBatch = ServiceHelper.Get<ISpriteBatchWrapperService>().SpriteBatchWrapper;
		collection = ServiceHelper.Get<IComponentBinService>().ComponentBin;
		base.DrawOrder = 2;
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		shadowimage = ServiceHelper.Get<IContentManagerService>().ContentManager.Load<Texture2D>("GFX/Sprites/shadow");
	}

	public override void Draw(GameTime gameTime)
	{
		foreach (Shadow shadow in shadows)
		{
			// shadow.size (in CollidesWith) already self-normalises against shadowimage.Width, so
			// the draw scale needs NO supersample divide -- a resized shadow.png is auto-corrected
			// there. The former "/ SuperSampleFactor" double-compensated and shrank the shadow to
			// 1/4 (the "Mars shadow too small" bug); this matches the original decompiled draw.
			// Extracted to DrawShadowScalars so the sprite harness renders an IDENTICAL shadow.
			DrawShadowScalars(spriteBatch, shadowimage, shadow.x, shadow.height, shadow.size, shadow.yoff);
		}
		base.Draw(gameTime);
	}

	// The Floor's shadow math, extracted static so the sprite harness (Compat/HarnessScene) can render
	// a shadow IDENTICAL to what ships -- its scene has no Floor, so it used to hand-roll a different
	// (fainter / mis-sized / mis-placed) shadow, which made by-eye shadow tuning in the harness not
	// match live play. ShadowScalars = the CollidesWith build (a caster's box + its ShadowOffset/
	// ShadowSize -> the draw scalars); DrawShadowScalars = the Draw step. KEEP ShadowScalars in lockstep
	// with the inline math in CollidesWith below (same formula), and DrawShadowScalars with Draw above.
	public static void ShadowScalars(CollisionBox box, Vector2 shadowOffset, float shadowSize, int shadowWidth,
		out float x, out float height, out float size, out float yoff)
	{
		x = box.Left + (box.Right - box.Left) / 2f + shadowOffset.X;
		height = MathHelper.Clamp(1f - (560f - box.Bottom) / 310f, 0f, 1f);
		size = (box.Right - box.Left) / ((float)shadowWidth * 0.7f) * shadowSize;
		yoff = shadowOffset.Y;
	}

	public static void DrawShadowScalars(SpriteBatchWrapper spriteBatch, Texture2D shadowimage, float x, float height, float size, float yoff)
	{
		Color color = new Color(new Vector4(1f, 1f, 1f, height));
		spriteBatch.Draw(shadowimage, new Vector2(x, MathHelper.Lerp(520f, 560f, height) + yoff), 0f, size * (2f - height), center: true, color);
	}

	public override void Initialize()
	{
		shadows.Clear();
		collection.Add((GameComponent)(object)floorbottom);
		base.Initialize();
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);
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
			item.size = (collisionBox.Right - collisionBox.Left) / ((float)shadowimage.LogicalWidth() * 0.7f);
			// Per-caster shadow tuning (identity for all but a tuned landed Mars UFO).
			if (other is AlienDrawableGameComponent adgc)
			{
				item.x += adgc.ShadowOffset.X;
				item.yoff = adgc.ShadowOffset.Y;
				item.size *= adgc.ShadowSize;
			}
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
