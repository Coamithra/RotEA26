using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;

namespace EvilAliens;

public class SongInstance
{
	private enum State
	{
		fadeIn,
		fadeOut,
		silent,
		playing
	}

	private const float FADETIME = 2.5f;

	private const float MAXVOL = 0.3f;

	public static string[] songFiles = new string[8] { "sjaak", "stage1", "bach", "stage2", "stage3", "classic", "kylikova", "sjaakslow" };

	private State state;

	private SoundEffect soundEffect;

	private SoundEffectInstance soundEffectInstance;

	private float volume;

	public SongInstance(Songs song)
	{
		state = State.silent;
		soundEffect = ServiceHelper.Get<IContentManagerService>().ContentManager.Load<SoundEffect>(songFiles[(int)song]);
		volume = 0f;
	}

	public void Start(bool fadeIn)
	{
		if (fadeIn)
		{
			state = State.fadeIn;
		}
		else
		{
			volume = 0.3f;
			state = State.playing;
		}
		if (soundEffectInstance != null)
		{
			soundEffectInstance.Resume();
			soundEffectInstance.Volume = volume;
		}
		SetRate(0f);
	}

	public void Stop(bool fadeOut)
	{
		if (fadeOut)
		{
			state = State.fadeOut;
			return;
		}
		state = State.silent;
		volume = 0f;
		soundEffectInstance.Volume = 0f;
		soundEffectInstance.Stop();
	}

	public bool IsPlaying()
	{
		return state != State.silent;
	}

	public void Update(GameTime gameTime)
	{
		switch (state)
		{
		case State.fadeIn:
			volume += 0.120000005f * (float)gameTime.ElapsedGameTime.TotalSeconds;
			if (volume > 0.3f)
			{
				volume = 0.3f;
				state = State.playing;
			}
			soundEffectInstance.Volume = volume;
			break;
		case State.fadeOut:
			volume -= 0.120000005f * (float)gameTime.ElapsedGameTime.TotalSeconds;
			if (volume < 0f)
			{
				volume = 0f;
				state = State.silent;
				soundEffectInstance.Stop();
			}
			soundEffectInstance.Volume = volume;
			break;
		case State.silent:
		case State.playing:
			break;
		}
	}

	public void SetRate(float rate)
	{
		if (soundEffectInstance != null)
		{
			soundEffectInstance.Pitch = rate;
		}
	}
}
