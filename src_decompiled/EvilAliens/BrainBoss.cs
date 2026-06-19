using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.GamerServices;

namespace EvilAliens;

internal class BrainBoss : KillableAlien
{
	private enum BossState
	{
		entry,
		wait,
		spawnstuff,
		asplode,
		smallwaitafterasplosion
	}

	private enum StuffToSpawn
	{
		brainz,
		bulletz,
		skullz,
		ufoz,
		minez
	}

	private const float plasmatimerstart = 2500f;

	private const float plasmatimermax = 800f;

	private const float pulsatestart = 1600f;

	private const float pulsatemax = 700f;

	private const float pulsateextrastart = 0.04f;

	private const float pulsateextramax = 0.1f;

	private bool isChallengeBoss;

	private Curve pulsateCurve;

	private Timer pulsetimer;

	private Timer soundtimer;

	private Timer spawnsoundtimer;

	private Timer stateTimer;

	private Timer brainspawntimer;

	private Timer plasmatimer;

	private float[] spawnTime;

	private StuffToSpawn stuff;

	private BossState state;

	private Cables cablesfront;

	private Cables cablesback;

	private static List<StuffToSpawn> stuffToSpawnValues = Game1.GetEnumValues<StuffToSpawn>();

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

	public BrainBoss(Game game)
		: base(game)
	{
		LoadAnimation(new AnimationData("GFX/Sprites/brainlargetransglow"));
		((DrawableGameComponent)this).DrawOrder = 21;
		SetHitPoints(1700, scaleWithDifficulty: false);
		pulsetimer = new Timer(1600f, repeating: true);
		timers.Add(pulsetimer);
		soundtimer = new Timer(2000f, repeating: false);
		timers.Add(soundtimer);
		stateTimer = new Timer(42f, repeating: false);
		timers.Add(stateTimer);
		brainspawntimer = new Timer(20f, repeating: true);
		timers.Add(brainspawntimer);
		spawnsoundtimer = new Timer(500f, repeating: false);
		timers.Add(spawnsoundtimer);
		plasmatimer = new Timer(2500f, repeating: true);
		timers.Add(plasmatimer);
		base.Colorize = true;
		base.IsBoss = true;
		spawnTime = new float[Game1.GetEnumValues<StuffToSpawn>().Count];
		spawnTime[0] = 26.6f;
		spawnTime[3] = 93.100006f;
		spawnTime[2] = 133f;
		spawnTime[4] = 425.6f;
		spawnTime[1] = 19.95f;
		PointValue = 5000f;
	}

	public static BrainBoss NewBrainBoss(ComponentBin collection, Game game)
	{
		BrainBoss brainBoss = collection.Recycle<BrainBoss>();
		if (brainBoss == null)
		{
			brainBoss = new BrainBoss(game);
		}
		return brainBoss;
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		pulsateCurve = content.Load<Curve>("GFX/Effects/BrainCurve");
	}

	public void Setup(bool challenge)
	{
		isChallengeBoss = challenge;
	}

	public override void Initialize()
	{
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_0070: Unknown result type (might be due to invalid IL or missing references)
		base.Initialize();
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
		Vector2 position = default(Vector2);
		position.X = 400f;
		position.Y = (0f - (float)texture.Height) / 2f;
		base.Position = position;
		pulsetimer.Duration = 1600f;
		stateTimer.Duration = 6234f;
		stateTimer.Start();
		stateTimer.Reset();
		soundtimer.Stop();
		state = BossState.entry;
		stuff = StuffToSpawn.brainz;
		base.Collides = true;
		scale = 1f;
		cablesback = Cables.NewAlien(collection, ((GameComponent)this).Game);
		cablesback.Setup(this, front: false);
		cablesfront = Cables.NewAlien(collection, ((GameComponent)this).Game);
		cablesfront.Setup(this, front: true);
		collection.Add((GameComponent)(object)cablesback);
		collection.Add((GameComponent)(object)cablesfront);
	}

	public override void Draw(GameTime gameTime)
	{
		base.Draw(gameTime);
	}

