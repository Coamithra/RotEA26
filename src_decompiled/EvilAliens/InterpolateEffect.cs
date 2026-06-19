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
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			return offset;
		}
		set
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0002: Unknown result type (might be due to invalid IL or missing references)
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
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
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
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		base.SaveState();
		oldoffset = offset;
		olddelta = delta;
	}
}
