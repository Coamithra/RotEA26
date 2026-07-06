using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.GamerServices;

namespace EvilAliens.Constants;

public static class General
{
	public const float MillisecondsPerTick = 16.666666f;

	public const int DrawOrderMenuBackdrop = 1;

	public const int DrawOrderSubMenu = 2;

	public const int MaxPlayers = 4;

	public const string Version = "2026.0";

	private const float safeZonePercentage = 5f;

	private static bool isTrial = false;

	private static string path = "Content/";

	public static Rectangle SafeZone = new Rectangle(40, 30, 720, 540);

	public static bool IsTrial
	{
		get
		{
			return Guide.IsTrialMode;
		}
		set
		{
			Guide.SimulateTrialMode = value;
		}
	}

	public static string Path => path;

	// Which levels capture a live level-select thumbnail (vs showing bundled static art).
	// The XBLIG enabled only the three story levels; the web port extends it to every
	// challenge in the carousel too. WebcamAliens is opt-in (privacy: the shot contains
	// the player's camera image) via the Settings.WebcamScreenshot toggle. Demo1/2/3
	// (attract-mode) and the Tutorial have no carousel slot, so they never capture.
	public static bool ScreenshotEnabled(Levels level)
	{
		switch (level)
		{
		case Levels.Level1:
		case Levels.Level2:
		case Levels.Level3:
		case Levels.SpaceDodge:
		case Levels.Braineroids:
		case Levels.ClassicAliens:
		case Levels.Paratrooper:
		case Levels.OwnLevel:
		case Levels.CrazyGame:
		case Levels.InsaneBossI:
		case Levels.TeamChallenge:
			return true;
		case Levels.WebcamAliens:
			return Settings.GetInstance().WebcamScreenshot;
		default:
			return false;
		}
	}
}
