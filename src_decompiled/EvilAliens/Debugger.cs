using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

public class Debugger : DrawableGameComponent
{
	private SpriteBatchWrapper batch;

	private SpriteFont font;

	private static List<string> text = new List<string>();

	public static void WriteLine(string line)
	{
		text.Add(line);
	}

	protected override void LoadContent()
	{
		((DrawableGameComponent)this).LoadContent();
		font = ServiceHelper.Get<IContentManagerService>().ContentManager.Load<SpriteFont>("GFX/Menu/menufont");
	}

	public Debugger(Game game)
		: base(game)
	{
		_ = ServiceHelper.Get<IContentManagerService>().ContentManager;
		batch = ServiceHelper.Get<ISpriteBatchWrapperService>().SpriteBatchWrapper;
		((GameComponent)this).UpdateOrder = int.MinValue;
		((DrawableGameComponent)this).DrawOrder = int.MaxValue;
	}

	public override void Initialize()
	{
		((DrawableGameComponent)this).Initialize();
	}

	public override void Update(GameTime gameTime)
	{
		text.Clear();
		((GameComponent)this).Update(gameTime);
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		float num = 0f;
		try
		{
			foreach (string item in text)
			{
				batch.DrawString(item, new Vector2(100f, 100f + num), Color.White, 0f, Vector2.Zero, 1f, (SpriteEffects)0, 1f);
				num += 25f;
			}
		}
		catch (Exception)
		{
		}
	}
}