	public override void Update(GameTime gameTime)
	{
		//IL_0552: Unknown result type (might be due to invalid IL or missing references)
		//IL_055e: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00be: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ce: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ee: Unknown result type (might be due to invalid IL or missing references)
		//IL_057a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0589: Unknown result type (might be due to invalid IL or missing references)
		//IL_0180: Unknown result type (might be due to invalid IL or missing references)
		//IL_0187: Unknown result type (might be due to invalid IL or missing references)
		//IL_018d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0192: Unknown result type (might be due to invalid IL or missing references)
		//IL_0197: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_0604: Unknown result type (might be due to invalid IL or missing references)
		//IL_060a: Unknown result type (might be due to invalid IL or missing references)
		//IL_060f: Unknown result type (might be due to invalid IL or missing references)
		//IL_061e: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_02b0: Unknown result type (might be due to invalid IL or missing references)
		//IL_02b6: Unknown result type (might be due to invalid IL or missing references)
		//IL_02bb: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c0: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_030a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0311: Unknown result type (might be due to invalid IL or missing references)
		//IL_0317: Unknown result type (might be due to invalid IL or missing references)
		//IL_031c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0321: Unknown result type (might be due to invalid IL or missing references)
		//IL_0338: Unknown result type (might be due to invalid IL or missing references)
		//IL_038b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0392: Unknown result type (might be due to invalid IL or missing references)
		//IL_0398: Unknown result type (might be due to invalid IL or missing references)
		//IL_039d: Unknown result type (might be due to invalid IL or missing references)
		//IL_03a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_03b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_0224: Unknown result type (might be due to invalid IL or missing references)
		//IL_022b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0231: Unknown result type (might be due to invalid IL or missing references)
		//IL_0236: Unknown result type (might be due to invalid IL or missing references)
		//IL_023b: Unknown result type (might be due to invalid IL or missing references)
		//IL_023f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0413: Unknown result type (might be due to invalid IL or missing references)
		//IL_041f: Unknown result type (might be due to invalid IL or missing references)
		//IL_042e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0433: Unknown result type (might be due to invalid IL or missing references)
		//IL_043f: Unknown result type (might be due to invalid IL or missing references)
		//IL_044e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0453: Unknown result type (might be due to invalid IL or missing references)
		//IL_0473: Unknown result type (might be due to invalid IL or missing references)
		//IL_087c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0883: Unknown result type (might be due to invalid IL or missing references)
		//IL_088a: Unknown result type (might be due to invalid IL or missing references)
		//IL_088f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0894: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a55: Unknown result type (might be due to invalid IL or missing references)
		//IL_09d6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a28: Unknown result type (might be due to invalid IL or missing references)
		//IL_0918: Unknown result type (might be due to invalid IL or missing references)
		//IL_08e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a6e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a75: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a7c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a81: Unknown result type (might be due to invalid IL or missing references)
		//IL_0979: Unknown result type (might be due to invalid IL or missing references)
		//IL_096c: Unknown result type (might be due to invalid IL or missing references)
		UpdateMusic();
		pulsetimer.Duration = MathHelper.Lerp(700f, 1600f, base.HitPointsNormalized);
		float num = MathHelper.Lerp(0.1f, 0.04f, base.HitPointsNormalized);
		scale = 1f + num * pulsateCurve.Evaluate(1f - pulsetimer.Normalized);
		if (base.HitPointsNormalized < 0.33f)
		{
			float hitsPerSec = MathHelper.Lerp(5f, 1f, base.HitPointsNormalized * 3f);
			if (RandomHelper.RandomFromAverage(hitsPerSec, gameTime))
			{
				BloodExplosion bloodExplosion = BloodExplosion.NewExplosion(collection, ((GameComponent)this).Game);
				FindSpawnSpot(out var angle, out var range);
				Vector2 position = MyMath.AngleToVector(angle) * range + base.Position;
				float num2 = MathHelper.Lerp(2f, 0.8f, base.HitPointsNormalized * 3f);
				bloodExplosion.Setup(position, num2, num2, 0.12f, angle);
				collection.Add((GameComponent)(object)bloodExplosion);
			}
		}
		base.Update(gameTime);
		switch (state)
		{
		case BossState.asplode:
		{
			float num3 = MathHelper.Lerp(5f, 1f, stateTimer.Normalized);
			if (RandomHelper.RandomFromAverage(10f * num3, gameTime))
			{
				BloodExplosion bloodExplosion3 = BloodExplosion.NewExplosion(collection, ((GameComponent)this).Game);
				FindSpawnSpot(out var angle3, out var range3);
				Vector2 position2 = MyMath.AngleToVector(angle3) * range3 + base.Position;
				float num4 = 2f;
				bloodExplosion3.Setup(position2, num4, num4, 0.12f, angle3);
				collection.Add((GameComponent)(object)bloodExplosion3);
			}
			if (RandomHelper.RandomFromAverage(3f, gameTime))
			{
				sound.PlayCue("small head asplode");
			}
			if (RandomHelper.RandomFromAverage(1f * num3, gameTime))
			{
				sound.PlayCue("head asplode");
				for (int i = 0; i < 10; i++)
				{
					BloodExplosion bloodExplosion4 = BloodExplosion.NewExplosion(collection, ((GameComponent)this).Game);
					FindSpawnSpot(out var angle4, out var range4);
					Vector2 position3 = MyMath.AngleToVector(angle4) * range4 + base.Position;
					bloodExplosion4.Setup(position3, 5f + (float)i / 5f, 1f + (float)i / 5f, 0f, base.Direction);
					collection.Add((GameComponent)(object)bloodExplosion4);
				}
			}
			if (RandomHelper.RandomFromAverage(0.5f * num3, gameTime))
			{
				FindSpawnSpot(out var angle5, out var range5);
				Vector2 position4 = MyMath.AngleToVector(angle5) * range5 + base.Position;
				Explosion explosion = Explosion.NewExplosion(collection, ((GameComponent)this).Game);
				explosion.Setup(position4, 3.5f, 2.5f, 0f, 0f);
				collection.Add((GameComponent)(object)explosion);
				FindSpawnSpot(out angle5, out range5);
				position4 = MyMath.AngleToVector(angle5) * range5 + base.Position;
				explosion = Explosion.NewExplosion(collection, ((GameComponent)this).Game);
				explosion.Setup(position4, 2f, 1.3f, 0f, 0f);
				collection.Add((GameComponent)(object)explosion);
				sound.PlayCue("expl2");
			}
			if (RandomHelper.RandomFromAverage(2f * num3, gameTime))
			{
				FindSpawnSpot(out var angle6, out var range6);
				Vector2 position5 = MyMath.AngleToVector(angle6) * range6 + base.Position;
				Explosion explosion2 = Explosion.NewExplosion(collection, ((GameComponent)this).Game);
				explosion2.Setup(position5, 1f, 1f, 0f, 0f);
				collection.Add((GameComponent)(object)explosion2);
				sound.PlayCue("expl1");
			}
			if (stateTimer.Finished)
			{
				sound.PlayCue("expl2");
				UberExplosion(base.Position);
				UberExplosion(base.Position - new Vector2(100f, 0f));
				UberExplosion(base.Position + new Vector2(100f, 0f));
				BloodExplosion bloodExplosion5 = BloodExplosion.NewExplosion(collection, ((GameComponent)this).Game);
				bloodExplosion5.Setup(base.Position, 7f, 3f, 0f, base.Direction);
				collection.Add((GameComponent)(object)bloodExplosion5);
				state = BossState.smallwaitafterasplosion;
				stateTimer.Duration = 700f;
				stateTimer.Reset();
				stateTimer.Start();
				cablesback.Free();
				cablesfront.Free();
			}
			break;
		}
		case BossState.smallwaitafterasplosion:
			scale = MyMath.PowerCurve(0f, 1f, 0.5f, stateTimer.Normalized);
			if (stateTimer.Finished)
			{
				Die();
				AwardScoreToAll(combo: true);
			}
			break;
		case BossState.entry:
		{
			float num5 = MathHelper.SmoothStep(80f, (0f - (float)texture.Height) / 2f, stateTimer.Normalized);
			base.Position = new Vector2(base.Position.X, num5);
			if (stateTimer.Finished)
			{
				base.Position = new Vector2(base.Position.X, 80f);
				base.Speed = 0f;
				state = BossState.wait;
				stateTimer.Duration = 15000f;
				stateTimer.Reset();
				stateTimer.Start();
			}
			break;
		}
		case BossState.wait:
			if (stateTimer.Normalized > 0.2f && plasmatimer.Finished)
			{
				PlasmaBall plasmaBall = PlasmaBall.NewAlien(collection, ((GameComponent)this).Game);
				float direction = MyMath.VectorToAngle(oracle.GetRandomPlayerPosition() - base.Position);
				plasmaBall.Setup(base.Position, direction);
				collection.Add((GameComponent)(object)plasmaBall);
				plasmatimer.Duration = MathHelper.Lerp(800f, 2500f, base.HitPointsNormalized);
			}
			if (stateTimer.Finished)
			{
				state = BossState.spawnstuff;
				stateTimer.Duration = 10000f;
				stateTimer.Reset();
				stateTimer.Start();
				spawnsoundtimer.Start();
				spawnsoundtimer.Reset();
			}
			break;
		case BossState.spawnstuff:
			switch (stuff)
			{
			case StuffToSpawn.brainz:
				((DrawableGameComponent)this).DrawOrder = 21;
				break;
			case StuffToSpawn.bulletz:
				((DrawableGameComponent)this).DrawOrder = 15;
				break;
			case StuffToSpawn.skullz:
				((DrawableGameComponent)this).DrawOrder = 15;
				break;
			case StuffToSpawn.ufoz:
				((DrawableGameComponent)this).DrawOrder = 21;
				break;
			case StuffToSpawn.minez:
				((DrawableGameComponent)this).DrawOrder = 15;
				break;
			}
			if (stateTimer.Finished)
			{
				stuff++;
				if ((int)stuff >= stuffToSpawnValues.Count)
				{
					stuff = StuffToSpawn.brainz;
				}
				state = BossState.wait;
				stateTimer.Duration = 15000f;
				stateTimer.Reset();
				stateTimer.Start();
			}
			else
			{
				if (!brainspawntimer.Finished)
				{
					break;
				}
				if (Settings.GetInstance().DifficultyModifier <= 1f)
				{
					brainspawntimer.Duration = spawnTime[(int)stuff] / Settings.GetInstance().DifficultyFactorized(1.5f);
				}
				else
				{
					brainspawntimer.Duration = spawnTime[(int)stuff] / Settings.GetInstance().DifficultyFactorized(0.5f);
				}
				brainspawntimer.Duration *= 1f + (base.HitPointsNormalized - 1f) * 0.4f;
				if (spawnsoundtimer.Finished)
				{
					if (RandomHelper.RandomNextFloat(0f, 1f) <= 0.2f)
					{
						sound.PlayCue("head asplode");
					}
					else
					{
						sound.PlayCue("small head asplode");
					}
					spawnsoundtimer.Start();
					spawnsoundtimer.Randomize();
				}
				BloodExplosion bloodExplosion2 = BloodExplosion.NewExplosion(collection, ((GameComponent)this).Game);
				FindSpawnSpot(out var angle2, out var range2);
				Vector2 val = base.Position + MyMath.AngleToVector(angle2) * range2;
				switch (stuff)
				{
				case StuffToSpawn.brainz:
				{
					Braineroid braineroid = Braineroid.NewBraineroid(collection, ((GameComponent)this).Game);
					if (RandomHelper.RandomNextFloat(0f, 1f) <= 0.2f)
					{
						braineroid.Setup(val, BrainSize.medium, 0f, wrapping: false);
						if (RandomHelper.RandomNextFloat(0f, 1f) <= 0.3f)
						{
							braineroid.MakeBonus();
						}
					}
					else
					{
						braineroid.Setup(val, BrainSize.small, 0f, wrapping: false);
					}
					braineroid.SetDirection(angle2);
					collection.Add((GameComponent)(object)braineroid);
					break;
				}
				case StuffToSpawn.ufoz:
				{
					UFO uFO = UFO.NewUFO(collection, ((GameComponent)this).Game);
					if (RandomHelper.RandomNextFloat(0f, 1f) <= 0.05f)
					{
						uFO.Setup(val, isBig: true, EnemyBehaviour.normal);
					}
					else
					{
						uFO.Setup(val, isBig: false, EnemyBehaviour.normal);
						if (RandomHelper.RandomNextFloat(0f, 1f) <= 0.06f)
						{
							uFO.SetAsBonus();
						}
					}
					uFO.SetDirection(angle2);
					uFO.SpeedUp();
					collection.Add((GameComponent)(object)uFO);
					break;
				}
				case StuffToSpawn.skullz:
				{
					EvilSkull evilSkull = EvilSkull.NewEvilSkull(collection, ((GameComponent)this).Game);
					evilSkull.SetupLaunch(val, angle2);
					if (RandomHelper.RandomNextFloat(0f, 1f) <= 0.1f)
					{
						evilSkull.MakeBonus();
					}
					evilSkull.SetMaze(p: false);
					collection.Add((GameComponent)(object)evilSkull);
					break;
				}
				case StuffToSpawn.minez:
				{
					StarMine starMine = StarMine.NewStarMine(collection, ((GameComponent)this).Game);
					starMine.SetupLaunch(val, angle2);
					collection.Add((GameComponent)(object)starMine);
					break;
				}
				case StuffToSpawn.bulletz:
				{
					EvilBullet evilBullet = EvilBullet.NewEvilBullet(collection, ((GameComponent)this).Game);
					evilBullet.Setup(val, angle2);
					collection.Add((GameComponent)(object)evilBullet);
					break;
				}
				}
				bloodExplosion2.Setup(base.Position + MyMath.AngleToVector(angle2) * range2, 1f, 1f, 0.06f, angle2);
				collection.Add((GameComponent)(object)bloodExplosion2);
			}
			break;
		}
	}

