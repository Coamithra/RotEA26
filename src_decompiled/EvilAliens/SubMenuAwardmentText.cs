using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

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
			awardmentExplanation = "Complete all seven challenges on HARD mode";
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
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		//IL_0064: Unknown result type (might be due to invalid IL or missing references)
		//IL_006e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c2: Unknown result type (might be due to invalid IL or missing references)
		Vector2 val = font.MeasureString(awardmentName) / 2f;
		base.SpriteBatch.DrawString(font, awardmentName, new Vector2(400f, 50f), Color.AliceBlue, 0f, val, 1f, (SpriteEffects)0, 0f);
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
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		//IL_0065: Unknown result type (might be due to invalid IL or missing references)
		//IL_006a: Unknown result type (might be due to invalid IL or missing references)
		//IL_006d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0077: Unknown result type (might be due to invalid IL or missing references)
		//IL_007c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0086: Unknown result type (might be due to invalid IL or missing references)
		base.SpriteBatch.Draw(skull, new Vector2(400f, 200f), 0f, 0.6f, center: true, Color.LightGray);
		Vector2 val = font.MeasureString("Status: LOCKED") / 2f;
		SpriteBatchWrapper spriteBatch = base.SpriteBatch;
		Vector2 position = new Vector2(400f, 200f);
		Color red = Color.Red;
		spriteBatch.DrawString("Status: LOCKED", position, new Color(new Vector4(((Color)(ref red)).ToVector3(), 0.8f)), -(float)Math.PI / 12f, val, 1.2f, (SpriteEffects)0, 1f);
	}

	private void drawWin(GameTime gameTime)
	{
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		//IL_006d: Unknown result type (might be due to invalid IL or missing references)
		//IL_007e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0088: Unknown result type (might be due to invalid IL or missing references)
		//IL_008d: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ca: Unknown result type (might be due to invalid IL or missing references)
		float num = (float)gameTime.TotalGameTime.TotalSeconds;
		float num2 = MyMath.Mod(num / 2f, 1f);
		num2 = brainPulsate.Evaluate(num2);
		Color color = Color.Lerp(Color.White, Color.LightGreen, 1f - num2);
		base.SpriteBatch.Draw(skull, new Vector2(400f, 200f), 0f, 0.6f, center: true, color);
		Vector2 val = font.MeasureString("Status: UNLOCKED") / 2f;
		SpriteBatchWrapper spriteBatch = base.SpriteBatch;
		Vector2 position = new Vector2(400f, 200f);
		Color limeGreen = Color.LimeGreen;
		spriteBatch.DrawString("Status: UNLOCKED", position, new Color(new Vector4(((Color)(ref limeGreen)).ToVector3(), 0.9f)), -(float)Math.PI / 12f, val, 1.2f, (SpriteEffects)0, 1f);
	}
}
