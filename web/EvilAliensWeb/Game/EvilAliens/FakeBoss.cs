using System;
using EvilAliens.Constants;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class FakeBoss : KillableAlien
{
	private enum FakeBossState
	{
		entry,
		movemiddleleft,
		moveleftmiddle,
		movemiddleright,
		moverightmiddle,
		spawnufos,
		vomitbullets,
		halfmoonbullets,
		asplode
	}

	private const float finalPosition = 100f;

	private const float SHOOTYTIMERVALUE = 500f;

	private bool forceDifficulty;

	private Settings.DifficultyLevel forcedDifficultyLevel;

	private FakeBossState state;

	private Timer stateTimer = new Timer(1f, repeating: false);

	private Vector2 mouthhotspot = new Vector2(0f, 90f);

	private bool positiveXaxisSpeed;

	private Vector2 startpos;

	private Vector2 stoppos;

	private AnimatedSprite sprite;

	private float animationProgress;

	private Timer shootyTimer = new Timer(500f, repeating: true);

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

	public FakeBoss(Game game)
		: base(game)
	{
		base.DrawOrder = 15;
		scale = 1.2f;
		SetHitPoints(500, scaleWithDifficulty: false);
		base.IsBoss = true;
		base.Colorize = true;
		timers.Add(stateTimer);
		PointValue = 3000f;
	}

	public static FakeBoss NewFakeBoss(ComponentBin collection, Game game)
	{
		FakeBoss fakeBoss = collection.Recycle<FakeBoss>();
		if (fakeBoss == null)
		{
			fakeBoss = new FakeBoss(game);
		}
		return fakeBoss;
	}

	public void Setup()
	{
		forceDifficulty = false;
	}

	public override void Initialize()
	{
		GamerCollectionEnumerator<SignedInGamer> enumerator = ((GamerCollection<SignedInGamer>)(object)Gamer.SignedInGamers).GetEnumerator();
		try
		{
			while (enumerator.MoveNext())
			{
				SignedInGamer current = enumerator.Current;
				current.Presence.PresenceMode = (GamerPresenceMode)34;
			}
		}
		finally
		{
			((IDisposable)enumerator).Dispose();
		}
		base.Position = new Vector2(400f, (0f - ((CollisionMultibox)CollisionType).Items[0].Height * scale) / 2f);
		state = FakeBossState.entry;
		stateTimer.Duration = 5000f;
		stateTimer.Start();
		base.Collides = true;
		shootyTimer.Reset();
		shootyTimer.Start();
		base.Initialize();
		if (isClassic())
		{
			base.HitPoints = 175;
		}
	}

	public override void Draw(GameTime gameTime)
	{
		if (isBlinking())
		{
			spriteBatch.lightenEffect.Enable();
		}
		spriteBatch.BlendMode = (SpriteBlendMode)1;
		sprite.Draw((int)animationProgress, base.Position, color, scale, center: true);
		if (isBlinking())
		{
			spriteBatch.lightenEffect.Disable();
		}
	}

	public override void Update(GameTime gameTime)
	{
		animationProgress = MyMath.Mod(animationProgress + (float)gameTime.ElapsedGameTime.TotalSeconds * 20f, sprite.Frames);
		switch (state)
		{
		case FakeBossState.movemiddleleft:
		case FakeBossState.moveleftmiddle:
		case FakeBossState.movemiddleright:
		case FakeBossState.moverightmiddle:
			shootyTimer.Update(gameTime);
			if (shootyTimer.Finished)
			{
				float direction = MyMath.VectorToAngle(oracle.GetRandomPlayerPosition() - (base.Position + mouthhotspot));
				EvilBullet evilBullet = EvilBullet.NewEvilBullet(collection, base.Game);
				evilBullet.Setup(base.Position + mouthhotspot, direction);
				collection.Add((GameComponent)(object)evilBullet);
				shootyTimer.Duration = 500f / Settings.GetInstance().DifficultyModifier;
			}
			break;
		}
		switch (state)
		{
		case FakeBossState.entry:
			base.Position = new Vector2(base.Position.X, MathHelper.SmoothStep(100f, (0f - ((CollisionMultibox)CollisionType).Items[0].Height * scale) / 2f, stateTimer.Normalized));
			if (stateTimer.Finished)
			{
				state = FakeBossState.movemiddleleft;
				base.Position = new Vector2(400f, 100f);
				ResetTimerForMovement();
				startpos = new Vector2(400f, 100f);
				stoppos = new Vector2(50f, 100f);
			}
			break;
		case FakeBossState.movemiddleleft:
		{
			float num = MathHelper.SmoothStep(1f, 0f, stateTimer.Normalized);
			base.Position = Vector2.Lerp(startpos, stoppos, num);
			if (stateTimer.Finished)
			{
				state = FakeBossState.moveleftmiddle;
				ResetTimerForMovement();
				startpos = new Vector2(50f, 100f);
				stoppos = new Vector2(400f, 100f);
			}
			break;
		}
		case FakeBossState.moveleftmiddle:
		{
			float num = MathHelper.SmoothStep(1f, 0f, stateTimer.Normalized);
			base.Position = Vector2.Lerp(startpos, stoppos, num);
			if (stateTimer.Finished)
			{
				positiveXaxisSpeed = true;
				DoSpecial();
			}
			break;
		}
		case FakeBossState.movemiddleright:
		{
			float num = MathHelper.SmoothStep(1f, 0f, stateTimer.Normalized);
			base.Position = Vector2.Lerp(startpos, stoppos, num);
			if (stateTimer.Finished)
			{
				state = FakeBossState.moverightmiddle;
				ResetTimerForMovement();
				startpos = new Vector2(750f, 100f);
				stoppos = new Vector2(400f, 100f);
			}
			break;
		}
		case FakeBossState.moverightmiddle:
		{
			float num = MathHelper.SmoothStep(1f, 0f, stateTimer.Normalized);
			base.Position = Vector2.Lerp(startpos, stoppos, num);
			if (stateTimer.Finished)
			{
				positiveXaxisSpeed = false;
				DoSpecial();
			}
			break;
		}
		case FakeBossState.halfmoonbullets:
		{
			int num4 = (int)(12f * GetPersonalDifficultyModifier());
			float num5 = 0f;
			for (int k = 0; k < num4; k++)
			{
				float direction = (float)k * (float)Math.PI / (float)num4 + num5;
				EvilBullet evilBullet = EvilBullet.NewEvilBullet(collection, base.Game);
				evilBullet.Setup(base.Position + mouthhotspot, direction);
				collection.Add((GameComponent)(object)evilBullet);
			}
			RestartMoving(50f);
			break;
		}
		case FakeBossState.vomitbullets:
			if (stateTimer.Finished)
			{
				RestartMoving(50f);
			}
			if (RandomHelper.RandomFromAverage(6.5f * GetPersonalDifficultyFactorized(0.5f), gameTime))
			{
				float num2 = MyMath.VectorToAngle(oracle.GetRandomPlayerPosition() - (base.Position + mouthhotspot));
				num2 += RandomHelper.RandomNextFloat(-0.3f, 0.3f);
				EvilBullet evilBullet = EvilBullet.NewEvilBullet(collection, base.Game);
				evilBullet.Setup(base.Position + mouthhotspot, num2);
				collection.Add((GameComponent)(object)evilBullet);
			}
			break;
		case FakeBossState.spawnufos:
			if (stateTimer.Finished)
			{
				RestartMoving(50f);
			}
			if (RandomHelper.RandomFromAverage(5f * GetPersonalDifficultyFactorized(0.5f), gameTime))
			{
				float num3 = MyMath.VectorToAngle(oracle.GetRandomPlayerPosition() - (base.Position + mouthhotspot));
				num3 += RandomHelper.RandomNextFloat(-5f, 5f);
				UFO uFO = UFO.NewUFO(collection, base.Game);
				uFO.Setup(behaviour: isClassic() ? EnemyBehaviour.classic : EnemyBehaviour.normal, position: base.Position + mouthhotspot, isBig: false);
				uFO.SetDirection(num3);
				uFO.SpeedUp();
				if (!isClassic() && RandomHelper.RandomNextFloat(0f, 1f) <= 0.1f)
				{
					uFO.SetAsBonus();
				}
				uFO.SetInvincible();
				collection.Add((GameComponent)(object)uFO);
			}
			break;
		case FakeBossState.asplode:
		{
			if (RandomHelper.RandomFromAverage(10f, gameTime))
			{
				Explosion explosion = Explosion.NewExplosion(collection, base.Game);
				FindSpawnSpot(out var angle, out var range);
				Vector2 position = MyMath.AngleToVector(angle) * range + base.Position;
				explosion.Setup(position, 1f, 1f, 0.12f, angle);
				collection.Add((GameComponent)(object)explosion);
				sound.PlayCue("expl1");
			}
			if (!stateTimer.Finished)
			{
				break;
			}
			for (int i = 0; i < 5; i++)
			{
				for (int j = 0; j < 15; j++)
				{
					BloodExplosion bloodExplosion = BloodExplosion.NewExplosion(collection, base.Game);
					FindSpawnSpot(out var angle2, out var range2);
					Vector2 position = MyMath.AngleToVector(angle2) * range2 + base.Position;
					bloodExplosion.Setup(position, 5f + (float)j / 5f, 1f + (float)j / 5f, 0f, base.Direction);
					collection.Add((GameComponent)(object)bloodExplosion);
				}
			}
			sound.PlayCue("head asplode");
			AwardScoreToAll(combo: true);
			Die();
			break;
		}
		}
		base.Update(gameTime);
	}

	private void ResetTimerForMovement()
	{
		stateTimer.Duration = 1080f / GetPersonalDifficultyModifier();
		stateTimer.Start();
		stateTimer.Reset();
	}

	private static void FindSpawnSpot(out float angle, out float range)
	{
		angle = RandomHelper.RandomNextAngle();
		range = MyMath.PowerCurve(100f, 0f, 2f, RandomHelper.RandomNextFloat(0f, 1f));
	}

	private void DoSpecial()
	{
		switch (RandomHelper.Random.Next(3))
		{
		case 0:
			state = FakeBossState.halfmoonbullets;
			break;
		case 1:
			state = FakeBossState.spawnufos;
			stateTimer.Duration = 4200f;
			stateTimer.Reset();
			stateTimer.Start();
			break;
		case 2:
			state = FakeBossState.vomitbullets;
			stateTimer.Duration = 4200f;
			stateTimer.Reset();
			stateTimer.Start();
			break;
		}
	}

	private void RestartMoving(float edge)
	{
		if (positiveXaxisSpeed)
		{
			state = FakeBossState.movemiddleright;
			ResetTimerForMovement();
			startpos = new Vector2(400f, 100f);
			stoppos = new Vector2(800f - edge, 100f);
		}
		else
		{
			state = FakeBossState.movemiddleleft;
			ResetTimerForMovement();
			startpos = new Vector2(400f, 100f);
			stoppos = new Vector2(edge, 100f);
		}
	}

	public override void CollidesWith(ICollidable other)
	{
		base.CollidesWith(other);
	}

	protected override void KilledBy(ICollidable other, bool isComboGenerator)
	{
		state = FakeBossState.asplode;
		stateTimer.Duration = 4000f;
		stateTimer.Reset();
		stateTimer.Start();
		base.Collides = false;
		Explosion explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(base.Position, 3.5f, 2.5f, 0f, 0f);
		collection.Add((GameComponent)(object)explosion);
		explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(base.Position, 2f, 1.3f, 0f, 0f);
		collection.Add((GameComponent)(object)explosion);
		sound.PlayCue("expl2");
	}

	internal void ForceDifficulty(Settings.DifficultyLevel forcedDifficultyLevel)
	{
		forceDifficulty = true;
		this.forcedDifficultyLevel = forcedDifficultyLevel;
	}

	private float GetPersonalDifficultyModifier()
	{
		if (forceDifficulty)
		{
			return Settings.GetInstance().GetDifficultyValue(forcedDifficultyLevel);
		}
		return Settings.GetInstance().DifficultyModifier;
	}

	private float GetPersonalDifficultyFactorized(float factor)
	{
		return 1f + (GetPersonalDifficultyModifier() - 1f) * factor;
	}

	private bool isClassic()
	{
		return forceDifficulty;
	}

	protected override void LoadContent()
	{
		sprite = new AnimatedSprite("GFX/alienboss/alienboss");
		base.LoadContent();
	}

	// ---- Online co-op replication seams (Compat/Net/Descriptors/DescriptorsCoverage) --------
	// Mirrors ClassicBoss/BattleSkull: the alienboss body runs off `animationProgress` (its own
	// 20fps clock, advanced only in Update -> frozen on a puppet). scale + the HP-redden colorize
	// arrive in the base state (Scale, and Hp -> NetApplyHp; initialhitpoints is a fixed 500
	// either side, so the redden matches exactly). The Update-spawned bullets/UFOs replicate as
	// their own types.
	//
	// The frame is STILL SENT and no longer APPLIED (card 5f506d11): a puppet runs the loop
	// itself in NetDriveExtras below. Same seam, same audit, same reason as BattleSkull's --
	// Compat/Net/NetBodyAnim.
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

	// The client half: advance the body loop on the driver's REAL dt, exactly as Update does.
	internal override void NetDriveExtras(GameTime gameTime)
	{
		animationProgress = EvilAliensWeb.Compat.Net.NetBodyAnim.Advance(animationProgress,
			(float)gameTime.ElapsedGameTime.TotalSeconds, 20f, (sprite != null) ? sprite.Frames : 0);
	}
}
