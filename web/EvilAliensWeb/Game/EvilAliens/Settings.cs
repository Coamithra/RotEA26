using System;
using System.IO;
using System.Xml.Serialization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Storage;

namespace EvilAliens;

public class Settings : Savable
{
	public enum DifficultyLevel
	{
		Easy,
		Medium,
		Hard,
		Very_Hard,
		Inzane
	}

	private const float difficultychangeperlife = 0.17f;

	private static Settings instance;

	private bool _difficultyLocked;

	private DifficultyLevel _difficultyLockedAt;

	public bool PlayMusic = true;

	// The aiming reticle's RENDER MODE (card 51276dcd). There is ALWAYS a reticle in a
	// keyboard-controlled level, and it always plays the one-shot scale+rotate intro; HWMouse
	// only chooses HOW it's drawn afterwards:
	//   true  (default) -> HARDWARE: the reticle IS the OS cursor (canvas.style.cursor:
	//                      url(reticle/<px>.png)) -- zero-lag, no game-loop sprite trailing.
	//   false           -> SOFTWARE: the reticle is a SPRITE drawn in-game every frame following
	//                      the mouse (the original 2008 look), with the OS pointer hidden over
	//                      the canvas so there's no double cursor.
	// Menus always use the plain OS arrow. The "Hardware Mouse" Options toggle flips this.
	// See MousePointer / Compat/CursorInterop / eaCursor in index.html.
	public bool HWMouse = true;

	public bool VSync = true;

	public bool AdaptiveDifficulty;

	public bool ToonShader;

	public bool Invulnerability;

	public bool Bloom = true;

	public bool FullScreen;

	public int Friends;

	public bool InfiniteLives;

	public bool GalagaMode;

	public bool PowerUp;

	public int Turbo = 100;

	public bool Connector;

	public bool Interpolate = true;

	public bool DirectRespawn;

	public bool DevComments;

	public bool Stretch;

	public bool HideSafeArea;

	// Web port: opt-in capture of a level-select thumbnail for the webcam challenge
	// (Levels.WebcamAliens). Default OFF — when on, the shot composites the player's
	// segmented camera overlay in (WebcamLevel + ScreenshotSaver). Appended field, so
	// an existing Settings.xml without it deserializes to false.
	public bool WebcamScreenshot;

	// Public game browser (card 2001fbd8): while ON, starting/hosting an eligible game
	// registers it with the signaling server so strangers can find + join it (see
	// Compat/Net/NetListing). Cheekily DEFAULT ON. Because the field initializer runs in
	// the constructor XmlSerializer calls, an existing Settings.xml without this element
	// deserializes back to true (unlike WebcamScreenshot above, which has no initializer).
	// This is the one place a single-player game now opens a socket to the server; the
	// Options toggle + the pause-menu "Listed online" indicator are the mitigation.
	public bool AllowOnlineJoins = true;

	public float Scale = 1f;

	public float Gamma = 1f;

	private DifficultyLevel _difficultyLevel;

	private float _difficultyMin = 1f;

	private float _difficultyModifier = 1f;

	public PlayerSettings MainPlayerSettings;

	public PlayerSettings[] OtherPlayersSettings = new PlayerSettings[4];

	public DifficultyLevel CurrentDifficulty
	{
		get
		{
			return _difficultyLevel;
		}
		set
		{
			SetDifficultyTo(value);
		}
	}

	[XmlIgnore]
	public float DifficultyModifier
	{
		get
		{
			if (_difficultyLocked)
			{
				return GetDifficultyValue(_difficultyLockedAt);
			}
			return _difficultyModifier;
		}
		set
		{
			_difficultyModifier = value;
		}
	}

	// The tier the CURRENT FIGHT is actually being run at, as opposed to the one the player picked
	// in the menu. `DifficultyModifier` above already honours the lock; `CurrentDifficulty` does
	// not, and the gap is not academic -- Demo1/2/3 lock Hard and TutorialLevel locks Very_Hard,
	// so during an attract demo `CurrentDifficulty` still reports whatever the player last chose
	// while every enemy on screen is scaled to Hard.
	// Added for the difficulty-scaled AI (card c10e3e7f): keying the bot's skill off
	// `CurrentDifficulty` would fly an Easy-tier pilot against a Hard-tier demo for anyone whose
	// saved setting is Easy. Anything picking a tier for the LIVE fight wants this; menus and the
	// save file want `CurrentDifficulty`. (Get-only, so XmlSerializer skips it either way -- the
	// attribute is belt-and-braces against someone later adding a setter.)
	[XmlIgnore]
	public DifficultyLevel EffectiveDifficulty => _difficultyLocked ? _difficultyLockedAt : _difficultyLevel;

