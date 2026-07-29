namespace EvilAliens;

// Level -> (title, bundled screenshot, difficulty name) lookup (card 2001fbd8). The online
// game browser (SubMenuOnlineGames) shows the LEVEL's screenshot art for each listed game;
// this is the same title/screenshot mapping the level-select carousel spells out inline in
// MenuScene, pulled into one table so the browser reads the same art. A level with no bundled
// art gets NULL from ScreenshotPath, not a default -- each of the three callers wants a
// different answer, so the fallback lives at the call sites (see ScreenshotPath below).
internal static class LevelArt
{
    // Nullable because the public game browser's level arrives off the wire: null = not a
    // Levels value this build knows, which shares the generic "Mission" answer with the
    // levels that have no title of their own (the demos).
    public static string Title(Levels? level)
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

    // What a caller draws when a level has no bundled art of its own -- Mission 1's empty
    // shot. Only the two call sites that MUST render something use it (see ScreenshotPath).
    internal const string DefaultScreenshotPath = "GFX/Screenshots/level1empty";

    // The bundled level-select thumbnail, or NULL for a level that has none. The single
    // source: what the carousel draws for a level the player has no saved screenshot of yet
    // (SubMenuLevelChoice.loadScreenshots), the art the online game browser shows for a listed
    // game (SubMenuOnlineGames.EnsureArt -- an in-progress game rarely has a saved
    // ScreenshotSaver capture for the joiner's profile), and the set ScreenshotSaver.StockShots
    // preloads and splash-warms.
    //
    // Card 0d166364: this switch IS the membership list. It used to have a twin, a
    // HasCarouselEntry predicate spelling out the same twelve levels, with a
    // `_ => "GFX/Screenshots/level1empty"` default here -- so a level added to the predicate
    // but missed here fell silently through that default, StockShots deduped the duplicate
    // away, and the carousel just drew Mission 1's art for the new level. Two hand lists that
    // had to agree became one where the drift cannot be expressed. Adding a carousel level =
    // adding one line here.
    //
    // NULL IS NOT AN ERROR EVERYWHERE, which is why the fallback lives at the call sites and
    // not here -- each of the three wants something different:
    //   - ScreenshotSaver.BuildStockShots: skip. Null IS "no bundled art to warm".
    //   - SubMenuOnlineGames.EnsureArt: draw DefaultScreenshotPath, silently. A listed game's
    //     level arrives OFF THE WIRE from a stranger's build, so an unmapped (or out-of-enum)
    //     level is reachable in production and a listed game must always have something to
    //     draw. This is the case that kept the old `_ =>` default honest.
    //   - SubMenuLevelChoice.loadScreenshots: draw DefaultScreenshotPath, LOUDLY. Every
    //     carousel entry is authored in MenuScene against a level in this table, so null there
    //     is an authoring bug, and a silent fallback would be the exact quiet failure this card
    //     removed. It is also what keeps tools/headless/probes/stockshots_warm.txt sensitive:
    //     the mutation that used to show up as a COLD decode of the dropped asset now resolves
    //     to already-warm level1empty art, so the WARNING is the signal.
    //
    // NOT the same question as General.ScreenshotEnabled, and that one cannot stand in for
    // membership: it answers "does this level CAPTURE a live thumbnail", and for WebcamAliens
    // it returns the Settings.WebcamScreenshot opt-in, which is OFF by default. Deriving the
    // stock set from it would drop gfx/screenshots/webcamss -- precisely the asset whose
    // absence from the warm set was the bug that led to card 8d6883f3.
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
            // Tutorial (launched from the main menu) and Demo1/2/3 (attract rotation) have no
            // carousel slot, so they need no bundled thumbnail. So does any value that is not
            // in the enum at all -- a listed game's Level is an int off the wire.
            _ => null,
        };
    }

    // Takes the CHECKED nullable, never a raw int: the only caller is the public game browser,
    // whose difficulty arrives off the wire and is validated at the boundary
    // (NetGameBrowser.GameEntry.KnownDifficulty -> NetProtocol.TryDifficulty). Null = a tier
    // this build does not know, which is a normal case for a stranger on a newer build.
    // Rig for that branch: ?gamebrowser=fallback.
    public static string DifficultyName(Settings.DifficultyLevel? difficulty)
    {
        return difficulty.HasValue ? difficulty.Value.ToString().Replace('_', ' ') : "?";
    }
}
