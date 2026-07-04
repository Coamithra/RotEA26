using System;
using System.Collections.Generic;
using EvilAliens.Constants;
using EvilAliensWeb.Compat;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

// "I Made This!" (Levels.WebcamAliens) — a remake of the 2004 webcam game the
// splash-screen meme comes from. The PLAYER'S OWN CAMERA IMAGE is the ship:
// the browser side (wwwroot/webcam.js) shows a camera-setup dialog, removes the
// room background, and overlays the mirrored person on the game's starfield;
// per frame it pushes a 40x30 person-mask grid that this scene hit-tests
// against (Compat/WebcamInterop.cs — the full JS/C# split is documented there).
//
// Rules (faithful to the original, per the splash screenshot: a row of hearts
// top-left, a kill counter top-right, saucers on a starfield):
//   * Saucers fly in from the screen edges and wander. TOUCH one with your
//     body to asplode it.
//   * Left alone, a saucer starts blinking faster and faster, then fires one
//     big slow plasma orb at you. If it hits your image you lose one of
//     3 hearts; 0 hearts = game over.
//   * Asplode KillTarget saucers to win.
//
// There is NO PlayerShip in this level (spawnPlayerNormally = false) — the
// pause menu, score HUD, victory/defeat flows are all inherited from GameScene,
// but lives are the hearts here, not the stock lives strip (score.Lives stays 0
// so LoseLife() lands directly in the GameOver flow when the hearts run out).
internal class WebcamLevel : GameScene
{
	private const int KillTarget = 20;

	private const int StartHearts = 3;

	private int kills;

	private int hearts;

	private bool won;

	private bool introShown;

	private Timer spawnTimer = new Timer(1800f, repeating: false);

	// Post-hit mercy window: incoming plasma still bursts but doesn't hurt.
	private Timer graceTimer = new Timer(2200f, repeating: false);

	private readonly List<WebcamUfo> ufos = new List<WebcamUfo>();

	private readonly List<WebcamPlasma> plasmas = new List<WebcamPlasma>();

	private SpriteFont font;

	private Texture2D heart;

	private Texture2D blank;

	public WebcamLevel(Game game)
		: base(game, Levels.WebcamAliens)
	{
		// draw the hearts/kill-counter HUD above the stock score HUD (1000)
		base.DrawOrder = DrawOrders.DrawOrderHUD + 1;
		AllowAIFriends = false;
		base.OnFinished += WebcamLevel_OnFinished;
	}

	protected override void PreloadGraphicalContent()
	{
		base.PreloadGraphicalContent();
		ContentManager contentManager = ServiceHelper.Get<IContentManagerService>().ContentManager;
		contentManager.Load<Texture2D>("GFX/Sprites/ufosheet");
		contentManager.Load<Texture2D>("GFX/Sprites/plasmaball2");
		font = contentManager.Load<SpriteFont>("GFX/menu/menufont");
		heart = contentManager.Load<Texture2D>("GFX/Sprites/heart");
		blank = contentManager.Load<Texture2D>("GFX/Menu/blank");
	}

	protected override void PopulateEventList()
	{
		// The gameplay is all direct spawning in UpdateNormal; the event list only
		// needs a checkpoint so the InfiniteLives-cheat reset path has something
		// to revert to.
		WaitEvent waitEvent = new WaitEvent(base.Game, 0.1f);
		eventList.AddEvent(waitEvent, halting: true);
		eventList.AddHalt();
		eventList.SetLastEventAsCheckPoint();
	}

	public override void Initialize()
	{
		setPresence((GamerPresenceMode)14);
		score.DisableCombos();
		// the meme screenshot's plain starfield, not the holodeck sim chamber
		Background.SetSpace();
		// Ungated fun challenge with no real difficulty pick -> clean instrumental.
		// (Follow-up card 8fcc7a8e gives it difficulty; then route through
		// SoundManager.ClassicForDifficulty() so Hard+ webcam runs earn the lyrics.)
		base.SoundManager.PlayMusic(Songs.ClassicClean);
		base.Initialize();
		// GameScene showed the keyboard player's crosshair cursor, but there is no
		// ship to aim here — the player's body is the pointer. Keep it hidden.
		((DrawableGameComponent)ServiceHelper.Get<IMousePointerService>().MousePointer).Visible = false;
		Settings.GetInstance().LockDifficulty();
		// No PlayerShip: the player is the webcam image. Keep score.Lives at 0 so
		// the stock lives strip stays empty and LoseLife() (called when the last
		// heart goes) drops straight into the GameOver flow.
		spawnPlayerNormally = false;
		score.Lives = 0;
		kills = 0;
		hearts = StartHearts;
		won = false;
		introShown = false;
		ufos.Clear();
		plasmas.Clear();
		spawnTimer.Reset();
		spawnTimer.Start();
		graceTimer.Reset();
		graceTimer.Stop();
		// Hand the browser the stage: camera picker + preview + background
		// removal. The level idles underneath until the player joins (or exits
		// via the dialog's Back, which lands in the Cancelled poll below).
		WebcamInterop.BeginSetup();
	}