	public float DifficultyMinimum => _difficultyMin;

	public static void SetInstance(Settings newInstance)
	{
		instance = newInstance;
	}

	public static Settings GetInstance()
	{
		if (instance == null)
		{
			instance = new Settings();
		}
		return instance;
	}

	public PlayerSettings GetPlayerSettings(ControlDevice controller)
	{
		PlayerIndex val = (PlayerIndex)(controller switch
		{
			ControlDevice.PadOne => 0, 
			ControlDevice.PadTwo => 1, 
			ControlDevice.PadThree => 2, 
			ControlDevice.PadFour => 3, 
			ControlDevice.Keyboard => 0, 
			_ => throw new NotSupportedException(), 
		});
		if (val == Storage.ActivePlayer)
		{
			return MainPlayerSettings;
		}
		return OtherPlayersSettings[(int)val];
	}

	public Settings()
	{
		for (int i = 0; i < OtherPlayersSettings.Length; i++)
		{
			OtherPlayersSettings[i] = new PlayerSettings();
		}
		MainPlayerSettings = new PlayerSettings();
		try
		{
			SignedInGamer val = null;
			GamerCollectionEnumerator<SignedInGamer> enumerator = ((GamerCollection<SignedInGamer>)(object)Gamer.SignedInGamers).GetEnumerator();
			try
			{
				while (enumerator.MoveNext())
				{
					SignedInGamer current = enumerator.Current;
					if (current.PlayerIndex == Storage.ActivePlayer)
					{
						val = current;
					}
				}
			}
			finally
			{
				((IDisposable)enumerator).Dispose();
			}
			if (val != null)
			{
				GameDifficulty gameDifficulty = val.GameDefaults.GameDifficulty;
				switch ((int)gameDifficulty)
				{
				case 0:
					CurrentDifficulty = DifficultyLevel.Easy;
					break;
				case 2:
					CurrentDifficulty = DifficultyLevel.Hard;
					break;
				case 1:
					CurrentDifficulty = DifficultyLevel.Medium;
					break;
				default:
					CurrentDifficulty = DifficultyLevel.Easy;
					break;
				}
			}
		}
		catch (Exception)
		{
		}
	}

	public float MultiPlayerDifficultyModifier(int players)
	{
		if (players <= 1)
		{
			return 1f;
		}
		return 1f + (float)(players - 1) * DifficultyModifier * 0.4f;
	}

	public void LockDifficulty()
	{
		_difficultyLocked = true;
		_difficultyLockedAt = _difficultyLevel;
	}

	public void LockDifficulty(DifficultyLevel difficultyLevel)
	{
		_difficultyLocked = true;
		_difficultyLockedAt = difficultyLevel;
	}

	public void UnlockDifficulty()
	{
		_difficultyLocked = false;
	}

	public float GetDifficultyValue(DifficultyLevel difficultyLevel)
	{
		return difficultyLevel switch
		{
			DifficultyLevel.Easy => 0.35f, 
			DifficultyLevel.Medium => 0.6f, 
			DifficultyLevel.Hard => 0.8f, 
			DifficultyLevel.Very_Hard => 1f, 
			DifficultyLevel.Inzane => 1.2f, 
			_ => 1f, 
		};
	}

	public void SetDifficultyTo(DifficultyLevel difficultylevel)
	{
		_difficultyMin = GetDifficultyValue(difficultylevel);
		_difficultyModifier = GetDifficultyValue(difficultylevel);
		_difficultyLevel = difficultylevel;
	}

	public float DifficultyFactorized(float factor)
	{
		return 1f + (DifficultyModifier - 1f) * factor;
	}

