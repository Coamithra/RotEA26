using System.Collections.Generic;
using EvilAliens.Constants;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;

namespace EvilAliens;

public class SoundManager : ISoundManagerService
{
	public enum Texts
	{
		Nothing,
		Warning,
		Danger,
		PowerUp,
		ChallengeUnlocked,
		CheatUnlocked,
		LevelUnlocked,
		DifficultyUnlocked,
		WaveCompleted,
		GetReady,
		AwardmentUnlocked
	}

	private Cue currentMusic;

	private Songs currentSong;

	private int currentspeechpriority;

	private AudioEngine engine;

	private WaveBank wavebank;

	private SoundBank soundbank;

	private Game game;

	private Dictionary<Songs, SongInstance> loadedSongs = new Dictionary<Songs, SongInstance>();

	SoundManager ISoundManagerService.SoundManager => this;

	public string GetTTSName()
	{
		return "Karel";
	}

	public void PlayText(Texts text, int priority)
	{
		switch (text)
		{
		case Texts.Warning:
			PlayCue("ttf_warning");
			break;
		case Texts.Danger:
			PlayCue("ttf_danger");
			break;
		case Texts.PowerUp:
			PlayCue("ttf_powerup");
			break;
		case Texts.ChallengeUnlocked:
			PlayCue("ttf_challengeUnlocked");
			break;
		case Texts.CheatUnlocked:
			PlayCue("ttf_cheatUnlocked");
			break;
		case Texts.LevelUnlocked:
			PlayCue("ttf_levelUnlocked");
			break;
		case Texts.DifficultyUnlocked:
			PlayCue("ttf_difficultyUnlocked");
			break;
		case Texts.WaveCompleted:
			PlayCue("ttf_waveCompleted");
			break;
		case Texts.GetReady:
			PlayCue("ttf_getReady");
			break;
		case Texts.AwardmentUnlocked:
			PlayCue("ttf_awardmentUnlocked");
			break;
		case Texts.Nothing:
			break;
		}
	}

	public bool TTSIsSilent()
	{
		return true;
	}

	public void SetMusicRate(float rate)
	{
		if (currentSong == Songs.Kylikova && currentMusic != null)
		{
			currentMusic.SetVariable("Pitch", rate);
		}
	}

	public Cue Play(string name)
	{
		Cue val;
		try
		{
			val = soundbank.GetCue(name);
			val.Play();
		}
		catch (InstancePlayLimitException)
		{
			val = null;
		}
		return val;
	}

	public void PlayCue(string name)
	{
		try
		{
			soundbank.PlayCue(name);
		}
		catch (InstancePlayLimitException)
		{
		}
	}

	public void PlayMusic(Songs song)
	{
		if (Settings.GetInstance().PlayMusic && SongInstance.songFiles[(int)song] != "")
		{
			currentMusic = Play(SongInstance.songFiles[(int)song]);
			currentSong = song;
		}
	}

	public void PlayMusicOld(Songs song)
	{
		if (Settings.GetInstance().PlayMusic)
		{
			if (isASongPlaying())
			{
				fadeOutAllSongs();
				fadeInSong(song);
			}
			else
			{
				playSong(song);
			}
		}
	}

	private void fadeInSong(Songs song)
	{
		if (!loadedSongs.ContainsKey(song))
		{
			loadedSongs.Add(song, new SongInstance(song));
		}
		loadedSongs[song].Start(fadeIn: true);
	}

	private void fadeOutAllSongs()
	{
		foreach (SongInstance value in loadedSongs.Values)
		{
			value.Stop(fadeOut: true);
		}
	}

	private void playSong(Songs song)
	{
		if (!loadedSongs.ContainsKey(song))
		{
			loadedSongs.Add(song, new SongInstance(song));
		}
		loadedSongs[song].Start(fadeIn: false);
	}

	private bool isASongPlaying()
	{
		bool flag = false;
		foreach (SongInstance value in loadedSongs.Values)
		{
			flag |= value.IsPlaying();
		}
		return flag;
	}

	public void StopMusic()
	{
		if (currentMusic != null)
		{
			currentMusic.Stop((AudioStopOptions)1);
		}
	}

	public void Update(GameTime gameTime)
	{
		engine.Update();
		foreach (SongInstance value in loadedSongs.Values)
		{
			value.Update(gameTime);
		}
	}

	public void Stop(Cue cue)
	{
		if (cue != null)
		{
			cue.Stop((AudioStopOptions)0);
		}
	}

	public SoundManager(Game game)
	{
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Expected O, but got Unknown
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Expected O, but got Unknown
		//IL_0057: Unknown result type (might be due to invalid IL or missing references)
		//IL_0061: Expected O, but got Unknown
		engine = new AudioEngine("Content/SFX/alienssfx.xgs");
		wavebank = new WaveBank(engine, General.Path + "SFX/Wave Bank.xwb");
		soundbank = new SoundBank(engine, General.Path + "SFX/Sound Bank.xsb");
		this.game = game;
	}
}
