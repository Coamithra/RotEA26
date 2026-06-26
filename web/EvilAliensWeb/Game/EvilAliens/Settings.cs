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

	// Web default: software reticle (MousePointer) instead of the OS arrow for aiming.
	// false => while the cursor component is Visible (i.e. in a keyboard-controlled level)
	// MousePointer draws the reticle every frame -- spinning intro via showtimer, then the
	// static reticle -- and never forces Game.IsMouseVisible true, so the OS cursor stays
	// hidden over the canvas (it reappears off-canvas, where the reticle is also hidden).
	public bool HWMouse = false;

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
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
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
		//IL_009b: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ac: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f6: Expected I4, but got Unknown
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
