using System;
using Microsoft.JSInterop;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

public class MousePointer : DrawableGameComponent, IMousePointerService
{
	private InputHandler input;

	private Texture2D texture;

	private SpriteBatchWrapper spriteBatch;

	private Timer showtimer;

	// Set from JS (canvas pointerenter/leave, see wwwroot/index.html). When the mouse
	// leaves the game surface we stop drawing the software reticle so it doesn't sit
	// clamped at the screen edge while the OS cursor (shown off-canvas) takes over.
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
			showtimer.Reset();
			showtimer.Start();
		}
	}

	public override void Initialize()
	{
		input = ServiceHelper.Get<IInputHandlerService>().InputHandler;
		spriteBatch = ServiceHelper.Get<ISpriteBatchWrapperService>().SpriteBatchWrapper;
		showtimer.Reset();
		showtimer.Start();
		base.Initialize();
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		texture = ServiceHelper.Get<IContentManagerService>().ContentManager.Load<Texture2D>("GFX/cursor2");
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_006e: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00dc: Unknown result type (might be due to invalid IL or missing references)
		if (!Settings.GetInstance().HWMouse || showtimer.Active)
		{
			// Software-cursor mode: hide the reticle while the pointer is off-canvas so
			// the OS cursor alone shows there (no reticle stuck clamped at the edge).
			if (!Settings.GetInstance().HWMouse && !pointerOnCanvas)
			{
				return;
			}
			Vector2 mousePosition = input.MousePosition;
			if (!Settings.GetInstance().HWMouse)
			{
				spriteBatch.BlendMode = (SpriteBlendMode)2;
				mousePosition.X = MathHelper.Clamp(input.MousePosition.X, 0f, 800f);
				mousePosition.Y = MathHelper.Clamp(input.MousePosition.Y, 0f, 600f);
			}
			if (showtimer.Active)
			{
				float num = MathHelper.SmoothStep(0f, 1f, showtimer.Normalized);
				float scale = 1f + num * 3f;
				float rotation = num * ((float)Math.PI * 2f) * 1.5f;
				spriteBatch.Draw(texture, mousePosition, rotation, scale, center: true);
			}
			else
			{
				spriteBatch.Draw(texture, mousePosition, 0f, 1f, center: true);
			}
			base.Draw(gameTime);
		}
	}

	public override void Update(GameTime gameTime)
	{
		if (Settings.GetInstance().HWMouse)
		{
			if (base.Visible && !showtimer.Active && !base.Game.IsMouseVisible)
			{
				base.Game.IsMouseVisible = true;
			}
			if ((!base.Visible || showtimer.Active) && base.Game.IsMouseVisible)
			{
				base.Game.IsMouseVisible = false;
			}
		}
		showtimer.Update(gameTime);
		base.Update(gameTime);
	}
}
