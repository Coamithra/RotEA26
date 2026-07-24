using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using EvilAliensWeb.Compat;

namespace EvilAliens;

internal class AnimatedMessage : DrawableGameComponent, IComponentWatcher
{
	private enum MessageState
	{
		enter,
		show,
		leave,
		end
	}

	public enum MessageType
	{
		starwarsblue,
		redwarning,
		unlocked,
		defeat,
		cheatwarning,
		devcomment
	}

	public enum UnlockType
	{
		challenge,
		cheat,
		level,
		difficulty
	}

	public delegate void FinishEvent(object sender);

	private bool isShort;

	private string text;

	private SoundManager.Texts speechText;

	private Timer timer = new Timer(1000f, repeating: false);

	private Color color;

	private Vector2 position;

	private float scale;

	private bool soundplayed;

	private float fadefactor;

	private MessageState state;

	private MessageType type;

	private UnlockType unlocktype;

	private ContentManager content;

	private SpriteFont font;

	private Texture2D arrow;

	private Texture2D blank;

	private SoundManager sound;

	private SpriteBatchWrapper spriteBatch;

	private ComponentBin collection;

	private InputHandler input;

	private int selectedaspect;

	private float warningDirection;

	private Vector4 targetcolor;

	private Vector4 initialcolor;

	// Card 623f16d9: max design-space width the "X Unlocked!" popup's two lines are allowed
	// to fill before shrinking (800-wide playfield, ~40px margin each side).
	private const float UnlockedTextMaxWidth = 720f;

	public event FinishEvent OnFinished;

	protected override void LoadContent()
	{
		base.LoadContent();
		font = content.Load<SpriteFont>("GFX/Menu/menufont");
		arrow = content.Load<Texture2D>("GFX/Sprites/arrow");
		blank = content.Load<Texture2D>("GFX/Game/blank");
	}

	public AnimatedMessage(Game game)
		: base(game)
	{
		base.DrawOrder = 910;
		spriteBatch = ServiceHelper.Get<ISpriteBatchWrapperService>().SpriteBatchWrapper;
		content = ServiceHelper.Get<IContentManagerService>().ContentManager;
		collection = ServiceHelper.Get<IComponentBinService>().ComponentBin;
		input = ServiceHelper.Get<IInputHandlerService>().InputHandler;
		sound = ServiceHelper.Get<ISoundManagerService>().SoundManager;
	}

	public static AnimatedMessage NewAnimatedMessage(ComponentBin collection, Game game)
	{
		AnimatedMessage animatedMessage = collection.Recycle<AnimatedMessage>();
		if (animatedMessage == null)
		{
			animatedMessage = new AnimatedMessage(game);
		}
		return animatedMessage;
	}

	public void SetUnlockType(UnlockType type)
	{
		unlocktype = type;
	}

	public void Setup(string text, SoundManager.Texts speechText, MessageType type)
	{
		isShort = false;
		switch (type)
		{
		case MessageType.cheatwarning:
			timer.Duration = 5000f;
			position = new Vector2(400f, 300f);
			color = Color.AliceBlue;
			scale = 1f;
			break;
		case MessageType.starwarsblue:
			timer.Duration = 400f;
			position = new Vector2(400f, 150f);
			targetcolor = new Vector4(0.44f, 0.7f, 1f, 0.8f);
			initialcolor = targetcolor / 3f;
			scale = 0f;
			break;
		case MessageType.redwarning:
			timer.Duration = 200f;
			color = new Color(Vector4.Zero);
			scale = 2.3f;
			SetWarningDirection(-(float)Math.PI / 4f);
			break;
		case MessageType.unlocked:
			color = new Color(new Vector4(0.8f, 0.4f, 0.2f, 0f));
			timer.Duration = 200f;
			scale = 2.7f;
			position = new Vector2(0f, 100f);
			break;
		case MessageType.defeat:
		{
			timer.Duration = 1500f;
			Color red = Color.Red;
			color = new Color(new Vector4((red).ToVector3(), 0f));
			scale = 2.5f;
			fadefactor = 0f;
			break;
		}
		case MessageType.devcomment:
			color = Color.AliceBlue;
			scale = 0.8f;
			break;
		}
		this.type = type;
		this.text = text;
		this.speechText = speechText;
	}

	public override void Initialize()
	{
		timer.Reset();
		timer.Start();
		state = MessageState.enter;
		soundplayed = false;
		base.Initialize();
	}