	private void UpdateMusic()
	{
		if (!isChallengeBoss)
		{
			if (oracle.LiveShips == 0)
			{
				sound.StopMusic();
			}
			else
			{
				sound.SetMusicRate(MyMath.PowerCurve(50f, 68f, 2f, 1f - base.HitPointsNormalized));
			}
		}
	}

	private void UberExplosion(Vector2 p)
	{
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_004c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0085: Unknown result type (might be due to invalid IL or missing references)
		//IL_00be: Unknown result type (might be due to invalid IL or missing references)
		Explosion explosion = Explosion.NewExplosion(collection, ((GameComponent)this).Game);
		explosion.Setup(p, 2f, 1.3f, 0f, 0f);
		collection.Add((GameComponent)(object)explosion);
		explosion = Explosion.NewExplosion(collection, ((GameComponent)this).Game);
		explosion.Setup(p, 3.5f, 2.5f, 0f, 0f);
		collection.Add((GameComponent)(object)explosion);
		explosion = Explosion.NewExplosion(collection, ((GameComponent)this).Game);
		explosion.Setup(p, 5f, 3f, 0f, 0f);
		collection.Add((GameComponent)(object)explosion);
		explosion = Explosion.NewExplosion(collection, ((GameComponent)this).Game);
		explosion.Setup(p, 8f, 3f, 0f, 0f);
		collection.Add((GameComponent)(object)explosion);
	}

