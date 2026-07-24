using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using EvilAliensWeb.Compat;   // LogicalWidth/LogicalHeight — padded-dxt-safe texture dimensions

namespace EvilAliens;

public abstract class AlienDrawableGameComponent : DrawableGameComponent, ICollidable, IComponentWatcher
{
	public enum InterpolationOptions
	{
		always,
		never,
		as_specified
	}

	public delegate void DeathEvent(object sender);

	protected ScoreVisualiser Score;

	protected float PointValue;

	private bool awarded;

	protected List<Timer> timers = new List<Timer>();

	public Texture2D texture;

	public string texturename;

	public float rotation;

	public float scale = 1f;

	// Supersample decoupling. An upscaled sheet has more texels per frame, but the engine
	// sizes a sprite as frameTexels * scale, so a bigger frame would render bigger. textureScale
	// = actualFrameWidth / designFrameWidth; dividing the draw scale (and collision size) by it
	// keeps on-screen size/position/collision IDENTICAL while the extra texels just add crispness.
	// Sheets opt in via the registry below (name -> original/design frame width); others stay 1.
	public float textureScale = 1f;

	// Per-instance ground-shadow tuning, read by Floor.cs when it builds this entity's shadow.
	// Identity (offset 0, size 1) is the default for every entity and reproduces the original
	// shadow exactly; the landed Mars UFOs set these from LandedOffsets while parked so the
	// author can nudge the parked ship's shadow (x + y along the floor line, and width).
	public Vector2 ShadowOffset = Vector2.Zero;

	public float ShadowSize = 1f;

