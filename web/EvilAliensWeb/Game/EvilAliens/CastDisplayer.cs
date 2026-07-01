using System;
using System.Collections.Generic;
using EvilAliens.Constants;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

public class CastDisplayer : DrawableGameComponent, IComponentWatcher
{
	private enum CastState
	{
		intro,
		waiting,
		ufo,
		braineroid,
		boss,
		junkboss,
		spider,
		spiderboss,
		evilskull,
		battleskull,
		deathstar,
		brainboss,
		playership,
		end
	}

	private Curve pulsateCurve;

	private Timer pulsetimer = new Timer(1150f, repeating: true);

	private Vector2 spawnposition = new Vector2(400f, 200f);

	public bool done;

	public GameComponent owner;

	private Timer stateTimer = new Timer(1f, repeating: false);

	private SpriteBatchWrapper spriteBatch;

	private ComponentBin collection;

	private ContentManager content;

	private SpriteFont font;

	private InputHandler inputHandler;

	private SoundManager sound;

	private Texture2D texture;

	private string texturename;

	private float rotation;

	private float scale = 1f;

	// supersample factor of the loaded sheet (4 for an HD-registered sheet, else 1). The cast
	// display draws frames at their actual texel size, so divide the draw scale by this to keep
	// every cast member at its original on-screen size after a sheet is upscaled.
	private float textureScale = 1f;

	private int rows;

	private int columns;

	private int separatingspace;

	private float curframe;

	private float fps;

	private SpriteEffects spriteEffects;

	private Color color;

	private Texture2D spiderdebris1;

	private Texture2D spiderdebris2;

	private Texture2D spiderdebris3;

	private string bossTextureName = "GFX/Sprites/mothershipA";

	private List<float> debrisrotation = new List<float>();

	private List<float> debrisrotationspeed = new List<float>();

	private List<Vector2> debrisposition = new List<Vector2>();

	private List<Vector2> debrisspeed = new List<Vector2>();

	private Timer spiderdeadtimer = new Timer(5000f, repeating: false);

	private Texture2D wing;

	private AnimatedSprite spiderFly;

	private AnimatedSprite alienBoss;

	private Texture2D AButton;

	private CastState state;

	private CastState nextstate;

	private float _time;

	private string alienname;

	private string alientext;

	public CastDisplayer(Game game)
		: base(game)
	{
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		base.DrawOrder = 1000;
	}

	public void LoadAnimation(AnimationData animationData)
	{
		//IL_005a: Unknown result type (might be due to invalid IL or missing references)
		//IL_005f: Unknown result type (might be due to invalid IL or missing references)
		texture = content.Load<Texture2D>(animationData.TextureName);
		texturename = animationData.TextureName;
		rows = animationData.rows;
		columns = animationData.columns;
		fps = animationData.fps;
		separatingspace = animationData.separatingspace;
		int frameWidth = columns > 0 ? (texture.Width - (columns - 1) * separatingspace) / columns : texture.Width;
		textureScale = AlienDrawableGameComponent.SuperSampleFactor(texturename, frameWidth);
		color = Color.White;
	}

	// The per-state Update code used to call LoadAnimation(new AnimationData(...)) EVERY tick,
	// re-doing the content lookup + SuperSampleFactor for a sheet that hadn't changed. Each cast
	// state holds a single sheet for its whole duration, so reload only when the sheet name
	// actually differs from what's loaded (the boss state flips its texture mid-state, which
	// changes texturename, so a name compare covers that case too). ASSUMES a texture name
	// uniquely determines its grid/fps — true for every cast state today; a future state that
	// reused a name with a different grid would need to compare the full AnimationData.
	private void EnsureAnimation(AnimationData animationData)
	{
		if (texturename == animationData.TextureName)
		{
			return;
		}
		LoadAnimation(animationData);
	}

