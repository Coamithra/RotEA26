using System;
using EvilAliens.Constants;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using EvilAliensWeb.Compat;

namespace EvilAliens;

// The main-menu header + its richly-framed row list (Stage 13 reskin). The name
// is kept for MenuScene's sake; the old grey title + separate evilskull are
// retired — the hi-res "title-revenged" logo already carries the alien/UFO
// mascot. This class OWNS the main menu's look: it overrides DrawMenu to draw
// the title and then a custom, framed row list (angular HUD frames, green idle /
// violet selected, a ► pointer), so only the MAIN menu gets the heavy chrome —
// the shared base (MenuSub1.DrawMenu) keeps its lighter palette-only treatment
// for the option/cheat sub-menus, whose entries vary too much in width to frame.
//
// Everything draws in 800x600 design space (RenderScale.Matrix scales it up to
// the menu render target) and is built from the solid-white 10x10 `blank` sprite
// (lines/rects) + the white `pointer` triangle, tinted at draw time so they pick
// up the scene bloom like the rest of the menu.
internal class MenuSubWithSkull : MenuSub1
{
	private Texture2D title;
	private Texture2D blank;
	private Texture2D pointer;

	// Vertical offset of the row list (design space). Bumped up from the old 75 because
	// the reskinned title card is taller than the original, which left the rows crowding
	// it up top with dead space below EXIT. DrawRows AND GetListCentre (the ring centre)
	// both key off this so they stay in lockstep.
	private const float RowsYOffset = 96f;

	// Card 45c16ef6: with everything unlocked the eight rows no longer fitted and EXIT was
	// drawn clipped off the bottom edge. The 2008 layout only ever grows DOWNWARD -- curY0
	// keys off the FULL entry count, which UNLOCKING does not change (a locked row is merely
	// skipped), so every newly visible row adds LineSpacing at the bottom and moves nothing
	// up. RowsStartY therefore shifts the whole block up by exactly its overflow past
	// RowsBottomLimit. Measured at the shipped LineSpacing of 45 and the 8-entry main menu:
	// **<=7 visible rows do not move at all** (7 rows bottom out at 564.5, inside the limit),
	// 8 shift up 39.5px and ride ~10 design-px into the title's bottom banner (the card comment
	// explicitly allows a little overlap), and the 9+ case reachable only with debug menus
	// keeps shifting under the same one rule rather than needing a second.
	//
	// The limit is the project's own safe-area bottom: the tips band starts ~40px above it, but
	// the tips sit at the far LEFT and RIGHT (x<=146 / x>=640) while the frames span x~218-582,
	// so they never collide -- a stricter limit would only buy title overlap nobody asked for.
	// Read off General.SafeZone rather than written as its value, so moving the safe zone moves
	// this with it instead of leaving a comment that used to be true.
	private static float RowsBottomLimit => (float)(General.SafeZone).Bottom;

	// Degenerate guard for a hypothetical 12+ entry menu: stop shifting rather than walk the
	// list off the TOP of the screen. Nothing reachable today hits it.
	private const float RowsTopLimit = 10f;

	// Perf batch 2: the selected row's glow ring (constant offsets, r=4.5) and the octagon
	// outline's 8-point array were allocated fresh every frame — the outline one per visible
	// row. Hoist the constant ring; reuse a scratch array for the per-row outline points.
	private static readonly Vector2[] SelectionGlowRing = BuildGlowRing(4.5f);

	private readonly Vector2[] frameOutlinePts = new Vector2[8];

	// Perf follow-up (card febc71de): the chamfered-octagon FILL used to submit ~one 1px-tall strip per
	// design row (~frameH strips) PER ROW EVERY FRAME on an idle screen. The shape is identical for every
	// row and only changes when the frame size does (a menu unlock changes the widest visible label), so it
	// is rasterised ONCE into a white alpha mask and each row draws it as a SINGLE tinted quad — the
	// per-selection fill colour is applied as the draw tint (white*tint = the fill colour, straight alpha),
	// so idle and selected rows reuse the one mask. Any chamfer-edge softening from the single scaled quad
	// is hidden under DrawFrameOutline, drawn crisp on top. Rebuilt (disposing the old) only on a size
	// change, so it's effectively built once; the last mask leaks on scene teardown, consistent with the
	// menu's other GPU resources (tracked by the separate "unload per-scene managers" backlog card).
	private Texture2D fillMask;
	private int fillMaskW = -1;
	private int fillMaskH = -1;

