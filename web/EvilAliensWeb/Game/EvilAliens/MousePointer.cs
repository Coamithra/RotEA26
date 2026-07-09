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
//                        cursor (canvas.style.cursor: url(reticle.png)) so it is ZERO-LAG
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
			showtimer.Stop();
			showtimer.Reset();
			CursorInterop.Set("menu");
			return;
		}
		reticleHandedOff = false;
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
		// handoff doesn't pop. The CSS reticle is a fixed CssCursorPx window px while the sprite
		// draws at (texture px x design scale x window-per-design), so the end scale is
		// window-size-dependent; recomputed per frame (cheap) in case of a mid-intro resize.
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

	// wwwroot/reticle.png's pixel size in tools/cursor/build_cursor.py (a CSS cursor image is
	// fixed CSS px, which == canvas backing px here: index.html sizes the canvas to
	// clientWidth/Height, no devicePixelRatio factor). Keep in sync with the tool's SIZE.
	private const float CssCursorPx = 48f;

	// Design-space draw scale at which the reticle SPRITE's on-screen size equals the CSS
	// cursor's. On-screen px per design px is the UNCAPPED letterbox fit (WindowDestRect
	// height / design height) -- NOT RenderScale.Scale, which is capped at MaxHeight and
	// diverges from the on-screen geometry on very large windows. cursor2's art fills its full
	// 26px texture (alpha bbox == texture bounds) and reticle.png is that art bbox-fit into
	// 48px, so texture.Width is the correct sprite-size denominator.
	private float CssHandoffScale()
	{
		Rectangle dest = RenderScale.WindowDestRect(RenderScale.WindowWidth, RenderScale.WindowHeight);
		float windowPerDesign = (float)dest.Height / RenderScale.DesignHeight;
		if (windowPerDesign <= 0f)
		{
			windowPerDesign = 1f;
		}
		return CssCursorPx / windowPerDesign / texture.Width;
	}

	public override void Update(GameTime gameTime)
	{
		showtimer.Update(gameTime);
		// Intro just finished: hand the pointer to the zero-lag CSS reticle cursor and stop
		// drawing the sprite.
		if (base.Visible && !reticleHandedOff && !Settings.GetInstance().HWMouse && !showtimer.Active)
		{
			reticleHandedOff = true;
			CursorInterop.Set("reticle");
		}
		base.Update(gameTime);
	}
}
