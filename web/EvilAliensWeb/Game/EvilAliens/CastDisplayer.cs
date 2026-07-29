using System;
using System.Collections.Generic;
using EvilAliens.Constants;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using EvilAliensWeb.Compat;

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

	// Card 208da2fe: the "Brain Spawn" cast entry shows the animated cyborg-brain sheet
	// (brainanimated) with the additive blue glow (brainanimatedglow) behind it — the same
	// art the in-game Braineroid uses — instead of the old static brainlargetransglow. The
	// brain frame is drawn through the same interpolate.fx cross-fade the in-game Braineroid
	// uses (DrawInterpolatedFrame, gated by ShouldInterpolate), so the sparse 20-frame sheet
	// plays smooth; because the
	// shader fills the gaps, BrainFps can stay low like the in-game 0.4. The other cast
	// members hand-step single frames, which reads fine at their denser fps.
	private Texture2D brainGlow;

	// Baked defaults for the cast brain's on-screen size + animation speed. Overridable by eye
	// via ?castbrain (DebugFlags.CastBrainScale/CastBrainFps); null override => these ship.
	// Design width 100 (see AlienDrawableGameComponent) => on-screen brain width ~= 100 * scale.
	private const float DefaultBrainScale = 1.7f;

	private const float DefaultBrainFps = 10f;

	// Additive blue-glow shimmer, mirrored from Braineroid.DrawGlow so the cast look matches.
	private const float BrainGlowOmega = 2.6f;

	private const float BrainGlowScaleBase = 1.05f;

	private const float BrainGlowScaleShimmer = 0.04f;

	private const float BrainGlowAlphaBase = 0.5f;

	private const float BrainGlowAlphaShimmer = 0.12f;

	// Debug (?castbrain): boot straight onto the braineroid entry and ignore the advance/asplode
	// input, so the cast brain can be viewed + tuned in place via HarnessScene. Set before the
	// component is added (so Initialize sees it); false in normal play.
	public bool BrainShowcase;

	// Debug (?cast): run the FULL cast state machine via HarnessScene (DebugFlags.CastShow).
	// Unlike BrainShowcase it does not lock to one entry — Enter advances through every
	// member. Set before the component is added (so Initialize sees it); false in normal play.
	public bool CastShowcase;

	private static float BrainScale => EvilAliensWeb.Compat.DebugFlags.CastBrainScale ?? DefaultBrainScale;

	private static float BrainFps => EvilAliensWeb.Compat.DebugFlags.CastBrainFps ?? DefaultBrainFps;

	private CastState state;

	private CastState nextstate;

	private float _time;

	private string alienname;

	private string alientext;

	public CastDisplayer(Game game)
		: base(game)
	{
		base.DrawOrder = 1000;
	}

	public void LoadAnimation(AnimationData animationData)
	{
		texture = content.Load<Texture2D>(animationData.TextureName);
		texturename = animationData.TextureName;
		rows = animationData.rows;
		columns = animationData.columns;
		fps = animationData.fps;
		separatingspace = animationData.separatingspace;
		int frameWidth = columns > 0 ? (texture.LogicalWidth() - (columns - 1) * separatingspace) / columns : texture.LogicalWidth();
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
		// Debug (?castbrain): park straight on the Brain Spawn entry so it can be viewed/tuned.
		// The braineroid state never advances on stateTimer (only intro/waiting do), so stop it —
		// nothing should transition this showcase off the brain. alienname/alientext are set here
		// too (not only in the braineroid Update case): the displayer is added mid-frame, so its
		// FIRST Draw can run before its first Update, and Draw does font.MeasureString(alientext).
		// The real credits path is immune because it starts in `intro`, whose Draw early-returns;
		// forcing braineroid skips that guard, so a null alientext would NRE on frame one.
		if (BrainShowcase)
		{
			state = CastState.braineroid;
			alienname = "Brain Spawn";
			alientext = "Their eons-long goal is to destroy all other intelligent life,\nsince the thoughts of other beings screech at them like the\nforced laughs of a billion art-house movie patrons.";
			stateTimer.Stop();
		}
		// ?cast (full cast): start on intro but cut its 17s hold to a blink so it flips to the
		// first real member (ufo) immediately. The intro state draws nothing, and ufo's Update
		// sets its name/text before the first non-early-return Draw, so no first-frame NRE.
		if (CastShowcase)
		{
			stateTimer.Duration = 100f;
		}
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
		brainGlow = content.Load<Texture2D>("GFX/Sprites/brainanimatedglow");
		AButton = content.Load<Texture2D>("GFX/Preview/small_face_a");
	}

	public override void Update(GameTime gameTime)
	{
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
			// Animated cyborg-brain sheet (5 cols x 4 rows, 20 frames) — same art as the
			// in-game Braineroid. Draw() cross-fades frames through interpolate.fx
			// (DrawInterpolatedFrame), so BrainFps can be low and still play smooth. The
			// glow is drawn in Draw() too.
			EnsureAnimation(new AnimationData("GFX/Sprites/brainanimated", 4, 5, 0, BrainFps, 0, 20));
			_time += (float)gameTime.ElapsedGameTime.TotalSeconds;
			float num2 = 1f + (1f + (float)Math.Sin(_time * 3.32f)) * 0.07f;
			scale = BrainScale * num2;
			if (flag2 && !BrainShowcase)
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
			List<Vector2> posList;
			int posIndex;
			(posList = debrisposition)[posIndex = j] = posList[posIndex] + debrisspeed[j] * (float)gameTime.ElapsedGameTime.TotalMilliseconds;
			List<Vector2> speedList;
			int speedIndex;
			(speedList = debrisspeed)[speedIndex = j] = speedList[speedIndex] + new Vector2(0f, 0.001f * (float)gameTime.ElapsedGameTime.TotalMilliseconds);
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
		texture = null;
		sound.PlayCue("expl2");
		sound.PlayCue("hit_boss");
		Vector2 val = new Vector2(400f, 80f);
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
		BloodExplosion bloodExplosion = BloodExplosion.NewExplosion(collection, base.Game);
		FindSpawnSpot(out var angle, out var range);
		Vector2 position = MyMath.AngleToVector(angle) * range + spawnposition;
		bloodExplosion.Setup(position, size, size * 0.7f, 0.12f, angle);
		bloodExplosion.MakeGreen();
		collection.Add((GameComponent)(object)bloodExplosion);
	}

	private void AsplodeSpiderBoss()
	{
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

	// Soft additive blue glow behind the animated Brain Spawn, tracking its (pulsated) size.
	// Mirrors Braineroid.DrawGlow: the glow texture is pre-tinted blue, drawn white-with-alpha,
	// scaled by the same DrawScale (scale / textureScale) the brain frame uses so the two stay
	// aligned at any sheet resolution. Restores the normal blend mode for the brain draw.
	private void DrawBrainGlow(GameTime gameTime, Vector2 center)
	{
		if (brainGlow == null)
		{
			return;
		}
		float t = (float)gameTime.TotalGameTime.TotalSeconds;
		float s = (float)Math.Sin(t * BrainGlowOmega);
		float glowScale = scale / textureScale * BrainGlowScaleBase * (1f + BrainGlowScaleShimmer * s);
		float alpha = BrainGlowAlphaBase + BrainGlowAlphaShimmer * s;
		spriteBatch.BlendMode = (SpriteBlendMode)2;
		spriteBatch.Draw(brainGlow, center, rotation, glowScale, center: true, new Color(new Vector4(1f, 1f, 1f, alpha)));
		spriteBatch.BlendMode = (SpriteBlendMode)1;
	}

	// Which cast members animate through the interpolation shader — the ones whose in-game
	// object also interpolates. In-game, animated sprites go through
	// AlienDrawableGameComponent.Draw, which interpolates unless interpolationOptions == never;
	// the ones that DON'T interpolate in-game must not here either, or the cast would look
	// smoother than gameplay. Excluded (stepped, matching gameplay): the Alien Battleship
	// (boss = Boss/MarsBoss, both interpolationOptions=never), the Spider Wasp (spider =
	// Spider/FlyingSpider, never), the Spider Stag (spiderboss = SpiderBoss, never), and the
	// Alien Ruler (battleskull) — the in-game BattleSkull draws its AnimatedSprite by raw
	// integer frame, no interpolation. (spiderboss/battleskull are drawn via AnimatedSprite,
	// not this slice path, and brainboss is a single frame, so they never reach the branch
	// that consults this — they're named for the record.) The included ones (UFO, Brain Spawn,
	// Fleet Commander Drone/junkboss, Evil Grinning Face of Death/evilskull, Death Star, The
	// Unnamed Hero/playership) all call base.Draw in-game with interpolationOptions != never.
	private static bool ShouldInterpolate(CastState state)
	{
		switch (state)
		{
		case CastState.ufo:
		case CastState.braineroid:
		case CastState.junkboss:
		case CastState.evilskull:
		case CastState.deathstar:
		case CastState.playership:
			return true;
		default:
			return false;
		}
	}

	// Source rect of one frame of the currently loaded sheet (same grid math as Draw's slice
	// path and AlienDrawableGameComponent.getFrameRectangle).
	private Rectangle FrameRect(int frame)
	{
		int row = frame / columns;
		int col = frame % columns;
		int frameWidth = (texture.LogicalWidth() - (columns - 1) * separatingspace) / columns;
		int frameHeight = (texture.LogicalHeight() - (rows - 1) * separatingspace) / rows;
		return new Rectangle(col * (frameWidth + separatingspace), row * (frameHeight + separatingspace), frameWidth, frameHeight);
	}

	// Draw the current animation frame with frame-to-frame interpolation, mirroring
	// AlienDrawableGameComponent.drawWithInterpolation for the non-additive (blend mode 1)
	// case: the interpolate.fx variant samples the current frame AND the next (at UV +
	// Offset) and lerps by Delta = the fractional frame, and fade.fx carries the (white)
	// tint. CastDisplayer plays the full sheet (curframe wraps at rows*columns), so the next
	// frame wraps back to 0. This is the one cast draw path that interpolates — the others
	// hand-step single frames; ShouldInterpolate gates which states route here.
	private void DrawInterpolatedFrame(Vector2 center)
	{
		int frame = (int)curframe;
		float delta = curframe % 1f;
		int total = rows * columns;
		int nextFrame = (frame + 1) % total;
		Rectangle rect = FrameRect(frame);
		Rectangle nextRect = FrameRect(nextFrame);
		spriteBatch.interpolateEffect.Enable();
		// UV-space offset -> normalise by the ACTUAL (padded) texture size (SpriteBatch texcoords are
		// pixel/paddedSize); rect/nextRect are logical pixel-space frame rects (correct).
		spriteBatch.interpolateEffect.Offset = new Vector2((nextRect).Left - (rect).Left, (nextRect).Top - (rect).Top) / new Vector2((float)texture.Width, (float)texture.Height);
		spriteBatch.interpolateEffect.Delta = delta;
		spriteBatch.fadeEffect.Enable();
		spriteBatch.fadeEffect.Value = (color).ToVector4();
		spriteBatch.Draw(texture, rect, center, rotation, scale / textureScale, center: true, color, spriteEffects);
		spriteBatch.interpolateEffect.Disable();
		spriteBatch.fadeEffect.Disable();
	}

	public override void Draw(GameTime gameTime)
	{
		base.Draw(gameTime);
		spriteBatch.BlendMode = (SpriteBlendMode)1;
		if (state == CastState.intro)
		{
			return;
		}
		if (spiderdeadtimer.Active)
		{
			Color val = new Color(new Vector4(1f, 1f, 1f, MathHelper.Lerp(0f, 1f, spiderdeadtimer.TimeLeft * 3f / stateTimer.Duration)));
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
			int frame = (int)MyMath.Mod((float)gameTime.TotalGameTime.TotalSeconds * 30f, spiderFly.Frames);
			spiderFly.Draw(frame, spawnposition - new Vector2(450f, 200f), Color.White, scale, center: false);
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
			// Additive blue glow behind the animated Brain Spawn (mirrors Braineroid.DrawGlow);
			// drawn before the brain frame so it sits behind. Resets BlendMode to normal after.
			// Additive blue glow behind the animated Brain Spawn only (mirrors Braineroid.DrawGlow).
			if (state == CastState.braineroid)
			{
				DrawBrainGlow(gameTime, val3 + new Vector2(0f, num));
			}
			if ((columns > 1) | (rows > 1))
			{
				// Cast members whose in-game object interpolates get the same interpolate.fx
				// cross-fade here, so sparse sheets play smooth instead of stepping; the rest
				// (mothership, spiders — interpolationOptions=never in-game) hand-step, matching
				// gameplay. See ShouldInterpolate.
				if (ShouldInterpolate(state))
				{
					DrawInterpolatedFrame(val3 + new Vector2(0f, num));
				}
				else
				{
					int num2 = (int)curframe;
					int num3 = num2 / columns;
					int num4 = num2 % columns;
					int num5 = texture.LogicalWidth() - (columns - 1) * separatingspace;
					num5 /= columns;
					int num6 = texture.LogicalHeight() - (rows - 1) * separatingspace;
					num6 /= rows;
					Rectangle source = new Rectangle(num4 * (num5 + separatingspace), num3 * (num6 + separatingspace), num5, num6);
					spriteBatch.Draw(texture, source, val3 + new Vector2(0f, num), rotation, scale / textureScale, center: true, color, spriteEffects);
				}
			}
			else
			{
				spriteBatch.Draw(texture, val3 + new Vector2(0f, num), rotation, scale / textureScale, center: true, color, spriteEffects);
			}
		}
		float num7 = 0.5f;
		float num8 = 0.8f;
		float num9 = (float)(General.SafeZone).Bottom - MathHelper.Max((float)AButton.LogicalHeight() * num7, font.MeasureString("yo").Y * num8);
		spriteBatch.DrawString("CAST", new Vector2(400f, 50f), Color.AliceBlue, 0f, font.MeasureString("CAST") / 2f, 1.2f, (SpriteEffects)0, 0f);
		spriteBatch.DrawString(alienname, new Vector2(400f, 100f), Color.AliceBlue, 0f, font.MeasureString(alienname) / 2f, 1f, (SpriteEffects)0, 0f);
		spriteBatch.DrawString(alientext, new Vector2(400f, 375f), Color.AliceBlue, 0f, font.MeasureString(alientext) / 2f, 0.7f, (SpriteEffects)0, 0f);
		float num10 = (float)(General.SafeZone).Right - font.MeasureString("next").X * num8;
		float num11 = num10 - (float)AButton.LogicalWidth() * num7 - font.MeasureString(" ").X * num8;
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
