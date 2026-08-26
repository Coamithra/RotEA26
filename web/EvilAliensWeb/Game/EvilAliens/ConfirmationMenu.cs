using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class ConfirmationMenu : MenuSub1
{
	private ContentManager content;

	private ComponentBin collectionHelper;

	private Texture2D blankTexture;

	private string text;

	// Max design-space width (of 800) the prompt may occupy before it's scaled down to fit,
	// leaving a ~20px margin each side.
	private const float MaxTextWidth = 760f;

	// The prompt is drawn with its origin lifted this far below the block's centre, which is what
	// leaves room for the menu entries underneath it. Baked into the origin expression below.
	private const float PromptLift = 60f;

	// Clear space kept between the prompt's last line and the first menu entry.
	private const float PromptGap = 24f;

	// The design-space band the prompt+entries composite may occupy when it has to be relaid out.
	// Below `BandBottom` sit the "back"/"select" button tips (`MenuScene.drawButtonTips`).
	private const float BandTop = 48f;

	private const float BandBottom = 524f;

	// The y the base menu entries are drawn at (`DrawMenu`'s own extra offset).
	private const float EntryOffset = 75f;

	// Floor on the prompt's shrink -- an unreadable prompt is worse than a tight one.
	private const float MinPromptScale = 0.35f;

	// Last reported layout, so the diagnostic below prints on a change rather than per frame.
	// Compared FIELD BY FIELD, never as a formatted string -- `ReportBackTipOverlap`, whose idiom
	// this is, spells out why: this runs in Draw, every frame, and formatting a line only to throw
	// it away is exactly the per-frame garbage `MenuSub1`'s perf pass removed.
	private int lastLines = -1;

	private int lastEntries = -1;

	private bool lastRelaid;

	private int lastOverPx = int.MinValue;

	private int lastBottom = int.MinValue;

	public ConfirmationMenu(Game game, string text)
		: base(game)
	{
		content = ServiceHelper.Get<IContentManagerService>().ContentManager;
		collectionHelper = ServiceHelper.Get<IComponentBinService>().ComponentBin;
		base.DrawOrder = 2000;
		this.text = text;
	}

	// Card 11.4: the net lobby status menu re-texts one instance per phase (room code /
	// waiting / failure notices).
	public void SetText(string newText)
	{
		text = newText;
	}

	private void ForgetReportedLayout()
	{
		lastLines = -1;
		lastEntries = -1;
		lastRelaid = false;
		lastOverPx = int.MinValue;
		lastBottom = int.MinValue;
	}

	public override void Reset()
	{
		base.Reset();
		// A pooled menu shown, hidden and re-shown must report its layout again -- otherwise the
		// second showing is silent and a probe (or a dev) reads that as the reporter being dead.
		// NOT done in SetText: the net lobby panel re-texts every tick with the live roster, so
		// clearing there would print the line every frame -- the very cost the field-wise compare
		// below exists to avoid.
		ForgetReportedLayout();
	}

	public override void Initialize()
	{
		base.Initialize();
		selectedEntry = 1;
	}

	protected override void LoadContent()
	{
		blankTexture = content.Load<Texture2D>("GFX/Game/blank");
		base.LoadContent();
	}

	public static PausedScene newPausedScene(ComponentBin collection, Game game)
	{
		PausedScene pausedScene = collection.Recycle<PausedScene>();
		if (pausedScene == null)
		{
			pausedScene = new PausedScene(game);
		}
		return pausedScene;
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);
	}

	public override void DrawMenu(GameTime gameTime, float yoffset)
	{
		base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
		Vector2 size = font.MeasureString(text);
		// The prompt is drawn as one centred block; a long message (e.g. "Are you sure you want
		// to exit this game session?") is wider than the 800px design space and runs off both
		// edges. Shrink it to fit within a small margin. Scaling around the centred origin keeps
		// it centred; scale <= 1 so short prompts are unaffected.
		float scale = (size.X > MaxTextWidth) ? MaxTextWidth / size.X : 1f;
		float promptY = 300f;
		bool relaid = false;
		// Card bec47239: the prompt and the entries were laid out from two INDEPENDENT fixed
		// anchors (this block at y=300, the rows at y=300+EntryOffset), so a prompt taller than
		// the gap between them simply drew through the rows -- the four-seat online lobby roster
		// put "Start when your crew is aboard!" straight on top of Cancel. Measure the prompt and,
		// only when the default layout WOULD collide, lay the two out as one composite: shrink the
		// prompt further if the pair cannot fit the band, then push the rows below it. Every
		// pre-existing (short) prompt takes the early-out and is pixel-identical to before.
		if (PromptBottom(size, scale, promptY) + PromptGap > EntriesTop(yoffset + EntryOffset))
		{
			relaid = true;
			float entriesHeight = (float)font.LineSpacing * (float)menuEntries.Count;
			if (size.Y > 0f)
			{
				float fits = (BandBottom - BandTop - PromptGap - entriesHeight) / size.Y;
				if (fits < scale)
				{
					// Floored so a pathological entry count cannot scale the prompt to nothing --
					// but never ABOVE the width clamp, or a prompt already narrowed to fit the
					// 800px design space would be widened back off both edges.
					scale = Math.Max(fits, Math.Min(scale, MinPromptScale));
				}
			}
			float composite = size.Y * scale + PromptGap + entriesHeight;
			float top = (BandTop + BandBottom) / 2f - composite / 2f;
			// Put the prompt block's centre at `top + halfHeight`; the origin's PromptLift is
			// what the draw call subtracts, so add it back here.
			promptY = top + size.Y * scale / 2f + PromptLift * scale;
			// Solve the base layout for the offset that lands the first row under the prompt.
			// Added to the caller's `yoffset` rather than replacing it, so this branch honours an
			// incoming offset exactly as the other one does (no live caller passes a non-zero one
			// -- MenuSub1.Draw is the only path in -- but the two branches disagreeing about it
			// would be a trap for the first one that did).
			yoffset += top + size.Y * scale + PromptGap - EntriesTop(0f);
		}
		else
		{
			yoffset += EntryOffset;
		}
		base.DrawMenu(gameTime, yoffset);
		Vector2 val = size / 2f + new Vector2(0f, PromptLift);
		base.SpriteBatch.DrawMetalString(font, text, new Vector2(400f, promptY), Color.AliceBlue, 0f, val, scale);
		ReportLayout(relaid, PromptBottom(size, scale, promptY));
	}

	// Design-space Y of the bottom of the prompt block drawn at `promptY` with `scale`.
	private static float PromptBottom(Vector2 size, float scale, float promptY)
	{
		return promptY + (size.Y / 2f - PromptLift) * scale;
	}

	// Design-space Y of the top of the first menu row the base `DrawMenu(_, yoffset)` WOULD draw.
	// This is a model of that layout and is used only to DECIDE whether to relayout -- never to
	// report the result, which is measured off the rows the pass really recorded (see
	// ReportLayout). It mirrors the base's non-scrolling branch: rows start at `origin.Y`, the
	// list is lifted by a third of its height, and each row is centred on its LineSpacing. A
	// ConfirmationMenu never scrolls, so the scrolling branch is not modelled; `menuEntries.Count`
	// rather than the visible count is what the base's own `curY0` uses.
	private float EntriesTop(float yoffset)
	{
		return yoffset + ListOrigin.Y - (float)(font.LineSpacing * menuEntries.Count) / 3f - (float)font.LineSpacing / 2f;
	}

	// Reports the property under test -- whether the prompt runs into the rows -- rather than
	// which branch ran, because "prompt overlaps Cancel" and "prompt sits above it" are the same
	// still picture to anything but a human eye.
	//
	// `rowsTop` COMES OFF THE RECORDED HIT BOXES, never off EntriesTop. Deriving it from the same
	// expression that positioned the rows makes `overlap` algebraically constant -- the first
	// version of this line did exactly that and could not print a number on any input, so a
	// model that had stopped mirroring MenuSub1.DrawMenu reproduced the reported bug on screen
	// while the probe stayed green (demonstrated in review). The hit boxes are also what
	// HandleMouse clicks, so this covers the rows being where they can be clicked.
	//
	// `bottom` is the LAST row's recorded bottom -- observed for the same reason, and what says the
	// band clamp bit. A broken clamp leaves the layout non-overlapping and simply draws the rows
	// lower, into the button tips' band, which nothing else here would notice: `[backtip]` is a
	// RECTANGLE test and a centred row's x-range never meets the tip's, so it cannot see a purely
	// vertical push.
	//
	// The non-overlapping case prints too: a diagnostic that only spoke up on the bad branch would
	// pass on a run that never opened the panel this card is about (the `[backtip]` rule).
	private void ReportLayout(bool relaid, float promptBottom)
	{
		if (!TryGetFirstEntryTop(out float rowsTop) || !TryGetLastEntryBottom(out float rowsBottom))
		{
			return;
		}
		int lines = 1;
		for (int i = 0; i < text.Length; i++)
		{
			if (text[i] == '\n')
			{
				lines++;
			}
		}
		float over = promptBottom - rowsTop;
		int overPx = (over > 0f) ? (int)(over + 0.5f) : 0;
		int bottom = (int)(rowsBottom + 0.5f);
		if (lines == lastLines && menuEntries.Count == lastEntries && relaid == lastRelaid
			&& overPx == lastOverPx && bottom == lastBottom)
		{
			return;
		}
		lastLines = lines;
		lastEntries = menuEntries.Count;
		lastRelaid = relaid;
		lastOverPx = overPx;
		lastBottom = bottom;
		Console.WriteLine($"[confirm] lines={lines} entries={menuEntries.Count} layout={(relaid ? "relaid" : "default")} overlap={((overPx > 0) ? overPx.ToString() + "px" : "none")} bottom={bottom}");
	}

	public override void Draw(GameTime gameTime)
	{
		// Stage 10: full-screen darken in 800x600 design space (scaled by RenderScale.Matrix).
		base.SpriteBatch.Draw(blankTexture, new Rectangle(0, 0, 800, 600), new Color((byte)0, (byte)0, (byte)0, (byte)128));
		base.Draw(gameTime);
	}

	internal void Setup(ControlDevice pauser)
	{
		controller = pauser;
	}
}
