using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

public class MousePointer : DrawableGameComponent, IMousePointerService
{
	private InputHandler input;

	private Texture2D texture;

	private SpriteBatchWrapper spriteBatch;

	private Timer showtimer;

	MousePointer IMousePointerService.MousePointer => this;

	public MousePointer(Game game)
		: base(game)
	{
		showtimer = new Timer(2000f, repeating: false);
		((DrawableGameComponent)this).VisibleChanged += MousePointer_VisibleChanged;
		((DrawableGameComponent)this).DrawOrder = 3000;
	}

	private void MousePointer_VisibleChanged(object sender, EventArgs e)
	{
		if (((DrawableGameComponent)this).Visible)
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
		((DrawableGameComponent)this).Initialize();
	}

	protected override void LoadContent()
	{
		((DrawableGameComponent)this).LoadContent();
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
			((DrawableGameComponent)this).Draw(gameTime);
		}
	}

	public override void Update(GameTime gameTime)
	{
		if (Settings.GetInstance().HWMouse)
		{
			if (((DrawableGameComponent)this).Visible && !showtimer.Active && !((GameComponent)this).Game.IsMouseVisible)
			{
				((GameComponent)this).Game.IsMouseVisible = true;
			}
			if ((!((DrawableGameComponent)this).Visible || showtimer.Active) && ((GameComponent)this).Game.IsMouseVisible)
			{
				((GameComponent)this).Game.IsMouseVisible = false;
			}
		}
		showtimer.Update(gameTime);
		((GameComponent)this).Update(gameTime);
	}
}
