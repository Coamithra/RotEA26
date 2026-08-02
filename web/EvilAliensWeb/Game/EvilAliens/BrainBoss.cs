using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.GamerServices;
using EvilAliensWeb.Compat;

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

	private BrainAura aura;

	// Live animated overlay patches (mechanical bits flickering, fleshy folds pulsating)
	// composited over the static brain sprite; data-driven from Content/data/brainoverlays.json.
	private BrainBossOverlays overlays;

	private static List<StuffToSpawn> stuffToSpawnValues = Game1.GetEnumValues<StuffToSpawn>();

	public override ICollisionType CollisionType
	{
		get
		{
			// Hitbox tuned to the cyborg brain BALL, not the full texture (whose width is mostly
			// the off-screen cables). ~60% of the ~540x441 design ball (matching the original
			// brain's forgiving 60% box), centred 55px above Position (the ball sits high in the
			// frame), and pulsing with the boss via `scale`.
			float hw = 165f * scale;
			float hh = 135f * scale;
			float oy = -55f * scale;
			CollisionBox collisionBox = new CollisionBox(new Vector2(0f - hw, oy - hh), new Vector2(hw, oy + hh));
			collisionBox.TopLeft += base.Position;
			collisionBox.BottomRight += base.Position;
			return collisionBox;
		}
	}

	public BrainBoss(Game game)
		: base(game)
	{
		LoadAnimation(new AnimationData("GFX/Sprites/brainbosshd"));
		base.DrawOrder = 21;
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
		overlays = new BrainBossOverlays();
		overlays.Load(content);
	}

	public void Setup(bool challenge)
	{
		isChallengeBoss = challenge;
	}

	public override void Initialize()
	{
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
		position.Y = (0f - (float)texture.LogicalHeight() / textureScale) / 2f;
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
		aura = BrainAura.NewAura(collection, base.Game);
		aura.Setup(this);
		collection.Add((GameComponent)(object)aura);
		// Restart overlay playback at phase 0 on a recycled boss. overlays is created in
		// LoadContent, which base.Initialize() above has already run on first spawn — so a null
		// here is a real lifecycle bug and SHOULD NullRef rather than be swallowed by `?.`.
		overlays.Reset();
	}

	public override void Draw(GameTime gameTime)
	{
		// ?brainhitflash forces the flash on for a screenshot -- landing a real shot inside the
		// 35 ms hittimer window is not something a rig can time. It has to bracket base.Draw as
		// well as the overlays: the base sprite's own flash comes from KillableAlien.Draw keying
		// off isBlinking(), which the flag deliberately does NOT fake (nothing is damaged), so
		// without this the forced capture would show the patches flashing over an unlit brain --
		// the exact asymmetry the card is about, inverted.
		bool forcedFlash = EvilAliensWeb.Compat.DebugFlags.BrainHitFlash;
		if (forcedFlash)
		{
			spriteBatch.lightenEffect.Enable();
		}
		base.Draw(gameTime);
		// Live animated patches on top of the static brain. `color` is the base sprite's
		// live tint (reddens on low HP) so the overlays redden in lockstep; DrawScale +
		// Position glue them to the boss so they pulse and move with it. The last arg gates
		// the "exhaust" pods (gate:"spawn") so they only vent while the boss is actively
		// spawning a wave (BossState.spawnstuff), calm otherwise. The sprite harness freezes
		// Update (state stays `entry`), so force it on there to keep the pods inspectable.
		bool spawnActive = state == BossState.spawnstuff
			|| netVenting
			|| EvilAliensWeb.Compat.DebugFlags.Harness != null;
		// Card 9f90978c: the hit flash is KillableAlien.Draw bracketing lightenEffect around
		// base.Draw ONLY, and the overlays are drawn after it -- so the shipped patches (the eye
		// and the pods) stayed unlit while the brain under them flashed white. Re-open the bracket
		// here so the patches flash with the sprite they sit on. Both of overlays.Draw's branches
		// have a compiled variant with LIGHTEN in it (`lighten` for the plain one, whose tint
		// still rides the vertex colour; `lighten_interpolate_fade` for the interpolated one,
		// which already enables fade with the same tint) -- see EffectHandler.SelectEffect.
		bool flashing = isBlinking() || forcedFlash;
		if (flashing)
		{
			spriteBatch.lightenEffect.Enable();
		}
		overlays.Draw(spriteBatch, base.Position, DrawScale, texture.LogicalWidth(), texture.LogicalHeight(), color, spawnActive);
		if (flashing)
		{
			spriteBatch.lightenEffect.Disable();
		}
	}

	public override void Update(GameTime gameTime)
	{
		UpdateMusic();
		pulsetimer.Duration = MathHelper.Lerp(700f, 1600f, base.HitPointsNormalized);
		float num = MathHelper.Lerp(0.1f, 0.04f, base.HitPointsNormalized);
		scale = 1f + num * pulsateCurve.Evaluate(1f - pulsetimer.Normalized);
		if (base.HitPointsNormalized < 0.33f)
		{
			float hitsPerSec = MathHelper.Lerp(5f, 1f, base.HitPointsNormalized * 3f);
			if (RandomHelper.RandomFromAverage(hitsPerSec, gameTime))
			{
				BloodExplosion bloodExplosion = BloodExplosion.NewExplosion(collection, base.Game);
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
				BloodExplosion bloodExplosion = BloodExplosion.NewExplosion(collection, base.Game);
				FindSpawnSpot(out var angle3, out var range3);
				Vector2 position = MyMath.AngleToVector(angle3) * range3 + base.Position;
				float num4 = 2f;
				bloodExplosion.Setup(position, num4, num4, 0.12f, angle3);
				collection.Add((GameComponent)(object)bloodExplosion);
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
					BloodExplosion bloodExplosion = BloodExplosion.NewExplosion(collection, base.Game);
					FindSpawnSpot(out var angle4, out var range4);
					Vector2 position = MyMath.AngleToVector(angle4) * range4 + base.Position;
					bloodExplosion.Setup(position, 5f + (float)i / 5f, 1f + (float)i / 5f, 0f, base.Direction);
					collection.Add((GameComponent)(object)bloodExplosion);
				}
			}
			if (RandomHelper.RandomFromAverage(0.5f * num3, gameTime))
			{
				FindSpawnSpot(out var angle5, out var range5);
				Vector2 position = MyMath.AngleToVector(angle5) * range5 + base.Position;
				Explosion explosion = Explosion.NewExplosion(collection, base.Game);
				explosion.Setup(position, 3.5f, 2.5f, 0f, 0f);
				collection.Add((GameComponent)(object)explosion);
				FindSpawnSpot(out angle5, out range5);
				position = MyMath.AngleToVector(angle5) * range5 + base.Position;
				explosion = Explosion.NewExplosion(collection, base.Game);
				explosion.Setup(position, 2f, 1.3f, 0f, 0f);
				collection.Add((GameComponent)(object)explosion);
				sound.PlayCue("expl2");
			}
			if (RandomHelper.RandomFromAverage(2f * num3, gameTime))
			{
				FindSpawnSpot(out var angle6, out var range6);
				Vector2 position = MyMath.AngleToVector(angle6) * range6 + base.Position;
				Explosion explosion = Explosion.NewExplosion(collection, base.Game);
				explosion.Setup(position, 1f, 1f, 0f, 0f);
				collection.Add((GameComponent)(object)explosion);
				sound.PlayCue("expl1");
			}
			if (stateTimer.Finished)
			{
				sound.PlayCue("expl2");
				UberExplosion(base.Position);
				UberExplosion(base.Position - new Vector2(100f, 0f));
				UberExplosion(base.Position + new Vector2(100f, 0f));
				BloodExplosion bloodExplosion = BloodExplosion.NewExplosion(collection, base.Game);
				bloodExplosion.Setup(base.Position, 7f, 3f, 0f, base.Direction);
				collection.Add((GameComponent)(object)bloodExplosion);
				state = BossState.smallwaitafterasplosion;
				stateTimer.Duration = 300f;   // a really quick fadeout (see the state below)
				stateTimer.Reset();
				stateTimer.Start();
				aura.Free();
			}
			break;
		}
		case BossState.smallwaitafterasplosion:
			// Quick ALPHA fadeout, not a scale-down. Brain + cables are ONE sprite now (were
			// two), so shrinking toward 0 bares the sprite's hard rectangular edges; fading
			// alpha dissolves cleanly. `color` is red from the killing hit (RGB kept, only A
			// driven); the overlays draw with this same `color`, so they fade in lockstep, and
			// `scale` keeps pulsating (set above the switch). Brief and buried under the
			// death explosions anyway.
			color = new Color(color.R, color.G, color.B, (byte)MathHelper.Clamp((1f - stateTimer.Normalized) * 255f, 0f, 255f));
			if (stateTimer.Finished)
			{
				Die();
				AwardScoreToAll(combo: true);
			}
			break;
		case BossState.entry:
		{
			float num5 = MathHelper.SmoothStep(100f, (0f - (float)texture.LogicalHeight() / textureScale) / 2f, stateTimer.Normalized);
			base.Position = new Vector2(base.Position.X, num5);
			if (stateTimer.Finished)
			{
				base.Position = new Vector2(base.Position.X, 100f);
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
				PlasmaBall plasmaBall = PlasmaBall.NewAlien(collection, base.Game);
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
				base.DrawOrder = 21;
				break;
			case StuffToSpawn.bulletz:
				base.DrawOrder = 15;
				break;
			case StuffToSpawn.skullz:
				base.DrawOrder = 15;
				break;
			case StuffToSpawn.ufoz:
				base.DrawOrder = 21;
				break;
			case StuffToSpawn.minez:
				base.DrawOrder = 15;
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
				BloodExplosion bloodExplosion = BloodExplosion.NewExplosion(collection, base.Game);
				FindSpawnSpot(out var angle2, out var range2);
				Vector2 val = base.Position + MyMath.AngleToVector(angle2) * range2;
				switch (stuff)
				{
				case StuffToSpawn.brainz:
				{
					Braineroid braineroid = Braineroid.NewBraineroid(collection, base.Game);
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
					UFO uFO = UFO.NewUFO(collection, base.Game);
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
					EvilSkull evilSkull = EvilSkull.NewEvilSkull(collection, base.Game);
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
					StarMine starMine = StarMine.NewStarMine(collection, base.Game);
					starMine.SetupLaunch(val, angle2);
					collection.Add((GameComponent)(object)starMine);
					break;
				}
				case StuffToSpawn.bulletz:
				{
					EvilBullet evilBullet = EvilBullet.NewEvilBullet(collection, base.Game);
					evilBullet.Setup(val, angle2);
					collection.Add((GameComponent)(object)evilBullet);
					break;
				}
				}
				bloodExplosion.Setup(base.Position + MyMath.AngleToVector(angle2) * range2, 1f, 1f, 0.06f, angle2);
				collection.Add((GameComponent)(object)bloodExplosion);
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
		Explosion explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(p, 2f, 1.3f, 0f, 0f);
		collection.Add((GameComponent)(object)explosion);
		explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(p, 3.5f, 2.5f, 0f, 0f);
		collection.Add((GameComponent)(object)explosion);
		explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(p, 5f, 3f, 0f, 0f);
		collection.Add((GameComponent)(object)explosion);
		explosion = Explosion.NewExplosion(collection, base.Game);
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
		Explosion explosion = Explosion.NewExplosion(collection, base.Game);
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

	// ---- Online co-op replication seams (Compat/Net/Descriptors/DescriptorsCoverage) --------
	// The huge boss body is a single static frame tinted by `color` (reddens on low HP -- Colorize),
	// so scale + the redden ride the base state (Scale, and Hp -> NetApplyHp; initialhitpoints is a
	// fixed 1700 either side, so the redden matches exactly). The animated overlays + the BrainAura
	// child both run off gameTime in Draw (not Update), so they animate correctly on a frozen puppet
	// (the aura is respawned by the puppet's own Initialize). The one Draw ingredient a puppet can't
	// reach is the "exhaust pods" vent gate, which the host keys off BossState.spawnstuff -- streamed
	// here as a single bit so the pods vent on the client while the host is spawning a wave.
	private bool netVenting;

	internal bool NetVenting
	{
		get
		{
			return state == BossState.spawnstuff;
		}
		set
		{
			netVenting = value;
		}
	}
}