	private static readonly Dictionary<string, int> DesignFrameWidth = new Dictionary<string, int>
	{
		{ "GFX/Sprites/ufosheet", 48 },
		{ "GFX/Sprites/smallship", 48 },
		{ "GFX/Sprites/faceofdeathspritesheet", 48 },
		{ "GFX/Sprites/deathstarsheet2", 48 },
		{ "GFX/Sprites/playersheet", 48 },
		// single-frame "landed" stills: design width is the WHOLE texture (not a frame),
		// drawn directly in UFO.Draw so the factor is removed there via SuperSampleFactor.
		// Keys must match the stationarySpriteName strings exactly (note the capital S).
		{ "GFX/Sprites/ufometpootjes", 55 },
		{ "GFX/Sprites/Smallship_landed", 48 },
		{ "GFX/Sprites/Mediumship_landed", 216 },
		{ "GFX/Sprites/Mothership_landed", 456 },
		// drawn DIRECTLY (not via the component). spiderjump is now a 6x4 soar ANIMATION
		// sheet played frame-by-frame in Spider.Draw (design = per-CELL width 399/3);
		// wing1 is the FlyingSpider's flapping wing. Both divide draw scale by
		// SuperSampleFactor at their draw site.
		{ "GFX/Sprites/spiderjump", 133 },
		{ "GFX/Sprites/wing1", 92 },
		// spider_sheet2: the 7x7 "rear up" animation (drawn through the component by the grounded
		// Spider). Design width 160 -> 256px cells are 1:1 at a 1280x1024 window (160 * 1.6).
		{ "GFX/Sprites/spider_sheet2", 160 },
		// Asteroids (single frame, design = full texture width; drawn through the component so
		// size auto-corrects). large_asteroid is the hi-res (7x) big level-opener; the AsteroidSmall
		// variants are lower-res (1.5x) for the small normal asteroids (scale 0.45) AND the JunkBoss
		// balls (Ball.cs), picked at random per spawn.
		{ "GFX/Sprites/large_asteroid", 179 },
		{ "GFX/Sprites/AsteroidSmall1", 179 },
		{ "GFX/Sprites/AsteroidSmall2", 179 },
		{ "GFX/Sprites/AsteroidSmall3", 179 },
		{ "GFX/Sprites/AsteroidSmall4", 179 },
		// power-up bubble: HD (4x) replacement for the old 32px disc, tinted per type. Drawn
		// through the component by the Powerup entity; the HelpText/InstructionsMenu draw it
		// DIRECTLY and divide their scale by SuperSampleFactor.
		{ "GFX/Sprites/powerupbw", 32 },
		// awardment-screen decoration skull: HD (2x) still, design = full texture width. Drawn
		// DIRECTLY in SubMenuAwardmentText, which divides its scale by SuperSampleFactor.
		{ "GFX/Menu/evilskull", 376 },
		// JunkBoss "fleet commander drone": idle + attract grid anims (built by
		// tools/upscale/build_eye_anims.py from AnimGen takes). The body renders 1:1 at the
		// 1440 cap (cell px = body 47 * 2.4); attract's larger cell is the lightning halo
		// extending beyond the same static body, so both states draw the body at one size.
		{ "GFX/Sprites/eye_idle", 48 },
		{ "GFX/Sprites/eye_attract", 61 },
		// Brain final boss (InsaneBossI/Level3): the HD cyborg brain+cables (brainbosshd) and its
		// additive animated glow aura (brainbossaura) share ONE design width so they draw aligned.
		// Boss-specific art. (The Braineroids now use the animated brainanimated sheet below.)
		{ "GFX/Sprites/brainbosshd", 850 },
		{ "GFX/Sprites/brainbossaura", 850 },
		// Animated Braineroid sheet (5 cols x 4 rows, 512px cells). Design width 100
		// fixes on-screen size = 100*scale regardless of cell px (the cell resolution
		// only adds crispness); the Braineroid draws at scale ~2/1/0.35 (huge/med/small)
		// to match the original brainlargetransglow on-screen size.
		{ "GFX/Sprites/brainanimated", 100 },
		// small-sprite upscale effort (Trello): solid sprites re-rendered at higher res.
		// bullets + arrow are procedural (tools/upscale/gen_sprites.py); blooddrop(_green),
		// option, braingoo, photocamera, awardmentblade are keyed/repacked AI redraws
		// (tools/upscale/repack_misc.py). Component-drawn ones (bulletevil/good, blooddrop,
		// option) auto-correct via this registry; the direct-draw ones (arrow,
		// blooddrop_green, photocamera, awardmentblade) ALSO divide their draw scale by
		// SuperSampleFactor at their site. (braingoo is re-keyed at 1x, not upsized -- its
		// entry/divide are a harmless no-op, kept so a later factor bump just works.)
		{ "GFX/Sprites/bulletevil", 16 },
		{ "GFX/Sprites/bulletgood", 16 },
		{ "GFX/Sprites/blooddrop", 15 },
		{ "GFX/Sprites/blooddrop_green", 15 },
		{ "GFX/Sprites/option", 24 },
		// HUD bomb-count icon: a procedural red blast/shockwave (tools/upscale/gen_sprites.py
		// blast_burst). Drawn directly in ScoreVisualiser, which divides its scale by
		// SuperSampleFactor; design width 24 keeps the same on-screen size/spacing as the
		// old `option` reuse it replaced.
		{ "GFX/Sprites/bombicon", 24 },
		{ "GFX/Sprites/braingoo", 45 },
		{ "GFX/Sprites/photocamera", 54 },
		{ "GFX/Sprites/awardmentblade", 487 },
		{ "GFX/Sprites/arrow", 49 },
		// glow sprites (also upscaled). connector + blast draw through the component
		// (base.Draw) so the registry auto-corrects; singleconnectorglow (PlayerShip)
		// draws DIRECTLY and divides by SuperSampleFactor there. (Quad's flare
		// self-normalises via diameterPx/glow.Width, so it needs no factor; Floor's
		// shadow likewise self-normalises via shadowimage.Width -- no entry, no divide.)
		{ "GFX/Sprites/connector", 180 },
		{ "GFX/Sprites/blast", 384 },
		{ "GFX/Sprites/singleconnectorglow", 89 },
		// parachute + plasmaball2: AI redraws DOWNSCALED below the original res for ~1:1
		// texels (366->293, 697->523); registering the ORIGINAL width keeps on-screen size
		// unchanged (both component-drawn -> the draw auto-corrects; PlasmaBall's hand-rolled
		// collision radius multiplies by DrawScale, not raw scale, to track the visible disc).
		{ "GFX/Sprites/parachute", 366 },
		{ "GFX/Sprites/plasmaball2", 697 }
	};

	// effective on-screen draw scale once the supersample factor is removed
	protected float DrawScale => scale / textureScale;

