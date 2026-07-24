namespace EvilAliens;

// Level -> (title, bundled screenshot, difficulty name) lookup (card 2001fbd8). The online
// game browser (SubMenuOnlineGames) shows the LEVEL's screenshot art for each listed game;
// this is the same title/screenshot mapping the level-select carousel spells out inline in
// MenuScene, pulled into one table so the browser reads the same art. Missing/unmapped levels
// fall back to a sensible default so a listed game always has something to draw.
internal static class LevelArt
{
    public static string Title(Levels level)
    {
        return level switch
        {
            Levels.Level1 => "Mission 1",
            Levels.Level2 => "Mission 2",
            Levels.Level3 => "Mission 3",
            Levels.SpaceDodge => "Space Dodge!",
            Levels.Braineroids => "Braineroids",
            Levels.ClassicAliens => "Evil Aliens Classic",
            Levels.Paratrooper => "Paratrooper",
            Levels.OwnLevel => "Base Pressure",
            Levels.CrazyGame => "Crazy Game",
            Levels.InsaneBossI => "Boss Train",
            Levels.TeamChallenge => "Team Challenge",
            Levels.WebcamAliens => "I Made This!",
            Levels.Tutorial => "Tutorial",
            _ => "Mission",
        };
    }

    // The bundled level-select thumbnail. (An in-progress game rarely has a saved
    // ScreenshotSaver capture for the joiner's profile, so the browser uses this bundled art.)
    public static string ScreenshotPath(Levels level)
    {
        return level switch
        {
            Levels.Level1 => "GFX/Screenshots/level1empty",
            Levels.Level2 => "GFX/Screenshots/level2empty",
            Levels.Level3 => "GFX/Screenshots/level3empty",
            Levels.SpaceDodge => "GFX/Screenshots/SpaceDodge",
            Levels.Braineroids => "GFX/Screenshots/ss1",
            Levels.ClassicAliens => "GFX/Screenshots/classicss",
            Levels.Paratrooper => "GFX/Screenshots/Paratrooper",
            Levels.OwnLevel => "GFX/Screenshots/OwnLevel",
            Levels.CrazyGame => "GFX/Screenshots/crazygamess",
            Levels.InsaneBossI => "GFX/Screenshots/InsaneBossI",
            Levels.TeamChallenge => "GFX/Screenshots/teamchallengess",
            Levels.WebcamAliens => "GFX/Screenshots/webcamss",
            _ => "GFX/Screenshots/level1empty",
        };
    }

    public static string DifficultyName(int difficulty)
    {
        if (difficulty < 0 || difficulty > (int)Settings.DifficultyLevel.Inzane)
        {
            return "?";
        }
        return ((Settings.DifficultyLevel)difficulty).ToString().Replace('_', ' ');
    }
}
