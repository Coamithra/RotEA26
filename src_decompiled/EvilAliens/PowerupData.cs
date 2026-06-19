using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

public class PowerupData : DrawableGameComponent
{
	private enum State
	{
		fadein,
		fadeout,
		display,
		hidden
	}

	public delegate void LevelUpEvent(Powerup.PowerupType type, int newLevel, PowerupData sender);

	private Timer tutorialDisplayTimer = new Timer(5000f, repeating: false);

	private ScoreVisualiser.ScorePart tutorialDisplayItem;

	private State state;

	private Powerup.PowerupType type;

	private int level;

	private float progress;

	private float displayedprogress;

	private Vector2 position;

	private SpriteBatchWrapper batch;

	private SpriteFont font;

	private Texture2D barUnlit;

	private Texture2D barLit;

	private Texture2D barEdge;

	private Vector3 oneUpColorSliders;

	private Vector3 oneUpColorSlidersDirection;

	private Timer animationTimer = new Timer(500f, repeating: false);

	private string levelDisplayString;

	private float fade;

	public event LevelUpEvent onLevelUp;

	public PowerupData(Game game, Vector2 position, Powerup.PowerupType type)
		: base(game)
	{
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_0058: Unknown result type (might be due to invalid IL or missing references)
		//IL_0089: Unknown result type (might be due to invalid IL or missing references)
		//IL_008a: Unknown result type (might be due to invalid IL or missing references)
		oneUpColorSliders = new Vector3(1f, 0f, 0f);
		oneUpColorSlidersDirection = new Vector3(0f, 1f, 0f);
		state = State.hidden;
		level = 0;
		if (type == Powerup.PowerupType.OneUp)
		{
			level = 3;
		}
		((DrawableGameComponent)this).DrawOrder = 1000;
		((DrawableGameComponent)this).Visible = false;
		this.position = position;
		this.type = type;
		animationTimer.Reset();
		animationTimer.Stop();
	}

	protected override void LoadContent()
	{
		((DrawableGameComponent)this).LoadContent();
		ContentManager contentManager = ServiceHelper.Get<IContentManagerService>().ContentManager;
		barLit = contentManager.Load<Texture2D>("GFX/HUD/BarLit");
		barUnlit = contentManager.Load<Texture2D>("GFX/HUD/BarUnlit2");
		barEdge = contentManager.Load<Texture2D>("GFX/HUD/BarLitEdge");
		batch = ServiceHelper.Get<ISpriteBatchWrapperService>().SpriteBatchWrapper;
		font = ServiceHelper.Get<IContentManagerService>().ContentManager.Load<SpriteFont>("GFX/Menu/menufont");
	}

	public override void Update(GameTime gameTime)
	{
		tutorialDisplayTimer.Update(gameTime);
		((GameComponent)this).Update(gameTime);
		switch (state)
		{
		case State.fadein:
			fade += (float)gameTime.ElapsedGameTime.TotalSeconds * 2f;
			if (fade > 1f)
			{
				fade = 1f;
				state = State.display;
			}
			break;
		case State.fadeout:
			fade -= (float)gameTime.ElapsedGameTime.TotalSeconds * 2f;
			if (fade < 0f)
			{
				fade = 0f;
				state = State.hidden;
				((DrawableGameComponent)this).Visible = false;
			}
			break;
		}
		animationTimer.Update(gameTime);
		float difficultyModifier = Settings.GetInstance().DifficultyModifier;
		progress = MathHelper.Max(0f, progress - difficultyModifier * 0.05f * (float)gameTime.ElapsedGameTime.TotalSeconds);
		float num = Math.Abs(progress - displayedprogress);
		num *= 5f;
		if (num < 0.2f)
		{
			num = 0.2f;
		}
		if (progress > displayedprogress)
		{
			displayedprogress = MathHelper.Min(displayedprogress + (float)gameTime.ElapsedGameTime.TotalSeconds * num, progress);
		}
		else if (progress < displayedprogress)
		{
			displayedprogress = MathHelper.Max(displayedprogress - (float)gameTime.ElapsedGameTime.TotalSeconds * num, progress);
		}
	}

