using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using EvilAliensWeb.Compat;

namespace EvilAliens;

internal class SubMenuAwardmentText : MenuSub1
{
	private string awardmentName;

	private string awardmentExplanation;

	private bool awardmentStatus;

	private Texture2D skull;

	public SubMenuAwardmentText(Game game)
		: base(game)
	{
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		skull = Content.Load<Texture2D>("GFX/Menu/evilskull");
	}

	public void SetAwardment(Awardment awardment)
	{
		awardmentName = ServiceHelper.Get<IAwardmentBladeService>().get().AwardmentName(awardment);
		switch (awardment)
		{
		case Awardment.FirstAct:
			awardmentExplanation = "Complete the first mission on any difficulty";
			break;
		case Awardment.SecondAct:
			awardmentExplanation = "Complete the second mission on any difficulty";
			break;
		case Awardment.ThirdAct:
			awardmentExplanation = "Complete the third mission on any difficulty";
			break;
		case Awardment.TrueEnding:
			awardmentExplanation = "Defeat the Alien Overmind.\nRequires HARD mode.";
			break;
		case Awardment.Challenges:
			awardmentExplanation = "Complete all eight challenges on HARD mode";
			break;
		case Awardment.Coop:
			awardmentExplanation = "Connect four ships in cooperative play";
			break;
		case Awardment.Dunce:
			awardmentExplanation = "Battle the Spider Stag for three\nfull minutes without dying.\n(Any difficulty)";
			break;
		case Awardment.Pacifist:
			awardmentExplanation = "Survive for 90 seconds without firing.\n(Any mission/challenge, HARD mode)";
			break;
		case Awardment.Insane:
			awardmentExplanation = "Complete the missions and challenges on INZANE mode.\nGood luck.";
			break;
		case Awardment.FullPower:
			awardmentExplanation = "Power up all of your weapons to their highest level";
			break;
		}
		awardmentStatus = Achievements.GetInstance().GetAwardmentIsUnlocked((int)awardment);
	}

	public override void DrawMenu(GameTime gameTime, float yoffset)
	{
		Vector2 val = font.MeasureString(awardmentName) / 2f;
		base.SpriteBatch.DrawMetalString(font, awardmentName, new Vector2(400f, 50f), Color.AliceBlue, 0f, val, 1f);
		val = font.MeasureString(awardmentExplanation) / 2f;
		val.Y = 0f;
		if (awardmentStatus)
		{
			drawWin(gameTime);
		}
		else
		{
			drawLose();
		}
		base.SpriteBatch.DrawString(font, awardmentExplanation, new Vector2(400f, 350f), Color.AliceBlue, 0f, val, 0.7f, (SpriteEffects)0, 0f);
	}

	private void drawLose()
	{
		base.SpriteBatch.Draw(skull, new Vector2(400f, 200f), 0f, 0.6f / AlienDrawableGameComponent.SuperSampleFactor("GFX/Menu/evilskull", skull.LogicalWidth()), center: true, Color.LightGray);
		Vector2 val = font.MeasureString("Status: LOCKED") / 2f;
		SpriteBatchWrapper spriteBatch = base.SpriteBatch;
		Vector2 position = new Vector2(400f, 200f);
		Color red = Color.Red;
		spriteBatch.DrawString("Status: LOCKED", position, new Color(new Vector4((red).ToVector3(), 0.8f)), -(float)Math.PI / 12f, val, 1.2f, (SpriteEffects)0, 1f);
	}

	private void drawWin(GameTime gameTime)
	{
		float num = (float)gameTime.TotalGameTime.TotalSeconds;
		float num2 = MyMath.Mod(num / 2f, 1f);
		num2 = brainPulsate.Evaluate(num2);
		Color color = Color.Lerp(Color.White, Color.LightGreen, 1f - num2);
		base.SpriteBatch.Draw(skull, new Vector2(400f, 200f), 0f, 0.6f / AlienDrawableGameComponent.SuperSampleFactor("GFX/Menu/evilskull", skull.LogicalWidth()), center: true, color);
		Vector2 val = font.MeasureString("Status: UNLOCKED") / 2f;
		SpriteBatchWrapper spriteBatch = base.SpriteBatch;
		Vector2 position = new Vector2(400f, 200f);
		Color limeGreen = Color.LimeGreen;
		spriteBatch.DrawString("Status: UNLOCKED", position, new Color(new Vector4((limeGreen).ToVector3(), 0.9f)), -(float)Math.PI / 12f, val, 1.2f, (SpriteEffects)0, 1f);
	}
}