	private static Vector2[] BuildGlowRing(float r)
	{
		float d = r * 0.7071f;
		return new Vector2[8]
		{
			new Vector2(r, 0f), new Vector2(0f - r, 0f), new Vector2(0f, r), new Vector2(0f, 0f - r),
			new Vector2(d, d), new Vector2(0f - d, d), new Vector2(d, 0f - d), new Vector2(0f - d, 0f - d)
		};
	}

	public MenuSubWithSkull(Game game)
		: base(game)
	{
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		// Straight-alpha title + the straight NonPremultiplied blend = no conversion needed
		// (the chroma-keyed logo is straight, like the rest of the content now).
		title = Content.Load<Texture2D>("GFX/Menu/title-revenged");
		blank = Content.Load<Texture2D>("GFX/Menu/blank");
		pointer = Content.Load<Texture2D>("GFX/Menu/pointer");
	}

	public override void DrawMenu(GameTime gameTime, float yoffset)
	{
		base.SpriteBatch.BlendMode = (SpriteBlendMode)1;

		// Arcade marquee feel: a gentle scale "breathe" plus a subtle rotational
		// wobble at a detuned frequency (so it sways rather than ticks), and a tiny
		// vertical bob. Driven off wall-clock game time; pivots about the slot centre.
		float t = (float)gameTime.TotalGameTime.TotalSeconds;
		const float TwoPi = 6.28318548f;
		float pulse = 1f + 0.018f * (float)Math.Sin(TwoPi * 0.9f * t);   // ~+/-1.8% breathe
		float wobble = 0.0105f * (float)Math.Sin(TwoPi * 0.55f * t);     // ~+/-0.6 deg sway
		float bob = 1.5f * (float)Math.Sin(TwoPi * 0.4f * t);            // smooth float — integer-rounding made it snap/jerk

		// AspectFit the 2.5:1 logo, undistorted, into a horizontally-centred 540x210 slot
		// near the top of the 800x600 design surface; pulse/wobble pivot about its centre.
		float fit = Math.Min(540f / (float)title.LogicalWidth(), 210f / (float)title.LogicalHeight());
		Vector2 titleCentre = new Vector2(400f, 135f + bob);
		base.SpriteBatch.Draw(title, titleCentre, wobble, fit * pulse, center: true, Color.White);

		DrawRows(gameTime, RowsYOffset);
	}

