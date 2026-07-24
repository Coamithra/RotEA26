using Microsoft.Xna.Framework;

namespace EvilAliens;

public class InterpolateEffect : MySpriteEffect
{
	private Vector2 offset;

	private Vector2 oldoffset;

	private float delta;

	private float olddelta;

	public Vector2 Offset
	{
		get
		{
			return offset;
		}
		set
		{
			offset = value;
		}
	}

	public float Delta
	{
		get
		{
			return delta;
		}
		set
		{
			delta = value;
		}
	}

	public override bool hasStateChanged()
	{
		if (!base.hasStateChanged())
		{
			if (base.Enabled)
			{
				if (!(offset != oldoffset))
				{
					return delta != olddelta;
				}
				return true;
			}
			return false;
		}
		return true;
	}

	public override void SaveState()
	{
		base.SaveState();
		oldoffset = offset;
		olddelta = delta;
	}
}
