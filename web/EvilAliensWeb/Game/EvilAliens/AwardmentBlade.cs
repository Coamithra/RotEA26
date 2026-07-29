using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using EvilAliensWeb.Compat;

namespace EvilAliens;

public class AwardmentBlade : DrawableGameComponent, IAwardmentBladeService
{
	private enum State
	{
		Enter,
		Show,
		Exit,
		Idle
	}

	private State state;

	private SpriteBatchWrapper batch;

	private Timer bladeTimer = new Timer(1f, repeating: false);

	private Texture2D blade;

	private ContentManager content;

	// Card 1ec619b3: needed to MeasureString the two lines drawn in State.Show so long
	// awardment names ("I Don't Get The Spider Boss") can be shrunk to fit the blade's box.
	private SpriteFont font;

	private string[] awardmentStrings;

	private Awardment currentlyDisplaying;

	private Queue<Awardment> awardmentsQueue;

	// Fraction of the blade art's own design width the text is allowed to fill before
	// shrinking -- leaves a margin inside the frame graphic on both sides.
	private const float BladeTextWidthFraction = 0.82f;

	public AwardmentBlade(Game game)
		: base(game)
	{
		base.DrawOrder = 2500;
		awardmentStrings = new string[Game1.GetEnumValues<Awardment>().Count];
		awardmentStrings[4] = "Challenger Award";
		awardmentStrings[5] = "Fight Like A Team";
		awardmentStrings[6] = "I Don't Get The Spider Boss";
		awardmentStrings[0] = "Act The First";
		awardmentStrings[9] = "Real Ultimate Power";
		awardmentStrings[8] = "The Insane Award";
		awardmentStrings[7] = "Pacifist";
		awardmentStrings[1] = "Act The Second";
		awardmentStrings[2] = "Act The Third";
		awardmentStrings[3] = "True Ending";
		awardmentsQueue = new Queue<Awardment>();
	}

	public string AwardmentName(Awardment awardment)
	{
		return awardmentStrings[(int)awardment];
	}

	public override void Initialize()
	{
		base.Initialize();
		bladeTimer.Stop();
		state = State.Idle;
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		batch = ServiceHelper.Get<ISpriteBatchWrapperService>().SpriteBatchWrapper;
		content = ServiceHelper.Get<IContentManagerService>().ContentManager;
	}

	// The blade art + font are LAZY (card 57555583). This component is added in
	// Game1.Initialize, so its LoadContent ran during base.Initialize() -- before
	// Game1.LoadContent builds the warm queues, which is why no warm entry could ever
	// precede it and the decode always landed on the pre-splash black screen for a
	// component that only draws when an awardment pops. In practice this is a cache hit:
	// Game1.QueueIdleWarm warms the sheet during the splash (and the menu's own
	// QueueMenuWarm already warms menufont), so the decode happens off the critical path
	// rather than when the banner animates in.
	//
	// Guarded, unlike the eager load it replaces: that one ran at boot, where a missing or
	// wrong-cased asset is a black screen someone notices immediately (and check_deploy.py
	// probes for). Reached from Draw instead, an unguarded throw would first surface when a
	// player unlocks an awardment, mid-level. So it degrades to "no banner" the way
	// SplashScene's channelflip and Game1's holosim degrade, and Draw skips a null blade.
	private void EnsureContent()
	{
		if (blade != null && font != null)
		{
			return;
		}
		try
		{
			blade = content.Load<Texture2D>("GFX/Sprites/awardmentblade");
			font = content.Load<SpriteFont>("GFX/Menu/menufont");
		}
		catch (System.Exception ex)
		{
			System.Console.WriteLine("[awardmentblade] content load failed: " + ex.Message);
		}
	}

	public override void Update(GameTime gameTime)
	{
		if (awardmentsQueue.Count > 0 && state == State.Idle)
		{
			currentlyDisplaying = awardmentsQueue.Dequeue();
			if (Achievements.GetInstance().GetAwardmentIsUnlocked((int)currentlyDisplaying))
			{
				return;
			}
			EnsureContent();
			bladeTimer.Duration = 170f;
			bladeTimer.Reset();
			bladeTimer.Start();
			state = State.Enter;
		}
		base.Update(gameTime);
		bladeTimer.Update(gameTime);
		if (!bladeTimer.Finished)
		{
			return;
		}
		switch (state)
		{
		case State.Enter:
			bladeTimer.Duration = 6500f;
			bladeTimer.Reset();
			bladeTimer.Start();
			state = State.Show;
			Achievements.GetInstance().SetAwardmentIsUnlocked((int)currentlyDisplaying, value: true);
			Achievements.GetInstance().SaveThreaded();
			if (!Unlockables.GetInstance().IsUnlocked(Unlockables.Items.Awardments))
			{
				Unlockables.GetInstance().Unlock(Unlockables.Items.Awardments);
				Unlockables.GetInstance().SaveThreaded();
			}
			break;
		case State.Show:
			bladeTimer.Duration = 170f;
			bladeTimer.Reset();
			bladeTimer.Start();
			state = State.Exit;
			break;
		case State.Exit:
			state = State.Idle;
			break;
		case State.Idle:
			break;
		}
	}

