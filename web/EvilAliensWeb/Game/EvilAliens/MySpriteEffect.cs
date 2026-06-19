namespace EvilAliens;

public class MySpriteEffect
{
	private bool _enabled;

	private bool _oldenabled;

	public bool Enabled => _enabled;

	public void Enable()
	{
		_enabled = true;
	}

	public void Disable()
	{
		_enabled = false;
	}

	public MySpriteEffect()
	{
		_enabled = false;
		_oldenabled = false;
	}

	public virtual void SaveState()
	{
		_oldenabled = _enabled;
	}

	public virtual bool hasStateChanged()
	{
		return _oldenabled != _enabled;
	}
}
