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

	private readonly Dictionary<Levels, Texture2D> artCache = new Dictionary<Levels, Texture2D>();

	private Texture2D fallbackArt;

	public SubMenuOnlineGames(Game game)
		: base(game)
	{
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		fallbackArt = Content.Load<Texture2D>("GFX/Screenshots/level1empty");
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
		for (int i = 0; i < games.Count; i++)
		{
			AddEntry(LevelArt.Title((Levels)games[i].Level));
			AddEntryEvent(entrySelected);
			EnsureArt((Levels)games[i].Level);
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

	private void EnsureArt(Levels level)
	{
		if (artCache.ContainsKey(level))
		{
			return;
		}
		Texture2D t;
		try
		{
			t = Content.Load<Texture2D>(LevelArt.ScreenshotPath(level));
		}
		catch (System.Exception)
		{
			t = fallbackArt;
		}
		artCache[level] = t ?? fallbackArt;
	}

	private Texture2D ArtFor(Levels level)
	{
		if (artCache.TryGetValue(level, out Texture2D t))
		{
			return t;
		}
		EnsureArt(level);
		return artCache[level];
	}

	// Mirrors SubMenuLevelChoice's entry geometry exactly (same fly-in/scale/alpha), drawing
	// the game's level art instead of a level screenshot.
	protected override void DrawEntryAt(int entry, float step)
	{
		if (step > 1f || step < 0f || entry < 0 || entry >= games.Count)
		{
			return;
		}
		Texture2D art = ArtFor((Levels)games[entry].Level);
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
		string title = LevelArt.Title((Levels)g.Level);
		Vector2 tc = font.MeasureString(title) / 2f;
		base.SpriteBatch.DrawMetalString(font, title, new Vector2(400f, 50f), Color.AliceBlue, 0f, tc, 1f);

		string ping = g.PingMs < 0 ? "--" : g.PingMs + " ms";
		// Denominator is the real roster width, not a hard-coded 2: card 4d904410 relaxed a
		// listed game to ANY free seat, so a couch host genuinely advertises 1..3 of 4 taken.
		string details = "Difficulty: " + LevelArt.DifficultyName(g.Difficulty)
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