	public override void Draw(GameTime gameTime)
	{
		base.Draw(gameTime);
		batch.BlendMode = (SpriteBlendMode)1;
		if (state != State.Idle)
		{
			// Belt-and-braces: Update's Idle -> Enter transition is the only way to reach a
			// drawing state and it loads first -- but Draw is where `blade`/`font` are
			// actually dereferenced, so it does not get to assume that.
			EnsureContent();
			if (blade == null || font == null)
			{
				// The load above failed (and said so). Run the state machine out silently
				// rather than throwing every frame the banner would have been up.
				return;
			}
		}
		switch (state)
		{
		case State.Enter:
		{
			float num5 = MathHelper.SmoothStep(0f, 1f, 1f - bladeTimer.Normalized);
			float num6 = MathHelper.SmoothStep(0.5f, 1f, 1f - bladeTimer.Normalized);
			batch.Draw(blade, new Vector2(400f, 450f), 0f, new Vector2(num6, num5) / AlienDrawableGameComponent.SuperSampleFactor("GFX/Sprites/awardmentblade", blade.LogicalWidth()), center: true, new Color(new Vector4(1f, 1f, 1f, 0.65f)));
			break;
		}
		case State.Show:
		{
			float num3 = 1f;
			float num4 = 1f;
			batch.Draw(blade, new Vector2(400f, 450f), 0f, new Vector2(num4, num3) / AlienDrawableGameComponent.SuperSampleFactor("GFX/Sprites/awardmentblade", blade.LogicalWidth()), center: true, new Color(new Vector4(1f, 1f, 1f, 0.65f)));
			// Card 1ec619b3: a long awardment name ("I Don't Get The Spider Boss") can overflow
			// the blade's frame art at the fixed scale -- shrink to fit the frame's own design
			// width (never scale up). The box width is derived the same way the frame draw above
			// removes its supersample factor, so this tracks the art if it's ever re-authored.
			string title = "Awardment Unlocked!";
			string awardmentName = awardmentStrings[(int)currentlyDisplaying];
			float boxWidth = (float)blade.LogicalWidth() / AlienDrawableGameComponent.SuperSampleFactor("GFX/Sprites/awardmentblade", blade.LogicalWidth());
			float maxTextWidth = boxWidth * BladeTextWidthFraction;
			float titleScale = TextFit.FitScale(font.MeasureString(title).X, num4 * 0.8f, maxTextWidth);
			float nameScale = TextFit.FitScale(font.MeasureString(awardmentName).X, num4, maxTextWidth);
			batch.DrawString(title, new Vector2(400f, 433f), Color.AliceBlue, 0f, centered: true, new Vector2(titleScale, titleScale), (SpriteEffects)0, 1f);
			batch.DrawString(awardmentName, new Vector2(400f, 467f), Color.AliceBlue, 0f, centered: true, new Vector2(nameScale, nameScale), (SpriteEffects)0, 1f);
			break;
		}
		case State.Exit:
		{
			float num = MathHelper.SmoothStep(1f, 0f, 1f - bladeTimer.Normalized);
			float num2 = MathHelper.SmoothStep(1f, 0.5f, 1f - bladeTimer.Normalized);
			batch.Draw(blade, new Vector2(400f, 450f), 0f, new Vector2(num2, num) / AlienDrawableGameComponent.SuperSampleFactor("GFX/Sprites/awardmentblade", blade.LogicalWidth()), center: true, new Color(new Vector4(1f, 1f, 1f, 0.65f)));
			break;
		}
		}
		_ = bladeTimer.Active;
	}

	public void AwardAchievement(Awardment awardment)
	{
		if (!Achievements.GetInstance().GetAwardmentIsUnlocked((int)awardment) && !Settings.GetInstance().CheckForCheats())
		{
			awardmentsQueue.Enqueue(awardment);
		}
	}

	public AwardmentBlade get()
	{
		return this;
	}
}