	// factor for a registered sheet given its actual per-frame texel width (1 if not registered);
	// used by the few sites that draw these textures directly instead of through this component
	public static float SuperSampleFactor(string textureName, int actualFrameWidth)
	{
		return DesignFrameWidth.TryGetValue(textureName, out int dfw) && dfw > 0
			? (float)actualFrameWidth / dfw : 1f;
	}

	public int rows;

	public int columns;

	public int separatingspace;

	public float curframe;

	public float fps;

	// Active play/loop range: frames [FirstFrame, LastFrame) cycle (LastFrame exclusive).
	// LastFrame <= 0 falls back to the whole sheet (rows*columns). Set from AnimationData by
	// LoadAnimation; lets a sheet hold a non-grid frame count or a consumer loop a sub-range of
	// a longer animation. ActiveLastFrame resolves the <=0 "whole sheet" sentinel.
	public int FirstFrame;

	public int LastFrame;

	private int ActiveLastFrame => (LastFrame > FirstFrame) ? LastFrame : rows * columns;

	public SpriteEffects spriteEffects;

	public SpriteBlendMode blendMode = (SpriteBlendMode)1;

	protected Color color;

	public InterpolationOptions interpolationOptions = InterpolationOptions.as_specified;

	private bool _collides = true;

	protected InputHandler input;

	protected ComponentBin collection;

	protected SpriteBatchWrapper spriteBatch;

	protected ContentManager content;

	protected SoundManager sound;

	protected Oracle oracle;

	private Vector2 _position = Vector2.Zero;

	private float _minimumSpeed;

	private float _maximumSpeed;

	private float _deceleration;

	private float _acceleration;

	private float _direction;

	private float _speed;

	private bool isdead;

	private CollisionBox collisionBox;

	public abstract ICollisionType CollisionType { get; }

	public bool Collides
	{
		get
		{
			return _collides;
		}
		set
		{
			_collides = value;
		}
	}

	public bool IsDead => isdead;

	public Vector2 Position
	{
		get
		{
			return _position;
		}
		set
		{
			_position = value;
		}
	}

	protected float Speed
	{
		get
		{
			return _speed;
		}
		set
		{
			_speed = value;
		}
	}

	protected Vector2 SpeedVector
	{
		get
		{
			return _speed * MyMath.AngleToVector(_direction);
		}
		set
		{
			_speed = (value).Length();
			_direction = MyMath.VectorToAngle(value);
		}
	}

	protected float Direction
	{
		get
		{
			return _direction;
		}
		set
		{
			_direction = value;
		}
	}

	protected float Acceleration
	{
		get
		{
			return _acceleration;
		}
		set
		{
			_acceleration = value;
		}
	}

	protected float Deceleration
	{
		get
		{
			return _deceleration;
		}
		set
		{
			_deceleration = value;
		}
	}

	protected float MaxSpeed
	{
		get
		{
			return _maximumSpeed;
		}
		set
		{
			_maximumSpeed = value;
		}
	}

	protected float MinSpeed
	{
		get
		{
			return _minimumSpeed;
		}
		set
		{
			_minimumSpeed = value;
		}
	}

	protected Vector2 DirectionalVector
	{
		get
		{
			return MyMath.AngleToVector(_direction);
		}
		set
		{
			_direction = MyMath.VectorToAngle(value);
		}
	}

	public event DeathEvent OnDeath;

	public AlienDrawableGameComponent(Game game)
		: base(game)
	{
		input = ServiceHelper.Get<IInputHandlerService>().InputHandler;
		spriteBatch = ServiceHelper.Get<ISpriteBatchWrapperService>().SpriteBatchWrapper;
		collection = ServiceHelper.Get<IComponentBinService>().ComponentBin;
		content = ServiceHelper.Get<IContentManagerService>().ContentManager;
		sound = ServiceHelper.Get<ISoundManagerService>().SoundManager;
		Score = ServiceHelper.Get<IScoreService>().Score;
		oracle = ServiceHelper.Get<IOracleService>().Oracle;
	}

	protected bool OffScreen(float buffer)
	{
		if (!(Position.X > 800f + buffer) && !(Position.X < 0f - buffer) && !(Position.Y > 600f + buffer))
		{
			return Position.Y < 0f - buffer;
		}
		return true;
	}

	protected void AddTimer(Timer t)
	{
		timers.Add(t);
	}

