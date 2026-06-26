using System;
using System.Collections.Generic;
using EvilAliens.Constants;
using EvilAliensWeb.Compat;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

public class ScoreVisualiser : DrawableGameComponent, IScoreService, IComponentWatcher
{
	public enum ScorePart
	{
		Enhancement,
		Powerbar
	}

	private class ScoreInfo
	{
		public float score;

		public string scoreString;

		public int combo;

		public Timer combotimer;

		public Powerup.PowerupType powerup;

		public bool powerupactive;

		public int bombs;

		// Chrome-sheen glint is event-driven on the score: it sweeps once when the leading
		// (most-significant) digit of scoreString rolls over (9->10, 1900->2000, ...), then
		// rests. lastLeadDigit tracks the previous frame's first char; glintElapsed counts the
		// one-shot sweep while glinting (see UpdateGlint / GlintTime).
		public char lastLeadDigit = '0';

		public float glintElapsed;

		public bool glinting;

		public Dictionary<Powerup.PowerupType, PowerupData> powerupDatas = new Dictionary<Powerup.PowerupType, PowerupData>();

		public void SetScore(float score)
		{
			this.score = score;
			scoreString = ((int)score).ToString();
		}

		public void AddCombo()
		{
			combo++;
		}

		// Arm a one-shot glint sweep when the leading digit changes (skip the reset-to-"0"
		// and the empty edge case), then count the sweep up to its duration once armed.
		public void UpdateGlint(float dtSeconds)
		{
			char lead = (scoreString != null && scoreString.Length > 0) ? scoreString[0] : '0';
			if (lead != lastLeadDigit)
			{
				lastLeadDigit = lead;
				if (scoreString != "0")
				{
					glinting = true;
					glintElapsed = 0f;
				}
			}
			if (glinting)
			{
				glintElapsed += dtSeconds;
				if (glintElapsed >= SpriteBatchWrapper.MetalSweepDuration)
				{
					glinting = false;
				}
			}
		}
	}

	private const float combotime = 1000f;

	private const int MAX_PRECACHED_COMBOSTRINGS = 1000;

	private bool combosenabled = true;

	private Timer phototimer = new Timer(5000f, repeating: false);

	private Texture2D photocamera;

	private Texture2D bomb;

	private Color snapshotcolor;

	private bool displayPowerUpAtNextHit;

	private int lives;

	private List<ScoreInfo> scores = new List<ScoreInfo>();

	private List<float> saved = new List<float>();

	private SpriteFont font;

	private SpriteBatchWrapper spriteBatch;

	private SoundManager soundManager;

	private Oracle oracle;

	private List<FloatingText> floatingtexts = new List<FloatingText>();

	private List<FloatingText> pendingtexts = new List<FloatingText>();

	private Texture2D powerbar;

	private Texture2D playersheet;

	private MiniExplosion explosion;

	private ContentManager content;

	private string[] comboStrings = new string[1000];

	private Timer showPressStartTimer = new Timer(5000f, repeating: true);

	private bool showPressStart = true;

	private int showPressStartTimes;

	private ComponentBin collection;

	public bool IsTutorial { get; set; }

	public int Lives
	{
		get
		{
			return lives;
		}
		set
		{
			lives = value;
		}
	}

	public float HighScore
	{
		get
		{
			float num = 0f;
			foreach (ScoreInfo score in scores)
			{
				num = MathHelper.Max(num, score.score);
			}
			return num;
		}
	}

	public ScoreVisualiser Score => this;

	private Vector2 livePosition(int i)
	{
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		return new Vector2((float)(316 + i * 24 + 12), (float)((General.SafeZone).Bottom - 10));
	}

	public void RemoveLife()
	{
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		lives--;
		explosion.Show(livePosition(lives));
	}

	public int Combo(int player)
	{
		return scores[player].combo;
	}

	public float PointScore(int player)
	{
		return scores[player].score;
	}