	public override void Draw(GameTime gameTime)
	{
		base.Draw(gameTime);
		spriteBatch.BlendMode = (SpriteBlendMode)1;
		switch (type)
		{
		case MessageType.cheatwarning:
		{
			Vector2 origin6 = font.MeasureString(text) / 2f;
			spriteBatch.DrawString(font, text, position, color, 0f, origin6, scale, (SpriteEffects)0, 0f);
			break;
		}
		case MessageType.starwarsblue:
		{
			Vector2 origin5 = font.MeasureString(text) / 2f;
			spriteBatch.DrawString(font, text, position, color, 0f, origin5, scale, (SpriteEffects)0, 0f);
			break;
		}
		case MessageType.redwarning:
		{
			Vector2 origin4 = font.MeasureString(text) / 2f;
			// In-game "Warning!" / "Danger!" alert: plain text. The chrome sheen was removed
			// here (card 2b5867da) - it read wrong during actual gameplay. The tint alpha
			// flicker/fade is preserved; the rotating arrow was always plain.
			spriteBatch.DrawString(font, text, position, color, 0f, origin4, scale, (SpriteEffects)0, 0f);
			MyMath.Mod(warningDirection, (float)Math.PI * 2f);
			Vector2 val2 = new Vector2(400f, 300f) + MyMath.AngleToVector(warningDirection) * 275f;
			spriteBatch.Draw(arrow, val2, warningDirection + (float)Math.PI / 2f, 1f / AlienDrawableGameComponent.SuperSampleFactor("GFX/Sprites/arrow", arrow.LogicalWidth()), center: true, color);
			break;
		}
		case MessageType.unlocked:
		{
			Vector2 origin2 = font.MeasureString("Unlocked!") / 2f;
			Vector2 origin3 = font.MeasureString(text) / 2f;
			// Card 623f16d9: a long challenge/level/cheat name ("Evil Aliens Classic") could
			// overflow the 800-wide playfield at the fixed popup scale -- shrink each line to
			// fit independently (never scale up), reusing the already-measured widths so the
			// unscaled origin/centering above is unaffected.
			float headerScale = TextFit.FitScale(origin2.X * 2f, scale, UnlockedTextMaxWidth);
			float bodyScale = TextFit.FitScale(origin3.X * 2f, scale, UnlockedTextMaxWidth);
			// In-game unlock popup: plain text (chrome sheen removed, card 2b5867da - read wrong in gameplay).
			spriteBatch.DrawString(font, "Unlocked!", position, color, 0f, origin2, headerScale, (SpriteEffects)0, 0f);
			spriteBatch.DrawString(font, text, new Vector2(800f - position.X, position.Y + 125f), color, 0f, origin3, bodyScale, (SpriteEffects)0, 0f);
			break;
		}
		case MessageType.defeat:
		{
			Vector2 origin = font.MeasureString(text) / 2f;
			spriteBatch.Draw(blank, new Rectangle(0, 0, 800, 600), new Color(new Vector4(0f, 0f, 0f, fadefactor)));
			spriteBatch.DrawString(text, new Vector2(400f, 300f), color, 0f, origin, scale, (SpriteEffects)0, 1f);
			break;
		}
		case MessageType.devcomment:
		{
			Vector2 val = font.MeasureString(text);
			spriteBatch.DrawString(text, new Vector2(400f, 550f - val.Y), color, 0f, new Vector2(val.X / 2f, 0f), scale, (SpriteEffects)0, 0f);
			break;
		}
		}
	}

	private void SwitchStateIfFinished(MessageState state, float duration)
	{
		if (timer.Finished)
		{
			this.state = state;
			timer.Duration = duration;
			timer.Reset();
			timer.Start();
		}
	}

	public override void Update(GameTime gameTime)
	{
		timer.Update(gameTime);
		switch (type)
		{
		case MessageType.cheatwarning:
			UpdateCheatWarning(gameTime);
			break;
		case MessageType.starwarsblue:
			UpdateStarWarsBlue(gameTime);
			break;
		case MessageType.redwarning:
			UpdateRedWarning(gameTime);
			break;
		case MessageType.unlocked:
			UpdateUnlocked(gameTime);
			break;
		case MessageType.defeat:
			UpdateDefeat(gameTime);
			break;
		case MessageType.devcomment:
			UpdateDevComment(gameTime);
			break;
		}
		base.Update(gameTime);
	}

	private void UpdateDevComment(GameTime gameTime)
	{
		switch (state)
		{
		case MessageState.enter:
			state = MessageState.show;
			break;
		case MessageState.show:
			if (sound.TTSIsSilent())
			{
				state = MessageState.leave;
				timer.Duration = 1200f;
				timer.Reset();
				timer.Start();
			}
			break;
		case MessageState.leave:
			SwitchStateIfFinished(MessageState.end, 800f);
			break;
		case MessageState.end:
			scale = 0f;
			if (timer.Finished)
			{
				if (this.OnFinished != null)
				{
					this.OnFinished(this);
				}
				collection.Remove((GameComponent)(object)this);
			}
			break;
		}
	}

