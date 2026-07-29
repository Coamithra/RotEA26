using System.Collections.Generic;
using EvilAliensWeb.Compat.Net;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

// The public game browser carousel (card 2001fbd8). Reuses the SubMenuCarousel geometry (the
// flying/scaling screenshots) but keyed on the OPEN GAMES the server lists (NetGameBrowser)
// rather than levels: each entry shows the level's screenshot art; the selected entry's
// difficulty / players / ping / room code render in the info overlay. Selecting an entry hands
// its room code to MenuScene, which joins it via the normal 11.4 flow (join-in-progress).
//
// The list is DYNAMIC (the server refreshes it, pings fill in): Update snapshots
// NetGameBrowser.Games each frame and rebuilds the menu entries only when the set of codes
// changes, so the scroll animation isn't disturbed by a same-set refresh.
internal class SubMenuOnlineGames : SubMenuCarousel
{
	public delegate void GameChosen(string code);

	public event GameChosen OnGameSelected;

	private List<NetGameBrowser.GameEntry> games = new List<NetGameBrowser.GameEntry>();

	// Keyed on the RAW wire level, not on Levels: a listed game's level is an int off the wire
	// and may not be a member of our enum at all, so a `Levels` key here would be a value the
	// type says cannot exist (card 88f87ba2 -- NetGameBrowser.GameEntry carries the checked
	// Levels? beside the raw int, and this cache is the one place that still needs the raw).
	private readonly Dictionary<int, Texture2D> artCache = new Dictionary<int, Texture2D>();

	private Texture2D fallbackArt;

	// Levels seen in a listing that had no bundled art, recorded by EnsureArt as it resolves
	// them and reported by RefreshGames. Deliberately NOT re-derived at the report site: that
	// would be a second copy of the same test, and reverting EnsureArt's guard would then leave
	// the report -- and the probe reading it -- unchanged. Cumulative and idempotent, matching
	// artCache's own lifetime.
	private readonly HashSet<int> unmappedArtLevels = new HashSet<int>();

	public SubMenuOnlineGames(Game game)
		: base(game)
	{
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		fallbackArt = Content.Load<Texture2D>(LevelArt.DefaultScreenshotPath);
	}

	public override void Update(GameTime gameTime)
	{
		RefreshGames();
		base.Update(gameTime);
	}

