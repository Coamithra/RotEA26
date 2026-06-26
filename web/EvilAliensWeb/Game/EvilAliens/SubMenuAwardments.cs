using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class SubMenuAwardments : MenuSub1
{
	private Vector2 origin = new Vector2(400f, 300f);

	public SubMenuAwardments(Game game)
		: base(game)
	{
	}//IL_000b: Unknown result type (might be due to invalid IL or missing references)
	//IL_0010: Unknown result type (might be due to invalid IL or missing references)


	public override void DrawMenu(GameTime gameTime, float yoffset)
	{
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ed: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_0141: Unknown result type (might be due to invalid IL or missing references)
		//IL_018b: Unknown result type (might be due to invalid IL or missing references)
		//IL_018c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0192: Unknown result type (might be due to invalid IL or missing references)
		yoffset -= 35f;
		Vector2 position = default(Vector2);
		(position) = new Vector2(origin.X, yoffset + origin.Y - (float)(font.LineSpacing * menuEntries.Count) / 3f);
		Vector2 val = default(Vector2);
		for (int i = 0; i < menuEntries.Count; i++)
		{
			Color color;
			float num4;
			if (i == selectedEntry)
			{
				float num = 15f / font.MeasureString(menuEntries[i]).X;
				float num2 = (float)gameTime.TotalGameTime.TotalSeconds;
				float num3 = MyMath.Mod(num2 / 2f, 1f);
				color = ((!Achievements.GetInstance().GetAwardmentIsUnlocked(i)) ? Color.AliceBlue : Color.PaleGreen);
				num4 = 1f + num * brainPulsate.Evaluate(num3);
			}
			else
			{
				color = ((!Achievements.GetInstance().GetAwardmentIsUnlocked(i)) ? Color.Gray : Color.LimeGreen);
				num4 = 1f;
			}
			if (!unLockableDataEntries[i].isUnlockable || Unlockables.GetInstance().IsUnlocked(unLockableDataEntries[i].item))
			{
				float x = font.MeasureString(menuEntries[i]).X;
				RecordEntryHit(i, position, x, font.LineSpacing); // mouse hit box (centred on origin.X)
				(val) = new Vector2(x / 2f, (float)(font.LineSpacing / 2)); // centre on origin.X
				base.SpriteBatch.DrawMetalString(font, menuEntries[i], position, color, 0f, val, num4);
				position.Y += (float)font.LineSpacing;
			}
		}
	}

	// DrawMenu nudges the list up by 35 (yoffset -= 35), so report that for the HUD ring.
	public override Vector2 GetListCentre()
	{
		Vector2 c = base.GetListCentre();
		return new Vector2(c.X, c.Y - 35f);
	}
}
