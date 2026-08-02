using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using EvilAliensWeb.Compat;   // LogicalWidth/LogicalHeight — padded-dxt-safe texture dimensions

namespace EvilAliens;

public abstract class AlienDrawableGameComponent : DrawableGameComponent, ICollidable, IComponentWatcher, EvilAliensWeb.Compat.Net.INetEntity
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

	// See the Update() comment: measured movement per ms, the AI's only trustworthy velocity.
	private Vector2 _prevPosition;

	private Vector2 _observedVelocity;

	private bool _hasPrevPosition;

	internal Vector2 ObservedVelocity => _observedVelocity;

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

	// Memo for retrieveBoundsFromTexture's cell size. The sheet layout only changes on
	// LoadAnimation (or a texture swap), but the method is called several times per entity per
	// collision pass -- see CollisionHandler -- and each call otherwise costs two
	// TextureDims ConditionalWeakTable lookups. Keyed on everything the arithmetic reads, so a
	// layout change re-derives rather than going stale (card 391e11d2). Note the key is the
	// Texture2D IDENTITY, so it assumes a texture's registered LOGICAL dims never change after
	// first use -- true today (WebContentManager.TryLoadDds registers before handing the texture
	// out), and the reason the memo may hold a strong reference to a swapped-out texture for the
	// component's lifetime, which is one object and deliberately not worth a weak reference.
	private Texture2D cellDimsTexture;
	private int cellDimsColumns;
	private int cellDimsRows;
	private int cellDimsSeparation;
	private float cellDimsWidth;
	private float cellDimsHeight;

	protected CollisionBox retrieveBoundsFromTexture()
	{
		if (collisionBox == null)
		{
			collisionBox = new CollisionBox();
		}
		if ((object)cellDimsTexture != (object)texture || cellDimsColumns != columns
			|| cellDimsRows != rows || cellDimsSeparation != separatingspace)
		{
			float w = texture.LogicalWidth();
			w -= (float)((columns - 1) * separatingspace);
			cellDimsWidth = w / (float)columns;
			float h = texture.LogicalHeight();
			h -= (float)((rows - 1) * separatingspace);
			cellDimsHeight = h / (float)rows;
			cellDimsTexture = texture;
			cellDimsColumns = columns;
			cellDimsRows = rows;
			cellDimsSeparation = separatingspace;
		}
		float cellWidth = cellDimsWidth;
		float cellHeight = cellDimsHeight;
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
		float currentDirection = _direction;
		Vector2 velocity = MyMath.AngleToVector(currentDirection) * _speed;
		Vector2 decelStep = MyMath.AngleToVector(currentDirection) * -1f * MathHelper.Min(_deceleration * elapsedMs, _speed);
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
		// Observed velocity (card f4d1721f): how far this thing ACTUALLY moved last tick, in
		// px/ms. SpeedVector is derived from _speed/_direction and lies for every type that
		// writes Position directly -- which includes SpiderBoss's screen-crossing fly states, the
		// single most important thing for the AI to predict. Sampled at the TOP of Update so it
		// covers a whole previous tick regardless of whether the mover ran before or after
		// base.Update. (Same reasoning as the net layer's observed velocity, kept independent of
		// it because the AI must work with no session up. A frozen net puppet never Updates, so
		// this stays zero for one -- correct, since its owner drives it.)
		float dtMs = Convert.ToSingle(gameTime.ElapsedGameTime.TotalMilliseconds);
		if (_hasPrevPosition && dtMs > 0f)
		{
			_observedVelocity = (_position - _prevPosition) / dtMs;
		}
		_prevPosition = _position;
		_hasPrevPosition = true;
		Vector2 step = MyMath.AngleToVector(_direction) * _speed * dtMs;
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
		Rectangle result = new Rectangle(frameCol * (cellWidth + separatingspace), frameRow * (cellHeight + separatingspace), cellWidth, cellHeight);
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
		Rectangle currentFrameRectangle = getFrameRectangle(currentFrame);
		int nextFrame = currentFrame + 1;
		if (nextFrame >= ActiveLastFrame)
		{
			nextFrame = FirstFrame;
		}
		Rectangle nextFrameRectangle = getFrameRectangle(nextFrame);
		SpriteBlendMode mode = blendMode;
		switch ((int)mode)
		{
		case 2:
		{
			Color currentTint = new Color(new Vector4(1f, 1f, 1f, 1f - frameBlend));
			Color nextTint = new Color(new Vector4(1f, 1f, 1f, frameBlend));
			spriteBatch.Draw(texture, currentFrameRectangle, Position, rotation, DrawScale, center: true, currentTint, spriteEffects);
			spriteBatch.Draw(texture, nextFrameRectangle, Position, rotation, DrawScale, center: true, nextTint, spriteEffects);
			break;
		}
		case 0:
		case 1:
			spriteBatch.interpolateEffect.Enable();
			// UV-SPACE offset: interpolate.fx adds this to the texcoords SpriteBatch generates, which
			// are the frame-rect pixels divided by the ACTUAL (padded) texture size. So the frame->frame
			// delta must be normalised by the padded Width/Height here, NOT the logical size — the frame
			// RECTS above are logical pixel-space (correct), but this ratio lives in padded UV space.
			spriteBatch.interpolateEffect.Offset = new Vector2((float)((nextFrameRectangle).Left - (currentFrameRectangle).Left), (float)((nextFrameRectangle).Top - (currentFrameRectangle).Top)) / new Vector2((float)texture.Width, (float)texture.Height);
			spriteBatch.interpolateEffect.Delta = frameBlend;
			spriteBatch.fadeEffect.Enable();
			spriteBatch.fadeEffect.Value = (color).ToVector4();
			spriteBatch.Draw(texture, currentFrameRectangle, Position, rotation, DrawScale, center: true, color, spriteEffects);
			spriteBatch.interpolateEffect.Disable();
			spriteBatch.fadeEffect.Disable();
			break;
		}
	}

	public void AwardScore(bool combo, ICollidable other)
	{
		if ((!awarded & (PointValue > 0f)) && other is IAlienKiller && ((IAlienKiller)other).Player() >= 0)
		{
			int slot = ((IAlienKiller)other).Player();
			// Online co-op (card b0ab09ec): report the combo-modified figure we just credited.
			// Host -> it rides the death event; client -> it becomes a provisional credit the
			// host's figure later replaces. No-op offline.
			EvilAliensWeb.Compat.Net.NetSession.NoteAward(this, slot, Score.AddScore(PointValue, combo, Position, slot));
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
			// Each slot is credited with ITS OWN combo multiplier, so a boss pays four
			// different figures -- which is why the wire carries a per-slot award array
			// rather than one number (card b0ab09ec).
			if (first)
			{
				EvilAliensWeb.Compat.Net.NetSession.NoteAward(this, i, Score.AddScore(PointValue, combo, Position, i));
				first = false;
			}
			else
			{
				EvilAliensWeb.Compat.Net.NetSession.NoteAward(this, i, Score.AddScore(PointValue, combo, i));
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

	// Online co-op (card b0ab09ec): claim the award slot BEFORE running the real death path,
	// so its AwardScore/AwardScoreToAll no-ops. Used on a client applying a host EvDeath --
	// the FX must play, but the score has to be the host's figure off the wire, never this
	// peer's own combo multiplier. Idempotent; irrelevant offline.
	internal void NetSuppressAward()
	{
		awarded = true;
	}

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

	// One-shot cosmetic beat off the wire (EvFx / NetFxKind), applied to THIS puppet. The host
	// runs the real world, so a hit flash, a chunk breaking off or a fired beam happens inside an
	// Update this puppet never runs -- and unlike a sheet swap it is far too short-lived to ride
	// the round-robin snapshot (a 35ms blink against a 60ms-to-1.2s correction interval).
	//
	// DRAW AND AUDIO ONLY, and IDEMPOTENT: see the INetEntity.NetPlayFx contract. The base is a
	// no-op -- a type with a hit timer or a detach effect of its own overrides it. Do NOT put a
	// generic "start a blink" here: the blink lives in a private timer on each type that has one
	// (KillableAlien.hittimer, Ball.hittimer, SpiderBoss.hittimer), and there is no shared field
	// to write.
	internal virtual void NetPlayFx(EvilAliensWeb.Compat.Net.NetFxKind kind)
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

	// Does this type's `curframe` LOOP FREELY at a constant fps? Default TRUE, and a true answer
	// opts the puppet out of REPLICATED frames: NetPuppets pins curframe once at spawn and the
	// driver's NetAdvanceFrame owns it from there, so the snapshot's CurFrame field is ignored.
	// The NetSpinPerMs idiom one field over, and for the same reason (cards c92f3817 / 0dfc4495 /
	// d3add86f).
	//
	// WHY IT IS NOT FREE TO KEEP CORRECTING IT. Both peers advance the same loop at the same fps,
	// so in STEADY state the correction is a no-op and costs nothing -- it is the disturbances it
	// re-injects that show. MsgWorldSnapshot rides the STREAM lane, which is unordered with
	// maxRetransmits:0 and carries no sequence or timestamp, so a reordered or late entry hands
	// the driver an OLDER frame than the one it is already showing and the animation kicks
	// BACKWARD. Nothing reads curframe but Draw, so there is no correctness argument on the other
	// side of that trade: a locally-run loop is smoother, cheaper and cannot be reordered.
	//
	// OVERRIDE TO FALSE when either half of "free-running at a constant fps" fails:
	//   * Update WRITES curframe from a state machine (Spider's rear-up / land choreography), so
	//     the frame is host-gated and a local loop would animate a pose the host is not in; or
	//   * Update MUTATES fps (MarsBoss ramps 16 -> 32 with HitPointsNormalized), so a puppet --
	//     whose Update never runs -- would free-run at the wrong RATE and drift.
	// A type whose Draw does not read curframe at all (SpiderBoss / FakeBoss / BattleSkull animate
	// an AnimatedSprite through their own replicated animFrame state extra; Wall, Lazer,
	// StationaryBoss, BrainBoss and Powerup are single-frame) is unaffected either way and keeps
	// the default. When in doubt, override to false: the cost is only the pre-existing behaviour.
	internal virtual bool NetFrameLocal => true;

	// INSTANCE-level opt-out from replication (card 9a3175d0): this particular instance is pure
	// scenery, so it gets no NetId, no EvSpawn/EvDeath and no share of the world-snapshot round
	// robin. The SPAWNER is replicated instead (NetCosmeticKind, one "effect on/off" beat) and
	// each peer runs its own copy, so the two screens' copies are in different places -- which is
	// fine by definition for something nothing can interact with.
	//
	// It has to be per instance, not per type: the same FlyingSpider type is a real killable
	// enemy in its foreground form and pure fog in its background one.
	//
	// TWO CONDITIONS, both required, and both the caller's job to keep true:
	//   * the instance can NEVER become collidable -- a puppet that turns into a hazard on one
	//     screen and not the other is a desync, not a cosmetic divergence;
	//   * nothing gameplay-visible reads it. Both current users are in Oracle.GetBaddies, which
	//     is the AI's whole world model, and are invisible to it only because every consumer
	//     there gates on Collides (PlayerShip.IsAiShootable / the IsAiThreat scan).
	//
	// Read at the ComponentAdded seam, so it must be FINAL before ComponentBin.Add -- the same
	// configure-then-Add rule tools/audit_add_order.py already lints.
	internal virtual bool NetCosmeticOnly => false;

	// ---- INetEntity (card 25ad0659 step 2c-ii) ------------------------------------------
	// The net cores read this type through EvilAliensWeb.Compat.Net.INetEntity rather than by
	// name. Every member above is already the implementation; these are the forwards for the
	// ones an interface cannot reach directly.
	//
	// EXPLICIT, not widened to public: INetEntity is internal and this class is public, so an
	// implicit implementation would mean making a dozen net-only seams part of a public game
	// type's API. (INetScene took the opposite choice in 2c-i because GameScene is itself
	// internal, so widening there widened nothing.) Position, Enabled and IsDead are already
	// public and satisfy their members implicitly, with no forward needed.
	//
	// `scale`, `rotation` and `curframe` are FIELDS, which is the only reason the first three
	// exist at all -- an interface cannot expose a field. Nothing else may read or write them
	// through these; the fields stay the game's own.

	float EvilAliensWeb.Compat.Net.INetEntity.NetRotation
	{
		get
		{
			return rotation;
		}
		set
		{
			rotation = value;
		}
	}

	float EvilAliensWeb.Compat.Net.INetEntity.NetScale
	{
		get
		{
			return scale;
		}
		set
		{
			scale = value;
		}
	}

	// Read-only on the seam: the host CAPTURES curframe for a snapshot, and the two writers a
	// puppet has -- NetSetFrame (snap to a replicated frame) and NetAdvanceFrame (animate on
	// real dt) -- both wrap into the type's active frame range. A bare setter would let a
	// caller index off the sheet.
	float EvilAliensWeb.Compat.Net.INetEntity.NetCurFrame => curframe;

	Vector2 EvilAliensWeb.Compat.Net.INetEntity.NetSpeedVector
	{
		get
		{
			return NetSpeedVector;
		}
		set
		{
			NetSpeedVector = value;
		}
	}

	float EvilAliensWeb.Compat.Net.INetEntity.NetPointValue => NetPointValue;

	// Virtual on the class, so these forwards keep dispatching to the override -- the whole
	// point of NetSpinPerMs (Asteroid) and NetCosmeticOnly (background FlyingSpider) is that a
	// subtype answers differently.
	float EvilAliensWeb.Compat.Net.INetEntity.NetSpinPerMs => NetSpinPerMs;

	bool EvilAliensWeb.Compat.Net.INetEntity.NetFrameLocal => NetFrameLocal;

	bool EvilAliensWeb.Compat.Net.INetEntity.NetCosmeticOnly => NetCosmeticOnly;

	void EvilAliensWeb.Compat.Net.INetEntity.NetSetFrame(float frame)
	{
		NetSetFrame(frame);
	}

	void EvilAliensWeb.Compat.Net.INetEntity.NetAdvanceFrame(float dtSeconds)
	{
		NetAdvanceFrame(dtSeconds);
	}

	void EvilAliensWeb.Compat.Net.INetEntity.NetTickTimers(GameTime gameTime)
	{
		NetTickTimers(gameTime);
	}

	void EvilAliensWeb.Compat.Net.INetEntity.NetDriveExtras(GameTime gameTime)
	{
		NetDriveExtras(gameTime);
	}

	void EvilAliensWeb.Compat.Net.INetEntity.NetSuppressAward()
	{
		NetSuppressAward();
	}

	void EvilAliensWeb.Compat.Net.INetEntity.NetPlayFx(EvilAliensWeb.Compat.Net.NetFxKind kind)
	{
		NetPlayFx(kind);
	}

	// The two discriminants. The base answers "no" to both; KillableAlien and Powerup override
	// with `this`. Virtual rather than a type test inside this class so a future replicable
	// subtype declares its own answer where its own code lives.
	EvilAliensWeb.Compat.Net.INetKillable EvilAliensWeb.Compat.Net.INetEntity.NetKillable => NetKillableSelf;

	EvilAliensWeb.Compat.Net.INetPickup EvilAliensWeb.Compat.Net.INetEntity.NetPickup => NetPickupSelf;

	private protected virtual EvilAliensWeb.Compat.Net.INetKillable NetKillableSelf => null;

	private protected virtual EvilAliensWeb.Compat.Net.INetPickup NetPickupSelf => null;

	public virtual void OnComponentAdded(GameComponentCollectionEventArgs e)
	{
	}
}
