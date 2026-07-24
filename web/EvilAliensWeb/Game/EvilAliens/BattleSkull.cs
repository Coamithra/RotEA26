using System;
using EvilAliens.Constants;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class BattleSkull : KillableAlien
{
	private enum BattleSkullState
	{
		normal,
		dying
	}

	private bool fired;

	private AnimatedSprite sprite;

	private Texture2D blank;

	private Timer DeathTimer = new Timer(2500f, repeating: false);

	private float animationProgress;

	private BattleSkullState state;

	private CollisionMultibox boxes;

	public override ICollisionType CollisionType
	{
		get
		{
			if (boxes == null)
			{
				boxes = new CollisionMultibox();
				boxes.Items.Add(new CollisionBox());
				boxes.Items.Add(new CollisionBox());
			}
			boxes.Items[0].Width = MainGame.AlienBossSizeOne.X * scale;
			boxes.Items[0].Height = MainGame.AlienBossSizeOne.Y * scale;
			boxes.Items[0].CenterAround(base.Position - new Vector2(0f, 15f * scale));
			boxes.Items[1].Width = MainGame.AlienBossSizeTwo.X * scale;
			boxes.Items[1].Height = MainGame.AlienBossSizeTwo.Y * scale;
			boxes.Items[1].CenterAround(base.Position - new Vector2(0f, -30f * scale));
			return boxes;
		}
	}

	public BattleSkull(Game game)
		: base(game)
	{
		base.DrawOrder = 17;
		SetHitPoints(25, scaleWithDifficulty: false);
		PointValue = 1000f;
		timers.Add(DeathTimer);
	}

	public static BattleSkull NewBattleSkull(ComponentBin collection, Game game)
	{
		BattleSkull battleSkull = collection.Recycle<BattleSkull>();
		if (battleSkull == null)
		{
			battleSkull = new BattleSkull(game);
		}
		return battleSkull;
	}

	public void Setup(Vector2 position)
	{
		base.Position = position;
	}

	protected override void LoadContent()
	{
		blank = content.Load<Texture2D>("GFX/Game/blank");
		sprite = new AnimatedSprite("GFX/alienboss/alienboss");
		base.LoadContent();
	}

	public override void Initialize()
	{
		scale = 1f;
		base.Collides = true;
		state = BattleSkullState.normal;
		base.Speed = 0.06f;
		base.Colorize = true;
		base.Direction = -(float)Math.PI / 2f;
		base.Initialize();
		fired = false;
	}

	public override void Draw(GameTime gameTime)
	{
		if (isBlinking())
		{
			spriteBatch.lightenEffect.Enable();
		}
		if (state != BattleSkullState.dying)
		{
			int num = (int)(base.HitPointsNormalized * 100f);
			// In-game: recolour the sprite's hue band (-10,10) toward a target hue that
			// sweeps with HP (100 = green full HP -> 0 = red dead). The sprite harness
			// (Compat/HarnessScene.cs, ?harness=battleskull) can override the band + target
			// live to tune "the little lightbulbs don't colorize well" — see the ?huestart/
			// ?hueend/?huetarget/?huecycle flags in Compat/DebugFlags.cs. Overrides only take
			// effect while the harness is up, so normal play is byte-identical.
			Vector3 range = new Vector3(-10f, 10f, (float)num);
			range = EvilAliensWeb.Compat.HarnessColorize.Apply(range, gameTime);
			spriteBatch.colorizeEffect.RangeTarget = range;
			spriteBatch.colorizeEffect.Enable();
		}
		spriteBatch.BlendMode = (SpriteBlendMode)1;
		sprite.Draw((int)animationProgress, base.Position, color, scale, center: true);
		if (isBlinking())
		{
			spriteBatch.lightenEffect.Disable();
		}
		if (state != BattleSkullState.dying)
		{
			spriteBatch.colorizeEffect.Disable();
		}
	}

	public override void Update(GameTime gameTime)
	{
		animationProgress = MyMath.Mod(animationProgress + (float)gameTime.ElapsedGameTime.TotalSeconds * 20f, sprite.Frames);
		base.Update(gameTime);
		switch (state)
		{
		case BattleSkullState.normal:
			if ((base.Position.Y < 100f) & !fired)
			{
				fired = true;
				for (int i = 0; i < (int)(Settings.GetInstance().DifficultyModifier * 5f); i++)
				{
					float direction = (float)i * ((float)Math.PI * 2f) / (float)(int)(Settings.GetInstance().DifficultyModifier * 5f);
					EvilSkull evilSkull = EvilSkull.NewEvilSkull(collection, base.Game);
					evilSkull.SetupLaunch(base.Position + new Vector2(0f, 50f), direction);
					collection.Add((GameComponent)(object)evilSkull);
				}
			}
			if (base.Position.Y < -100f)
			{
				Die();
			}
			break;
		case BattleSkullState.dying:
		{
			if (DeathTimer.Finished)
			{
				Explosion explosion = Explosion.NewExplosion(collection, base.Game);
				explosion.Setup(base.Position, 3.5f, 2.5f, base.Speed * 0.3f, base.Direction);
				collection.Add((GameComponent)(object)explosion);
				explosion = Explosion.NewExplosion(collection, base.Game);
				explosion.Setup(base.Position, 2f, 1.3f, base.Speed * 0.95f, base.Direction);
				collection.Add((GameComponent)(object)explosion);
				sound.PlayCue("expl2");
				Die();
			}
			float num = MyMath.PowerCurve(0f, 1f, 2f, 1f - DeathTimer.Normalized);
			scale = MathHelper.Lerp(1f, 0.66f, num);
			color = new Color(new Vector3(MathHelper.Lerp(1f, 0.5f, num)));
			if ((double)RandomHelper.RandomNextFloat(0f, 1f) <= (double)MathHelper.Lerp(8f, 24f, num) * gameTime.ElapsedGameTime.TotalSeconds)
			{
				// Trello 8e439865: this is the death-flicker SERIES (many small pops over the
				// ~2.5s dying animation, before the DeathTimer.Finished finale below) — no shake
				// per pop, or a procession of these minibosses dying together rattles the screen
				// nonstop. The finale explosions above keep their shake.
				Explosion explosion2 = Explosion.NewExplosion(collection, base.Game);
				explosion2.Setup(base.Position + new Vector2(RandomHelper.RandomNextFloat(-60f, 60f), RandomHelper.RandomNextFloat(-90f, 90f)) * scale, 0.8f * scale, 0.8f * scale, 0f, 0f, noShake: true);
				collection.Add((GameComponent)(object)explosion2);
				sound.PlayCue("expl1");
			}
			break;
		}
		}
	}

	protected override void KilledBy(ICollidable other, bool isComboGenerator)
	{
		AwardScore(isComboGenerator, other);
		state = BattleSkullState.dying;
		base.Collides = false;
		DeathTimer.Start();
		DeathTimer.Reset();
		// Trello 8e439865: the opening pop of the death SERIES that continues through the dying
		// animation below (Update's random flicker) — no shake here either; only the
		// DeathTimer.Finished finale explosions (Update, above) still shake the screen.
		Explosion explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(base.Position, 2.3f, 1.3f, base.Speed * 0.95f, base.Direction, noShake: true);
		collection.Add((GameComponent)(object)explosion);
		sound.PlayCue("expl2");
	}

	// ---- Online co-op replication seams (Compat/Net/Descriptors/DescriptorsBosses1) --------
	// The body animation runs off `animationProgress` (its own 20fps clock, NOT the component
	// curframe), advanced only in Update -- frozen on a puppet. The host replicates the current
	// frame so the client's alienboss sprite still animates. The HP-driven hue (Draw's colorize
	// RangeTarget) needs no seam: initial HP is a fixed 25 (scaleWithDifficulty:false), so the
	// replicated absolute Hp reproduces HitPointsNormalized exactly on both peers.
	internal int NetAnimFrame
	{
		get
		{
			return (int)animationProgress;
		}
		set
		{
			animationProgress = value;
		}
	}
}