	protected void Die()
	{
		if (!isdead)
		{
			collection.Remove((GameComponent)(object)this);
			if (this.OnDeath != null)
			{
				this.OnDeath(this);
			}
			isdead = true;
		}
	}

	protected CollisionBox retrieveBoundsFromTexture()
	{
		if (collisionBox == null)
		{
			collisionBox = new CollisionBox();
		}
		float cellWidth = texture.LogicalWidth();
		cellWidth -= (float)((columns - 1) * separatingspace);
		cellWidth /= (float)columns;
		float cellHeight = texture.LogicalHeight();
		cellHeight -= (float)((rows - 1) * separatingspace);
		cellHeight /= (float)rows;
		collisionBox.TopLeft = new Vector2((0f - cellWidth * DrawScale) / 2f, (0f - cellHeight * DrawScale) / 2f) * 0.6f;
		collisionBox.BottomRight = new Vector2(cellWidth * DrawScale / 2f, cellHeight * DrawScale / 2f) * 0.6f;
		return collisionBox;
	}

	public void LoadAnimation(AnimationData animationData)
	{
		texture = content.Load<Texture2D>(animationData.TextureName);
		texturename = animationData.TextureName;
		rows = animationData.rows;
		columns = animationData.columns;
		fps = animationData.fps;
		separatingspace = animationData.separatingspace;
		FirstFrame = animationData.FirstFrame;
		LastFrame = (animationData.LastFrame > 0) ? animationData.LastFrame : rows * columns;
		textureScale = SuperSampleFactor(texturename, texture.LogicalWidth() / columns);
		curframe = FirstFrame;
		color = Color.White;
	}

	protected void Move(Vector2 direction, GameTime gameTime)
	{
		if (direction != Vector2.Zero)
		{
			Move((float?)MyMath.VectorToAngle(direction), gameTime);
		}
		else
		{
			Move((float?)null, gameTime);
		}
	}

	protected void Move(GameTime gameTime)
	{
		Move((float?)null, gameTime);
	}

	protected void Move(float? direction, GameTime gameTime)
	{
		float elapsedMs = Convert.ToSingle(gameTime.ElapsedGameTime.TotalMilliseconds);
		float direction2 = _direction;
		Vector2 velocity = MyMath.AngleToVector(direction2) * _speed;
		Vector2 decelStep = MyMath.AngleToVector(direction2) * -1f * MathHelper.Min(_deceleration * elapsedMs, _speed);
		Vector2 accelStep = ((!direction.HasValue) ? Vector2.Zero : (MyMath.AngleToVector(direction.Value) * (_acceleration + _deceleration) * elapsedMs));
		Vector2 newVelocity = velocity + decelStep + accelStep;
		_direction = MyMath.VectorToAngle(newVelocity);
		_speed = MathHelper.Clamp((newVelocity).Length(), _minimumSpeed, _maximumSpeed);
	}

	public override void Update(GameTime gameTime)
	{
		foreach (Timer timer in timers)
		{
			timer.Update(gameTime);
		}
		Vector2 step = MyMath.AngleToVector(_direction) * _speed * Convert.ToSingle(gameTime.ElapsedGameTime.TotalMilliseconds);
		_position += step;
		float span = ActiveLastFrame - FirstFrame;
		if (span <= 0f)
		{
			span = 1f;
		}
		curframe += fps * (float)gameTime.ElapsedGameTime.TotalSeconds;
		curframe = FirstFrame + ((curframe - FirstFrame) % span + span) % span;
		base.Update(gameTime);
	}

	public override void Draw(GameTime gameTime)
	{
		spriteBatch.BlendMode = blendMode;
		if (columns > 1 || rows > 1)
		{
			switch (interpolationOptions)
			{
			case InterpolationOptions.always:
				drawWithInterpolation();
				break;
			case InterpolationOptions.as_specified:
				if (Settings.GetInstance().Interpolate)
				{
					drawWithInterpolation();
				}
				else
				{
					drawWithoutInterpolation();
				}
				break;
			case InterpolationOptions.never:
				drawWithoutInterpolation();
				break;
			}
		}
		else
		{
			spriteBatch.Draw(texture, _position, rotation, DrawScale, center: true, color, spriteEffects);
		}
		base.Draw(gameTime);
	}