	private void WebcamLevel_OnFinished(object sender, FinishedArgs args)
	{
		// every exit path (victory, defeat, pause-exit, cancel) releases the camera
		WebcamInterop.Stop();
		score.EnableCombos();
		ufos.Clear();
		plasmas.Clear();
	}

	public override void Update(GameTime gameTime)
	{
		graceTimer.Update(gameTime);
		spawnTimer.Update(gameTime);
		// Backed out of the camera-setup dialog: leave the level like a pause-menu
		// exit would. (Stop() flips the state off Cancelled, so this fires once.)
		if (WebcamInterop.State == WebcamInterop.SessionState.Cancelled)
		{
			WebcamInterop.Stop();
			Terminate(FinishedMode.exit);
			return;
		}
		base.Update(gameTime);
	}

	protected override void UpdateNormal(GameTime gameTime)
	{
		base.UpdateNormal(gameTime);
		Prune(ufos);
		Prune(plasmas);
		if (WebcamInterop.State != WebcamInterop.SessionState.Playing)
		{
			return;
		}
		if (!introShown)
		{
			introShown = true;
			AnimatedMessage animatedMessage = AnimatedMessage.NewAnimatedMessage(Collection, base.Game);
			animatedMessage.Setup("Splat " + KillTarget + " saucers\nwith your body!", SoundManager.Texts.GetReady, AnimatedMessage.MessageType.starwarsblue);
			Collection.Add((GameComponent)(object)animatedMessage);
		}
		if (WebcamInterop.PlayerVisible)
		{
			SpawnSaucers();
			TestPlayerTouchesSaucers();
			TestPlasmaHitsPlayer();
		}
		if (kills >= KillTarget && !won)
		{
			won = true;
			Victory();
			AnimatedMessage animatedMessage2 = AnimatedMessage.NewAnimatedMessage(Collection, base.Game);
			animatedMessage2.Setup("Wave Completed!", SoundManager.Texts.WaveCompleted, AnimatedMessage.MessageType.starwarsblue);
			Collection.Add((GameComponent)(object)animatedMessage2);
		}
	}

	private static void Prune<T>(List<T> list) where T : AlienDrawableGameComponent
	{
		for (int i = list.Count - 1; i >= 0; i--)
		{
			if (list[i].IsDead)
			{
				list.RemoveAt(i);
			}
		}
	}

	private void SpawnSaucers()
	{
		if (won || !spawnTimer.Finished)
		{
			return;
		}
		// population ramps up as the player racks up kills
		int cap = Math.Min(1 + kills / 4, 5);
		if (ufos.Count >= cap)
		{
			// full house: check again shortly
			spawnTimer.Duration = 400f;
			spawnTimer.Reset();
			spawnTimer.Start();
			return;
		}
		float difficulty = Settings.GetInstance().DifficultyModifier;
		WebcamUfo webcamUfo = WebcamUfo.NewWebcamUfo(Collection, base.Game);
		// harder difficulty + later waves arm faster and blink shorter
		float armDelay = RandomHelper.RandomNextFloat(5000f, 9000f) / difficulty * MathHelper.Lerp(1f, 0.6f, Math.Min(1f, kills / (float)KillTarget));
		float blinkTime = RandomHelper.RandomNextFloat(2400f, 3200f) / difficulty;
		webcamUfo.Setup(RandomEdgePosition(), armDelay, blinkTime);
		webcamUfo.OnFired += ufo_OnFired;
		Collection.Add((GameComponent)(object)webcamUfo);
		ufos.Add(webcamUfo);
		spawnTimer.Duration = RandomHelper.RandomNextFloat(1400f, 3000f) / difficulty;
		spawnTimer.Reset();
		spawnTimer.Start();
	}