	public ScoreVisualiser(Game game)
		: base(game)
	{
		//IL_0127: Unknown result type (might be due to invalid IL or missing references)
		IsTutorial = false;
		for (int i = 0; i < comboStrings.Length; i++)
		{
			comboStrings[i] = i + "x";
		}
		List<Powerup.PowerupType> enumValues = Game1.GetEnumValues<Powerup.PowerupType>();
		for (int j = 0; j < 4; j++)
		{
			ScoreInfo scoreInfo = new ScoreInfo();
			scoreInfo.score = 0f;
			scoreInfo.scoreString = "0";
			scoreInfo.combo = 0;
			scoreInfo.powerupactive = false;
			scoreInfo.bombs = 0;
			scoreInfo.powerup = Powerup.PowerupType.Blast;
			scoreInfo.combotimer = new Timer(1000f, repeating: false);
			scoreInfo.combotimer.Stop();
			foreach (Powerup.PowerupType item in enumValues)
			{
				scoreInfo.powerupDatas[item] = new PowerupData(game, getScorePosition(j, out var _), item);
				scoreInfo.powerupDatas[item].onLevelUp += ScoreVisualiser_onLevelUp;
			}
			scores.Add(scoreInfo);
			saved.Add(0f);
		}
		content = ServiceHelper.Get<IContentManagerService>().ContentManager;
		spriteBatch = ServiceHelper.Get<ISpriteBatchWrapperService>().SpriteBatchWrapper;
		soundManager = ServiceHelper.Get<ISoundManagerService>().SoundManager;
		explosion = new MiniExplosion(game);
		base.DrawOrder = 1000;
	}

	private void ScoreVisualiser_onLevelUp(Powerup.PowerupType type, int newLevel, PowerupData sender)
	{
		int num = -1;
		for (int i = 0; i < 4; i++)
		{
			if (scores[i].powerupDatas[type] == sender)
			{
				num = i;
			}
		}
		PlayerShip playerShip = null;
		foreach (PlayerShip ship in oracle.GetShips())
		{
			if (ship.Owner == num)
			{
				playerShip = ship;
			}
		}
		if (playerShip != null)
		{
			playerShip.PowerUp(type, newLevel, doEffect: true);
			displayPowerUpAtNextHit = true;
		}
	}

	public void Save()
	{
		for (int i = 0; i < 4; i++)
		{
			saved[i] = scores[i].score;
		}
	}

	public void Load()
	{
		for (int i = 0; i < 4; i++)
		{
			scores[i].SetScore(saved[i]);
			// Checkpoint restore (post-death revert), not a scored rollover — re-baseline the
			// glint so the leading digit snapping back doesn't fire a spurious sweep.
			ScoreInfo s = scores[i];
			s.lastLeadDigit = (s.scoreString != null && s.scoreString.Length > 0) ? s.scoreString[0] : '0';
			s.glinting = false;
			s.glintElapsed = 0f;
		}
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		font = content.Load<SpriteFont>("GFX/Menu/menufont");
		powerbar = content.Load<Texture2D>("GFX/Menu/powerbar");
		playersheet = content.Load<Texture2D>("GFX/Sprites/playersheet");
		photocamera = content.Load<Texture2D>("GFX/Sprites/photocamera");
		bomb = content.Load<Texture2D>("GFX/Sprites/bombicon");
		collection = ServiceHelper.Get<IComponentBinService>().ComponentBin;
		explosion.LoadGraphics();
	}

	public void Reset()
	{
		phototimer.Stop();
		for (int i = 0; i < 4; i++)
		{
			ScoreInfo scoreInfo = scores[i];
			scoreInfo.scoreString = "0";
			scoreInfo.score = 0f;
			scoreInfo.combo = 0;
			scoreInfo.combotimer.Reset();
			scoreInfo.combotimer.Stop();
			scoreInfo.bombs = 0;
			scoreInfo.powerupactive = false;
			// Re-baseline the glint so the reset-to-"0" doesn't read as a digit change next frame.
			scoreInfo.lastLeadDigit = '0';
			scoreInfo.glinting = false;
			scoreInfo.glintElapsed = 0f;
			foreach (PowerupData value in scoreInfo.powerupDatas.Values)
			{
				((DrawableGameComponent)value).Visible = false;
				value.Reset();
			}
		}
		foreach (FloatingText floatingtext in floatingtexts)
		{
			pendingtexts.Add(floatingtext);
		}
		floatingtexts.Clear();
		lives = 0;
		explosion.Reset();
		showPressStartTimer.Stop();
	}

	public void AddBomb(int player)
	{
		scores[player].bombs = Math.Min(scores[player].bombs + 1, 3);
	}

	public void AddScore(float amount, bool isCombo, int player)
	{
		float num = ((!isCombo) ? amount : comboModify(amount, player));
		scores[player].SetScore(scores[player].score + num);
	}