	// Snapshot the live browser list; rebuild the menu entries only when the code set changes.
	// (Re-copied every frame regardless so the entries point at the current objects, whose
	// PingMs fills in live; the per-frame alloc is trivial for a transient menu.)
	private void RefreshGames()
	{
		IReadOnlyList<NetGameBrowser.GameEntry> live = NetGameBrowser.Games;
		bool changed = live.Count != games.Count;
		if (!changed)
		{
			for (int i = 0; i < live.Count; i++)
			{
				if (live[i].Code != games[i].Code)
				{
					changed = true;
					break;
				}
			}
		}
		if (!changed)
		{
			games = new List<NetGameBrowser.GameEntry>(live);
			return;
		}
		// Keep the highlight on the SAME room across a refresh (by code, not index), so a room
		// dropping off the list doesn't silently move the selection to a different game.
		string selectedCode = (selectedEntry >= 0 && selectedEntry < games.Count)
			? games[selectedEntry].Code
			: null;
		games = new List<NetGameBrowser.GameEntry>(live);
		RemoveAllEntries();
		// Card 0d166364: report any entry whose level resolved to no bundled art. That branch is
		// only reachable off the wire, so nothing but a live stranger's build -- or
		// ?gamebrowser=fallback's deliberately unmapped fake entries -- exercises it, and until this line
		// existed a broken fallback would have failed in TOTAL SILENCE, since EnsureArt's catch
		// absorbs throws. Asserted by tools/headless/probes/gamebrowser_fallback.txt.
		//
		// Membership comes from what EnsureArt RECORDED while resolving, never re-derived from
		// LevelArt here: a second copy of the test would keep printing the right answer with
		// EnsureArt's guard deleted, and the probe would pass on the mutation it exists to
		// catch. `reported` is separate because unmappedArtLevels is cumulative -- two listed
		// games on one unknown level would otherwise print it twice.
		System.Collections.Generic.List<int> reported = new System.Collections.Generic.List<int>();
		string unmappedArt = null;
		// Card 88f87ba2: the same report for a DIFFICULTY this build does not know -- the other
		// half of what a listing carries off the wire. Read straight off KnownDifficulty, which
		// IS the boundary validator the carousel itself reads, so this is the subject rather
		// than a second copy of it (unlike the art above, whose test lives in EnsureArt).
		System.Collections.Generic.List<int> reportedDiff = new System.Collections.Generic.List<int>();
		string unknownDifficulty = null;
		for (int i = 0; i < games.Count; i++)
		{
			NetGameBrowser.GameEntry g = games[i];
			AddEntry(LevelArt.Title(g.KnownLevel));
			AddEntryEvent(entrySelected);
			EnsureArt(g);
			if (unmappedArtLevels.Contains(g.Level) && !reported.Contains(g.Level))
			{
				reported.Add(g.Level);
				unmappedArt = (unmappedArt == null) ? LevelName(g) : unmappedArt + "," + LevelName(g);
			}
			if (!g.KnownDifficulty.HasValue && !reportedDiff.Contains(g.Difficulty))
			{
				reportedDiff.Add(g.Difficulty);
				unknownDifficulty = (unknownDifficulty == null)
					? g.Difficulty.ToString()
					: unknownDifficulty + "," + g.Difficulty;
			}
		}
		// Silent in the normal case: a public lobby rebuilds whenever a game opens or closes,
		// and every level being mapped is the answer every time. The line is a report of the
		// exceptional case, so it doubles as the probe's positive control (`entries=` can only
		// come from a rebuild that walked the entries) without logging on the player's path.
		if (unmappedArt != null || unknownDifficulty != null)
		{
			System.Console.WriteLine("[gamebrowser] rebuilt entries=" + games.Count
				+ (unmappedArt != null ? " unmappedArt=" + unmappedArt : "")
				+ (unknownDifficulty != null ? " unknownDifficulty=" + unknownDifficulty : ""));
		}
		if (selectedCode != null)
		{
			for (int i = 0; i < games.Count; i++)
			{
				if (games[i].Code == selectedCode)
				{
					selectedEntry = i;
					break;
				}
			}
		}
		SyncCarouselToSelection();
	}

	private void entrySelected(MenuSub1 sender)
	{
		if (selectedEntry >= 0 && selectedEntry < games.Count && this.OnGameSelected != null)
		{
			this.OnGameSelected(games[selectedEntry].Code);
		}
	}

	// Card 0d166364: a null ScreenshotPath is EXPECTED here and is not an error. This entry's
	// level came off the wire as an int from a stranger's build, so it can be a level with no
	// bundled art (Tutorial, a demo) or not in our Levels enum at all -- and a listed game must
	// always have something to draw. Handled BEFORE the try so we never hand Content.Load a
	// null, whose throw the catch below would silently absorb. The level is recorded in
	// unmappedArtLevels, which is what RefreshGames reports; the player just sees the default.
	private void EnsureArt(NetGameBrowser.GameEntry g)
	{
		if (artCache.ContainsKey(g.Level))
		{
			return;
		}
		// A level not in our enum at all has no art by definition; one that IS in it may still
		// have no carousel slot (Tutorial, the demos). Both are the same answer here.
		Levels? known = g.KnownLevel;
		string path = known.HasValue ? LevelArt.ScreenshotPath(known.Value) : null;
		if (path == null)
		{
			unmappedArtLevels.Add(g.Level);
			artCache[g.Level] = fallbackArt;
			return;
		}
		Texture2D t;
		try
		{
			t = Content.Load<Texture2D>(path);
		}
		catch (System.Exception)
		{
			t = fallbackArt;
		}
		artCache[g.Level] = t ?? fallbackArt;
	}

	private Texture2D ArtFor(NetGameBrowser.GameEntry g)
	{
		if (artCache.TryGetValue(g.Level, out Texture2D t))
		{
			return t;
		}
		EnsureArt(g);
		return artCache[g.Level];
	}

	// How a listed game's level reads in the diagnostic line: its enum NAME when this build
	// knows the value, otherwise the bare int -- which is exactly what a reader wants to see
	// for a level from a newer peer's build.
	private static string LevelName(NetGameBrowser.GameEntry g)
	{
		Levels? known = g.KnownLevel;
		return known.HasValue ? known.Value.ToString() : g.Level.ToString();
	}

