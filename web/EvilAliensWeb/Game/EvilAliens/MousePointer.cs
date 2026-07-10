using System;
using EvilAliensWeb.Compat;
using Microsoft.JSInterop;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

// Aiming cursor (card 51276dcd). Two modes, chosen off DrawableGameComponent.Visible:
//   Visible == false  -> "menu": the normal OS arrow (canvas cursor), NO drawn sprite,
//                        no intro animation. Menus and non-aiming scenes.
//   Visible == true   -> "gameplay": play a one-shot scale+rotate intro that introduces
//                        the pointer -> reticle change (OS cursor HIDDEN while the reticle
//                        sprite animates), then hand the pointer off to the CSS reticle
//                        cursor (canvas.style.cursor: url(reticle/<px>.png)) so it is ZERO-LAG
//                        for the rest of the level (no game-loop sprite trailing the mouse).
//                        Exactly one pointer is visible at all times.
// (KNI's BlazorGL never applies Game.IsMouseVisible to the DOM -- its _isMouseHidden flag is
//  dead and Mouse.PlatformSetCursor throws -- so the OS cursor is owned entirely via
//  CursorInterop -> eaCursor CSS; no more Game.IsMouseVisible toggling here.)
public class MousePointer : DrawableGameComponent, IMousePointerService
{
	private InputHandler input;

	private Texture2D texture;

	private SpriteBatchWrapper spriteBatch;

	private Timer showtimer;

	// True once the gameplay intro has finished and we've handed the pointer to the CSS
	// reticle cursor (or immediately, in HWMouse mode). While true we draw NOTHING -- the OS
	// cursor is the reticle. Reset to false each time the gameplay intro (re)starts.
	private bool reticleHandedOff;

	// The cursor-ladder size (window px) currently pushed to JS, or 0 for "none pushed".
	// Re-checked every Update while handed off so a window resize re-picks the bucket.
	private int cursorPx;

	// Set from JS (canvas pointerenter/leave, see wwwroot/index.html). While the intro sprite
	// animates we don't draw it off-canvas (cursor:none only applies over the canvas, so the
	// OS cursor shows off-canvas naturally).
	private static bool pointerOnCanvas = true;

	[JSInvokable("eaPointerOnCanvas")]
	public static void SetPointerOnCanvas(bool onCanvas)
	{
		pointerOnCanvas = onCanvas;
	}

	MousePointer IMousePointerService.MousePointer => this;

	public MousePointer(Game game)
		: base(game)
	{
		showtimer = new Timer(2000f, repeating: false);
		base.VisibleChanged += MousePointer_VisibleChanged;
		base.DrawOrder = 3000;
	}

	private void MousePointer_VisibleChanged(object sender, EventArgs e)
	{
		if (base.Visible)
		{
			EnterGameplay();
		}
		else
		{
			EnterMenu();
		}
	}

	// Menus / non-aiming scenes: plain OS arrow, no sprite, no intro.
	private void EnterMenu()
	{
		reticleHandedOff = false;
		cursorPx = 0;
		showtimer.Stop();
		showtimer.Reset();
		CursorInterop.Set("menu");
	}

	// Start of a keyboard/mouse level: kick off the reticle intro spin, or (HWMouse) skip
	// straight to the plain OS arrow.
	private void EnterGameplay()
	{
		if (Settings.GetInstance().HWMouse)
		{
			// Player opted for the plain OS arrow -- no reticle, no intro.
			reticleHandedOff = true;
			cursorPx = 0;
			showtimer.Stop();
			showtimer.Reset();
			CursorInterop.Set("menu");
			return;
		}
		reticleHandedOff = false;
		cursorPx = 0;
		showtimer.Reset();
		showtimer.Start();
		CursorInterop.Set("hidden");
	}