	private void increasecombo(int player)
	{
		if (combosenabled)
		{
			scores[player].AddCombo();
			if (scores[player].powerupactive)
			{
				scores[player].powerupDatas[scores[player].powerup].AddExp(scores[player].combo);
				checkPowerupAchievement(player);
			}
		}
	}

	private void checkPowerupAchievement(int player)
	{
		bool flag = false;
		foreach (PlayerShip ship in oracle.GetShips())
		{
			if (ship.Controller != ControlDevice.AI)
			{
				flag = true;
			}
		}
		if (GetPowerupLevel(Powerup.PowerupType.Blast, player) == 4 && GetPowerupLevel(Powerup.PowerupType.FirePower, player) == 4 && GetPowerupLevel(Powerup.PowerupType.Option, player) == 4 && GetPowerupLevel(Powerup.PowerupType.Range, player) == 4 && !IsTutorial && !Settings.GetInstance().CheckForCheats() && flag)
		{
			ServiceHelper.Get<IAwardmentBladeService>().get().AwardAchievement(Awardment.FullPower);
		}
	}

	public void SustainCombo(int player, Vector2 location)
	{
		if (combosenabled)
		{
			increasecombo(player);
			scores[player].combotimer.Start();
			scores[player].combotimer.Reset();
			CheckPowerup(ref location, player);
		}
	}

	public void AddScore(float amount, bool isCombo, Vector2 location, int player)
	{
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		float num = ((!isCombo) ? amount : comboModify(amount, player));
		AddScore(amount, isCombo, player);
		FloatingText text = GetText((int)num, location, FloatingText.ShowType.scrollup, "");
		floatingtexts.Add(text);
	}

	private void CheckPowerup(ref Vector2 location, int player)
	{
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0079: Unknown result type (might be due to invalid IL or missing references)
		if (displayPowerUpAtNextHit)
		{
			displayPowerUpAtNextHit = false;
			soundManager.PlayText(SoundManager.Texts.PowerUp, 1);
			FloatingText text = GetText(location, FloatingText.ShowType.pop, "Power Up!");
			floatingtexts.Add(text);
		}
		else if (scores[player].combo % 10 == 0 && scores[player].combo > 0)
		{
			FloatingText text = GetText(scores[player].combo, location, FloatingText.ShowType.pop, "X");
			floatingtexts.Add(text);
		}
	}

	private FloatingText GetText(Vector2 location, FloatingText.ShowType type, string suffix)
	{
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0040: Unknown result type (might be due to invalid IL or missing references)
		FloatingText floatingText;
		if (pendingtexts.Count > 0)
		{
			floatingText = pendingtexts[pendingtexts.Count - 1];
			pendingtexts.RemoveAt(pendingtexts.Count - 1);
			floatingText.Reset(location, type, suffix);
		}
		else
		{
			floatingText = new FloatingText(location, type, suffix);
		}
		return floatingText;
	}

	private FloatingText GetText(int amount, Vector2 location, FloatingText.ShowType type, string suffix)
	{
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		FloatingText floatingText;
		if (pendingtexts.Count > 0)
		{
			floatingText = pendingtexts[pendingtexts.Count - 1];
			pendingtexts.RemoveAt(pendingtexts.Count - 1);
			floatingText.Reset(amount, location, type, suffix);
		}
		else
		{
			floatingText = new FloatingText(amount, location, type, suffix);
		}
		return floatingText;
	}

	private float comboModify(float amount, int player)
	{
		return amount * (1f + (float)scores[player].combo / 20f);
	}

	public override void Initialize()
	{
		oracle = ServiceHelper.Get<IOracleService>().Oracle;
		base.Initialize();
		showPressStart = true;
		showPressStartTimes = 0;
		showPressStartTimer.Reset();
		showPressStartTimer.Stop();
		foreach (ScoreInfo score in scores)
		{
			foreach (PowerupData value in score.powerupDatas.Values)
			{
				collection.Add((GameComponent)(object)value);
				((DrawableGameComponent)value).Visible = false;
			}
		}
	}