	// The framed main-menu row list. Mirrors the base layout maths (vertical
	// centring from the FULL entry count, locked entries skipped without leaving a
	// gap) but centres each row on x=400 inside an equal-width angular frame.
	private void DrawRows(GameTime gameTime, float yoffset)
	{
		float t = (float)gameTime.TotalGameTime.TotalSeconds;
		const float TwoPi = 6.28318548f;
		float cx = 400f;

		// Equal-width frames sized to the widest visible label (+ padding), clamped.
		float maxW = 0f;
		for (int i = 0; i < menuEntries.Count; i++)
		{
			if (IsVisible(i))
				maxW = Math.Max(maxW, font.MeasureString(menuEntries[i]).X);
		}
		float frameW = Math.Min(520f, Math.Max(360f, maxW + 96f));
		float frameH = RowFrameHeight();

		// Vertical centring reference: the CAP band centre (cap-top..baseline), not the
		// full line box. Centring the line box (LineSpacing/2) leaves the visible caps
		// sitting high because of the empty ascender/descender leading; for text you want
		// the capitals centred, with descenders hanging below as normal (they still clear
		// the frame). Read it off a flat-topped capital's design-space cropping.
		float capCentreY = font.LineSpacing / 2f;
		foreach (char rc in "EXATHIS")
		{
			if (font.Glyphs.ContainsKey(rc))
			{
				Rectangle crop = font.Glyphs[rc].Cropping;
				capCentreY = crop.Y + crop.Height / 2f;
				break;
			}
		}

		float curY = RowsStartY(yoffset);
		for (int i = 0; i < menuEntries.Count; i++)
		{
			if (!IsVisible(i))
				continue;

			bool selected = (i == selectedEntry);
			string label = menuEntries[i];
			float textW = font.MeasureString(label).X;

			// The frame, glow and pointer carry the selection, so the text stays a FIXED
			// size inside its panel — a scale "heartbeat" pushed the tall caps out of the
			// frame. pulse01 still breathes the frame/aura glow for a sense of life.
			float scale = 1f;
			float pulse01 = brainPulsate.Evaluate(MyMath.Mod(t / 2f, 1f));

			Vector2 rowCentre = new Vector2(cx, curY);
			// Mouse hit box = the whole frame, so a click anywhere in the panel selects it.
			RecordEntryHit(i, rowCentre, frameW, frameH);
			DrawFrameFill(rowCentre, frameW, frameH, selected);

			// Text — centred in the frame. No drop shadow: the panel fill already lifts
			// the text off the background, and a shadow just smeared onto the frame.
			Vector2 origin = new Vector2(textW / 2f, capCentreY);
			Color textColor = selected ? MenuTheme.Selected : MenuTheme.Idle;
			if (selected)
			{
				Color aura = MenuTheme.WithAlpha(MenuTheme.Glow, (int)(70f + 50f * pulse01));
				foreach (Vector2 off in SelectionGlowRing)
					base.SpriteBatch.DrawString(font, label, rowCentre + off, aura, 0f, origin, scale, (SpriteEffects)0, 0f);
			}
			// Stage 13: the entry text gets the chrome sheen; the frame fill, the selection
			// glow ring (above) and the frame outline (below) stay as its setting. Per-entry
			// RT composite => each row's sheen is local to itself regardless of height.
			base.SpriteBatch.DrawMetalStringCached(label, rowCentre, textColor, 0f, origin, scale, t);

			// Frame outline LAST, on top of the text + glow, so the edges stay crisp.
			DrawFrameOutline(rowCentre, frameW, frameH, selected, pulse01);

			// ► pointer to the left of the selected frame, bobbing gently inward.
			if (selected)
			{
				float bob = 3f * (0.5f + 0.5f * (float)Math.Sin(TwoPi * 1.4f * t));
				float ptrH = frameH * 0.62f;
				Vector2 ptrPos = new Vector2(cx - frameW / 2f - 26f - bob, curY);
				base.SpriteBatch.Draw(pointer, ptrPos, 0f, ptrH / pointer.LogicalHeight(), center: true, MenuTheme.FrameSelected);
			}

			curY += font.LineSpacing;
		}
	}

	private bool IsVisible(int i)
	{
		return !unLockableDataEntries[i].isUnlockable
			|| Unlockables.GetInstance().IsUnlocked(unLockableDataEntries[i].item);
	}

	private int VisibleCount()
	{
		int visible = 0;
		for (int i = 0; i < menuEntries.Count; i++)
		{
			if (IsVisible(i))
				visible++;
		}
		return visible;
	}

	private float RowFrameHeight()
	{
		return Math.Min(52f, font.LineSpacing * 0.82f);
	}

	// The FIRST visible row's centre y, in 800x600 design space. Owns the whole vertical
	// placement of the row list: the 2008 centring reference (the FULL entry count, not the
	// visible one) plus the card-45c16ef6 fit-shift described at RowsBottomLimit. DrawRows and
	// GetListCentre BOTH go through it -- they must stay in lockstep or the HUD ring parks
	// somewhere the rows no longer are. Callers must have loaded `font` first.
	private float RowsStartY(float yoffset)
	{
		float y0 = yoffset + 300f - (float)(font.LineSpacing * menuEntries.Count) / 3f;
		int visible = VisibleCount();
		if (visible <= 0)
			return y0;
		float half = RowFrameHeight() / 2f;
		float overflow = (y0 + (float)(visible - 1) * font.LineSpacing + half) - RowsBottomLimit;
		if (overflow <= 0f)
			return y0;
		// Never shift so far that the top frame edge leaves the screen (degenerate case only).
		float headroom = Math.Max(0f, y0 - half - RowsTopLimit);
		return y0 - Math.Min(overflow, headroom);
	}

