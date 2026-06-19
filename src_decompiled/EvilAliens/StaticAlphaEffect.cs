namespace EvilAliens;

public class StaticAlphaEffect : MySpriteEffect
{
	private float alpha;

	private float oldalpha;

	public float Alpha
	{
		get
		{
			return alpha;
		}
		set
		{
			alpha = value;
		}
	}

	public override bool hasStateChanged()
	{
		if (!base.hasStateChanged())
		{
			if (base.Enabled)
			{
				return alpha != oldalpha;
			}
			return false;
		}
		return true;
	}

	public override void SaveState()
	{
		base.SaveState();
		oldalpha = alpha;
	}
}