	public override void Initialize()
	{
		input = ServiceHelper.Get<IInputHandlerService>().InputHandler;
		spriteBatch = ServiceHelper.Get<ISpriteBatchWrapperService>().SpriteBatchWrapper;
		// VisibleChanged only fires on a change; set the initial cursor to match the current
		// state (Game1 sets Visible=false before us, so this boots into "menu").
		if (base.Visible)
		{
			EnterGameplay();
		}
		else
		{
			EnterMenu();
		}
		base.Initialize();
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		texture = ServiceHelper.Get<IContentManagerService>().ContentManager.Load<Texture2D>("GFX/cursor2");
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_006e: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00dc: Unknown result type (might be due to invalid IL or missing references)
		// Post-intro (or HWMouse): the OS/CSS cursor IS the reticle -- draw nothing.
		if (reticleHandedOff)
		{
			return;
		}
		// During the intro the OS cursor is hidden (cursor:none); don't draw the sprite
		// off-canvas or it would clamp at the edge while the real cursor shows there.
		if (!pointerOnCanvas)
		{
			return;
		}
		spriteBatch.BlendMode = (SpriteBlendMode)2;
		Vector2 mousePosition = input.MousePosition;
		mousePosition.X = MathHelper.Clamp(input.MousePosition.X, 0f, 800f);
		mousePosition.Y = MathHelper.Clamp(input.MousePosition.Y, 0f, 600f);
		// Land the intro EXACTLY on the CSS cursor's on-screen size so the sprite -> OS-cursor
		// handoff doesn't pop. The cursor is whichever ladder rung ChooseCursorPx lands on (a
		// fixed window-px image) while the sprite draws at (texture px x design scale x
		// window-per-design), so the end scale is window-size-dependent; recomputed per frame
		// (cheap) so a mid-intro resize is tracked, exactly as the handed-off cursor is.
		float endScale = CssHandoffScale();
		if (showtimer.Active)
		{
			// Normalized counts 1 -> 0: the reticle starts 4x its final size + spinning and
			// settles to endScale / rotation 0, matching the CSS reticle it hands off to.
			float num = MathHelper.SmoothStep(0f, 1f, showtimer.Normalized);
			float scale = endScale * (1f + num * 3f);
			float rotation = num * ((float)Math.PI * 2f) * 1.5f;
			spriteBatch.Draw(texture, mousePosition, rotation, scale, center: true);
		}
		else
		{
			spriteBatch.Draw(texture, mousePosition, 0f, endScale, center: true);
		}
		base.Draw(gameTime);
	}

	// The reticle's size in DESIGN space (800x600). A CSS cursor image is a fixed pixel size,
	// but the game letterbox-upscales design space to the window -- so a fixed cursor is only
	// correctly sized at one window size and reads small on a big monitor (the bug this
	// replaced). Holding a design-space size instead makes the reticle occupy the same
	// fraction of the play field everywhere. 30 is the original 26px art plus the "a bit
	// bigger" it wanted; override live with ?reticlesize=<designpx>.
	private const float DefaultReticleDesignPx = 30f;

	// The cursor ladder built by tools/cursor/build_cursor.py (wwwroot/reticle/<px>.png).
	// Keep in sync with its SIZES: `range(24, 97, 8)`.
	private const int CursorPxStep = 8;
	private const int MinCursorPx = 24;
	private const int MaxCursorPx = 96;

	// On-screen px per design px: the UNCAPPED letterbox fit (WindowDestRect height / design
	// height) -- NOT RenderScale.Scale, which is capped at MaxHeight and so diverges from the
	// on-screen geometry on very large windows.
	private static float WindowPerDesign()
	{
		Rectangle dest = RenderScale.WindowDestRect(RenderScale.WindowWidth, RenderScale.WindowHeight);
		float windowPerDesign = (float)dest.Height / RenderScale.DesignHeight;
		return (windowPerDesign > 0f) ? windowPerDesign : 1f;
	}

	// Which rung of the cursor ladder best matches the wanted design-space size at the current
	// window size. Quantized to CursorPxStep (<=4px of error), because each rung is a separate
	// natively-drawn PNG -- a CSS cursor can't be scaled, only chosen.
	private static int ChooseCursorPx(float windowPerDesign)
	{
		float wanted = (DebugFlags.ReticleSize ?? DefaultReticleDesignPx) * windowPerDesign;
		int px = (int)Math.Round(wanted / CursorPxStep) * CursorPxStep;
		return MathHelper.Clamp(px, MinCursorPx, MaxCursorPx);
	}

	// Design-space draw scale at which the reticle SPRITE's on-screen size equals the CSS
	// cursor's, so the intro lands exactly on the cursor it hands off to at ANY window size.
	// Derived from the SAME ChooseCursorPx the handoff pushes to JS -- the two can't drift.
	// build_cursor.py draws every cursor rung and cursor2 as one crosshair at different
	// resolutions, all with the bars running edge to edge (alpha bbox == texture bounds), so
	// texture.Width is the right sprite-size denominator and this survives re-authoring.
	private float CssHandoffScale()
	{
		float windowPerDesign = WindowPerDesign();
		return ChooseCursorPx(windowPerDesign) / windowPerDesign / texture.Width;
	}

	public override void Update(GameTime gameTime)
	{
		showtimer.Update(gameTime);
		// Intro just finished: hand the pointer to the zero-lag CSS reticle cursor and stop
		// drawing the sprite. Once handed off, keep re-checking the ladder so a window resize
		// (or a ?reticlesize= tweak) re-picks the rung -- the cursor is a fixed-px image, so
		// it can only track the letterbox by swapping to a different one.
		if (base.Visible && !Settings.GetInstance().HWMouse && !showtimer.Active)
		{
			int px = ChooseCursorPx(WindowPerDesign());
			if (!reticleHandedOff || px != cursorPx)
			{
				reticleHandedOff = true;
				cursorPx = px;
				CursorInterop.SetReticle(px);
			}
		}
		base.Update(gameTime);
	}
}
