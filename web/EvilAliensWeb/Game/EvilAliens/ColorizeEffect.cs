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
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			return _param;
		}
		set
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0002: Unknown result type (might be due to invalid IL or missing references)
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
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		_param = new Vector3(0f, 0f, 0f);
		_oldparam = new Vector3(0f, 0f, 0f);
	}

	public override bool hasStateChanged()
	{
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
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
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		base.SaveState();
		_oldparam = _param;
	}
}
