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

    // Does this level have a level-select carousel entry (MenuScene's levelSelector or
    // challengeSelector)? Card 8d6883f3: this is the ONE membership list, and with
    // ScreenshotPath below it derives ScreenshotSaver.StockShots -- the set of bundled
    // thumbnails that must be preloaded AND splash-warmed. Adding a carousel level means
    // adding it here and to ScreenshotPath; nothing else has to be kept in step.
    //
    // NOT the same question as General.ScreenshotEnabled, and that one cannot stand in for
    // this: it answers "does this level CAPTURE a live thumbnail", and for WebcamAliens it
    // returns the Settings.WebcamScreenshot opt-in, which is OFF by default. Deriving the
    // stock set from it would drop gfx/screenshots/webcamss -- precisely the asset whose
    // absence from the warm set was the bug that led to this card.
    public static bool HasCarouselEntry(Levels level)
    {
        return level switch
        {
            Levels.Level1 or Levels.Level2 or Levels.Level3 => true,
            Levels.SpaceDodge or Levels.Braineroids or Levels.ClassicAliens
                or Levels.Paratrooper or Levels.OwnLevel or Levels.CrazyGame
                or Levels.InsaneBossI or Levels.TeamChallenge or Levels.WebcamAliens => true,
            // Tutorial (launched from the main menu) and Demo1/2/3 (attract rotation) have
            // no carousel slot, so they need no bundled thumbnail.
            _ => false,
        };
    }

    // The bundled level-select thumbnail: what the carousel draws for a level the player has
    // no saved screenshot of yet, and the art the online game browser shows for a listed game.
    // (An in-progress game rarely has a saved ScreenshotSaver capture for the joiner's
    // profile, so the browser uses this bundled art.) The single source of these paths --
    // SubMenuLevelChoice resolves each entry's image through here, and ScreenshotSaver.
    // StockShots is derived from here over HasCarouselEntry above.
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
