using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

// The Brain final boss's glow AURA: a separate ADDITIVE layer drawn BEHIND the brain
// (DrawOrder 2) that FOLLOWS the boss's heartbeat pulse (scale = boss.scale) and layers a
// smooth sine shimmer on top — a gentle size breathe + a brightness pulse — so the energy
// field looks alive independently of the brain's beat. Replaces the old front/back Cables
// (the new HD art bakes the cables into the brain texture). The aura texture
// (GFX/Sprites/brainbossaura) shares the brain's DesignFrameWidth so the two draw aligned.
//
// Animation runs in Draw off gameTime (not Update), so it shimmers even in the sprite
// harness where the boss itself is frozen.
internal class BrainAura : AlienDrawableGameComponent
{
	private const float ShimmerOmega = 2.8f;     // rad/s (~2.2s period) of the extra glow pulse
	private const float ScaleShimmer = 0.025f;   // +-2.5% size breathe ON TOP of the brain pulse
	private const float AlphaBase = 0.65f;
	private const float AlphaShimmer = 0.175f;   // glow brightness rides 0.475 .. 0.825 (inner 50%)

	private BrainBoss boss;

	private CollisionBox b = new CollisionBox(Vector2.Zero, Vector2.One);

	public override ICollisionType CollisionType => b;

	public BrainAura(Game game)
		: base(game)
	{
		base.Collides = false;
	}

	public static BrainAura NewAura(ComponentBin collection, Game game)
	{
		BrainAura aura = collection.Recycle<BrainAura>();
		if (aura == null)
		{
			aura = new BrainAura(game);
		}
		return aura;
	}

	public void Setup(BrainBoss owner)
	{
		boss = owner;
		base.DrawOrder = 2;                 // behind the brain (DrawOrder 21)
		blendMode = (SpriteBlendMode)2;     // Additive — adds light over space
		LoadAnimation(new AnimationData("GFX/Sprites/brainbossaura"));
		base.Position = boss.Position;
	}

	public override void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		base.OnComponentRemoved(e);
		if (e.GameComponent == boss)
		{
			boss = null;
		}
	}

	public override void Draw(GameTime gameTime)
	{
		if (boss == null)
		{
			return;
		}
		float t = (float)gameTime.TotalGameTime.TotalSeconds;
		float s = (float)Math.Sin(t * ShimmerOmega);
		scale = boss.scale * (1f + ScaleShimmer * s);    // follow brain heartbeat + breathe
		float alpha = AlphaBase + AlphaShimmer * s;       // brightness shimmer
		spriteBatch.BlendMode = blendMode;
		spriteBatch.Draw(texture, boss.Position, 0f, DrawScale, center: true, new Color(new Vector4(1f, 1f, 1f, alpha)));
	}

	public override void Update(GameTime gameTime)
	{
		if (boss != null)
		{
			base.Position = boss.Position;
		}
		else
		{
			Die();
		}
	}

	internal void Free()
	{
		boss = null;
	}
}