	private Vector2 getScorePosition(int player, out Color color)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0040: Unknown result type (might be due to invalid IL or missing references)
		//IL_0068: Unknown result type (might be due to invalid IL or missing references)
		//IL_006d: Unknown result type (might be due to invalid IL or missing references)
		//IL_008f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0094: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d3: Unknown result type (might be due to invalid IL or missing references)
		Rectangle safeZone = General.SafeZone;
		Vector2 result = default(Vector2);
		switch (player)
		{
		case 0:
			(result) = new Vector2((float)(safeZone).Left, (float)(safeZone).Top);
			color = Color.Blue;
			break;
		case 1:
			(result) = new Vector2((float)((safeZone).Right - 160), (float)(safeZone).Top);
			color = Color.Purple;
			break;
		case 2:
			(result) = new Vector2((float)(safeZone).Left, (float)((safeZone).Bottom - 65));
			color = Color.Red;
			break;
		case 3:
			(result) = new Vector2((float)((safeZone).Right - 160), (float)((safeZone).Bottom - 65));
			color = Color.Orange;
			break;
		default:
			throw new Exception("Score visualizer crashed because it's not equipped to deal with more than 4 players");
		}
		return result;
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		//IL_0068: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e1: Unknown result type (might be due to invalid IL or missing references)
		//IL_0145: Unknown result type (might be due to invalid IL or missing references)
		//IL_014c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0185: Unknown result type (might be due to invalid IL or missing references)
		//IL_0195: Unknown result type (might be due to invalid IL or missing references)
		base.Draw(gameTime);
		spriteBatch.BlendMode = (SpriteBlendMode)1;
		foreach (FloatingText floatingtext in floatingtexts)
		{
			floatingtext.Draw(font, spriteBatch);
		}
		for (int i = 0; i < 4; i++)
		{
			Color color = Color.Gray;
			Vector2 startpos = getScorePosition(i, out color);
			if (i < oracle.Players)
			{
				drawPlayerScore(i, ref color, ref startpos, gameTime);
			}
			else
			{
				drawPressStart(gameTime, i, ref color, ref startpos);
			}
		}
		for (int j = 0; j < lives; j++)
		{
			int fw = (playersheet.Width - 7) / 8;
			int fh = (playersheet.Height - 3) / 4;
			spriteBatch.Draw(playersheet, new Rectangle(0, 0, fw, fh), livePosition(j), 0f, 0.5f * 48f / (float)fw, center: true, new Color(new Vector4(1f, 1f, 1f, 0.5f)));
		}
		if (explosion.Active)
		{
			explosion.Draw(gameTime);
		}
		if (phototimer.Active)
		{
			float num = MathHelper.SmoothStep(0f, 1f, phototimer.Normalized);
			Color color2 = default(Color);
			(color2) = new Color(new Vector4((snapshotcolor).ToVector3(), num));
			float photoSsf = AlienDrawableGameComponent.SuperSampleFactor("GFX/Sprites/photocamera", photocamera.Width);
			spriteBatch.Draw(photocamera, new Vector2(400f, (float)(General.SafeZone).Top + (float)photocamera.Height / photoSsf / 2f), 0f, 1f / photoSsf, center: true, color2);
		}
		spriteBatch.BlendMode = (SpriteBlendMode)1;
	}

	private void drawPressStart(GameTime gameTime, int i, ref Color playercolor, ref Vector2 startpos)
	{
		//IL_00b2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0128: Unknown result type (might be due to invalid IL or missing references)
		//IL_0137: Unknown result type (might be due to invalid IL or missing references)
		//IL_013c: Unknown result type (might be due to invalid IL or missing references)
		//IL_014d: Unknown result type (might be due to invalid IL or missing references)
		if (showPressStartTimer.Active)
		{
			float num = 1f;
			if (showPressStartTimer.TimeElapsed < 500f)
			{
				num = showPressStartTimer.TimeElapsed / 500f;
			}
			if (0f <= showPressStartTimer.TimeLeft - 3000f && showPressStartTimer.TimeLeft - 3000f < 500f)
			{
				num = (showPressStartTimer.TimeLeft - 3000f) / 500f;
			}
			if (showPressStartTimer.TimeLeft - 3000f < 0f)
			{
				num = 0f;
			}
			num = MathHelper.SmoothStep(0f, 1f, num);
			Vector4 val = (playercolor).ToVector4();
			Color aliceBlue = Color.AliceBlue;
			Color color = default(Color);
			(color) = new Color(Vector4.Lerp(val, (aliceBlue).ToVector4(), num));
			string text = i switch
			{
				0 => "Player 1", 
				1 => "Player 2", 
				2 => "Player 3", 
				3 => "Player 4", 
				_ => "Gah?", 
			};
			string str = ((!showPressStart) ? "Press Start" : text);
			// Inactive-slot prompt: static chrome, never a sweep (no score to roll over).
			DrawStr(str, startpos + new Vector2(0f, -5f), 0.9f, num * 0.6f, color, ParkedGlint);
		}
	}

	private void drawPlayerScore(int i, ref Color playercolor, ref Vector2 startpos, GameTime gameTime)
	{
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cf: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f1: Unknown result type (might be due to invalid IL or missing references)
		//IL_0177: Unknown result type (might be due to invalid IL or missing references)
		//IL_0182: Unknown result type (might be due to invalid IL or missing references)
		//IL_0187: Unknown result type (might be due to invalid IL or missing references)
		//IL_0193: Unknown result type (might be due to invalid IL or missing references)
		//IL_012d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0138: Unknown result type (might be due to invalid IL or missing references)
		//IL_013d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0149: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c9: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ce: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d3: Unknown result type (might be due to invalid IL or missing references)
		// Only the score NUMBER is event-driven (sweeps on a leading-digit rollover); the combo
		// readout has no "first digit", so it keeps the static chrome with no sweep (ParkedGlint).
		DrawStr(scores[i].scoreString, startpos + new Vector2(0f, -5f), 0.9f, 1f, playercolor, GlintTime(i));
		if (scores[i].combo > 5)
		{
			float alpha = 0.2f + 0.8f * MathHelper.SmoothStep(0f, 1f, scores[i].combotimer.TimeLeft / 1000f);
			float num = MathHelper.Max(font.MeasureString(scores[i].scoreString).X * 0.9f + 17f, 100f);
			DrawStr("Combo!", startpos + new Vector2(num - 10f, -5f), 0.6f, alpha, playercolor, ParkedGlint);
			if (scores[i].combo < 1000)
			{
				DrawStr(comboStrings[scores[i].combo], startpos + new Vector2(num, 13f), 1f, alpha, playercolor, ParkedGlint);
			}
			else
			{
				DrawStr(scores[i].combo + "x", startpos + new Vector2(num, 13f), 1f, alpha, playercolor, ParkedGlint);
			}
		}
		float bombSsf = AlienDrawableGameComponent.SuperSampleFactor("GFX/Sprites/bombicon", bomb.Width);
		for (int j = 0; j < scores[i].bombs; j++)
		{
			spriteBatch.Draw(bomb, startpos + new Vector2((float)(30 + bomb.Width / bombSsf * j), 45f), 0f, 1f / bombSsf, center: false, Color.White);
		}
	}

	// metal.fx glint clock for a player's score readout: the live one-shot sweep time while a
	// leading-digit rollover is animating, else a value parked mid-rest so the glint stays off
	// (the static chrome gradient is time-independent and always shows). Replaces the old
	// always-on periodic sweep that fired every ~9s regardless of play.
	private static float ParkedGlint => SpriteBatchWrapper.MetalSweepPeriod * 0.5f;

	private float GlintTime(int player)
	{
		ScoreInfo s = scores[player];
		return s.glinting ? s.glintElapsed : ParkedGlint;
	}

	private void DrawStr(string str, Vector2 position, float scale, float alpha)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		DrawStr(str, position, scale, alpha, new Color((byte)100, (byte)100, byte.MaxValue, byte.MaxValue), ParkedGlint);
	}

	private void DrawStr(string str, Vector2 position, float scale, float alpha, Color color, float glintTime)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Unknown result type (might be due to invalid IL or missing references)
		//IL_0065: Unknown result type (might be due to invalid IL or missing references)
		//IL_0079: Unknown result type (might be due to invalid IL or missing references)
		//IL_007e: Unknown result type (might be due to invalid IL or missing references)
		//IL_008b: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f6: Unknown result type (might be due to invalid IL or missing references)
		// Shadow + text COLOURS (opaque); the shadow is the base hue, the text a brightened
		// version — exactly the two-tone drop the score always had. Transparency is applied
		// once to the whole flattened sprite below, not per layer.
		Color shadowColor = default(Color);
		Color textColor = default(Color);
		if (color == Color.White)
		{
			(shadowColor) = new Color((byte)0, (byte)0, byte.MaxValue, byte.MaxValue);
			(textColor) = new Color((byte)173, (byte)216, (byte)230, byte.MaxValue);
		}
		else
		{
			(shadowColor) = new Color((color).ToVector3());
			(textColor) = new Color((color).ToVector3() + new Vector3(0.65f, 0.65f, 0.65f));
		}
		// Flatten shadow+text into ONE semi-transparent sprite so the translucent shadow no
		// longer shows through the translucent text where they overlap. DebugFlags.MetalScore
		// (chrome-sheen, ON by default — the card author kept it; ?metalscore=0 A/Bs the plain
		// flatten) routes it through metal.fx. The chrome darkens the mid-band, so the metal
		// score reads a touch more solid (0.7) than the plain flatten (0.55) to compensate.
		bool metal = DebugFlags.MetalScore;
		float opacity = alpha * (metal ? 0.7f : 0.55f);
		spriteBatch.DrawShadowString(str, position, scale, shadowColor, textColor, new Vector2(2f, 2f), opacity, metal, glintTime);
	}

	public override void Update(GameTime gameTime)
	{
		showPressStartTimer.Update(gameTime);
		if (showPressStartTimer.Finished)
		{
			showPressStart = !showPressStart;
			showPressStartTimes++;
			if (showPressStartTimes >= 4)
			{
				showPressStartTimer.Stop();
			}
		}
		phototimer.Update(gameTime);
		if (explosion.Active)
		{
			explosion.Update(gameTime);
		}
		float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
		foreach (ScoreInfo score in scores)
		{
			score.combotimer.Update(gameTime);
			if (score.combotimer.Finished)
			{
				ResetCombo(scores.IndexOf(score));
			}
			score.UpdateGlint(dt);
		}
		for (int i = 0; i < floatingtexts.Count; i++)
		{
			floatingtexts[i].Update(gameTime);
			if (floatingtexts[i].done)
			{
				pendingtexts.Add(floatingtexts[i]);
				floatingtexts.RemoveAt(i);
				i--;
			}
		}
		base.Update(gameTime);
	}

	public void SetPowerup(Powerup.PowerupType type, int player)
	{
		scores[player].powerupDatas[scores[player].powerup].FadeOut();
		scores[player].powerupDatas[type].FadeIn();
		scores[player].powerup = type;
		scores[player].powerupactive = true;
	}

	public void ResetPowerup(int player)
	{
		scores[player].powerupactive = false;
		scores[player].bombs = 0;
		foreach (PowerupData value in scores[player].powerupDatas.Values)
		{
			value.Reset();
			((DrawableGameComponent)value).Visible = false;
		}
	}

	public void ResetCombo(int player)
	{
		scores[player].combo = 0;
		scores[player].combotimer.Stop();
	}

	internal void AddLife()
	{
		lives++;
	}

	public void Snapshot()
	{
	}

	internal void SnapshotRed()
	{
	}

	internal void DisableCombos()
	{
		combosenabled = false;
	}

	internal void EnableCombos()
	{
		combosenabled = true;
	}

	public void ShowStartMessages()
	{
		showPressStart = true;
		showPressStartTimes = 0;
		showPressStartTimer.Reset();
		showPressStartTimer.Start();
	}

	public void Tutorial_Show(ScorePart whatToShow)
	{
		foreach (ScoreInfo score in scores)
		{
			foreach (PowerupData value in score.powerupDatas.Values)
			{
				value.Tutorial_Show(whatToShow);
			}
		}
	}

	public void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		if (e.GameComponent != this)
		{
			return;
		}
		foreach (ScoreInfo score in scores)
		{
			foreach (PowerupData value in score.powerupDatas.Values)
			{
				collection.Remove((GameComponent)(object)value);
			}
		}
	}

	public void OnComponentAdded(GameComponentCollectionEventArgs e)
	{
	}

	internal int NrBombs(int player)
	{
		return scores[player].bombs;
	}

	internal void RemoveBomb(int player)
	{
		scores[player].bombs = Math.Max(0, scores[player].bombs - 1);
	}

	internal int GetPowerupLevel(Powerup.PowerupType powerupType, int player)
	{
		return scores[player].powerupDatas[powerupType].GetLevel();
	}

	internal void MaxExp(int player)
	{
		foreach (PowerupData value in scores[player].powerupDatas.Values)
		{
			value.MaxExp();
		}
	}

	internal float GetPowerupProgress(int player)
	{
		if (!scores[player].powerupactive)
		{
			return 0f;
		}
		return scores[player].powerupDatas[scores[player].powerup].GetProgress();
	}

	internal void RemovePowerup(int player)
	{
		scores[player].powerupactive = false;
		scores[player].powerupDatas[scores[player].powerup].FadeOut();
	}
}