	private void UpdateCheatWarning(GameTime gameTime)
	{
		if (!soundplayed)
		{
			soundplayed = true;
		}
		if (timer.Finished)
		{
			if (this.OnFinished != null)
			{
				this.OnFinished(this);
			}
			collection.Remove((GameComponent)(object)this);
		}
	}

	private void UpdateDefeat(GameTime gameTime)
	{
		switch (state)
		{
		case MessageState.enter:
		{
			if (!soundplayed)
			{
				// Announce the defeat (Stage 6): the original passed Texts.Nothing
				// here; the announcer line is a web re-cast (ElevenLabs "Brian").
				sound.PlayText(speechText, 2);
				soundplayed = true;
			}
			fadefactor = MathHelper.Lerp(0f, 0.5f, 1f - timer.Normalized);
			byte b = (byte)(255f * MathHelper.Lerp(1f, 0f, timer.Normalized));
			color = new Color((color).R, (color).G, (color).B, b);
			SwitchStateIfFinished(MessageState.show, 6000f);
			break;
		}
		case MessageState.show:
		{
			fadefactor = 0.5f;
			byte b = byte.MaxValue;
			color = new Color((color).R, (color).G, (color).B, b);
			SwitchStateIfFinished(MessageState.leave, 3000f);
			break;
		}
		case MessageState.leave:
		{
			fadefactor = MathHelper.Lerp(0.5f, 1f, 1f - timer.Normalized);
			byte b = (byte)(255f * MathHelper.Lerp(0f, 1f, timer.Normalized));
			color = new Color((color).R, (color).G, (color).B, b);
			SwitchStateIfFinished(MessageState.end, 0f);
			break;
		}
		case MessageState.end:
			if (this.OnFinished != null)
			{
				this.OnFinished(this);
			}
			collection.Remove((GameComponent)(object)this);
			break;
		}
	}

	private void UpdateUnlocked(GameTime gameTime)
	{
		switch (state)
		{
		case MessageState.enter:
			position.X = MathHelper.Lerp(0f, 400f, 1f - timer.Normalized);
			color = new Color(new Vector4(0.8f, 0.4f, 0.2f, 1f - timer.Normalized));
			SwitchStateIfFinished(MessageState.show, 2200f);
			break;
		case MessageState.show:
		{
			position.X = 400f;
			if (!soundplayed)
			{
				switch (unlocktype)
				{
				case UnlockType.challenge:
					sound.PlayText(SoundManager.Texts.ChallengeUnlocked, 2);
					break;
				case UnlockType.cheat:
					sound.PlayText(SoundManager.Texts.CheatUnlocked, 2);
					break;
				case UnlockType.level:
					sound.PlayText(SoundManager.Texts.LevelUnlocked, 2);
					break;
				case UnlockType.difficulty:
					sound.PlayText(SoundManager.Texts.DifficultyUnlocked, 2);
					break;
				}
				soundplayed = true;
			}
			targetcolor = new Vector4(0.8f, 0.4f, 0.2f, 1f);
			float num = (1f - timer.Normalized) * 2f;
			num = num * 4f % 2f;
			if (num > 1f)
			{
				num -= 1f;
				color = new Color(Vector4.Lerp(targetcolor, Vector4.One, 1f - num));
			}
			else
			{
				color = new Color(Vector4.Lerp(targetcolor, Vector4.One, num));
			}
			SwitchStateIfFinished(MessageState.leave, 200f);
			break;
		}
		case MessageState.leave:
			position.X = MathHelper.Lerp(400f, 800f, 1f - timer.Normalized);
			color = new Color(new Vector4(0.8f, 0.4f, 0.2f, timer.Normalized));
			SwitchStateIfFinished(MessageState.end, 1f);
			break;
		case MessageState.end:
			collection.Remove((GameComponent)(object)this);
			break;
		}
	}

	public void SetWarningDirection(float angle)
	{
		if (type == MessageType.redwarning)
		{
			warningDirection = MyMath.Mod(angle, (float)Math.PI * 2f);
			if (warningDirection <= (float)Math.PI)
			{
				position = new Vector2(400f, 300f) + MyMath.AngleToVector(warningDirection) * 250f - new Vector2(0f, 50f);
			}
			else
			{
				position = new Vector2(400f, 300f) + MyMath.AngleToVector(warningDirection) * 250f + new Vector2(0f, 50f);
			}
		}
	}

