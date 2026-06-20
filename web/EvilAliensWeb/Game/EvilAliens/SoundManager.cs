using System;
using System.Collections.Generic;
using EvilAliensWeb.Compat;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Content;

namespace EvilAliens;

// Stage 6 audio. The original drove XACT (.xgs/.xwb/.xsb), which KNI's BlazorGL
// backend has no runtime for. Instead the banks were cracked offline
// (tools/audio/build_audio.py) to plain assets, and this manager plays them
// natively:
//   * SFX + speech -> KNI SoundEffect / SoundEffectInstance (per-cue instance
//     caps + steal-oldest, random pitch/volume variation so repeats don't sound
//     machine-stamped, loop flags, Default/Speech gain groups).
//   * Music -> the WebAudio JS layer via MusicInterop (seamless loop points).
// The public surface the game calls is unchanged except Play()/Stop() now use
// SoundEffectInstance instead of the XACT Cue type.
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
		AwardmentUnlocked,
		MissionFailed,
		GameOver
	}

	private enum Category
	{
		Default,
		Speech
	}

	private sealed class CueConfig
	{
		public bool Loop;
		public int Cap;
		public Category Cat;
		public float Volume;
		public bool Vary;

		public CueConfig(bool loop = false, int cap = 6, Category cat = Category.Default,
			float vol = 1f, bool vary = true)
		{
			Loop = loop;
			Cap = cap;
			Cat = cat;
			Volume = vol;
			Vary = vary;
		}
	}

	// Group gains (the game has only a Music on/off toggle, no volume sliders).
	private const float SfxGain = 0.75f;
	private const float SpeechGain = 1f;

	// Per-cue overrides; anything not listed uses the default CueConfig. Looping
	// cues are the sustained ones the game holds a handle to (Lazer/bees/charge).
	private static readonly Dictionary<string, CueConfig> _cfg = new()
	{
		{ "lazershot", new CueConfig(loop: true, cap: 8, vol: 0.6f, vary: false) },
		{ "lazercharge", new CueConfig(loop: true, cap: 2, vol: 0.7f, vary: false) },
		{ "bees", new CueConfig(loop: true, cap: 1, vol: 0.5f, vary: false) },
		{ "lazershotnoloop", new CueConfig(cap: 8, vol: 0.6f) },
		{ "fire", new CueConfig(cap: 8, vol: 0.55f) },
		{ "evillaugh", new CueConfig(cap: 1, vol: 0.9f, vary: false) },
		{ "usepowerup", new CueConfig(cap: 2, vary: false) },
	};

	private readonly Game game;
	private ContentManager _content;
	private readonly Random _rng = new();
	private readonly Dictionary<string, SoundEffect> _effects = new();
	private readonly Dictionary<string, List<SoundEffectInstance>> _active = new();
	private readonly Dictionary<string, SoundEffect> _voCache = new();
	private SoundEffectInstance _speech;
	private SoundEffectInstance _narration;
	private string _currentMusicCue;

	SoundManager ISoundManagerService.SoundManager => this;

	public SoundManager(Game game)
	{
		this.game = game;
	}

	private ContentManager Content
	{
		// Lazy: the content service isn't registered yet when this ctor runs.
		get { return _content ??= ServiceHelper.Get<IContentManagerService>().ContentManager; }
	}

	private static CueConfig ConfigFor(string cue)
	{
		if (_cfg.TryGetValue(cue, out var c))
			return c;
		if (cue.StartsWith("ttf_"))
			return new CueConfig(cap: 1, cat: Category.Speech, vary: false);
		return new CueConfig();
	}

	private SoundEffect GetEffect(string cue)
	{
		if (_effects.TryGetValue(cue, out var fx))
			return fx;
		try
		{
			fx = Content.Load<SoundEffect>("sfx/" + cue.ToLowerInvariant().Replace(' ', '_'));
		}
		catch (Exception)
		{
			fx = null; // cache the miss so we don't retry the load every play
		}
		_effects[cue] = fx;
		return fx;
	}

	private List<SoundEffectInstance> ActiveList(string cue)
	{
		if (!_active.TryGetValue(cue, out var list))
			_active[cue] = list = new List<SoundEffectInstance>();
		return list;
	}

	private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

	private SoundEffectInstance Spawn(string cue)
	{
		SoundEffect fx = GetEffect(cue);
		if (fx == null)
			return null;
		CueConfig cfg = ConfigFor(cue);

		List<SoundEffectInstance> list = ActiveList(cue);
		list.RemoveAll(i => i.IsDisposed || i.State == SoundState.Stopped);
		while (list.Count >= cfg.Cap)
		{
			SoundEffectInstance oldest = list[0];
			list.RemoveAt(0);
			try { oldest.Stop(); oldest.Dispose(); } catch (Exception) { }
		}

		SoundEffectInstance inst;
		try { inst = fx.CreateInstance(); }
		catch (Exception) { return null; }
		inst.IsLooped = cfg.Loop;
		float gain = (cfg.Cat == Category.Speech ? SpeechGain : SfxGain) * cfg.Volume;
		if (cfg.Vary)
		{
			inst.Volume = Clamp01(gain * (0.9f + 0.1f * (float)_rng.NextDouble()));
			inst.Pitch = (float)((_rng.NextDouble() - 0.5) * 0.12); // +/- ~0.7 semitone
		}
		else
		{
			inst.Volume = Clamp01(gain);
			inst.Pitch = 0f;
		}
		try { inst.Play(); } catch (Exception) { }
		list.Add(inst);
		return inst;
	}

	public string GetTTSName()
	{
		return "Karel";
	}

	private static string TextCue(Texts text)
	{
		switch (text)
		{
		case Texts.Warning: return "ttf_warning";
		case Texts.Danger: return "ttf_danger";
		case Texts.PowerUp: return "ttf_powerup";
		case Texts.ChallengeUnlocked: return "ttf_challengeUnlocked";
		case Texts.CheatUnlocked: return "ttf_cheatUnlocked";
		case Texts.LevelUnlocked: return "ttf_levelUnlocked";
		case Texts.DifficultyUnlocked: return "ttf_difficultyUnlocked";
		case Texts.WaveCompleted: return "ttf_waveCompleted";
		case Texts.GetReady: return "ttf_getReady";
		case Texts.AwardmentUnlocked: return "ttf_awardmentUnlocked";
		case Texts.MissionFailed: return "ttf_missionFailed";
		case Texts.GameOver: return "ttf_gameOver";
		default: return null;
		}
	}

	public void PlayText(Texts text, int priority)
	{
		string cue = TextCue(text);
		if (cue == null)
			return;
		// One announcer line at a time (the old build carried a speech priority;
		// here a new line simply supersedes the previous so they don't overlap).
		if (_speech != null && !_speech.IsDisposed && _speech.State == SoundState.Playing)
		{
			try { _speech.Stop(); } catch (Exception) { }
		}
		_speech = Spawn(cue);
	}

	public void PlayNarration(string name)
	{
		// Cinematic narrator (ElevenLabs "Victor") over the CreditsScene story
		// crawls — a layer the XBLIG never had (the text scrolled silently). One
		// clip at a time, played once (not looped), at the Speech group level.
		StopNarration();
		string key = name.ToLowerInvariant();
		if (!_voCache.TryGetValue(key, out var fx))
		{
			try { fx = Content.Load<SoundEffect>("vo/" + key); }
			catch (Exception) { fx = null; }
			_voCache[key] = fx;
		}
		if (fx == null)
			return;
		try
		{
			_narration = fx.CreateInstance();
			_narration.Volume = SpeechGain;
			_narration.Play();
		}
		catch (Exception) { }
	}

	public void StopNarration()
	{
		if (_narration != null && !_narration.IsDisposed)
		{
			try { _narration.Stop(); } catch (Exception) { }
		}
	}

	public bool TTSIsSilent()
	{
		// The live-SAPI dev-commentary path was #if WINDOWS (gone, and unused);
		// keep reporting silent so that code stays a no-op.
		return true;
	}

	public void SetMusicRate(float rate)
	{
		MusicInterop.SetRate(_currentMusicCue, rate);
	}

	public SoundEffectInstance Play(string name)
	{
		return Spawn(name);
	}

	public void PlayCue(string name)
	{
		Spawn(name);
	}

	public void PlayMusic(Songs song)
	{
		if (!Settings.GetInstance().PlayMusic)
			return;
		string cue = SongInstance.songFiles[(int)song];
		if (string.IsNullOrEmpty(cue))
			return;
		_currentMusicCue = cue;
		MusicInterop.Play(cue);
	}

	public void StopMusic()
	{
		_currentMusicCue = null;
		MusicInterop.Stop();
	}

	public void Update(GameTime gameTime)
	{
		// Reap finished one-shots so their WebAudio nodes don't pile up.
		foreach (List<SoundEffectInstance> list in _active.Values)
		{
			for (int i = list.Count - 1; i >= 0; i--)
			{
				SoundEffectInstance inst = list[i];
				if (inst.IsDisposed || inst.State == SoundState.Stopped)
				{
					try { if (!inst.IsDisposed) inst.Dispose(); } catch (Exception) { }
					list.RemoveAt(i);
				}
			}
		}
	}

	public void Stop(SoundEffectInstance inst)
	{
		if (inst != null && !inst.IsDisposed)
		{
			try { inst.Stop(); } catch (Exception) { }
		}
	}
}
