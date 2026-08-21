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
		// (most-significant) digit of scoreString rolls over (9->10, 1900->2000, ...) OR the
		// digit count grows (180 + 900 -> 1080, same lead char), then rests. lastLeadDigit +
		// lastLen track the previous frame's first char and length; glintElapsed counts the
		// one-shot sweep while glinting (see UpdateGlint / GlintTime).
		public char lastLeadDigit = '0';

		// Defaults to 0 while scoreString starts as "0" (len 1); the first UpdateTick's
		// change-branch then syncs it, and the scoreString == "0" guard suppresses any glint,
		// so the initial mismatch never fires a spurious sweep.
		public int lastLen;

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

		// Arm a one-shot glint sweep when the leading digit OR the digit count changes (skip
		// the reset-to-"0" and the empty edge case), then count the sweep up to its duration
		// once armed. Tracking length too catches a rollover that keeps the lead char but
		// gains a place (180 + 900 -> 1080), which a lead-char-only compare would miss.
		public void UpdateGlint(float dtSeconds)
		{
			char lead = (scoreString != null && scoreString.Length > 0) ? scoreString[0] : '0';
			int len = (scoreString != null) ? scoreString.Length : 0;
			if (lead != lastLeadDigit || len != lastLen)
			{
				lastLeadDigit = lead;
				lastLen = len;
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

		// Snap the glint baseline to the current scoreString WITHOUT arming a sweep — used on
		// checkpoint restore (Load) and score reset so a non-scored change doesn't glint.
		public void ResetGlintBaseline()
		{
			lastLeadDigit = (scoreString != null && scoreString.Length > 0) ? scoreString[0] : '0';
			lastLen = (scoreString != null) ? scoreString.Length : 0;
			glinting = false;
			glintElapsed = 0f;
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

	// Player score panels. Callers taking a slot from the wire must bound against this.
	internal const int SlotCount = 4;

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

	// Empty-slot prompt rotation (was a bool "Player X" <-> "Press Start"). Card 2001fbd8
	// makes it an index so a LISTED game can inject a third string "Room code: XYZAB" -- the
	// beacon. Drawn as promptPhase % (listed ? 3 : 2); free-running mod 6 so both cycle cleanly.
	private int promptPhase;

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
			float highest = 0f;
			foreach (ScoreInfo score in scores)
			{
				highest = MathHelper.Max(highest, score.score);
			}
			return highest;
		}
	}

	public ScoreVisualiser Score => this;

	private Vector2 livePosition(int i)
	{
		return new Vector2((float)(316 + i * 24 + 12), (float)((General.SafeZone).Bottom - 10));
	}

	public void RemoveLife()
	{
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
		IsTutorial = false;
		for (int i = 0; i < comboStrings.Length; i++)
		{
			comboStrings[i] = i + "x";
		}
		List<Powerup.PowerupType> enumValues = Game1.GetEnumValues<Powerup.PowerupType>();
		for (int j = 0; j < SlotCount; j++)
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
		int slot = -1;
		for (int i = 0; i < 4; i++)
		{
			if (scores[i].powerupDatas[type] == sender)
			{
				slot = i;
			}
		}
		PlayerShip playerShip = FindShip(slot);
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
			scores[i].ResetGlintBaseline();
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
			scoreInfo.ResetGlintBaseline();
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

	public float AddScore(float amount, bool isCombo, int player)
	{
		float points = ((!isCombo) ? amount : comboModify(amount, player));
		scores[player].SetScore(scores[player].score + points);
		return points;
	}

	// Online co-op (card af96bcc2): adopt the OWNER's declared total for a slot this peer does
	// not own, VERBATIM. One writer per slot -- this peer never credits a non-owned slot
	// (AwardScore's OwnsSlot gate), so the local copy is a plain replica of a single writer:
	// it cannot drift, only be one MsgHudState packet (~100 ms) stale.
	//
	// The two designs this supersedes, kept as one line each so nobody re-derives them: the
	// original max(local, host) adoption turned an unbiased per-kill combo difference into
	// unbounded one-way drift (card b0ab09ec's ratchet); the provisional-ledger reconciliation
	// that replaced it (NetScoreLedger, settle-on-EvDeath, host+unsettled adoption) existed
	// solely to reconcile TWO writers per slot, and one writer makes all of it dead code.
	// eaNetScore.test keeps both as its negative controls.
	internal void NetSetScore(int player, float declaredTotal)
	{
		if (player >= 0 && player < scores.Count)
		{
			scores[player].SetScore(declaredTotal);
		}
	}

	// Online co-op (card 1a3ad45a), SEND side: the per-slot HUD state this peer owns and is
	// therefore authoritative for. `levels` is filled for the leading NetProtocol.HudLevelCount
	// powerup types (OneUp's level never moves, so it is not on the wire).
	// Mirrors NetSetHudState's shape: null = no powerup active on that slot, so a read/set
	// round trip needs no conversion and no HudPowerupNone handling of its own.
	internal void NetReadHudState(int player, int[] levels, out int combo, out float comboLeft, out Powerup.PowerupType? activeType, out float progress)
	{
		combo = 0;
		comboLeft = 0f;
		activeType = null;
		progress = 0f;
		if (player < 0 || player >= scores.Count)
		{
			return;
		}
		ScoreInfo info = scores[player];
		combo = info.combo;
		// The combo TIMER's remaining fraction (v23, folding card a5b1e941): what drives the
		// readout's fade alpha, so the observer can park its own timer in phase with ours
		// instead of refreshing it to full on every packet.
		comboLeft = info.combotimer.Normalized;
		if (info.powerupactive)
		{
			activeType = info.powerup;
			progress = info.powerupDatas[info.powerup].GetProgress();
		}
		for (int t = 0; t < levels.Length && t < EvilAliensWeb.Compat.Net.NetProtocol.HudLevelCount; t++)
		{
			levels[t] = info.powerupDatas[(Powerup.PowerupType)t].GetLevel();
		}
	}

	// Online co-op (card 1a3ad45a), RECEIVE side: adopt the owner's HUD state for a slot this
	// peer does NOT own (NetSession gates the call), replacing the local simulation increasecombo
	// no longer runs for it.
	//
	// Nobody SPENDS the adopted combo any more (card af96bcc2): each peer pays only slots it
	// owns, with its own local combo, so this figure is display-plus-bookkeeping -- the combo
	// readout, its fade, and the powerup bar. The slot's SCORE arrives separately on the same
	// packet and is adopted verbatim by NetSetScore.
	internal void NetSetHudState(int player, int combo, float comboLeft, Powerup.PowerupType? activeType, float progress, int[] levels)
	{
		if (player < 0 || player >= scores.Count || levels == null)
		{
			return;
		}
		ScoreInfo info = scores[player];
		info.combo = combo;
		// The readout's alpha is driven by combotimer.TimeLeft, and SustainCombo no longer runs
		// for this slot -- so without touching it here a replicated combo would draw at the
		// floor alpha and then be zeroed by the timer's own expiry. Since v23 (folding card
		// a5b1e941) the timer is PARKED at the owner's replicated remaining time rather than
		// refreshed to full: the alpha ramp tracks the owner's and the fade-out lands when
		// theirs does (~one 100 ms packet stale), instead of up to a full second late. Applied
		// only while the owner reports a live combo -- their own expiry zeroes the combo, so
		// the next packet carries combo == 0 and this slot's timer is left to lapse on its own.
		if (combo > 0)
		{
			info.combotimer.Start();
			info.combotimer.SetNormalized(comboLeft);
		}
		// Levels FIRST: a level change zeroes the bar (NetSetLevel), so applying progress before
		// them would have a catch-up packet snap the bar empty for an interval.
		for (int t = 0; t < levels.Length && t < EvilAliensWeb.Compat.Net.NetProtocol.HudLevelCount; t++)
		{
			NetSetPowerupLevel(player, (Powerup.PowerupType)t, levels[t]);
		}
		// Null = no powerup active on that slot. The wire byte is validated (and the
		// HudPowerupNone sentinel folded in) at the decode boundary -- see the wire-enum
		// contract in NetProtocol; do not re-test the range here.
		if (activeType.HasValue)
		{
			// SetPowerup restarts the panel's fade, so only touch it on a real change -- at the
			// ~10Hz HUD cadence an unconditional call would hold the bar permanently fading in.
			Powerup.PowerupType type = activeType.Value;
			if (!info.powerupactive || info.powerup != type)
			{
				SetPowerup(type, player);
			}
			info.powerupDatas[type].NetSetProgress(progress);
		}
		else if (info.powerupactive)
		{
			RemovePowerup(player);
		}
	}

	// Raise one slot's powerup level to the owner's, one step at a time through the SAME
	// PlayerShip.PowerUp path a local level-up takes -- so the puppet's re-fired bullets get the
	// owner's real asploding/bouncing/splitting loadout and its Option ships actually spawn.
	// Card d53431b4: a remote player levelling up shows the SAME PowerupEffect sparkle a local one
	// does -- but only when this really is a level-up, i.e. a climb of exactly ONE step. A
	// multi-step climb is a CATCH-UP (a join-in-progress peer adopting a slot already at level 4,
	// or the first HUD packet for a slot) and would fire four sparkles in one tick for events that
	// happened before we were watching. It needs no per-slot state, since a genuine level-up can
	// only ever be one step.
	// The converse does NOT hold, and the false positive is accepted: a peer whose FIRST packet
	// for a slot reports level 1 is a catch-up too, and sparkles. One sparkle at a join is not
	// worth a per-slot "have we seen this one yet" latch.
	//
	// OneUp is unreachable here (it is past HudLevelCount) and must stay that way. Since card
	// a66e190a the slow motion's EFFECT replicates (as EvSlowmo, so both peers scale together),
	// but its TRIGGER is still the owner's alone: a peer must never fire one off a slot it does
	// not own, which is what levelling OneUp here would do. A DOWN step (a reset the peers reached at different moments) only snaps the
	// readout; PowerUp's fields are MathHelper.Max accumulations and cannot be walked back.
	private void NetSetPowerupLevel(int player, Powerup.PowerupType type, int level)
	{
		// NetSetLevel is a deliberate no-op for OneUp (its level is pinned at 3), so a climb loop
		// would never advance and would hang the WASM thread -- a frozen tab, not a wrong number.
		// Unreachable today only because the caller stops at HudLevelCount and OneUp sits past it;
		// this is the guard that keeps that from being one constant away from a hang.
		if (type == Powerup.PowerupType.OneUp)
		{
			return;
		}
		level = Math.Clamp(level, 0, 4);
		PowerupData data = scores[player].powerupDatas[type];
		if (level < data.GetLevel())
		{
			data.NetSetLevel(level);
			return;
		}
		bool singleStep = level - data.GetLevel() == 1;
		while (data.GetLevel() < level)
		{
			int before = data.GetLevel();
			data.NetSetLevel(before + 1);
			if (data.GetLevel() == before)
			{
				break;
			}
			FindShip(player)?.PowerUp(type, data.GetLevel(), doEffect: singleStep);
		}
	}

	// `oracle` is bound in Initialize, which only runs once this component is added to the bin --
	// i.e. inside a GameScene. The HUD-state path (card 1a3ad45a) can be reached before that
	// (eaNetCombo.test from the main menu, and a wire packet that outruns the scene), so the null
	// is real: no ship exists to power up yet, and the level still lands on the panel.
	private PlayerShip FindShip(int slot)
	{
		if (oracle == null)
		{
			return null;
		}
		foreach (PlayerShip ship in oracle.GetShips())
		{
			if (ship.Owner == slot)
			{
				return ship;
			}
		}
		return null;
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
		bool hasHumanPlayer = false;
		foreach (PlayerShip ship in oracle.GetShips())
		{
			if (ship.Controller != ControlDevice.AI)
			{
				hasHumanPlayer = true;
			}
		}
		if (GetPowerupLevel(Powerup.PowerupType.Blast, player) == 4 && GetPowerupLevel(Powerup.PowerupType.FirePower, player) == 4 && GetPowerupLevel(Powerup.PowerupType.Option, player) == 4 && GetPowerupLevel(Powerup.PowerupType.Range, player) == 4 && !IsTutorial && !Settings.GetInstance().CheckForCheats() && hasHumanPlayer)
		{
			ServiceHelper.Get<IAwardmentBladeService>().get().AwardAchievement(Awardment.FullPower);
		}
	}

	// Online co-op (card 1a3ad45a): a slot's combo is a purely LOCAL simulation, and it runs for
	// EVERY slot -- a remote ship's shots are re-fired here through the real FireAt path, so they
	// are ordinary local Bullets stamped with that slot's owner and Bullet.CollidesWith lands
	// here. On a client those bullets hit frozen puppets interpolated ~100ms behind the host's
	// real entities, so the two sims diverge routinely.
	//
	// The counter diverging is cosmetic; what it DRIVES is not. increasecombo feeds AddExp while
	// the slot's powerupactive is set -- which card 4717d3cf started doing for a remote collector
	// -- and the resulting onLevelUp calls PlayerShip.PowerUp on the PUPPET. For OneUp that is
	// Oracle.SetSlowmotion(12f): twelve seconds of global slow motion fired unilaterally on one
	// peer off an invented combo. Option spawns a real extra Option ship; FirePower/Range give
	// the puppet a weapon its owner does not have.
	//
	// So the whole simulation is the OWNER's -- not just the AddExp branch. Gating only that
	// would leave AddCombo incrementing between the owner's 100ms packets and the combotimer
	// zeroing a live combo whenever OUR re-fired bullets miss for a second, i.e. the replicated
	// value fighting a local one. The owner's real combo, bar and levels arrive over MsgHudState
	// (NetSetHudState) instead. OwnsSlot is true offline and for our own slots, so single-player
	// and local co-op are unchanged.
	public void SustainCombo(int player, Vector2 location)
	{
		if (combosenabled && EvilAliensWeb.Compat.Net.NetSession.OwnsSlot(player))
		{
			increasecombo(player);
			scores[player].combotimer.Start();
			scores[player].combotimer.Reset();
			CheckPowerup(ref location, player);
		}
	}

	// The positional overload -- the ONE place a "+10" floater is born. The score itself is
	// credited either way; what is gated is the popup.
	//
	// Online co-op (card 7a8ec0d3): a floating score belongs to the player who earned it, and
	// only their own screen shows it. Since card af96bcc2 the CREDIT is gated one level up
	// (AwardScore writes only owned slots), so in practice this fires for owned slots only --
	// the gate stays here as well because this overload is also reachable directly (`?level=`
	// debug paths, future callers) and a popup for a slot whose credit was refused would be a
	// lie. OwnsSlot is unconditionally true offline and for couch slots, so
	// single-player and local co-op are unchanged, and a couch partner sharing your screen
	// still gets their own popups.
	//
	// The other two floater kinds need no gate: CheckPowerup's "Power Up!" and combo pops only
	// run inside SustainCombo, which card 1a3ad45a already gated on the same predicate.
	public float AddScore(float amount, bool isCombo, Vector2 location, int player)
	{
		float points = AddScore(amount, isCombo, player);
		if (EvilAliensWeb.Compat.Net.NetSession.OwnsSlot(player))
		{
			FloatingText floater = GetText((int)points, location, FloatingText.ShowType.scrollup, "");
			floatingtexts.Add(floater);
		}
		return points;
	}

	// How many floaters are in flight. A floater leaves no other trace -- it is a local list
	// drawn and then recycled, moving no score, no metric and no component -- so this readback
	// is what lets NetLocalFxTest assert the gate above instead of eyeballing a screenshot.
	internal int FloatingTextCount => floatingtexts.Count;

	private void CheckPowerup(ref Vector2 location, int player)
	{
		if (displayPowerUpAtNextHit)
		{
			displayPowerUpAtNextHit = false;
			soundManager.PlayText(SoundManager.Texts.PowerUp, 1);
			FloatingText floater = GetText(location, FloatingText.ShowType.pop, "Power Up!");
			floatingtexts.Add(floater);
		}
		else if (scores[player].combo % 10 == 0 && scores[player].combo > 0)
		{
			FloatingText floater = GetText(scores[player].combo, location, FloatingText.ShowType.pop, "X");
			floatingtexts.Add(floater);
		}
	}

	private FloatingText GetText(Vector2 location, FloatingText.ShowType type, string suffix)
	{
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
		promptPhase = 0;
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
			// Per SLOT, not `i < Players`: online co-op's roster is host-allocated and sparse, so a
			// hole would show an empty panel for a seat nobody has and hide a real player's score.
			if (oracle.IsSeated(i))
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
			int fw = (playersheet.LogicalWidth() - 7) / 8;
			int fh = (playersheet.LogicalHeight() - 3) / 4;
			spriteBatch.Draw(playersheet, new Rectangle(0, 0, fw, fh), livePosition(j), 0f, 0.5f * 48f / (float)fw, center: true, new Color(new Vector4(1f, 1f, 1f, 0.5f)));
		}
		if (explosion.Active)
		{
			explosion.Draw(gameTime);
		}
		if (phototimer.Active)
		{
			float alpha = MathHelper.SmoothStep(0f, 1f, phototimer.Normalized);
			Color photoTint = new Color(new Vector4((snapshotcolor).ToVector3(), alpha));
			float photoSsf = AlienDrawableGameComponent.SuperSampleFactor("GFX/Sprites/photocamera", photocamera.LogicalWidth());
			spriteBatch.Draw(photocamera, new Vector2(400f, (float)(General.SafeZone).Top + (float)photocamera.LogicalHeight() / photoSsf / 2f), 0f, 1f / photoSsf, center: true, photoTint);
		}
		spriteBatch.BlendMode = (SpriteBlendMode)1;
	}

	private void drawPressStart(GameTime gameTime, int i, ref Color playercolor, ref Vector2 startpos)
	{
		if (showPressStartTimer.Active)
		{
			float alpha = 1f;
			if (showPressStartTimer.TimeElapsed < 500f)
			{
				alpha = showPressStartTimer.TimeElapsed / 500f;
			}
			if (0f <= showPressStartTimer.TimeLeft - 3000f && showPressStartTimer.TimeLeft - 3000f < 500f)
			{
				alpha = (showPressStartTimer.TimeLeft - 3000f) / 500f;
			}
			if (showPressStartTimer.TimeLeft - 3000f < 0f)
			{
				alpha = 0f;
			}
			alpha = MathHelper.SmoothStep(0f, 1f, alpha);
			Vector4 baseColor = (playercolor).ToVector4();
			Color aliceBlue = Color.AliceBlue;
			Color color = new Color(Vector4.Lerp(baseColor, (aliceBlue).ToVector4(), alpha));
			string playerLabel = i switch
			{
				0 => "Player 1",
				1 => "Player 2",
				2 => "Player 3",
				3 => "Player 4",
				_ => "Gah?",
			};
			// Card 2001fbd8 beacon: while the game is listed online the rotation gains a third
			// string carrying the room code, so a streamer can read it out on any cycle.
			// Card 10d9f8e3: the code is shown BARE, with no "Room code: " label. This slot is
			// a corner prompt sharing one narrow column with "Player 2"/"Press Start", and the
			// label cost more width than it bought -- the only thing a viewer needs to copy is
			// the five characters, and the rotation it sits in already gives them their context.
			// The LABELLED spellings elsewhere stay (MenuScene's lobby panel, SubMenuOnlineGames'
			// browser row): those are full-width panels where the label is the only thing saying
			// what the five characters are.
			string code = EvilAliensWeb.Compat.Net.NetListing.RoomCode;
			bool listed = EvilAliensWeb.Compat.Net.NetListing.Listed && !string.IsNullOrEmpty(code);
			int phase = promptPhase % (listed ? 3 : 2);
			string str = phase switch
			{
				1 => "Press Start",
				2 => code,
				_ => playerLabel,
			};
			// Inactive-slot prompt: static chrome, never a sweep (no score to roll over). Shares the
			// slot's primary-line cache key with the active-player score (only one is drawn per slot
			// per frame; the dirty check rebuilds when a slot flips between prompt and score).
			DrawStr(i * 4, str, startpos + new Vector2(0f, -5f), 0.9f, alpha * 0.6f, color, ParkedGlint);
		}
	}

	private void drawPlayerScore(int i, ref Color playercolor, ref Vector2 startpos, GameTime gameTime)
	{
		// Only the score NUMBER is event-driven (sweeps on a leading-digit rollover); the combo
		// readout has no "first digit", so it keeps the static chrome with no sweep (ParkedGlint).
		// Cache keys: i*4 = primary line (score), i*4+1 = "Combo!" label, i*4+2 = combo count.
		DrawStr(i * 4, scores[i].scoreString, startpos + new Vector2(0f, -5f), 0.9f, 1f, playercolor, GlintTime(i));
		if (scores[i].combo > 5)
		{
			float alpha = 0.2f + 0.8f * MathHelper.SmoothStep(0f, 1f, scores[i].combotimer.TimeLeft / 1000f);
			float comboX = MathHelper.Max(font.MeasureString(scores[i].scoreString).X * 0.9f + 17f, 100f);
			DrawStr(i * 4 + 1, "Combo!", startpos + new Vector2(comboX - 10f, -5f), 0.6f, alpha, playercolor, ParkedGlint);
			if (scores[i].combo < 1000)
			{
				DrawStr(i * 4 + 2, comboStrings[scores[i].combo], startpos + new Vector2(comboX, 13f), 1f, alpha, playercolor, ParkedGlint);
			}
			else
			{
				DrawStr(i * 4 + 2, scores[i].combo + "x", startpos + new Vector2(comboX, 13f), 1f, alpha, playercolor, ParkedGlint);
			}
		}
		float bombSsf = AlienDrawableGameComponent.SuperSampleFactor("GFX/Sprites/bombicon", bomb.LogicalWidth());
		for (int j = 0; j < scores[i].bombs; j++)
		{
			spriteBatch.Draw(bomb, startpos + new Vector2((float)(30 + bomb.LogicalWidth() / bombSsf * j), 45f), 0f, 1f / bombSsf, center: false, Color.White);
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

	// cacheKey identifies the persistent HUD element this string belongs to (per player slot + role,
	// see the call sites) so SpriteBatchWrapper.DrawShadowStringCached re-rasterises it only when the
	// text/scale/colour actually change instead of every frame.
	private void DrawStr(int cacheKey, string str, Vector2 position, float scale, float alpha, Color color, float glintTime)
	{
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
		// (chrome-sheen, ON by default — restored by card 16dad393; ?metalscore=0 A/Bs the plain
		// flatten) routes it through metal.fx. The chrome darkens the mid-band, so the metal
		// score reads a touch more solid (0.7) than the plain flatten (0.55) to compensate.
		bool metal = DebugFlags.MetalScore;
		float opacity = alpha * (metal ? 0.7f : 0.55f);
		spriteBatch.DrawShadowStringCached(cacheKey, str, position, scale, shadowColor, textColor, new Vector2(2f, 2f), opacity, metal, glintTime);
	}

	public override void Update(GameTime gameTime)
	{
		// Beacon (card 2001fbd8): while listed online, keep the empty-slot prompt cycling
		// forever (it carries the room code). ShowStartMessages normally stops it after 4
		// cycles; if that already fired, restart it here the moment the game becomes listed.
		bool listed = EvilAliensWeb.Compat.Net.NetListing.Listed;
		if (listed && !showPressStartTimer.Active)
		{
			showPressStartTimes = 0;
			showPressStartTimer.Reset();
			showPressStartTimer.Start();
		}
		showPressStartTimer.Update(gameTime);
		if (showPressStartTimer.Finished)
		{
			promptPhase = (promptPhase + 1) % 6;
			showPressStartTimes++;
			if (showPressStartTimes >= 4 && !listed)
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
		promptPhase = 0;
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

	// Is a powerup indicator lit on this slot? The gate `increasecombo` reads before feeding
	// AddExp, so it is live game state and not decoration -- a self-test that settles a real
	// pickup has to put it back (card 25ad0659 step 4).
	internal bool NetPowerupActive(int player)
	{
		return scores[player].powerupactive;
	}

	internal void RemovePowerup(int player)
	{
		scores[player].powerupactive = false;
		scores[player].powerupDatas[scores[player].powerup].FadeOut();
	}
}