	private void UpdateRedWarning(GameTime gameTime)
	{
		switch (state)
		{
		case MessageState.enter:
			color = new Color(Vector4.Lerp(Vector4.Zero, new Vector4(1f, 0f, 0f, 0.6f), 1f - timer.Normalized));
			SwitchStateIfFinished(MessageState.show, 3000f);
			break;
		case MessageState.show:
			color = new Color(new Vector4(1f, 0f, 0f, 0.6f));
			if (timer.TimeElapsed % 800f >= 600f)
			{
				scale = 0f;
				soundplayed = false;
				if (isShort)
				{
					state = MessageState.end;
				}
			}
			else
			{
				if (!soundplayed)
				{
					sound.PlayText(speechText, 2);
					soundplayed = true;
				}
				scale = 2.3f;
			}
			SwitchStateIfFinished(MessageState.end, 0f);
			break;
		case MessageState.end:
			collection.Remove((GameComponent)(object)this);
			break;
		case MessageState.leave:
			break;
		}
	}

	private void UpdateStarWarsBlue(GameTime gameTime)
	{
		switch (state)
		{
		case MessageState.enter:
		{
			float num3 = MathHelper.SmoothStep(0f, 1f, 1f - timer.Normalized);
			color = new Color(Vector4.Lerp(initialcolor, targetcolor, num3));
			scale = MathHelper.Lerp(1f, 2f, num3);
			position.Y = MathHelper.Lerp(150f, 200f, num3);
			SwitchStateIfFinished(MessageState.show, 800f);
			break;
		}
		case MessageState.show:
		{
			if (!soundplayed)
			{
				sound.PlayText(speechText, 2);
				soundplayed = true;
			}
			float num2 = (1f - timer.Normalized) * 2f;
			num2 = num2 * 3f % 2f;
			if (num2 > 1f)
			{
				num2 -= 1f;
				color = new Color(Vector4.Lerp(targetcolor, Vector4.One, 1f - num2));
			}
			else
			{
				color = new Color(Vector4.Lerp(targetcolor, Vector4.One, num2));
			}
			SwitchStateIfFinished(MessageState.leave, 400f);
			break;
		}
		case MessageState.leave:
		{
			float num = MathHelper.SmoothStep(1f, 0f, 1f - timer.Normalized);
			position.Y = MathHelper.Lerp(250f, 200f, num);
			color = new Color(Vector4.Lerp(initialcolor, targetcolor, num));
			scale = MathHelper.Lerp(6f, 2f, num);
			SwitchStateIfFinished(MessageState.end, 0f);
			break;
		}
		case MessageState.end:
			collection.Remove((GameComponent)(object)this);
			break;
		}
	}

	private void ChangeColor(GameTime gameTime)
	{
		if (input.Down(MyKeys.Up))
		{
			switch (selectedaspect)
			{
			case 0:
			{
				ref Vector4 reference3 = ref targetcolor;
				reference3.X += 0.5f * (float)gameTime.ElapsedGameTime.TotalSeconds;
				break;
			}
			case 1:
			{
				ref Vector4 reference2 = ref targetcolor;
				reference2.Y += 0.5f * (float)gameTime.ElapsedGameTime.TotalSeconds;
				break;
			}
			case 2:
			{
				ref Vector4 reference = ref targetcolor;
				reference.Z += 0.5f * (float)gameTime.ElapsedGameTime.TotalSeconds;
				break;
			}
			}
		}
		if (input.Down(MyKeys.Down))
		{
			switch (selectedaspect)
			{
			case 0:
			{
				ref Vector4 reference6 = ref targetcolor;
				reference6.X -= 0.5f * (float)gameTime.ElapsedGameTime.TotalSeconds;
				break;
			}
			case 1:
			{
				ref Vector4 reference5 = ref targetcolor;
				reference5.Y -= 0.5f * (float)gameTime.ElapsedGameTime.TotalSeconds;
				break;
			}
			case 2:
			{
				ref Vector4 reference4 = ref targetcolor;
				reference4.Z -= 0.5f * (float)gameTime.ElapsedGameTime.TotalSeconds;
				break;
			}
			}
		}
		targetcolor = Vector4.Clamp(targetcolor, Vector4.Zero, Vector4.One);
		text = ((object)targetcolor).ToString();
		initialcolor = targetcolor / 3f;
		if (input.Pressed(MyKeys.Right))
		{
			selectedaspect = MyMath.Mod(selectedaspect + 1, 3);
		}
		if (input.Pressed(MyKeys.Left))
		{
			selectedaspect = MyMath.Mod(selectedaspect - 1, 3);
		}
	}

	internal void MakeShort()
	{
		isShort = true;
	}

	public void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		if (e.GameComponent == this)
		{
			this.OnFinished = null;
		}
	}

	public void OnComponentAdded(GameComponentCollectionEventArgs e)
	{
	}
}