	private static void FindSpawnSpot(out float angle, out float range)
	{
		angle = RandomHelper.RandomNextAngle();
		range = MyMath.PowerCurve(150f, 0f, 2f, RandomHelper.RandomNextFloat(0f, 1f));
	}

	public override void CollidesWith(ICollidable other)
	{
		base.CollidesWith(other);
	}

	protected override void HitBy(ICollidable other, bool isComboGenerator)
	{
		base.HitBy(other, isComboGenerator);
		if (!soundtimer.Active)
		{
			sound.PlayCue("hit_boss");
		}
		soundtimer.Reset();
		soundtimer.Start();
	}

	protected override void KilledBy(ICollidable other, bool isComboGenerator)
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		Explosion explosion = Explosion.NewExplosion(collection, ((GameComponent)this).Game);
		explosion.Setup(base.Position, 3.5f, 2.5f, 0f, 0f);
		collection.Add((GameComponent)(object)explosion);
		sound.PlayCue("expl2");
		state = BossState.asplode;
		stateTimer.Duration = 20000f;
		stateTimer.Reset();
		stateTimer.Start();
		base.Collides = false;
		collection.Purge<EvilBullet>();
		collection.Purge<Braineroid>();
		collection.Purge<EvilSkull>();
		collection.Purge<StarMine>();
		collection.Purge<UFO>();
		collection.Purge<Lazer>();
		collection.Purge<PlasmaBall>();
		sound.StopMusic();
	}
}