	// Mirrors SubMenuLevelChoice's entry geometry exactly (same fly-in/scale/alpha), drawing
	// the game's level art instead of a level screenshot.
	protected override void DrawEntryAt(int entry, float step)
	{
		if (step > 1f || step < 0f || entry < 0 || entry >= games.Count)
		{
			return;
		}
		Texture2D art = ArtFor(games[entry]);
		if (art == null)
		{
			return;
		}
		step *= 2f;
		if (step > 1f)
		{
			step -= 1f;
			float num = MathHelper.Lerp(1f, 0f, step);
			Vector2 position = new Vector2(MathHelper.Lerp(800f, 400f, num), 200f);
			Color color = new Color(new Vector4(1f, 1f, 1f, MathHelper.Lerp(0.3f, 1f, num)));
			float num2 = MathHelper.Lerp(0.25f, 0.4f, num);
			float num3 = 800f / (float)art.Width;
			float num4 = 600f / (float)art.Height;
			Vector2 scale = new Vector2(num3 * num2, num4 * num2);
			base.SpriteBatch.BlendMode = (SpriteBlendMode)0;
			base.SpriteBatch.Draw(art, position, 0f, scale, center: true, color);
			base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
			RecordEntryHit(entry, position, 800f * num2, 600f * num2);
		}
		else
		{
			float num5 = MathHelper.Lerp(0f, 1f, step);
			Vector2 position = new Vector2(MathHelper.Lerp(0f, 400f, num5), 200f);
			Color color = new Color(new Vector4(1f, 1f, 1f, MathHelper.Lerp(0.3f, 1f, num5)));
			float num6 = MathHelper.Lerp(0.25f, 0.4f, num5);
			float num7 = 800f / (float)art.Width;
			float num8 = 600f / (float)art.Height;
			Vector2 scale = new Vector2(num7 * num6, num8 * num6);
			base.SpriteBatch.BlendMode = (SpriteBlendMode)0;
			base.SpriteBatch.Draw(art, position, 0f, scale, center: true, color);
			base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
			RecordEntryHit(entry, position, 800f * num6, 600f * num6);
		}
	}

	protected override void DrawCarouselOverlay(GameTime gameTime)
	{
		base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
		if (games.Count == 0)
		{
			string msg = NetGameBrowser.FailText.Length > 0
				? NetGameBrowser.FailText
				: "Searching for open games...\n\nStart a game with 'Allow Online Joins' on\nand a friend can drop into yours.";
			Vector2 c = font.MeasureString(msg) / 2f;
			base.SpriteBatch.DrawString(font, msg, new Vector2(400f, 260f), Color.AliceBlue, 0f, c, 0.7f, (SpriteEffects)0, 0f);
			return;
		}
		int sel = selectedEntry;
		if (sel < 0 || sel >= games.Count)
		{
			sel = 0;
		}
		NetGameBrowser.GameEntry g = games[sel];
		string title = LevelArt.Title(g.KnownLevel);
		Vector2 tc = font.MeasureString(title) / 2f;
		base.SpriteBatch.DrawMetalString(font, title, new Vector2(400f, 50f), Color.AliceBlue, 0f, tc, 1f);

		string ping = g.PingMs < 0 ? "--" : g.PingMs + " ms";
		// Denominator is the real roster width, not a hard-coded 2: card 4d904410 relaxed a
		// listed game to ANY free seat, so a couch host genuinely advertises 1..3 of 4 taken.
		string details = "Difficulty: " + LevelArt.DifficultyName(g.KnownDifficulty)
			+ "     Players: " + g.Players + "/" + Oracle.MaxPlayers + "     Ping: " + ping;
		Vector2 dc = font.MeasureString(details) / 2f;
		dc.Y = 0f;
		base.SpriteBatch.DrawString(font, details, new Vector2(400f, 340f), Color.AliceBlue, 0f, dc, 0.7f, (SpriteEffects)0, 0f);

		string codeLine = "Room code: " + g.Code;
		Vector2 cc = font.MeasureString(codeLine) / 2f;
		cc.Y = 0f;
		base.SpriteBatch.DrawString(font, codeLine, new Vector2(400f, 375f), Color.Gold, 0f, cc, 0.75f, (SpriteEffects)0, 0f);
	}
}