	// The vertical centre of the visible row list, in 800x600 design space, computed the
	// same way DrawRows lays them out (yoffset RowsYOffset=96, centred from the FULL entry count with
	// locked entries skipped). MenuScene centres the HUD ring on this so the reticle tracks
	// the menu as rows unlock (Challenges/Awardments/Cheats change the visible count, and
	// thus the centre). Falls back to a sane value before content (font) has loaded.
	public override Vector2 GetListCentre()
	{
		if (font == null)
			return new Vector2(400f, 384f);
		int visible = VisibleCount();
		float curY0 = RowsStartY(RowsYOffset);
		float centreY = curY0 + ((visible > 0) ? (visible - 1) / 2f * font.LineSpacing : 0f);
		return new Vector2(400f, centreY);
	}

	// The angular (chamfered-octagon) HUD frame is drawn in TWO passes so the crisp
	// outline lands AFTER the row text: the fill goes down first (a dark backing the
	// text reads against), then the text + its glow, then DrawFrameOutline on top — so
	// the selected row's glow can't bleed over the frame edges and smear them.

	// Pass 1 (before text): fill the FULL octagon, INCLUDING the chamfered corners, so the
	// bright background can't show through the cut corners (the old 3-rect fill was the
	// octagon MINUS its corners). Cached as a white alpha mask (see EnsureFillMask + the field
	// comment) and drawn as ONE tinted quad per row: white*fill = the fill colour under straight
	// alpha — the same fill the old per-strip loop produced, to within the sub-pixel centering + linear
	// sampling of one scaled quad (hidden under the outline). Both selection states reuse the mask.
	private void DrawFrameFill(Vector2 centre, float w, float h, bool selected)
	{
		int mw = Math.Max(1, (int)Math.Round(w));
		int mh = Math.Max(1, (int)Math.Round(h));
		EnsureFillMask(mw, mh);
		Color fill = selected ? new Color(46, 18, 80, 150) : MenuTheme.FrameFill;
		base.SpriteBatch.Draw(fillMask, centre, 0f, 1f, center: true, fill);
	}

	// Build (once, or when the frame size changes) a white octagon alpha mask matching the old fill's
	// chamfer profile: opaque white inside, transparent outside, 1 design-px per texel and the SAME rowW
	// math as the retired strip loop, so the filled shape is identical. Centre-independent (chamfer keyed
	// off the mask's own centre), so one mask serves every row regardless of its screen Y.
	private void EnsureFillMask(int w, int h)
	{
		if (fillMask != null && !((GraphicsResource)fillMask).IsDisposed && fillMaskW == w && fillMaskH == h)
			return;
		if (fillMask != null && !((GraphicsResource)fillMask).IsDisposed)
			((GraphicsResource)fillMask).Dispose();

		const float c = 12f;
		float hh = h / 2f;
		Color[] data = new Color[w * h];                                  // default(Color) = transparent
		for (int y = 0; y < h; y++)
		{
			float ad = Math.Abs((y + 0.5f) - hh);                        // distance from mask centre
			float rowW = (ad > hh - c) ? w - 2f * (ad - (hh - c)) : w;    // chamfer the two ends
			if (rowW < 1f)                                                // old strip loop's exact tip test
				continue;
			int fillW = (int)Math.Round(rowW);                           // width = FillRect's Round(rowW)
			if (fillW > w)
				fillW = w;
			int x0 = (w - fillW) / 2;                                     // centred, matching FillRect
			int rowBase = y * w;
			for (int x = x0; x < x0 + fillW; x++)
				data[rowBase + x] = Color.White;
		}
		fillMask = new Texture2D(base.GraphicsDevice, w, h, false, SurfaceFormat.Color);
		fillMask.SetData(data);
		fillMaskW = w;
		fillMaskH = h;
	}