	public override void Initialize()
	{
		collection = ServiceHelper.Get<IComponentBinService>().ComponentBin;
		spriteBatch = ServiceHelper.Get<ISpriteBatchWrapperService>().SpriteBatchWrapper;
		content = ServiceHelper.Get<IContentManagerService>().ContentManager;
		inputHandler = ServiceHelper.Get<IInputHandlerService>().InputHandler;
		sound = ServiceHelper.Get<ISoundManagerService>().SoundManager;
		LoadAnimation(new AnimationData("GFX/Sprites/ufosheet", 4, 8, 1, 25f));
		base.Initialize();
		done = false;
		stateTimer.Duration = 17000f;
		stateTimer.Reset();
		stateTimer.Start();
		state = CastState.intro;
		spiderdeadtimer.Stop();
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		font = content.Load<SpriteFont>("GFX/Menu/menufont");
		if (texturename != null)
		{
			texture = content.Load<Texture2D>(texturename);
		}
		wing = content.Load<Texture2D>("GFX/Sprites/wing1");
		pulsateCurve = content.Load<Curve>("GFX/Effects/BrainCurve");
		spiderdebris1 = content.Load<Texture2D>("GFX/Sprites/spiderdebris1");
		spiderdebris2 = content.Load<Texture2D>("GFX/Sprites/spiderdebris2");
		spiderdebris3 = content.Load<Texture2D>("GFX/Sprites/spiderdebris3");
		spiderFly = new AnimatedSprite("GFX/Spider/spiderfly");
		alienBoss = new AnimatedSprite("GFX/Alienboss/alienboss");
		AButton = content.Load<Texture2D>("GFX/Preview/small_face_a");
	}