	private static Vector2 RandomEdgePosition()
	{
		switch (RandomHelper.Random.Next(4))
		{
		case 0:
			return new Vector2(-40f, RandomHelper.RandomNextFloat(60f, 540f));
		case 1:
			return new Vector2(840f, RandomHelper.RandomNextFloat(60f, 540f));
		case 2:
			return new Vector2(RandomHelper.RandomNextFloat(60f, 740f), -40f);
		default:
			return new Vector2(RandomHelper.RandomNextFloat(60f, 740f), 640f);
		}
	}

	private void ufo_OnFired(WebcamUfo sender, Vector2 target)
	{
		WebcamPlasma webcamPlasma = WebcamPlasma.NewWebcamPlasma(Collection, base.Game);
		webcamPlasma.Setup(sender.Position, target);
		Collection.Add((GameComponent)(object)webcamPlasma);
		plasmas.Add(webcamPlasma);
		base.SoundManager.PlayCue("lazershotnoloop");
	}

	private void TestPlayerTouchesSaucers()
	{
		foreach (WebcamUfo ufo in ufos)
		{
			if (!ufo.IsDead && WebcamInterop.HitCircle(ufo.Position, ufo.HitRadius))
			{
				ufo.Asplode();
				kills++;
				score.AddScore(PointValues.WebcamUfo, isCombo: false, ufo.Position, 0);
			}
		}
	}

	private void TestPlasmaHitsPlayer()
	{
		foreach (WebcamPlasma plasma in plasmas)
		{
			// slightly forgiving: the orb must reach INTO the body, not just graze it
			if (!plasma.IsDead && WebcamInterop.HitCircle(plasma.Position, plasma.HitRadius * 0.7f))
			{
				plasma.Detonate(withExplosion: true);
				PlayerHit();
			}
		}
	}

	private void PlayerHit()
	{
		if (graceTimer.Active || Settings.GetInstance().Invulnerability)
		{
			return;
		}
		base.SoundManager.PlayCue("head_asplode");
		graceTimer.Reset();
		graceTimer.Start();
		if (Settings.GetInstance().InfiniteLives)
		{
			return;
		}
		hearts--;
		if (hearts <= 0)
		{
			// score.Lives is 0, so this routes to the standard GameOver flow
			// ("Mission Failed" + the evil laugh, then back to the menu).
			LoseLife();
		}
	}

	public override void Draw(GameTime gameTime)
	{
		base.Draw(gameTime);
		SpriteBatchWrapper spriteBatch = base.SpriteBatch;
		spriteBatch.BlendMode = (SpriteBlendMode)1;
		// hearts, top-left — the original game's lives row
		for (int i = 0; i < hearts; i++)
		{
			spriteBatch.Draw(heart, new Vector2((float)((General.SafeZone).Left + 24 + i * 44), (float)((General.SafeZone).Top + 22)), 0f, 0.9f, center: true, Color.White);
		}
		// kill counter, top-right — the original's "Killed: N"
		if (font != null)
		{
			string text = "Killed: " + kills + " / " + KillTarget;
			Vector2 size = font.MeasureString(text) * 0.7f;
			spriteBatch.DrawShadowString(text, new Vector2((float)(General.SafeZone).Right - size.X, (float)(General.SafeZone).Top + 6f), 0.7f, Color.Black, Color.White, new Vector2(2f, 2f), 1f, metal: false);
			// prompts: mask gone stale / player out of frame
			if (WebcamInterop.State == WebcamInterop.SessionState.Playing && !WebcamInterop.PlayerVisible && introShown)
			{
				float pulse = 0.6f + 0.4f * (float)Math.Sin(gameTime.TotalGameTime.TotalSeconds * 4.0);
				string hint = "Step into view!";
				Vector2 hintSize = font.MeasureString(hint);
				spriteBatch.DrawShadowString(hint, new Vector2(400f - hintSize.X / 2f, 150f), 1f, Color.Black, Color.White, new Vector2(2f, 2f), pulse, metal: false);
			}
		}
		// short red sting when a heart is lost (the grace window's opening beat)
		if (graceTimer.Active && graceTimer.TimeElapsed < 450f)
		{
			float a = 0.45f * (1f - graceTimer.TimeElapsed / 450f);
			spriteBatch.Draw(blank, new Rectangle(0, 0, 800, 600), new Color(1f, 0f, 0f, a));
		}
		spriteBatch.BlendMode = (SpriteBlendMode)1;
	}
}