	private void drawWithoutInterpolation()
	{
		bool needsFade = spriteBatch.colorizeEffect.Enabled || spriteBatch.lightenEffect.Enabled;
		Rectangle frameRectangle = getFrameRectangle((int)curframe);
		if (needsFade)
		{
			spriteBatch.fadeEffect.Enable();
			spriteBatch.fadeEffect.Value = (color).ToVector4();
		}
		spriteBatch.Draw(texture, frameRectangle, Position, rotation, DrawScale, center: true, color, spriteEffects);
		if (needsFade)
		{
			spriteBatch.fadeEffect.Disable();
		}
	}

	private Rectangle getFrameRectangle(int framenr)
	{
		int frameRow = framenr / columns;
		int frameCol = framenr % columns;
		int cellWidth = texture.LogicalWidth() - (columns - 1) * separatingspace;
		cellWidth /= columns;
		int cellHeight = texture.LogicalHeight() - (rows - 1) * separatingspace;
		cellHeight /= rows;
		Rectangle result = default(Rectangle);
		(result) = new Rectangle(frameCol * (cellWidth + separatingspace), frameRow * (cellHeight + separatingspace), cellWidth, cellHeight);
		return result;
	}

	private void drawWithInterpolation()
	{
		int currentFrame = (int)curframe;
		float frameBlend = curframe % 1f;
		if (!spriteBatch.colorizeEffect.Enabled)
		{
			_ = spriteBatch.lightenEffect.Enabled;
		}
		Rectangle frameRectangle = getFrameRectangle(currentFrame);
		int nextFrame = currentFrame + 1;
		if (nextFrame >= ActiveLastFrame)
		{
			nextFrame = FirstFrame;
		}
		Rectangle frameRectangle2 = getFrameRectangle(nextFrame);
		SpriteBlendMode mode = blendMode;
		switch ((int)mode)
		{
		case 2:
		{
			Color currentTint = default(Color);
			(currentTint) = new Color(new Vector4(1f, 1f, 1f, 1f - frameBlend));
			Color nextTint = default(Color);
			(nextTint) = new Color(new Vector4(1f, 1f, 1f, frameBlend));
			spriteBatch.Draw(texture, frameRectangle, Position, rotation, DrawScale, center: true, currentTint, spriteEffects);
			spriteBatch.Draw(texture, frameRectangle2, Position, rotation, DrawScale, center: true, nextTint, spriteEffects);
			break;
		}
		case 0:
		case 1:
			spriteBatch.interpolateEffect.Enable();
			// UV-SPACE offset: interpolate.fx adds this to the texcoords SpriteBatch generates, which
			// are the frame-rect pixels divided by the ACTUAL (padded) texture size. So the frame->frame
			// delta must be normalised by the padded Width/Height here, NOT the logical size — the frame
			// RECTS above are logical pixel-space (correct), but this ratio lives in padded UV space.
			spriteBatch.interpolateEffect.Offset = new Vector2((float)((frameRectangle2).Left - (frameRectangle).Left), (float)((frameRectangle2).Top - (frameRectangle).Top)) / new Vector2((float)texture.Width, (float)texture.Height);
			spriteBatch.interpolateEffect.Delta = frameBlend;
			spriteBatch.fadeEffect.Enable();
			spriteBatch.fadeEffect.Value = (color).ToVector4();
			spriteBatch.Draw(texture, frameRectangle, Position, rotation, DrawScale, center: true, color, spriteEffects);
			spriteBatch.interpolateEffect.Disable();
			spriteBatch.fadeEffect.Disable();
			break;
		}
	}

	public void AwardScore(bool combo, ICollidable other)
	{
		if ((!awarded & (PointValue > 0f)) && other is IAlienKiller && ((IAlienKiller)other).Player() >= 0)
		{
			Score.AddScore(PointValue, combo, Position, ((IAlienKiller)other).Player());
			awarded = true;
		}
	}

	public void AwardScoreToAll(bool combo)
	{
		if (!(!awarded & (PointValue > 0f)))
		{
			return;
		}
		// Per SEATED slot, not 0..Players-1: online co-op's roster is host-allocated and sparse
		// (card 4d904410), so indexing by loop counter would pay unseated slots and skip real
		// ones. The FIRST seated player still gets the positional floating text.
		bool first = true;
		for (int i = 0; i < Oracle.MaxPlayers; i++)
		{
			if (!oracle.IsSeated(i))
			{
				continue;
			}
			if (first)
			{
				Score.AddScore(PointValue, combo, Position, i);
				first = false;
			}
			else
			{
				Score.AddScore(PointValue, combo, i);
			}
		}
		awarded = true;
	}

