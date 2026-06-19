using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class MP3Player
{
	private List<AudioData> streams = new List<AudioData>();

	public void SetMusicRate(float rate)
	{
		foreach (AudioData stream in streams)
		{
			stream.SetRate(rate);
		}
	}

	public void Update(GameTime gameTime)
	{
		foreach (AudioData stream in streams)
		{
			stream.Update(gameTime);
		}
	}

	public void StopMusic(bool fadeout)
	{
		foreach (AudioData stream in streams)
		{
			stream.Stop(fadeout);
		}
	}

	public void PlayMP3(string filename, bool crossfade)
	{
	}
}
