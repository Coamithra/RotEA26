using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class AudioData
{
	public enum StreamState
	{
		pending,
		playing,
		fadeIn,
		fadeOut
	}

	private const int Volume_Silent = -5000;

	private const int Volume_Normal = -1200;

	private const float FadeSpeed = 1.5f;

	private const int E_ABORT = -2147467260;

	private float volume;

	private StreamState state;

	public StreamState State => state;

	public AudioData()
	{
		state = StreamState.pending;
		NewGraph();
	}

	private void ResetGraph()
	{
	}

	private void NewGraph()
	{
	}

	public void Update(GameTime gameTime)
	{
	}

	public void SetRate(double rate)
	{
	}

	public void PlayFile(string filename, bool fadein)
	{
	}

	public void Stop(bool fadeout)
	{
	}
}
