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
//   * SFX + speech -> KNI SoundEffect / SoundEffectInstance with the authored XACT
//     mix re-applied: per-cue volume from the recovered sound-header byte (real
//     logistic law), per-CATEGORY instance limits (Default=32 FailToPlay, Speech
//     unlimited), loop flags, and a subtle 5% humanize on repeats. Category gains
//     are all 0 dB (unity) per the .xgs -- no cross-bus trim.
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
		public Category Cat;
		public int VolByte;   // authored XACT sound-header volume byte (-> VolToLinear)
		public bool Vary;

		public CueConfig(bool loop = false, Category cat = Category.Default,
			int volByte = 90, bool vary = true)
		{
			Loop = loop;
			Cat = cat;
			VolByte = volByte;
			Vary = vary;
		}
	}

	// XACT category gains (alienssfx.xgs): Default / Music / Speech are ALL 0 dB
	// (unity) -- the original authored no cross-bus trim, so there is no SFX/speech
	// attenuation here. A cue's level comes entirely from its sound-header volume
	// byte below. (The old SfxGain=0.75 was a port guess; dropped for the authored
	// flat mix, which puts baseline SFX ~level with the music layer.)

	// Default (SFX) category instance cap from the .xgs: max 32 concurrent, and the
	// authored behavior is FailToPlay (it never steals). Speech is unlimited; music
	// is one-at-a-time on the WebAudio layer.
	private const int SfxMaxInstances = 32;

	// XACT volume byte -> linear amplitude. MonoGame's logistic law: byte 0xB4=180
	// is ~0 dB (unity); the modal SFX byte 90 is ~-12 dB. Mirrors tools/audio/xact.py
	// vol_to_linear (validated against XACT's 8 calibration points). Every played
	// cue lands <= ~0.57 linear, so no offline boost / clip is ever needed.
	private static float VolToLinear(int b)
	{
		double db = (-96.0 - 67.7385212334047)
			/ (1.0 + Math.Pow(b / 80.1748600297963, 0.432254984608615)) + 67.7385212334047;
		return (float)Math.Pow(10.0, db / 20.0);
	}

	// Per-cue overrides; anything not listed defaults to (Default, byte 90, vary).
	// Volume bytes are the authored values recovered from Sound Bank.xsb (xact.py
	// parse_soundbank_meta). Looping cues are the sustained ones the game holds a
	// handle to; lazercharge's loop is a port addition (gameplay holds the charge).
	private static readonly Dictionary<string, CueConfig> _cfg = new()
	{
		{ "lazershot", new CueConfig(loop: true, volByte: 90, vary: false) },
		{ "lazercharge", new CueConfig(loop: true, volByte: 135, vary: false) },
		{ "bees", new CueConfig(loop: true, volByte: 135, vary: false) },
		{ "blast", new CueConfig(volByte: 107) },
		{ "evillaugh", new CueConfig(volByte: 113, vary: false) },
		// Authored ~14.7 dB below baseline (byte 39 vs 90) -- a full-scale recording
		// the original cut hard; that authored cut is the whole reason the un-attenuated
		// port "bzzzt" was so loud. Now applied straight from the bank.
		{ "usepowerup", new CueConfig(volByte: 39, vary: false) },
		// Port addition (no XACT cue): the splash channel-flip "static channel swap"
		// burst. A touch above baseline so it carries the transition, no humanize
		// (static shouldn't be pitch-wobbled).
		{ "channelswap", new CueConfig(volByte: 100, vary: false) },
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
			return new CueConfig(cat: Category.Speech, volByte: 130, vary: false);
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

		// Authored XACT instance limiting is per-CATEGORY, not per-cue: the Default
		// (SFX) category caps at 32 concurrent and fails-to-play when full (it never
		// steals); Speech is unlimited. Reap finished instances first so the count is
		// live, then enforce the Default pool.
		ReapStopped();
		if (cfg.Cat == Category.Default && CountActive(Category.Default) >= SfxMaxInstances)
			return null;

		SoundEffectInstance inst;
		try { inst = fx.CreateInstance(); }
		catch (Exception) { return null; }
		inst.IsLooped = cfg.Loop;
		float gain = VolToLinear(cfg.VolByte);   // category gain is unity (0 dB)
		if (cfg.Vary)
		{
			// Subtle ~5% humanize so rapid repeats aren't machine-stamped. The bank
			// authored no variation; this is a small deliberate port embellishment.
			float rv = (float)(_rng.NextDouble() * 2.0 - 1.0);
			float rp = (float)(_rng.NextDouble() * 2.0 - 1.0);
			inst.Volume = Clamp01(gain * (1f + 0.05f * rv));
			inst.Pitch = 0.03f * rp;   // +/- ~0.35 semitone
		}
		else
		{
			inst.Volume = Clamp01(gain);
			inst.Pitch = 0f;
		}
		try { inst.Play(); } catch (Exception) { }
		ActiveList(cue).Add(inst);
		return inst;
	}

	// A cue's category without allocating a CueConfig (ConfigFor builds one for
	// unlisted cues). Only ttf_ cues are Speech; everything else is Default.
	private static Category CategoryOf(string cue)
	{
		if (_cfg.TryGetValue(cue, out var c))
			return c.Cat;
		return cue.StartsWith("ttf_") ? Category.Speech : Category.Default;
	}

	// Live count of active instances in a category (across all of its cues).
	private int CountActive(Category cat)
	{
		int n = 0;
		foreach (KeyValuePair<string, List<SoundEffectInstance>> kv in _active)
		{
			if (CategoryOf(kv.Key) != cat)
				continue;
			foreach (SoundEffectInstance inst in kv.Value)
				if (!inst.IsDisposed && inst.State != SoundState.Stopped)
					n++;
		}
		return n;
	}

	// Dispose + drop finished instances from every cue list (frees their WebAudio
	// nodes and keeps CountActive accurate). Called each frame and before each spawn.
	private void ReapStopped()
	{
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
			_narration.Volume = 1f;   // credits narrator (a port feature, not a bank cue)
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
		ReapStopped();
	}

	public void Stop(SoundEffectInstance inst)
	{
		if (inst != null && !inst.IsDisposed)
		{
			try { inst.Stop(); } catch (Exception) { }
		}
	}
}
