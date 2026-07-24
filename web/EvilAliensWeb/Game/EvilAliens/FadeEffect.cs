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
			return value;
		}
		set
		{
			this.value = value;
		}
	}

	public override bool hasStateChanged()
	{
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
		base.SaveState();
		oldvalue = value;
	}
}
