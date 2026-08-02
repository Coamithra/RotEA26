using Microsoft.Xna.Framework;

namespace EvilAliens;

public class Timer
{
	private float _time;

	private float _duration;

	private bool _repeating;

	private bool _ringing;

	private bool _on;

	public bool Finished
	{
		get
		{
			if (!_ringing)
			{
				return _time == 0f;
			}
			return true;
		}
	}

	public float Normalized
	{
		get
		{
			if (_duration != 0f)
			{
				return MathHelper.Clamp(_time / _duration, 0f, 1f);
			}
			return 1f;
		}
	}

	public bool Active => _on;

	public float TimeLeft => MathHelper.Max(_time, 0f);

	public float TimeElapsed => MathHelper.Max(_duration - _time, 0f);

	public float Duration
	{
		get
		{
			return _duration;
		}
		set
		{
			_duration = value;
			if (_time > _duration)
			{
				_time = _duration;
			}
		}
	}

	public Timer(float duration, bool repeating)
	{
		_duration = duration;
		_time = duration;
		_repeating = repeating;
		_on = true;
		_ringing = false;
	}

	public void Start()
	{
		_on = true;
	}

	public void Stop()
	{
		_on = false;
	}

	public void Reset()
	{
		_time = _duration;
		_ringing = false;
	}

	public void Update(GameTime gameTime)
	{
		_ringing = false;
		if (!_on)
		{
			return;
		}
		_time -= (float)gameTime.ElapsedGameTime.TotalMilliseconds;
		if (!(_time < 0f))
		{
			return;
		}
		_ringing = true;
		if (_repeating)
		{
			while (_time < 0f)
			{
				_time += _duration;
				if (_duration <= 0f)
				{
					_time = 0f;
				}
			}
		}
		else
		{
			_time = 0f;
			_on = false;
		}
	}

	internal void Randomize()
	{
		_time = RandomHelper.RandomNextFloat(0f, _duration);
	}

	// Park this timer at a given point in its cycle, in the same 0..1 units Normalized reports
	// (which count DOWN -- Normalized is TimeLeft/Duration). Added for the online co-op anchored
	// motion lane (card c1a38ef9), where a puppet's swivel phase is pinned to the host's rather
	// than left on Randomize's own roll; the value is clamped, so a phase off the wire can never
	// park the timer outside its cycle.
	internal void SetNormalized(float normalized)
	{
		_time = MathHelper.Clamp(normalized, 0f, 1f) * _duration;
	}
}