	public void AddExp(int combo)
	{
		if (level == 4)
		{
			return;
		}
		float num = 0.06f;
		float num2 = (float)Math.Pow(0.6299999952316284, level);
		float num3 = 1f + 0.019f * (float)combo;
		if (num3 > 6.348013f)
		{
			num3 = 6.348013f;
		}
		float num4 = 1f / Settings.GetInstance().DifficultyModifier;
		progress += num * num2 * num3 * num4;
		if (progress >= 1f)
		{
			progress = 0f;
			displayedprogress = 0f;
			if (type != Powerup.PowerupType.OneUp)
			{
				level++;
			}
			setDisplayString();
			animationTimer.Reset();
			animationTimer.Start();
			if (this.onLevelUp != null)
			{
				this.onLevelUp(type, level, this);
			}
		}
	}

	public override void Draw(GameTime gameTime)
	{
		((DrawableGameComponent)this).Draw(gameTime);
		drawPowerbar(gameTime);
		drawEnhancement();
	}

	private void drawPowerbar(GameTime gameTime)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d6: Unknown result type (might be due to invalid IL or missing references)
		//IL_02bf: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c0: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c1: Unknown result type (might be due to invalid IL or missing references)
		//IL_02cb: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d1: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_02df: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_0101: Unknown result type (might be due to invalid IL or missing references)
		//IL_010b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0110: Unknown result type (might be due to invalid IL or missing references)
		//IL_0115: Unknown result type (might be due to invalid IL or missing references)
		//IL_008c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0097: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_0387: Unknown result type (might be due to invalid IL or missing references)
		//IL_038d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0392: Unknown result type (might be due to invalid IL or missing references)
		//IL_0393: Unknown result type (might be due to invalid IL or missing references)
		//IL_03a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_03ab: Unknown result type (might be due to invalid IL or missing references)
		//IL_03c2: Unknown result type (might be due to invalid IL or missing references)
		//IL_03c7: Unknown result type (might be due to invalid IL or missing references)
		//IL_03c8: Unknown result type (might be due to invalid IL or missing references)
		//IL_03d4: Unknown result type (might be due to invalid IL or missing references)
		//IL_03d9: Unknown result type (might be due to invalid IL or missing references)
		//IL_03e3: Unknown result type (might be due to invalid IL or missing references)
		//IL_03e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_03f1: Unknown result type (might be due to invalid IL or missing references)
		//IL_0423: Unknown result type (might be due to invalid IL or missing references)
		//IL_0429: Unknown result type (might be due to invalid IL or missing references)
		//IL_042e: Unknown result type (might be due to invalid IL or missing references)
		//IL_042f: Unknown result type (might be due to invalid IL or missing references)
		//IL_043f: Unknown result type (might be due to invalid IL or missing references)
		//IL_044a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0461: Unknown result type (might be due to invalid IL or missing references)
		//IL_0466: Unknown result type (might be due to invalid IL or missing references)
		//IL_0467: Unknown result type (might be due to invalid IL or missing references)
		//IL_0473: Unknown result type (might be due to invalid IL or missing references)
		//IL_0478: Unknown result type (might be due to invalid IL or missing references)
		//IL_0482: Unknown result type (might be due to invalid IL or missing references)
		//IL_0488: Unknown result type (might be due to invalid IL or missing references)
		//IL_0493: Unknown result type (might be due to invalid IL or missing references)
		//IL_04f1: Unknown result type (might be due to invalid IL or missing references)
		//IL_04f7: Unknown result type (might be due to invalid IL or missing references)
		//IL_04fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_04fd: Unknown result type (might be due to invalid IL or missing references)
		//IL_050d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0515: Unknown result type (might be due to invalid IL or missing references)
		//IL_052c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0531: Unknown result type (might be due to invalid IL or missing references)
		//IL_0532: Unknown result type (might be due to invalid IL or missing references)
		//IL_053e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0543: Unknown result type (might be due to invalid IL or missing references)
		//IL_054d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0553: Unknown result type (might be due to invalid IL or missing references)
		//IL_055b: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a9: Unknown result type (might be due to invalid IL or missing references)
		float num = 1f;
		Vector2 val = position;
		if (tutorialDisplayTimer.Active && tutorialDisplayItem == ScoreVisualiser.ScorePart.Powerbar)
		{
			float num2 = ((tutorialDisplayTimer.TimeElapsed <= 1500f) ? (tutorialDisplayTimer.TimeElapsed / 1500f) : ((!(tutorialDisplayTimer.TimeLeft <= 1500f)) ? 1f : (tutorialDisplayTimer.TimeLeft / 1500f)));
			num = MathHelper.SmoothStep(1f, 3f, num2);
			val += new Vector2(15f, 15f) * (num - 1f);
		}
		Vector2 val2 = default(Vector2);
		((Vector2)(ref val2))._002Ector(-16f, 13f);
		batch.BlendMode = (SpriteBlendMode)1;
		Color val3 = Powerup.PowerUpColor(type);
		if (type == Powerup.PowerupType.OneUp)
		{
			oneUpColorSliders += oneUpColorSlidersDirection * (float)gameTime.ElapsedGameTime.TotalSeconds * 3.4f;
			if (oneUpColorSliders.Y > 1f)
			{
				oneUpColorSliders.Y = 1f;
				oneUpColorSlidersDirection.Y = 0f;
				oneUpColorSlidersDirection.X = -1f;
			}
			if (oneUpColorSliders.X < 0f)
			{
				oneUpColorSliders.X = 0f;
				oneUpColorSlidersDirection.X = 0f;
				oneUpColorSlidersDirection.Z = 1f;
			}
			if (oneUpColorSliders.Z > 1f)
			{
				oneUpColorSliders.Z = 1f;
				oneUpColorSlidersDirection.Z = 0f;
				oneUpColorSlidersDirection.Y = -1f;
			}
			if (oneUpColorSliders.Y < 0f)
			{
				oneUpColorSliders.Y = 0f;
				oneUpColorSlidersDirection.Y = 0f;
				oneUpColorSlidersDirection.X = 1f;
			}
			if (oneUpColorSliders.X > 1f)
			{
				oneUpColorSliders.X = 1f;
				oneUpColorSlidersDirection.X = 0f;
				oneUpColorSlidersDirection.Z = -1f;
			}
			if (oneUpColorSliders.Z < 0f)
			{
				oneUpColorSliders.Z = 0f;
				oneUpColorSlidersDirection.Z = 0f;
				oneUpColorSlidersDirection.Y = 1f;
			}
			((Color)(ref val3))._002Ector(oneUpColorSliders);
		}
		batch.Draw(barUnlit, val + val2, 0f, Vector2.One * num, center: false, new Color(val3, fade));
		if (animationTimer.Active || level == 4)
		{
			float num3 = 1f;
			float num4 = animationTimer.Duration / 3f;
			if (animationTimer.TimeElapsed < num4)
			{
				num3 = animationTimer.TimeElapsed / num4;
			}
			if (animationTimer.TimeLeft < num4)
			{
				num3 = animationTimer.TimeLeft / num4;
			}
			float num5 = (float)Math.Round(96.0);
			batch.Draw(barLit, new Rectangle(0, 0, (int)num5, barLit.Height), position + val2, 0f, 1f, center: false, new Color(val3, fade));
			batch.Draw(barEdge, position + val2 + new Vector2(num5, 0f), 0f, Vector2.One, center: false, new Color(val3, fade));
			batch.BlendMode = (SpriteBlendMode)2;
			batch.Draw(barLit, new Rectangle(0, 0, (int)num5, barLit.Height), position + val2, 0f, 1f, center: false, new Color(val3, num3 * fade));
			batch.Draw(barEdge, position + val2 + new Vector2(num5, 0f), 0f, Vector2.One, center: false, new Color(val3, num3 * fade));
			batch.BlendMode = (SpriteBlendMode)1;
		}
		else if (displayedprogress > 0f)
		{
			float num6 = (float)Math.Round(21f + 75f * displayedprogress);
			batch.Draw(barLit, new Rectangle(0, 0, (int)num6, barLit.Height), position + val2, 0f, 1f, center: false, new Color(val3, fade));
			batch.Draw(barEdge, position + val2 + new Vector2(num6, 0f), 0f, Vector2.One, center: false, new Color(val3, fade));
		}
	}

	private void drawEnhancement()
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ce: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e4: Unknown result type (might be due to invalid IL or missing references)
		//IL_0122: Unknown result type (might be due to invalid IL or missing references)
		//IL_0127: Unknown result type (might be due to invalid IL or missing references)
		//IL_0128: Unknown result type (might be due to invalid IL or missing references)
		//IL_0133: Unknown result type (might be due to invalid IL or missing references)
		//IL_013e: Unknown result type (might be due to invalid IL or missing references)
		//IL_008b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0096: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ac: Unknown result type (might be due to invalid IL or missing references)
		float num = 1f;
		Vector2 val = position;
		if (tutorialDisplayTimer.Active && tutorialDisplayItem == ScoreVisualiser.ScorePart.Enhancement)
		{
			float num2 = ((tutorialDisplayTimer.TimeElapsed <= 1500f) ? (tutorialDisplayTimer.TimeElapsed / 1500f) : ((!(tutorialDisplayTimer.TimeLeft <= 1500f)) ? 1f : (tutorialDisplayTimer.TimeLeft / 1500f)));
			num = MathHelper.SmoothStep(1f, 3f, num2);
			val += new Vector2(15f, 15f) * (num - 1f);
		}
		batch.DrawString(Powerup.PowerUpString(type), new Vector2(0f, 44f) + val, new Color(Powerup.PowerUpColor(type), fade), 0f, centered: false, 0.75f * num, (SpriteEffects)0, 0f);
		if (type != Powerup.PowerupType.OneUp)
		{
			batch.DrawString(levelDisplayString, new Vector2(17f * num, 44f) + val, new Color(Powerup.PowerUpColor(type), fade), 0f, centered: false, 0.55f * num, (SpriteEffects)0, 0f);
		}
	}

	public void FadeIn()
	{
		state = State.fadein;
		((DrawableGameComponent)this).Visible = true;
	}

	public void FadeOut()
	{
		state = State.fadeout;
	}

	internal void Reset()
	{
		state = State.hidden;
		level = 0;
		if (type == Powerup.PowerupType.OneUp)
		{
			level = 3;
		}
		progress = 0f;
		displayedprogress = 0f;
		setDisplayString();
		animationTimer.Reset();
		animationTimer.Stop();
		tutorialDisplayTimer.Stop();
		tutorialDisplayTimer.Reset();
	}

	private void setDisplayString()
	{
		levelDisplayString = (level + 1).ToString();
	}

	internal int GetLevel()
	{
		return level;
	}

	internal void Tutorial_Show(ScoreVisualiser.ScorePart whatToShow)
	{
		tutorialDisplayItem = whatToShow;
		tutorialDisplayTimer.Start();
		tutorialDisplayTimer.Reset();
	}

	internal void MaxExp()
	{
		if (type != Powerup.PowerupType.OneUp)
		{
			level = 4;
		}
		setDisplayString();
	}

	internal float GetProgress()
	{
		return progress;
	}
}
