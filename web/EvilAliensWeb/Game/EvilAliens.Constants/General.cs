using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.GamerServices;

namespace EvilAliens.Constants;

public static class General
{
	public const float MillisecondsPerTick = 16.666666f;

	public const int DrawOrderMenuBackdrop = 1;

	public const int DrawOrderSubMenu = 2;

	public const int MaxPlayers = 4;

	public const string Version = "2.10";

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

	public static bool ScreenshotEnabled(Levels level)
	{
		if (level != Levels.Level1 && level != Levels.Level2)
		{
			return level == Levels.Level3;
		}
		return true;
	}
}
