using Microsoft.Xna.Framework;

namespace EvilAliens;

public class FadeEffect : MySpriteEffect
{
	private Vector4 value;

	private Vector4 oldvalue;

	public Vector4 Value
	{
		get
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			return value;
		}
		set
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0002: Unknown result type (might be due to invalid IL or missing references)
			this.value = value;
		}
	}

	public override bool hasStateChanged()
	{
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		if (!base.hasStateChanged())
		{
			if (base.Enabled)
			{
				return value != oldvalue;
			}
			return false;
		}
		return true;
	}

	public override void SaveState()
	{
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		base.SaveState();
		oldvalue = value;
	}
}