	// Pass 2 (after text): the octagon outline = 8 line segments; the selected frame gets
	// a brighter violet stroke plus a dim wider "glow" pass under it. Drawn last so the
	// frame edges stay crisp on top of the text glow.
	private void DrawFrameOutline(Vector2 centre, float w, float h, bool selected, float pulse01)
	{
		float hw = w / 2f, hh = h / 2f, c = 12f;
		Vector2 P(float x, float y) => centre + new Vector2(x, y);
		// Perf batch 2: reuse a scratch array instead of allocating one octagon per row per frame.
		Vector2[] o = frameOutlinePts;
		o[0] = P(-hw + c, -hh);
		o[1] = P(hw - c, -hh);
		o[2] = P(hw, -hh + c);
		o[3] = P(hw, hh - c);
		o[4] = P(hw - c, hh);
		o[5] = P(-hw + c, hh);
		o[6] = P(-hw, hh - c);
		o[7] = P(-hw, -hh + c);

		if (selected)
		{
			// Dim, wide under-stroke for glow (already soft — no feather), then the
			// bright crisp stroke (feathered for AA).
			Color glow = MenuTheme.WithAlpha(MenuTheme.FrameSelected, (int)(60f + 50f * pulse01));
			for (int k = 0; k < 8; k++)
				DrawLine(o[k], o[(k + 1) % 8], 6f, glow, feather: false);
			for (int k = 0; k < 8; k++)
				DrawLine(o[k], o[(k + 1) % 8], 2.5f, MenuTheme.FrameSelected);
		}
		else
		{
			for (int k = 0; k < 8; k++)
				DrawLine(o[k], o[(k + 1) % 8], 1.5f, MenuTheme.FrameIdle);
		}
	}

	// Exact mitre extension for THIS octagon's corners. Every vertex of the chamfered
	// rectangle is a 135-degree interior angle (a 90-degree corner cut once at 45), so to
	// make two centred stroke quads meet in a clean mitre — no gap, no overshoot — each
	// edge extends past the vertex by (thickness/2) * cot(67.5 deg) = thickness * 0.2071.
	// The old value (thickness * 0.5) is the 90-degree-corner mitre, which OVER-extends
	// these shallower corners by 0.29*thickness on every end — the funky bumps the edges
	// showed (worst on the 6px selected glow: a ~1.8px blob at each of the 8 corners).
	private const float Miter135 = 0.20710678f;

	// A source rect one texel INSIDE `blank`'s logical bounds. `blank` is a padded dxt
	// (transparent mult-of-4 pad on the bottom/right), and a whole-texture draw clamped to
	// LogicalBounds still lets LINEAR filtering sample across the logical edge into the
	// transparent pad — a half-texel bleed. On a normal sprite that's an invisible fringe,
	// but DrawLine stretches this 10x10 sprite ~50x into each frame edge, so the bleed
	// becomes a visible fade running along the stroke (the "errant fading" on the selected
	// frame). blank is uniform white, so sampling a 1px-inset interior stretches identically
	// with zero pad bleed at any pad size (incl. --padtest 0's minimal mult-of-4 pad).
	private Rectangle InteriorFill(Texture2D t)
	{
		int w = Math.Max(1, t.LogicalWidth() - 2);
		int h = Math.Max(1, t.LogicalHeight() - 2);
		return new Rectangle(1, 1, w, h);
	}

	// A line segment a->b of the given thickness, drawn from the white `blank`
	// stretched + rotated, offset half a stroke so the path is centred on the line.
	// `feather` lays a slightly wider, dim pass under the crisp core so the aliased 45
	// chamfer diagonals get a soft 1px fringe (cheap AA — the menu RT has no MSAA).
	private void DrawLine(Vector2 a, Vector2 b, float thickness, Color color, bool feather = true)
	{
		Vector2 delta = b - a;
		float len = delta.Length();
		if (len < 0.01f)
			return;
		Vector2 dir = delta / len;
		float ang = (float)Math.Atan2(dir.Y, dir.X);
		float ext = thickness * Miter135;
		Vector2 perp = new Vector2(-dir.Y, dir.X);
		float fullLen = len + 2f * ext;
		// Sample blank's interior (not its LogicalBounds edge) so the stretch never bleeds
		// the transparent pad along the stroke; blank is uniform, so this is pixel-identical.
		Rectangle src = InteriorFill(blank);
		if (feather)
		{
			// One soft, ~1px-wider underlay at reduced alpha — feathers the long edges.
			float fThick = thickness + 1.3f;
			Vector2 fpos = a - dir * ext - perp * (fThick / 2f);
			Color fcol = MenuTheme.WithAlpha(color, (int)(color.A * 0.4f));
			base.SpriteBatch.Draw(blank, src, fpos, ang, new Vector2(fullLen / src.Width, fThick / src.Height), Vector2.Zero, fcol);
		}
		Vector2 pos = a - dir * ext - perp * (thickness / 2f);
		base.SpriteBatch.Draw(blank, src, pos, ang, new Vector2(fullLen / src.Width, thickness / src.Height), Vector2.Zero, color);
	}
}