	public override void Update(GameTime gameTime)
	{
		//IL_054c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0559: Unknown result type (might be due to invalid IL or missing references)
		//IL_056e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0573: Unknown result type (might be due to invalid IL or missing references)
		//IL_058f: Unknown result type (might be due to invalid IL or missing references)
		//IL_05af: Unknown result type (might be due to invalid IL or missing references)
		//IL_05b4: Unknown result type (might be due to invalid IL or missing references)
		//IL_0601: Unknown result type (might be due to invalid IL or missing references)
		//IL_061a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0640: Unknown result type (might be due to invalid IL or missing references)
		//IL_0658: Unknown result type (might be due to invalid IL or missing references)
		//IL_0663: Unknown result type (might be due to invalid IL or missing references)
		stateTimer.Update(gameTime);
		spiderdeadtimer.Update(gameTime);
		base.Update(gameTime);
		float num = curframe;
		curframe = (curframe + fps * (float)gameTime.ElapsedGameTime.TotalSeconds) % (float)(rows * columns);
		if (curframe < num && state == CastState.boss)
		{
			if (bossTextureName == "GFX/Sprites/mothershipA")
			{
				bossTextureName = "GFX/Sprites/mothershipB";
			}
			else
			{
				bossTextureName = "GFX/Sprites/mothershipA";
			}
		}
		bool flag = false;
		flag |= inputHandler.Pressed(MyKeys.Enter) || inputHandler.Pressed(MyKeys.Esc);
		for (int i = 0; i < 4; i++)
		{
			flag |= inputHandler.PadPressed(PadKeys.Start, i);
			flag |= inputHandler.PadPressed(PadKeys.Back, i);
			flag |= inputHandler.PadPressed(PadKeys.A, i);
			flag |= inputHandler.PadPressed(PadKeys.B, i);
			flag |= inputHandler.PadPressed(PadKeys.LTRT, i);
		}
		bool flag2 = flag;
		switch (state)
		{
		case CastState.intro:
			if (stateTimer.Finished)
			{
				state = CastState.ufo;
				alienname = "";
				alientext = "";
			}
			break;
		case CastState.waiting:
			if (stateTimer.Finished)
			{
				state = nextstate;
			}
			break;
		case CastState.ufo:
			alienname = "UFO";
			alientext = "Various forms of UFOs make up the\nbrunt of the alien fleet.\n\nLarge UFOs can sometimes be seen\nleading squadrons of smaller ones\ninto battle.";
			EnsureAnimation(new AnimationData("GFX/Sprites/ufosheet", 4, 8, 1, 25f));
			scale = 1f;
			if (flag2)
			{
				Asplode();
				Next();
			}
			break;
		case CastState.braineroid:
		{
			alienname = "Brain Spawn";
			alientext = "Their eons-long goal is to destroy all other intelligent life,\nsince the thoughts of other beings screech at them like the\nforced laughs of a billion art-house movie patrons.";
			EnsureAnimation(new AnimationData("GFX/Sprites/brainlargetransglow"));
			_time += (float)gameTime.ElapsedGameTime.TotalSeconds;
			float num2 = 1f + (1f + (float)Math.Sin(_time * 3.32f)) * 0.07f;
			scale = 0.4f * num2;
			if (flag2)
			{
				AsplodeBraineroid();
				Next();
			}
			break;
		}
		case CastState.boss:
			alienname = "Alien Battleship";
			alientext = "These massive UFOs serve as command stations for\nthe generals of the Evil Alien invasion fleet.\n\nThey are usually equipped with multiple lazer arrays.";
			EnsureAnimation(new AnimationData(bossTextureName, 4, 4, 1, 16f));
			scale = 1f;
			if (flag2)
			{
				AsplodeBig();
				Next();
			}
			break;
		case CastState.junkboss:
			alienname = "Fleet Commander Drone";
			alientext = "Robotic field probes that are in direct\ncontact with the Alien Overmind.\n\nOften equipped with ultragraviton field.";
			EnsureAnimation(new AnimationData("GFX/Sprites/eye_idle", 4, 2, 1, 12f));
			scale = 1f;
			if (flag2)
			{
				AsplodeBig();
				Next();
			}
			break;
		case CastState.spider:
			alienname = "Spider Wasp";
			alientext = "Indigenous life form to Mars.\n\nThese resilient bugs have been brought\nout of hiding by the Evil Aliens'\nactivities, and threaten both you and\nthe Aliens indifferently.";
			// Same shared rear-up sheet + reared sub-range loop as the FlyingSpider (was the old
			// 1x4 crawl slicing, which broke when the sheet became the 49-frame rear-up).
			EnsureAnimation(new AnimationData("GFX/Sprites/spider_sheet2", 7, 7, 1, 12f, 22, 31));
			scale = 1f;
			if (flag2)
			{
				AsplodeSpider();
				Next();
			}
			break;
		case CastState.spiderboss:
			alienname = "Spider Stag";
			alientext = "An armor plated insectoid killing machine!\nImpervious to normal assault.\n\n(technically not an insect but a salticida)";
			scale = 1f;
			if (flag2)
			{
				AsplodeSpiderBoss();
				Next();
			}
			break;
		case CastState.evilskull:
			alienname = "Evil Grinning Face of Death";
			alientext = "These foes are able to bend time and space\nand shoot volleys of bullets after appearing\nright behind you!";
			EnsureAnimation(new AnimationData("GFX/Sprites/faceofdeathspritesheet", 4, 8, 1, 12f));
			scale = 1f;
			if (flag2)
			{
				Asplode();
				Next();
			}
			break;
		case CastState.battleskull:
			alienname = "Alien Ruler";
			alientext = "These giant aliens make up the higher\nranks of the Evil Alien Empire.";
			scale = 1.2f;
			if (flag2)
			{
				AsplodeRuler();
				Next();
			}
			break;
		case CastState.deathstar:
			alienname = "Death Star";
			alientext = "Special heat seeking space mines that\nlock on to their target and explode into\nraw electromagnetic energy!";
			EnsureAnimation(new AnimationData("GFX/Sprites/deathstarsheet2", 4, 8, 1, 25f));
			scale = 1f;
			if (flag2)
			{
				AsplodeDeathStar();
				Next();
			}
			break;
		case CastState.brainboss:
			alienname = "Alien Overmind";
			alientext = "Pure.. throbbing.. evil!\n\nGood thing you killed it.";
			EnsureAnimation(new AnimationData("GFX/Sprites/brainbosshd"));
			scale = 1f;
			if (flag2)
			{
				AsplodeBrainBoss();
				Next();
			}
			break;
		case CastState.playership:
			alienname = "The Unnamed Hero";
			alientext = "That's actually the name of the ship.\nHmm hmm.";
			EnsureAnimation(new AnimationData("GFX/Sprites/playersheet", 4, 8, 1, 6f));
			if (flag2)
			{
				AsplodePlayer();
				Next();
			}
			break;
		case CastState.end:
			done = true;
			collection.Remove((GameComponent)(object)this);
			break;
		}
		if (!spiderdeadtimer.Active)
		{
			return;
		}
		for (int j = 0; j < debrisposition.Count; j++)
		{
			List<Vector2> list;
			int index;
			(list = debrisposition)[index = j] = list[index] + debrisspeed[j] * (float)gameTime.ElapsedGameTime.TotalMilliseconds;
			List<Vector2> list2;
			int index2;
			(list2 = debrisspeed)[index2 = j] = list2[index2] + new Vector2(0f, 0.001f * (float)gameTime.ElapsedGameTime.TotalMilliseconds);
			debrisrotation[j] += debrisrotationspeed[j] * (float)gameTime.ElapsedGameTime.TotalMilliseconds;
			if (debrisposition[j].Y > 550f && debrisspeed[j].Y > 0f)
			{
				debrisspeed[j] = new Vector2(0.5f * debrisspeed[j].X, -0.5f * debrisspeed[j].Y);
				debrisrotationspeed[j] *= 0.5f;
			}
		}
	}

