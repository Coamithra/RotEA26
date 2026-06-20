using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;
using EvilAliensWeb.Compat;

namespace EvilAliens;

public class StartScreen : Scene
{
	public delegate void FinishedHandler(object sender);

	private bool startPressed;

	private float scale = 1f;

	private SpriteFont font;

	private Curve brainPulsate;

	private string text;

	public event FinishedHandler OnFinished;

	public StartScreen(Game game)
		: base(game)
	{
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		brainPulsate = Content.Load<Curve>("GFX/Effects/BrainCurve");
		font = Content.Load<SpriteFont>("GFX/Menu/menufont");
	}

	public override void Initialize()
	{
		base.Initialize();
		startPressed = false;
		text = "Press Start";
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_0079: Unknown result type (might be due to invalid IL or missing references)
		//IL_007e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0088: Unknown result type (might be due to invalid IL or missing references)
		base.Draw(gameTime);
		base.GraphicsDevice.Clear(Color.Black);
		float x = font.MeasureString(text).X;
		float num = (x * scale - x) / 2f;
		Vector2 origin = default(Vector2);
		(origin) = new Vector2(num, (float)(font.LineSpacing / 2));
		base.SpriteBatch.DrawString(font, text, new Vector2(400f - x / 2f, 300f), Color.AliceBlue, 0f, origin, scale, (SpriteEffects)0, 0f);
	}

	public override void Update(GameTime gameTime)
	{
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ba: Unknown result type (might be due to invalid IL or missing references)
		base.Update(gameTime);
		float num = 15f / font.MeasureString(text).X;
		float num2 = (float)gameTime.TotalGameTime.TotalSeconds;
		float num3 = MyMath.Mod(num2 / 2f, 1f);
		scale = 1f + num * brainPulsate.Evaluate(num3);
		if (!startPressed)
		{
			int num4 = -1;
			// Debug (?menu / ?autostart / ?level=...): auto-"Press Start" as keyboard
			// player 0 so the Press Start screen advances itself without a key press.
			if (DebugFlags.AutoStart)
			{
				num4 = 0;
			}
			// Web/PC port: keyboard Enter starts the game as the local player (index 0).
			else if (base.InputHandler.Pressed(MyKeys.Enter))
			{
				num4 = 0;
			}
			for (int i = 0; i < 4; i++)
			{
				bool flag = false;
				flag |= base.InputHandler.PadPressed(PadKeys.Start, i);
				if (flag | base.InputHandler.PadPressed(PadKeys.A, i))
				{
					num4 = i;
				}
			}
			if (num4 < 0)
			{
				return;
			}
			PlayerIndex starter = (PlayerIndex)num4;
			// Web/PC port: there is no Xbox LIVE sign-in. The Xbox build gated start on
			// isSignedIn(starter) and otherwise showed the (now no-op) sign-in blade,
			// which would leave the web build stuck forever on "Press Start". PC builds
			// had no signed-in-gamer requirement, so proceed directly as a local profile.
			Storage.Init(base.Game, starter);
			startPressed = true;
			text = "Loading";
		}
		else if (!Storage.Busy && !Guide.IsVisible)
		{
			Settings.GetInstance().Load();
			Unlockables.GetInstance().Load();
			Achievements.GetInstance().Load();
			ScreenshotSaver.Init();
			if (this.OnFinished != null)
			{
				this.OnFinished(this);
			}
		}
	}

	private bool isSignedIn(PlayerIndex starter)
	{
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		GamerCollectionEnumerator<SignedInGamer> enumerator = ((GamerCollection<SignedInGamer>)(object)Gamer.SignedInGamers).GetEnumerator();
		try
		{
			while (enumerator.MoveNext())
			{
				SignedInGamer current = enumerator.Current;
				if (current.PlayerIndex == starter)
				{
					return true;
				}
			}
		}
		finally
		{
			((IDisposable)enumerator).Dispose();
		}
		return false;
	}
}