	public override void Initialize()
	{
		isdead = false;
		awarded = false;
		foreach (Timer timer in timers)
		{
			timer.Reset();
		}
		base.Initialize();
	}

	public bool DetectCollision(ICollidable other)
	{
		if (Collides)
		{
			if (other is AlienDrawableGameComponent)
			{
				if (((AlienDrawableGameComponent)other).Collides)
				{
					return CollisionType.TestCollision(((AlienDrawableGameComponent)other).CollisionType);
				}
				return false;
			}
			return CollisionType.TestCollision(other.GetCollisionType());
		}
		return false;
	}

	public virtual void CollidesWith(ICollidable other)
	{
	}

	public ICollisionType GetCollisionType()
	{
		return CollisionType;
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		if (texturename != null)
		{
			texture = content.Load<Texture2D>(texturename);
		}
	}

	public virtual void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		if (e.GameComponent == this)
		{
			this.OnDeath = null;
		}
	}

	// ---- Online co-op replication seams (Compat/Net, card 11.2) --------------------------
	// Client puppets run with Enabled=false (gameplay Update never ticks); the NetPuppet
	// driver moves them through these instead. Host snapshot encode reads them.

	internal Vector2 NetSpeedVector
	{
		get
		{
			return SpeedVector;
		}
		set
		{
			SpeedVector = value;
		}
	}

	internal float NetPointValue => PointValue;

	// Advance the sheet animation exactly like Update does (same wrap math), on real dt.
	internal void NetAdvanceFrame(float dtSeconds)
	{
		float span = ActiveLastFrame - FirstFrame;
		if (span <= 0f)
		{
			span = 1f;
		}
		curframe += fps * dtSeconds;
		curframe = FirstFrame + ((curframe - FirstFrame) % span + span) % span;
	}

	// Snap the animation to a replicated frame, wrapped into the active range (the host may
	// run a different FirstFrame/LastFrame window mid-transition; never index off the sheet).
	internal void NetSetFrame(float frame)
	{
		float span = ActiveLastFrame - FirstFrame;
		if (span <= 0f)
		{
			span = 1f;
		}
		curframe = FirstFrame + ((frame - FirstFrame) % span + span) % span;
	}

	// A frozen puppet's timers still need to run: KillableAlien's hit-blink decay, cosmetic
	// pulse timers, etc. all live in `timers` and are only ever ticked from Update.
	internal void NetTickTimers(GameTime gameTime)
	{
		foreach (Timer timer in timers)
		{
			timer.Update(gameTime);
		}
	}

	// Per-tick client-puppet hook, called by NetPuppetDriver once per frame (UPDATE phase, not
	// Draw/snapshot) after the base dead-reckon. Default no-op. A type overrides it when a frozen
	// puppet needs to manage a live CHILD component the host draws by hand -- e.g. the enemy
	// laser-charge glow (a LazerGenerator the frozen Update would normally spawn); the descriptor's
	// ApplyStateExtra only records the replicated charge state, and the actual child spawn/free
	// happens HERE, so the descriptor contract's "never spawn from ApplyStateExtra" holds.
	internal virtual void NetDriveExtras(GameTime gameTime)
	{
	}

	// Radians/ms of FREE-SPINNING, purely cosmetic rotation. A type overriding this to a
	// non-zero value opts its puppets OUT of replicated rotation: the driver spins them
	// locally at this rate and the snapshot's rotation field is ignored for them.
	//
	// WHY: a puppet's Update is frozen, so a continuously spinning type's rotation could only
	// ever advance when its turn came up in the ~16.7 Hz round-robin snapshot -- visibly
	// choppy (asteroids). Rotation that no hitbox reads is not worth a wire round-trip to get
	// wrong; spinning locally is both smoother and free. Only override it where rotation is
	// genuinely cosmetic -- a type whose Draw or collision depends on a rotation the HOST
	// chose (a lazer's beam angle) must keep taking the replicated value.
	internal virtual float NetSpinPerMs => 0f;

	public virtual void OnComponentAdded(GameComponentCollectionEventArgs e)
	{
	}
}