	private void AsplodePlayer()
	{
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		texture = null;
		Explosion explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(spawnposition, 2f, 2f, 0f, 0f);
		collection.Add((GameComponent)(object)explosion);
		explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(spawnposition, 3.5f, 3.5f, 0f, 0f);
		collection.Add((GameComponent)(object)explosion);
		sound.PlayCue("expl2");
		stateTimer.Duration = 3000f;
	}

	private void UberExplosion(Vector2 p)
	{
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_004c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0085: Unknown result type (might be due to invalid IL or missing references)
		//IL_00be: Unknown result type (might be due to invalid IL or missing references)
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

	private void AsplodeBrainBoss()
	{
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_0040: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0066: Unknown result type (might be due to invalid IL or missing references)
		//IL_006b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0088: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fa: Unknown result type (might be due to invalid IL or missing references)
		//IL_0133: Unknown result type (might be due to invalid IL or missing references)
		//IL_016c: Unknown result type (might be due to invalid IL or missing references)
		texture = null;
		sound.PlayCue("expl2");
		sound.PlayCue("hit_boss");
		Vector2 val = default(Vector2);
		(val) = new Vector2(400f, 80f);
		UberExplosion(val);
		UberExplosion(val - new Vector2(100f, 0f));
		UberExplosion(val + new Vector2(100f, 0f));
		BloodExplosion bloodExplosion = BloodExplosion.NewExplosion(collection, base.Game);
		bloodExplosion.Setup(val, 7f, 6f, 0f, 0f);
		collection.Add((GameComponent)(object)bloodExplosion);
		bloodExplosion = BloodExplosion.NewExplosion(collection, base.Game);
		bloodExplosion.Setup(val, 7f, 6f, 0f, 0f);
		collection.Add((GameComponent)(object)bloodExplosion);
		bloodExplosion = BloodExplosion.NewExplosion(collection, base.Game);
		bloodExplosion.Setup(val, 7f, 6f, 0f, 0f);
		collection.Add((GameComponent)(object)bloodExplosion);
		bloodExplosion = BloodExplosion.NewExplosion(collection, base.Game);
		bloodExplosion.Setup(val, 7f, 6f, 0f, 0f);
		collection.Add((GameComponent)(object)bloodExplosion);
		bloodExplosion = BloodExplosion.NewExplosion(collection, base.Game);
		bloodExplosion.Setup(val, 7f, 6f, 0f, 0f);
		collection.Add((GameComponent)(object)bloodExplosion);
		stateTimer.Duration = 4000f;
	}

	private void AsplodeDeathStar()
	{
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_005f: Unknown result type (might be due to invalid IL or missing references)
		texture = null;
		Explosion explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(spawnposition, 3.5f, 2.5f, 0f, 0f);
		explosion.MakeBlue();
		collection.Add((GameComponent)(object)explosion);
		explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(spawnposition, 2f, 1.3f, 0f, 0f);
		explosion.MakeBlue();
		collection.Add((GameComponent)(object)explosion);
		sound.PlayCue("expl2");
		sound.PlayCue("targetacquired");
		stateTimer.Duration = 2500f;
	}

	private static void FindSpawnSpot(out float angle, out float range)
	{
		angle = RandomHelper.RandomNextAngle();
		range = MyMath.PowerCurve(100f, 0f, 2f, RandomHelper.RandomNextFloat(0f, 1f));
	}

	private void AsplodeRuler()
	{
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		AsplodeBig();
		texture = null;
		for (int i = 0; i < 5; i++)
		{
			for (int j = 0; j < 15; j++)
			{
				BloodExplosion bloodExplosion = BloodExplosion.NewExplosion(collection, base.Game);
				FindSpawnSpot(out var angle, out var range);
				Vector2 position = MyMath.AngleToVector(angle) * range + spawnposition;
				bloodExplosion.Setup(position, 5f + (float)j / 5f, 1f + (float)j / 5f, 0f, 0f);
				collection.Add((GameComponent)(object)bloodExplosion);
			}
		}
		stateTimer.Duration = 2500f;
		sound.PlayCue("head asplode");
	}

	private void Bleed(float size)
	{
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		BloodExplosion bloodExplosion = BloodExplosion.NewExplosion(collection, base.Game);
		FindSpawnSpot(out var angle, out var range);
		Vector2 position = MyMath.AngleToVector(angle) * range + spawnposition;
		bloodExplosion.Setup(position, size, size * 0.7f, 0.12f, angle);
		bloodExplosion.MakeGreen();
		collection.Add((GameComponent)(object)bloodExplosion);
	}

	private void AsplodeSpiderBoss()
	{
		//IL_00cb: Unknown result type (might be due to invalid IL or missing references)
		//IL_0105: Unknown result type (might be due to invalid IL or missing references)
		texture = null;
		sound.PlayCue("spiderbossdeath");
		sound.PlayCue("head asplode");
		sound.PlayCue("bugdies");
		for (int i = 0; i < 8; i++)
		{
			Bleed(2.5f);
		}
		for (int j = 0; j < 8; j++)
		{
			Bleed(3f);
		}
		for (int k = 0; k < 8; k++)
		{
			Bleed(5f);
		}
		for (int l = 0; l < 8; l++)
		{
			Bleed(6f);
		}
		debrisposition.Clear();
		debrisspeed.Clear();
		debrisrotation.Clear();
		debrisrotationspeed.Clear();
		for (int m = 0; m < 8; m++)
		{
			debrisposition.Add(spawnposition);
			debrisspeed.Add(new Vector2(RandomHelper.RandomNextFloat(-0.3f, 0.3f), -0.3f + 0.5f * RandomHelper.RandomNextFloat(-0.3f, 0.3f)));
			debrisrotation.Add(RandomHelper.RandomNextAngle());
			debrisrotationspeed.Add(RandomHelper.RandomNextFloat(-0.03f, 0.03f));
		}
		spiderdeadtimer.Reset();
		spiderdeadtimer.Start();
	}

	private void AsplodeSpider()
	{
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_005f: Unknown result type (might be due to invalid IL or missing references)
		texture = null;
		BloodExplosion bloodExplosion = BloodExplosion.NewExplosion(collection, base.Game);
		bloodExplosion.Setup(spawnposition, 5f, 0.75f, 0f, 0f);
		bloodExplosion.MakeGreen();
		collection.Add((GameComponent)(object)bloodExplosion);
		bloodExplosion = BloodExplosion.NewExplosion(collection, base.Game);
		bloodExplosion.Setup(spawnposition, 3f, 0.5f, 0f, 0f);
		bloodExplosion.MakeGreen();
		collection.Add((GameComponent)(object)bloodExplosion);
		stateTimer.Duration = 1000f;
		sound.PlayCue("bugdies");
		sound.PlayCue("small head asplode");
	}

	private void AsplodeBraineroid()
	{
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		//IL_005c: Unknown result type (might be due to invalid IL or missing references)
		texture = null;
		for (int i = 0; i < 3; i++)
		{
			Braineroid braineroid = Braineroid.NewBraineroid(collection, base.Game);
			braineroid.Setup(spawnposition, BrainSize.medium, 0f, wrapping: false);
			collection.Add((GameComponent)(object)braineroid);
		}
		for (int j = 0; j < 10; j++)
		{
			BloodExplosion bloodExplosion = BloodExplosion.NewExplosion(collection, base.Game);
			bloodExplosion.Setup(spawnposition, 3f + (float)j / 10f, 1f + (float)j / 10f, 0f, 0f);
			collection.Add((GameComponent)(object)bloodExplosion);
		}
		sound.PlayCue("head asplode");
		stateTimer.Duration = 1000f;
	}

	private void Next(CastState castState)
	{
		nextstate = castState;
		state = CastState.waiting;
		stateTimer.Reset();
		stateTimer.Start();
	}

	private void Next()
	{
		CastState castState = state;
		castState++;
		Next(castState);
	}

	private void AsplodeBig()
	{
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_0097: Unknown result type (might be due to invalid IL or missing references)
		texture = null;
		Explosion explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(spawnposition, 2f, 1.3f, 0f, 0f);
		collection.Add((GameComponent)(object)explosion);
		explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(spawnposition, 4f, 2.3f, 0f, 0f);
		collection.Add((GameComponent)(object)explosion);
		explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(spawnposition, 6f, 5.3f, 0f, 0f);
		collection.Add((GameComponent)(object)explosion);
		sound.PlayCue("expl2");
		stateTimer.Duration = 3000f;
	}

	private void Asplode()
	{
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		texture = null;
		Explosion explosion = Explosion.NewExplosion(collection, base.Game);
		explosion.Setup(spawnposition, 1f, 1f, 0f, 0f);
		collection.Add((GameComponent)(object)explosion);
		sound.PlayCue("expl1");
		stateTimer.Duration = 1000f;
	}

	private void SetState(CastState state)
	{
		this.state = state;
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_0069: Unknown result type (might be due to invalid IL or missing references)
		//IL_0110: Unknown result type (might be due to invalid IL or missing references)
		//IL_011f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0124: Unknown result type (might be due to invalid IL or missing references)
		//IL_0129: Unknown result type (might be due to invalid IL or missing references)
		//IL_017b: Unknown result type (might be due to invalid IL or missing references)
		//IL_018a: Unknown result type (might be due to invalid IL or missing references)
		//IL_018f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0194: Unknown result type (might be due to invalid IL or missing references)
		//IL_0418: Unknown result type (might be due to invalid IL or missing references)
		//IL_0442: Unknown result type (might be due to invalid IL or missing references)
		//IL_0447: Unknown result type (might be due to invalid IL or missing references)
		//IL_045c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0466: Unknown result type (might be due to invalid IL or missing references)
		//IL_0491: Unknown result type (might be due to invalid IL or missing references)
		//IL_0496: Unknown result type (might be due to invalid IL or missing references)
		//IL_04ac: Unknown result type (might be due to invalid IL or missing references)
		//IL_04b6: Unknown result type (might be due to invalid IL or missing references)
		//IL_04e1: Unknown result type (might be due to invalid IL or missing references)
		//IL_04e6: Unknown result type (might be due to invalid IL or missing references)
		//IL_04fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0506: Unknown result type (might be due to invalid IL or missing references)
		//IL_0531: Unknown result type (might be due to invalid IL or missing references)
		//IL_055e: Unknown result type (might be due to invalid IL or missing references)
		//IL_057e: Unknown result type (might be due to invalid IL or missing references)
		//IL_058b: Unknown result type (might be due to invalid IL or missing references)
		//IL_05a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_05a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bd: Unknown result type (might be due to invalid IL or missing references)
		//IL_020a: Unknown result type (might be due to invalid IL or missing references)
		//IL_020f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0261: Unknown result type (might be due to invalid IL or missing references)
		//IL_027a: Unknown result type (might be due to invalid IL or missing references)
		//IL_037b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0384: Unknown result type (might be due to invalid IL or missing references)
		//IL_0389: Unknown result type (might be due to invalid IL or missing references)
		//IL_039c: Unknown result type (might be due to invalid IL or missing references)
		//IL_03a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_033a: Unknown result type (might be due to invalid IL or missing references)
		//IL_033c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0345: Unknown result type (might be due to invalid IL or missing references)
		//IL_034a: Unknown result type (might be due to invalid IL or missing references)
		//IL_035d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0363: Unknown result type (might be due to invalid IL or missing references)
		//IL_03c2: Unknown result type (might be due to invalid IL or missing references)
		//IL_03db: Unknown result type (might be due to invalid IL or missing references)
		base.Draw(gameTime);
		spriteBatch.BlendMode = (SpriteBlendMode)1;
		if (state == CastState.intro)
		{
			return;
		}
		if (spiderdeadtimer.Active)
		{
			Color val = default(Color);
			(val) = new Color(new Vector4(1f, 1f, 1f, MathHelper.Lerp(0f, 1f, spiderdeadtimer.TimeLeft * 3f / stateTimer.Duration)));
			for (int i = 0; i < debrisposition.Count; i++)
			{
				Texture2D val2 = (Texture2D)(i switch
				{
					0 => spiderdebris1, 
					1 => spiderdebris3, 
					_ => spiderdebris2, 
				});
				spriteBatch.Draw(val2, debrisposition[i], debrisrotation[i], scale, center: true, val);
			}
		}
		if (state == CastState.battleskull)
		{
			int frame = (int)MyMath.Mod((float)gameTime.TotalGameTime.TotalSeconds * 20f, alienBoss.Frames);
			alienBoss.Draw(frame, spawnposition + new Vector2(0f, 50f), Color.White, scale, center: true);
		}
		else if (state == CastState.spiderboss)
		{
			int frame2 = (int)MyMath.Mod((float)gameTime.TotalGameTime.TotalSeconds * 30f, spiderFly.Frames);
			spiderFly.Draw(frame2, spawnposition - new Vector2(450f, 200f), Color.White, scale, center: false);
		}
		else if (texture != null)
		{
			float num = 0f;
			if (state == CastState.junkboss)
			{
				num = (float)Math.Sin(gameTime.TotalGameTime.TotalSeconds * 6.0) * 3f;
			}
			Vector2 val3 = default(Vector2);
			if (state == CastState.brainboss)
			{
				(val3) = new Vector2(400f, 80f);
			}
			else
			{
				val3 = spawnposition;
			}
			if (state == CastState.brainboss)
			{
				pulsetimer.Update(gameTime);
				scale = 1f + 0.07f * pulsateCurve.Evaluate(1f - pulsetimer.Normalized);
			}
			if ((columns > 1) | (rows > 1))
			{
				int num2 = (int)curframe;
				int num3 = num2 / columns;
				int num4 = num2 % columns;
				int num5 = texture.Width - (columns - 1) * separatingspace;
				num5 /= columns;
				int num6 = texture.Height - (rows - 1) * separatingspace;
				num6 /= rows;
				Rectangle source = default(Rectangle);
				(source) = new Rectangle(num4 * (num5 + separatingspace), num3 * (num6 + separatingspace), num5, num6);
				spriteBatch.Draw(texture, source, val3 + new Vector2(0f, num), rotation, scale / textureScale, center: true, color, spriteEffects);
			}
			else
			{
				spriteBatch.Draw(texture, val3 + new Vector2(0f, num), rotation, scale / textureScale, center: true, color, spriteEffects);
			}
		}
		float num7 = 0.5f;
		float num8 = 0.8f;
		float num9 = (float)(General.SafeZone).Bottom - MathHelper.Max((float)AButton.Height * num7, font.MeasureString("yo").Y * num8);
		spriteBatch.DrawString("CAST", new Vector2(400f, 50f), Color.AliceBlue, 0f, font.MeasureString("CAST") / 2f, 1.2f, (SpriteEffects)0, 0f);
		spriteBatch.DrawString(alienname, new Vector2(400f, 100f), Color.AliceBlue, 0f, font.MeasureString(alienname) / 2f, 1f, (SpriteEffects)0, 0f);
		spriteBatch.DrawString(alientext, new Vector2(400f, 375f), Color.AliceBlue, 0f, font.MeasureString(alientext) / 2f, 0.7f, (SpriteEffects)0, 0f);
		float num10 = (float)(General.SafeZone).Right - font.MeasureString("next").X * num8;
		float num11 = num10 - (float)AButton.Width * num7 - font.MeasureString(" ").X * num8;
		spriteBatch.Draw(AButton, new Vector2(num11, num9), 0f, num7, center: false, Color.White);
		spriteBatch.DrawString("next", new Vector2(num10, num9), Color.AliceBlue, 0f, centered: false, num8, (SpriteEffects)0, 1f);
	}

	public void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		if (e.GameComponent == owner)
		{
			collection = ServiceHelper.Get<IComponentBinService>().ComponentBin;
			collection.Remove((GameComponent)(object)this);
		}
	}

	public void OnComponentAdded(GameComponentCollectionEventArgs e)
	{
	}
}
