namespace EvilAliens;

public class OutlineEffect : MySpriteEffect
{
	private float _LineThickness = 0.1f;

	private float _oldLineThickness = 0.1f;

	public float LineThickness
	{
		get
		{
			return _LineThickness;
		}
		set
		{
			_LineThickness = value;
		}
	}

	public override bool hasStateChanged()
	{
		if (!base.hasStateChanged())
		{
			if (base.Enabled)
			{
				return _LineThickness != _oldLineThickness;
			}
			return false;
		}
		return true;
	}

	public override void SaveState()
	{
		base.SaveState();
		_oldLineThickness = _LineThickness;
	}
}