	// Called on player death. This is the CORE of what "adaptive" means (see also the ceiling in
	// Update): NON-adaptive tiers hard-reset the ramped modifier back to the tier floor, so a death
	// undoes all the tension the time-ramp built up; ADAPTIVE (Easy) only eases it 20% -- a gentle
	// rubber-band DOWN for a struggling player, not a full reset. The time-ramp itself (Update) runs
	// on every non-locked tier either way, so "adaptive" is a slight misnomer -- it governs the death
	// rule + ceiling, not whether difficulty changes at all.
	public void ResetDifficulty()
	{
		if (!AdaptiveDifficulty)
		{
			_difficultyModifier = _difficultyMin;
		}
		else
		{
			_difficultyModifier *= 0.8f;
		}
	}

	public void DisableCheats()
	{
		GetInstance().GalagaMode = false;
		GetInstance().PowerUp = false;
		GetInstance().InfiniteLives = false;
		GetInstance().Friends = 0;
		GetInstance().Turbo = 100;
		GetInstance().Connector = false;
	}

	public bool CheckForCheats()
	{
		bool flag = false;
		flag |= GetInstance().Connector;
		flag |= GetInstance().PowerUp;
		flag |= GetInstance().InfiniteLives;
		flag |= GetInstance().GalagaMode;
		flag |= GetInstance().Friends > 0;
		return flag | (GetInstance().Turbo != 100);
	}

	public void Update(GameTime gameTime)
	{
		RandomHelper.RandomNextFloat(0f, 999f);
		_ = 1f;
		// The difficulty time-ramp: while NOT locked (challenges/tutorial LockDifficulty, freezing this),
		// the modifier creeps up every frame (+ elapsedMinutes * 0.17 * tierValue) so a level gets harder
		// the longer you survive -- on EVERY tier, adaptive or not. The ONLY difference the adaptive flag
		// makes here is the ceiling: non-adaptive caps at tier*2 (Hard 1.6 / Very_Hard 2.0 / Inzane 2.4),
		// adaptive caps at Inzane*2 = 2.4 so a strong Easy run can climb as high as Inzane. (Pairs with
		// ResetDifficulty's death rule above.)
		if (!_difficultyLocked)
		{
			if (!AdaptiveDifficulty)
			{
				_difficultyModifier = MathHelper.Min((float)gameTime.ElapsedGameTime.TotalMinutes * 0.17f * GetDifficultyValue(_difficultyLevel) + _difficultyModifier, GetDifficultyValue(_difficultyLevel) * 2f);
			}
			else
			{
				_difficultyModifier = MathHelper.Min((float)gameTime.ElapsedGameTime.TotalMinutes * 0.17f * GetDifficultyValue(_difficultyLevel) + _difficultyModifier, GetDifficultyValue(DifficultyLevel.Inzane) * 2f);
			}
		}
	}

	protected override void saveData(StorageContainer c)
	{
		string path = c.Path + "Settings.xml";
		XmlSerializer xmlSerializer = new XmlSerializer(typeof(Settings));
		using StreamWriter textWriter = new StreamWriter(path, append: false);
		xmlSerializer.Serialize(textWriter, instance);
	}

	protected override void loadData(StorageContainer c)
	{
		string path = c.Path + "Settings.xml";
		if (File.Exists(path))
		{
			XmlSerializer xmlSerializer = new XmlSerializer(typeof(Settings));
			using StreamReader textReader = new StreamReader(path);
			instance = xmlSerializer.Deserialize(textReader) as Settings;
			// Self-heal a bug (fixed here): an earlier build of the ?invuln debug flag wrote
			// straight into Settings.Invulnerability, which then persisted via any later
			// Settings.SaveThreaded() (options exit, difficulty pick, ...), leaving a save
			// permanently invulnerable even on a plain boot. There is currently NO shipped menu
			// entry that legitimately sets this field true (the original "playtest" invincibility
			// toggle was never wired into the web port's MenuScene), so a deserialized `true` can
			// only be leftover fallout from that bug -- force it back off on load. If a real
			// in-game toggle for this cheat is ever wired up, this line must be removed.
			if (instance != null)
			{
				instance.Invulnerability = false;
			}
		}
		else
		{
			instance = new Settings();
		}
		Game1.SettingsLoaded();
	}

	protected override void onLoadError()
	{
		instance = new Settings();
		Game1.SettingsLoaded();
	}

	protected override bool checkData()
	{
		return true;
	}
}
