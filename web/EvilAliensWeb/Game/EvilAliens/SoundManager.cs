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

	// ---- SAME-TICK COALESCING (card 8732568e) -------------------------------------------------
	//
	// At most ONE start of any given cue per game tick. Reported as "multiplayer games (on a
	// joining peer side) seem to have a lot of loud explosion effect sounds", and the physics is
	// the whole reason it reads as LOUD rather than as busy: N copies of the SAME sample started
	// at the SAME instant are phase-identical, so they sum COHERENTLY -- amplitude x N, i.e.
	// +20*log10(N) dB. Ten simultaneous `expl1` is not ten explosions, it is one explosion
	// twenty decibels louder. (Two of the same sample even a few ms apart sum incoherently and
	// read as two hits, which is why the window is one tick and not longer.)
	//
	// WHY A JOINING PEER HITS IT HARDER, though it is not net-specific. `EvDeath` rides the
	// reliable ordered lane and the client applies a whole batch in ONE `DrainRx`, inside one
	// tick -- so deaths the host spread across several of its own frames all fire their cue on
	// the same client tick. Offline the same shape exists (a bomb clearing the screen) and is
	// fixed here too, which is what makes the change verifiable without a second machine.
	//
	// PER CUE, NOT A GLOBAL "one sound at a time". The ticket offered both. A global cap would
	// break deliberate LAYERING -- `SpiderBoss.BeginDeathThroes` plays "spiderbossdeath" and
	// "head asplode" together, `CastDisplayer` stacks three -- and none of that is the problem,
	// because two DIFFERENT samples do not sum coherently. Per-cue is what the physics asks for.
	//
	// IT APPLIES TO `PlayCue` AND NOTHING ELSE, and that boundary is the one that matters. The
	// rule is the SURFACE, not the cue: `PlayCue` is fire-and-forget, while `Play` RETURNS the
	// instance and `PlayText` keeps it in `_speech` -- and a caller that keeps the handle is a
	// caller that can be broken by a null. `PlayText` is where that bites: it STOPS the in-flight
	// line and then assigns `Spawn`'s result, so coalescing it would stop the first line, return
	// null, and leave the announcer SILENT -- worse than either the old or the intended
	// behaviour. (Found in review; an earlier cut of this said "looping cues are the only kept
	// handles", which is false -- `StarMine`'s `targetacquired` is another.)
	//
	// LOOPING CUES ARE EXEMPT TOO, independently. Nothing in the game `PlayCue`s one today, so
	// this is defence in depth rather than a live case: a sustained cue folded into an earlier
	// start would be an unstoppable loop, since `PlayCue` discards the handle and nothing reaps
	// a Playing instance.
	//
	// ONE DELIBERATE OPT-OUT, `PlayCue(cue, allowSameTick: true)`: `SpiderBoss.CollidesWith`
	// plays "bugdies" TWICE in a row, verbatim 2008 code (src_decompiled line 673-674), which is
	// an authored +6 dB emphasis on hitting that boss rather than an accident. Coalescing it
	// would quietly halve a set-piece's hit. `BrainBoss`'s two `expl2` calls are NOT this: they
	// sit in separate random branches and their coincidence is exactly the pile-up being fixed.
	//
	// The decision is taken BEFORE the effect is loaded, deliberately: on a machine with no audio
	// device `GetEffect` caches null and `Spawn` bails early, so a decision behind that load
	// could never be exercised headlessly -- which is where this is verified.
	private int _tick;
	private readonly Dictionary<string, int> _lastStartTick = new();
	private long _sfxRequests;
	private long _sfxCoalesced;
	private long _sfxAdmitted;
	private long _sfxPlayed;
	private readonly Dictionary<string, long> _coalescedByCue = new();

	// Per-cue REQUEST counts. `SfxRequests` alone says a sound was asked for; this says WHICH,
	// which is what lets a suite assert that a specific cue was played on a box with no audio
	// device at all (card 745728f9's join-peer homing cue is verified exactly this way -- there is
	// no other observable for it, since a puppet's cue leaves no state and eahl has no mixer).
	private readonly Dictionary<string, long> _requestsByCue = new();
	private readonly Dictionary<string, SoundEffect> _voCache = new();
	private SoundEffectInstance _speech;
	private SoundEffectInstance _narration;
	private string _currentMusicCue;

	// The Songs value behind _currentMusicCue (-1 = stopped). Kept separately because the wire
	// carries the enum, and because it is latched even when music is muted or the cue is empty.
	private int _currentSong = -1;

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

	// Shared fallbacks for cues not in _cfg, so a Play of an unlisted cue doesn't allocate a
	// CueConfig every time. CueConfig is treated as immutable after construction (nothing mutates
	// a returned config), so one instance each is safe to hand out repeatedly.
	private static readonly CueConfig _defaultCfg = new();
	private static readonly CueConfig _ttfSpeechCfg = new CueConfig(cat: Category.Speech, volByte: 130, vary: false);

	private static CueConfig ConfigFor(string cue)
	{
		if (_cfg.TryGetValue(cue, out var c))
			return c;
		if (cue.StartsWith("ttf_"))
			return _ttfSpeechCfg;
		return _defaultCfg;
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

	private SoundEffectInstance Spawn(string cue, bool coalescable)
	{
		CueConfig cfg = ConfigFor(cue);
		_sfxRequests++;
		_requestsByCue.TryGetValue(cue, out long asked);
		_requestsByCue[cue] = asked + 1;
		if (coalescable && !cfg.Loop && DebugFlags.SfxCoalesce
			&& _lastStartTick.TryGetValue(cue, out int startedAt) && startedAt == _tick)
		{
			_sfxCoalesced++;
			_coalescedByCue.TryGetValue(cue, out long n);
			_coalescedByCue[cue] = n + 1;
			return null;
		}
		// Marked before the load, so the decision above is exercised on a machine with no audio
		// device too -- see the coalescing header. A cue whose effect fails to load is still
		// "admitted this tick" as far as this ledger is concerned, which costs nothing: the only
		// thing it can suppress is another failed load in the same tick.
		//
		// An EXEMPT call marks the tick as well: it really did start a copy, so a `PlayCue` that
		// follows it in the same tick has something to fold into.
		_lastStartTick[cue] = _tick;
		_sfxAdmitted++;
		SoundEffect fx = GetEffect(cue);
		if (fx == null)
			return null;

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
		try { inst.Play(); _sfxPlayed++; } catch (Exception) { }
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
		// NEVER coalesced (card 8732568e): this stops the in-flight line FIRST and then assigns
		// the result, so a null would leave the announcer silent -- worse than the overlap the
		// coalescer exists to remove, and worse than the supersede this method already does.
		// One announcer line at a time is this method's own job, not the mixer's.
		_speech = Spawn(cue, coalescable: false);
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

	// NEVER coalesced: this hands the caller the instance, and a caller that keeps a handle is a
	// caller a null can break (card 8732568e).
	public SoundEffectInstance Play(string name)
	{
		return Spawn(name, coalescable: false);
	}

	public void PlayCue(string name)
	{
		Spawn(name, coalescable: true);
	}

	// `allowSameTick: true` is the ONE deliberate opt-out from same-tick coalescing, for a call
	// site that plays the SAME cue twice on purpose. Exactly one exists -- SpiderBoss's authored
	// double "bugdies" -- and it should stay that way: every other same-tick repeat in this game
	// is the pile-up the coalescer is for. See the header on _lastStartTick.
	public void PlayCue(string name, bool allowSameTick)
	{
		Spawn(name, coalescable: !allowSameTick);
	}

	// The retro "classic" tune ships in two variants: a clean, lyric-free loopable
	// instrumental (Songs.ClassicClean, the default for the tutorial + Easy/Medium
	// challenges) and the full Japanese-vocal "insane" cut (Songs.Classic), served
	// only as a reward when the player takes a challenge on Hard or above (higher
	// challenge difficulties are gated behind finishing the challenge first, so the
	// lyrics are genuinely earned). Difficulty-selected challenges pick with this.
	public static Songs ClassicForDifficulty()
	{
		return (Settings.GetInstance().CurrentDifficulty >= Settings.DifficultyLevel.Hard)
			? Songs.Classic
			: Songs.ClassicClean;
	}

	// The track the game last asked for (-1 = stopped), for the join-in-progress catch-up
	// (card 45a4e48d). Latched alongside the OnMusic hook and therefore, like it, ABOVE the
	// local mute check and independent of whether the call actually reached a peer: a muted
	// host still owes a joiner the right track, and a level's own Initialize sets this before
	// NetActiveScene exists (so live replication skips it) yet it is exactly what a later
	// joiner needs.
	internal int NetCurrentSong => _currentSong;

	public void PlayMusic(Songs song)
	{
		// Online co-op (card 11.3): mid-level music switches come from the level script /
		// boss code, which is host-only -- replicate the call (no-op unless active host).
		// Card 4a3b22b7: the ONE observable that says which track the game asked for and when.
		// A music switch is otherwise invisible to every verification tool here -- eahl stubs
		// eaMusic.* entirely, so a beat that never fires and a beat that fires correctly look
		// identical without this. Rare enough (a handful per level) to log unconditionally;
		// tools/headless/probes/bosstrain_music.txt asserts against it.
		System.Console.WriteLine("[music] play " + song + " cue=" + SongInstance.songFiles[(int)song] + " was=" + (_currentSong < 0 ? "none" : ((Songs)_currentSong).ToString()));
		_currentSong = (int)song;
		EvilAliensWeb.Compat.Net.NetSession.OnMusic((int)song);
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
		System.Console.WriteLine("[music] stop was=" + (_currentSong < 0 ? "none" : ((Songs)_currentSong).ToString()));
		_currentSong = -1;
		EvilAliensWeb.Compat.Net.NetSession.OnMusic(-1);
		_currentMusicCue = null;
		MusicInterop.Stop();
	}

	// Client-side EvMusic handler (song < 0 = stop). Dedupe against the cue already
	// playing: both peers start the level's initial track from their OWN Initialize, so
	// the host's boot-time PlayMusic must not restart the client's copy mid-intro.
	internal void NetApplyMusic(int song)
	{
		if (song < 0)
		{
			_currentSong = -1;
			_currentMusicCue = null;
			MusicInterop.Stop();
			return;
		}
		if (song >= SongInstance.songFiles.Length)
			return;
		// Latch above the mute check and above the cue dedupe below, mirroring PlayMusic: this
		// is what the game is ON, not what it just restarted, so a muted client still reports
		// the peer's track to the eaNetBg() catch-up diff.
		_currentSong = song;
		if (!Settings.GetInstance().PlayMusic)
			return;
		string cue = SongInstance.songFiles[song];
		if (string.IsNullOrEmpty(cue) || cue == _currentMusicCue)
			return;
		_currentMusicCue = cue;
		MusicInterop.Play(cue);
	}

	// Pause "underwater" muffle: mutes+muddies the BGM while the game is paused
	// (a lowpass + duck on the JS music bus), restored on resume. GameScene drives
	// it on pause-enter / every resume path.
	public void SetPauseMuffle(bool on)
	{
		MusicInterop.SetPauseMuffle(on);
	}

	public void Update(GameTime gameTime)
	{
		// One game tick, which is the coalescing window (see the header on _lastStartTick).
		// Called unconditionally from Game1.UpdateInner -- BEFORE base.Update, the collision
		// sweep and the net rx drain -- so everything one tick does falls under one number, and
		// the counter keeps advancing under a pause (where the menus still play cues).
		_tick++;
		// Reap finished one-shots so their WebAudio nodes don't pile up.
		ReapStopped();
	}

	// ---- the coalescer's observable (card 8732568e) -------------------------------------------
	//
	// An SFX change has NO PIXELS and, headlessly, no sound either -- eahl runs with the mixer
	// silenced and, in a container, with no audio device at all. So the only honest evidence is
	// the DECISION, read back as data. Counters rather than a log line: a busy frame coalesces
	// dozens of starts and a per-start line would be its own noise problem.
	internal int SfxTick => _tick;

	internal long SfxRequests => _sfxRequests;

	// ADMITTED, not "played": counted before the effect is loaded and before the 32-instance
	// Default cap, so on a box with no audio device it still moves (which is what makes the
	// decision testable there). `SfxPlayed` is the one that only counts a real `inst.Play()`, and
	// the gap between them is the cap plus any load failure.
	internal long SfxAdmitted => _sfxAdmitted;

	internal long SfxPlayed => _sfxPlayed;

	// How many times ONE cue has been asked for since the last reset -- see _requestsByCue.
	internal long SfxRequestsOf(string cue)
	{
		_requestsByCue.TryGetValue(cue, out long asked);
		return asked;
	}

	internal long SfxCoalesced => _sfxCoalesced;

	// "expl1=11 expl2=3", ordered by count then name so the string is stable across runs.
	internal string SfxCoalescedByCue()
	{
		if (_coalescedByCue.Count == 0)
		{
			return "none";
		}
		List<KeyValuePair<string, long>> rows = new List<KeyValuePair<string, long>>(_coalescedByCue);
		rows.Sort((a, b) => a.Value != b.Value ? b.Value.CompareTo(a.Value)
			: string.CompareOrdinal(a.Key, b.Key));
		System.Text.StringBuilder sb = new System.Text.StringBuilder();
		foreach (KeyValuePair<string, long> row in rows)
		{
			if (sb.Length > 0)
			{
				sb.Append(' ');
			}
			sb.Append(row.Key).Append('=').Append(row.Value);
		}
		return sb.ToString();
	}

	// Stop every live instance of one cue. Exists for the suite's LOOPING-cue leg: `PlayCue`
	// discards the handle and `ReapStopped` never reaps a Playing instance, so a looping cue
	// started through it would otherwise be unreachable for the process's lifetime -- and the
	// suite is menu-runnable in a real browser, where that is a real sound nobody can silence.
	internal void SfxStopCueForTest(string cue)
	{
		if (!_active.TryGetValue(cue, out List<SoundEffectInstance> list))
		{
			return;
		}
		foreach (SoundEffectInstance inst in list)
		{
			try { if (!inst.IsDisposed) inst.Stop(); } catch (Exception) { }
		}
		ReapStopped();
	}

	// True for a cue the config marks as looping -- read by the `eaSfx.burst` seam, which refuses
	// one for the reason above.
	internal static bool SfxCueLoops(string cue)
	{
		return _cfg.TryGetValue(cue, out CueConfig c) && c.Loop;
	}

	internal void SfxResetCounters()
	{
		_sfxRequests = 0;
		_sfxAdmitted = 0;
		_sfxPlayed = 0;
		_sfxCoalesced = 0;
		_coalescedByCue.Clear();
		_requestsByCue.Clear();
	}

	public void Stop(SoundEffectInstance inst)
	{
		if (inst != null && !inst.IsDisposed)
		{
			try { inst.Stop(); } catch (Exception) { }
		}
	}
}
