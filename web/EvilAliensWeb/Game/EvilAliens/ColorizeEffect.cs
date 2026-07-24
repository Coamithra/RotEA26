using Microsoft.Xna.Framework;

namespace EvilAliens;

public class ColorizeEffect : MySpriteEffect
{
	private Vector3 _param;

	private Vector3 _oldparam;

	public Vector3 RangeTarget
	{
		get
		{
			return _param;
		}
		set
		{
			_param = value;
		}
	}

	public float Minimum
	{
		get
		{
			return _param.X;
		}
		set
		{
			_param.X = value;
		}
	}

	public float Maximum
	{
		get
		{
			return _param.Y;
		}
		set
		{
			_param.Y = value;
		}
	}

	public float Target
	{
		get
		{
			return _param.Z;
		}
		set
		{
			_param.Z = value;
		}
	}

	public ColorizeEffect()
	{
		_param = new Vector3(0f, 0f, 0f);
		_oldparam = new Vector3(0f, 0f, 0f);
	}

	public override bool hasStateChanged()
	{
		if (!base.hasStateChanged())
		{
			if (base.Enabled)
			{
				return _param != _oldparam;
			}
			return false;
		}
		return true;
	}

	public override void SaveState()
	{
		base.SaveState();
		_oldparam = _param;
	}
}
