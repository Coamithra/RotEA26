using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using EvilAliensWeb.Compat;

namespace EvilAliens;

internal class MenuSub1 : Scene
{
	protected class UnlockableData
	{
		public bool isUnlockable;

		public Unlockables.Items item;

		public UnlockableData(Unlockables.Items item)
		{
			isUnlockable = true;
			this.item = item;
		}

		public UnlockableData()
		{
			isUnlockable = false;
		}
	}

	protected enum SubMenuState
	{
		entry,
		normal,
		exit
	}

	public delegate void ExitMenu(MenuSub1 sender);

	public delegate void ItemSelected(MenuSub1 sender);

	public delegate void TimeOut(MenuSub1 sender);

	private bool isScrolling;

	protected Curve brainPulsate;

	protected bool allowNormalExit = true;

	protected ControlDevice? controller;

	private bool firstUpdate;

	private Timer timeouttimer = new Timer(20000f, repeating: false);

	protected SubMenuState state;

	private Vector2 origin = new Vector2(400f, 300f);

	protected List<string> menuEntries = new List<string>();

	protected List<UnlockableData> unLockableDataEntries = new List<UnlockableData>();

	protected int selectedEntry;

	// --- Mouse selection (card: menus should be mouse selectable & clickable) ---
	// The menus' layouts differ too much (centred lists, the framed main menu, the
	// difficulty column, the level carousel) to hit-test from one formula, so each DrawMenu
	// records the design-space (800x600) box of every entry it draws (RecordEntryHit;
	// locked/undrawn entries are skipped, so they never become hittable). HandleMouse maps
	// the cursor onto one of those boxes — see it for the full design.
	private readonly List<(int index, Rectangle rect)> entryHitBounds = new List<(int index, Rectangle rect)>();

	private Vector2 lastMousePos;

	private bool mouseInitialised;

	// Set by HandleMouse when a click invokes an entry's event; HandleInput then returns
	// before running its keyboard blocks against a menu the click may have removed.
	private bool mouseActivated;

	// The level/challenge carousel opts out of hover-select: gliding the cursor across a
	// flying screenshot shouldn't snap the selection — only a click selects there.
	protected bool mouseHoverSelects = true;

	// Card e3c78bb8: on a carousel, a click on an entry that is NOT the centred one only
	// scrolls the carousel to it -- it takes a second click, on the now-centred entry, to
	// activate. The default (any click selects AND activates) is right for a static list,
	// where what you click is what you get; on a carousel the side entries are small,
	// partly off-screen and still flying, so treating a click on one as "launch that level"
	// meant aiming at a moving target with a level start as the penalty for missing.
	// Deliberately NOT folded into mouseHoverSelects: that one says hover does not select,
	// which is a different question with a different answer for a hypothetical static menu.
	protected bool mouseClickSelectsBeforeActivating;

	protected SpriteFont font;

	private RenderTarget2D myRenderTarget;

	private Timer fadeTimer = new Timer(400f, repeating: false);

	// Perf batch 2: the selected-entry glow was two rings (r=4 and r=8) of 8 offsets each,
	// rebuilt into fresh float[]/Vector2[] arrays every frame. The offsets are constant, so
	// hoist them into one static array (flattened; each is drawn with the same glow colour).
	private static readonly Vector2[] SelectionGlowRing = BuildGlowRing(4f, 8f);

	private static Vector2[] BuildGlowRing(params float[] radii)
	{
		Vector2[] result = new Vector2[radii.Length * 8];
		int n = 0;
		foreach (float r in radii)
		{
			float d = r * 0.7071f;
			result[n++] = new Vector2(r, 0f);
			result[n++] = new Vector2(0f - r, 0f);
			result[n++] = new Vector2(0f, r);
			result[n++] = new Vector2(0f, 0f - r);
			result[n++] = new Vector2(d, d);
			result[n++] = new Vector2(0f - d, d);
			result[n++] = new Vector2(d, 0f - d);
			result[n++] = new Vector2(0f - d, 0f - d);
		}
		return result;
	}

	public List<ItemSelected> ItemSelectedEvents = new List<ItemSelected>();

	public int GetSelectedEntry => selectedEntry;

	// True while the menu is still playing its zoom-in entry animation. MenuScene holds
	// the HUD ring's recalibrate until this clears, so the ring reacts to the menu having
	// appeared rather than moving in lock-step with it.
	public bool IsEntering => state == SubMenuState.entry;

	public event ExitMenu OnExit;

	public event TimeOut OnTimeOut;

	public MenuSub1(Game game)
		: base(game)
	{
		base.DrawOrder = 2;
		controller = null;
	}

	public void SetScrolling()
	{
		isScrolling = true;
	}

	public void RemoveEntry(string text)
	{
		for (int i = 0; i < menuEntries.Count; i++)
		{
			if (menuEntries[i] == text)
			{
				if (i == selectedEntry)
				{
					selectNext();
				}
				menuEntries.RemoveAt(i);
				unLockableDataEntries.RemoveAt(i);
				ItemSelectedEvents.RemoveAt(i);
				if (selectedEntry > i)
				{
					selectedEntry--;
				}
			}
		}
	}

	public void RemoveAllEntries()
	{
		for (int i = 0; i < menuEntries.Count; i++)
		{
			menuEntries.Clear();
			unLockableDataEntries.Clear();
			ItemSelectedEvents.Clear();
		}
	}

	public void AddEntry(string text)
	{
		menuEntries.Add(text);
		ItemSelectedEvents.Add(null);
		unLockableDataEntries.Add(new UnlockableData());
	}

	public void AddEntry(string text, Unlockables.Items lockItem)
	{
		menuEntries.Add(text);
		ItemSelectedEvents.Add(null);
		unLockableDataEntries.Add(new UnlockableData(lockItem));
	}

	public void AddEntryEvent(ItemSelected selectedEvent)
	{
		ItemSelectedEvents[menuEntries.Count - 1] = selectedEvent;
	}

	internal void SetEntry(int p, string p_2)
	{
		menuEntries[p] = p_2;
	}

	public void SetEntry(string newText)
	{
		menuEntries[selectedEntry] = newText;
	}

	public virtual void Reset()
	{
		selectedEntry = 0;
		timeouttimer.Reset();
		timeouttimer.Start();
	}

	public override void Initialize()
	{
		timeouttimer.Reset();
		timeouttimer.Start();
		base.Initialize();
		state = SubMenuState.entry;
		fadeTimer.Reset();
		fadeTimer.Start();
		firstUpdate = true;
		// Re-seed mouse tracking each time the menu is shown so the first frame can't read a
		// stale movement delta (these menus are long-lived and re-Show()n).
		mouseInitialised = false;
	}

	public override void Update(GameTime gameTime)
	{
		fadeTimer.Update(gameTime);
		timeouttimer.Update(gameTime);
		switch (state)
		{
		case SubMenuState.entry:
			if (fadeTimer.Finished)
			{
				state = SubMenuState.normal;
			}
			break;
		case SubMenuState.exit:
			if (fadeTimer.Finished)
			{
				Collection.Remove((GameComponent)(object)this);
			}
			break;
		}
		if ((state == SubMenuState.entry) | (state == SubMenuState.normal))
		{
			HandleInput();
		}
		if (timeouttimer.Finished)
		{
			if (this.OnTimeOut != null)
			{
				this.OnTimeOut(this);
			}
			timeouttimer.Reset();
			timeouttimer.Start();
		}
	}

	private void HandleInput()
	{
		bool acted = false;
		if (firstUpdate)
		{
			firstUpdate = false;
			return;
		}
		if (HandleMouse())
		{
			acted = true;
		}
		if (mouseActivated)
		{
			// A click already activated an entry; the handler may have removed this menu or
			// shown another, so don't fall through to the keyboard blocks this frame.
			timeouttimer.Reset();
			return;
		}
		bool backPressed = false;
		for (int i = 0; i < 4; i++)
		{
			if (!controller.HasValue || controlDeviceToInt(controller.Value) == i)
			{
				backPressed |= base.InputHandler.PadPressed(PadKeys.Back, i) || base.InputHandler.PadPressed(PadKeys.B, i);
			}
		}
		if (!controller.HasValue || controller.Value == ControlDevice.Keyboard)
		{
			backPressed |= base.InputHandler.Pressed(MyKeys.Esc);
		}
		if (backPressed && allowNormalExit)
		{
			if (this.OnExit != null)
			{
				this.OnExit(this);
			}
			acted = true;
		}
		if (menuEntries.Count <= 0)
		{
			return;
		}
		bool prevPressed = false;
		if (!controller.HasValue || controller.Value == ControlDevice.Keyboard)
		{
			prevPressed |= base.InputHandler.Pressed(MyKeys.Up) | base.InputHandler.Pressed(MyKeys.Left);
		}
		for (int j = 0; j < 4; j++)
		{
			if (!controller.HasValue || controlDeviceToInt(controller.Value) == j)
			{
				prevPressed |= base.InputHandler.PadPressed(PadKeys.Up, j);
				prevPressed |= base.InputHandler.PadPressed(PadKeys.Left, j);
			}
		}
		if (prevPressed)
		{
			selectPrevious();
			acted = true;
		}
		bool nextPressed = false;
		if (!controller.HasValue || controller.Value == ControlDevice.Keyboard)
		{
			nextPressed |= base.InputHandler.Pressed(MyKeys.Down) | base.InputHandler.Pressed(MyKeys.Right);
		}
		for (int k = 0; k < 4; k++)
		{
			if (!controller.HasValue || controlDeviceToInt(controller.Value) == k)
			{
				nextPressed |= base.InputHandler.PadPressed(PadKeys.Right, k);
				nextPressed |= base.InputHandler.PadPressed(PadKeys.Down, k);
			}
		}
		if (nextPressed)
		{
			selectNext();
			acted = true;
		}
		bool selectPressed = false;
		if (!controller.HasValue || controller.Value == ControlDevice.Keyboard)
		{
			selectPressed |= base.InputHandler.Pressed(MyKeys.Enter) | base.InputHandler.Pressed(MyKeys.Generic_Start);
		}
		for (int l = 0; l < 4; l++)
		{
			if (!controller.HasValue || controlDeviceToInt(controller.Value) == l)
			{
				selectPressed |= base.InputHandler.PadPressed(PadKeys.Start, l);
				selectPressed |= base.InputHandler.PadPressed(PadKeys.A, l);
			}
		}
		if (selectPressed)
		{
			if (ItemSelectedEvents[selectedEntry] != null)
			{
				ItemSelectedEvents[selectedEntry](this);
			}
			acted = true;
		}
		if (acted)
		{
			timeouttimer.Reset();
		}
	}

	// Maps the cursor onto a menu entry using the boxes captured by the last DrawMenu's
	// RecordEntryHit calls. InputHandler.MousePosition is already in 800x600 design space
	// (RenderScale.WindowToDesign), so it compares directly to the boxes. Hover highlights
	// (set selectedEntry); a left-click selects AND activates that entry — the same effect
	// as arrowing to it and pressing Enter. Returns true if it changed the selection or
	// activated an entry, so HandleInput resets the attract-demo timeout.
	// Gated on the normal state: only there is the layout static frame-to-frame (so the
	// box captured last frame is still exactly right), and the composited menu sits 1:1 on
	// its design-space coords rather than mid entry/exit zoom.
	private bool HandleMouse()
	{
		mouseActivated = false;
		if (state != SubMenuState.normal || entryHitBounds.Count == 0)
		{
			return false;
		}
		Vector2 m = base.InputHandler.MousePosition;
		// The first frame after the menu (re)appears only SEEDS the position: a cursor that
		// happens to rest over a different entry must not snap the selection away from what
		// Reset()/Initialize() chose — hover-select waits for a real movement delta.
		bool moved = mouseInitialised && (m - lastMousePos).LengthSquared() > 1f;
		lastMousePos = m;
		mouseInitialised = true;
		int hovered = -1;
		Point p = new Point((int)m.X, (int)m.Y);
		// Iterate back-to-front so an entry drawn later (e.g. the carousel's centred,
		// overlapping screenshot) wins the hit over the ones drawn under it.
		for (int i = entryHitBounds.Count - 1; i >= 0; i--)
		{
			if (entryHitBounds[i].rect.Contains(p))
			{
				hovered = entryHitBounds[i].index;
				break;
			}
		}
		// Any real cursor movement counts as the player being present, so it resets the
		// attract-demo idle timeout (the caller resets on a true return) — even when the
		// cursor isn't over an entry. Without this, only selection changes and clicks kept
		// the demo away, so nudging the mouse around blank menu space still dropped to attract.
		bool changed = moved;
		if (hovered >= 0)
		{
			if (mouseHoverSelects && moved && hovered != selectedEntry)
			{
				selectedEntry = hovered;
				changed = true;
			}
			if (base.InputHandler.Pressed(MyKeys.Mouse1))
			{
				// A carousel's off-centre entries only get selected (which starts the scroll);
				// everything else selects and activates in one click, as before.
				// `hovered == selectedEntry` alone is not enough: selection is instant but the
				// scroll is an ANIMATION, so a quick double-click on a side tile would satisfy
				// it on the second click and launch the level anyway -- the very thing this is
				// meant to prevent. MouseActivationSettled makes the carousel say when the tile
				// has actually arrived under the cursor.
				bool activate = !mouseClickSelectsBeforeActivating
					|| (hovered == selectedEntry && MouseActivationSettled());
				selectedEntry = hovered;
				if (activate)
				{
					if (ItemSelectedEvents[selectedEntry] != null)
					{
						ItemSelectedEvents[selectedEntry](this);
					}
					mouseActivated = true;
				}
				changed = true;
			}
		}
		return changed;
	}

	// The clickable "(B) back" tip (card 2a4110d0) is a hit box the menus know nothing about,
	// so its one hard invariant -- it must not overlap ANY menu entry's box, or a single click
	// would both go back AND activate whatever row it landed on -- has no natural owner and no
	// visible failure until a player hits it. It holds today with room to spare (the tip lives
	// at x<=~150, the widest main-menu frame starts at x~218), but it is held by two unrelated
	// layouts: widen a frame, lengthen a label, or move the safe zone and it silently stops
	// holding. So every menu reports the verdict itself, once per layout change (not per
	// frame), positive case included -- a probe that only looked for the failure line would
	// pass just as happily on a run that never opened a menu.
	// Compared instead of the formatted line, so the common case (nothing changed) costs no
	// allocation -- this runs in Draw, every frame, per shown menu, and formatting a string only
	// to throw it away is the exact per-frame garbage the "Perf batch 2" work above removed.
	private Rectangle lastReportedTip;

	private int lastReportedEntries = -1;

	private int lastReportedOverlap = -2;

	private void ReportBackTipOverlap()
	{
		if (!EvilAliensWeb.Compat.BackTipHit.TryGetRect(out Rectangle tip))
		{
			return;
		}
		int hit = -1;
		for (int i = 0; i < entryHitBounds.Count; i++)
		{
			if (entryHitBounds[i].rect.Intersects(tip))
			{
				hit = entryHitBounds[i].index;
				break;
			}
		}
		if (hit == lastReportedOverlap && entryHitBounds.Count == lastReportedEntries && tip == lastReportedTip)
		{
			return;
		}
		lastReportedOverlap = hit;
		lastReportedEntries = entryHitBounds.Count;
		lastReportedTip = tip;
		Console.WriteLine($"[backtip] menu={GetType().Name} entries={entryHitBounds.Count} tip={tip.X},{tip.Y},{tip.Width},{tip.Height} overlap={((hit < 0) ? "none" : hit.ToString())}");
	}

	// Whether a click may activate the selected entry right now. Only consulted when
	// `mouseClickSelectsBeforeActivating` is set; a menu whose layout is static the moment the
	// selection changes (i.e. every non-carousel one) is always settled.
	protected virtual bool MouseActivationSettled()
	{
		return true;
	}

	// Records entry `index`'s clickable box (design space, 800x600), centred at `centre`.
	// Each DrawMenu calls this for every entry it actually draws; consumed by HandleMouse
	// on the next frame.
	protected void RecordEntryHit(int index, Vector2 centre, float width, float height)
	{
		int w = (int)Math.Ceiling(width);
		int h = (int)Math.Ceiling(height);
		entryHitBounds.Add((index, new Rectangle((int)Math.Round(centre.X - (float)w / 2f), (int)Math.Round(centre.Y - (float)h / 2f), w, h)));
	}

	private int controlDeviceToInt(ControlDevice device)
	{
		return device switch
		{
			ControlDevice.PadOne => 0, 
			ControlDevice.PadTwo => 1, 
			ControlDevice.PadThree => 2, 
			ControlDevice.PadFour => 3, 
			_ => -1, 
		};
	}

	protected virtual void selectNext()
	{
		do
		{
			selectedEntry = MyMath.Mod(selectedEntry + 1, menuEntries.Count);
		}
		while (unLockableDataEntries[selectedEntry].isUnlockable && !Unlockables.GetInstance().IsUnlocked(unLockableDataEntries[selectedEntry].item));
	}

	protected virtual void selectPrevious()
	{
		do
		{
			selectedEntry = MyMath.Mod(selectedEntry - 1, menuEntries.Count);
		}
		while (unLockableDataEntries[selectedEntry].isUnlockable && !Unlockables.GetInstance().IsUnlocked(unLockableDataEntries[selectedEntry].item));
	}

	protected void doExit()
	{
		if (this.OnExit != null)
		{
			this.OnExit(this);
		}
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		brainPulsate = Content.Load<Curve>("GFX/Effects/BrainCurve");
		font = Content.Load<SpriteFont>("GFX/Menu/menufont");
		EnsureRenderTarget();
	}

	// Stage 10: the menu renders its entries into this offscreen target (so the whole
	// menu can be scaled+faded as a unit on entry/exit), then composites it into the
	// scene. Size it to the unified render resolution (RenderScale) so the menu text is
	// crisp and the 1:1 DrawPresent composite aligns with the scene. Use Color (RGBA8):
	// the original window-sized Bgr565 target renders nothing on WebGL (Stage 5).
	// PreserveContents ((RenderTargetUsage)1) is kept. Recreated on a render-size change.
	private void EnsureRenderTarget()
	{
		int w = RenderScale.Width;
		int h = RenderScale.Height;
		if (myRenderTarget != null && ((Texture2D)myRenderTarget).Width == w && ((Texture2D)myRenderTarget).Height == h)
		{
			return;
		}
		if (myRenderTarget != null)
		{
			((Texture2D)myRenderTarget).Dispose();
		}
		myRenderTarget = new RenderTarget2D(base.GraphicsDevice, w, h, false, SurfaceFormat.Color, DepthFormat.None, 0, (RenderTargetUsage)1);
	}

	protected override void UnloadContent()
	{
		base.UnloadContent();
		if (myRenderTarget != null)
		{
			((Texture2D)myRenderTarget).Dispose();
		}
		myRenderTarget = null;
	}

	public virtual void DrawMenu(GameTime gameTime, float yoffset)
	{
		Vector2 position = default(Vector2);
		if (isScrolling)
		{
			(position) = new Vector2(origin.X, yoffset + origin.Y - (float)(selectedEntry * font.LineSpacing));
		}
		else
		{
			(position) = new Vector2(origin.X, yoffset + origin.Y - (float)(font.LineSpacing * menuEntries.Count) / 3f);
		}
		Vector2 entryOrigin = default(Vector2);
		// Stage 13: metal-sheen glint clock — shared by every entry so the rows glint in sync.
		float time = (float)gameTime.TotalGameTime.TotalSeconds;
		for (int i = 0; i < menuEntries.Count; i++)
		{
			Color color;
			float entryScale;
			if (i == selectedEntry)
			{
				float pulseAmount = 15f / font.MeasureString(menuEntries[i]).X;
				// Same clock as `time` above, read a second time -- not a second clock.
				float pulseTime = (float)gameTime.TotalGameTime.TotalSeconds;
				float pulsePhase = MyMath.Mod(pulseTime / 2f, 1f);
				color = MenuTheme.Selected;
				entryScale = 1f + pulseAmount * brainPulsate.Evaluate(pulsePhase);
			}
			else
			{
				color = MenuTheme.Idle;
				entryScale = 1f;
			}
			if (!unLockableDataEntries[i].isUnlockable || Unlockables.GetInstance().IsUnlocked(unLockableDataEntries[i].item))
			{
				float x = font.MeasureString(menuEntries[i]).X;
				// Mouse hit box: this entry is centred on `position` (origin = (x/2, LineSpacing/2)).
				RecordEntryHit(i, position, x, font.LineSpacing);
				// Centre each entry on origin.X (was left-aligned at origin.X-75); the centre
				// origin keeps the selected-row pulse symmetric. Matches the framed main menu
				// so the HUD ring (which centres on the menu) lines up for the submenus too.
				(entryOrigin) = new Vector2(x / 2f, (float)(font.LineSpacing / 2));
				// Polish: a soft drop shadow under every entry lifts the text off the busy
				// starfield/planet backdrop (the flat gray items in particular were reading
				// weakly). Same glyph string, offset a few design-space px in translucent
				// black, drawn first so the coloured text lands on top. Straight-alpha
				// (NonPremultiplied) so it darkens the scene behind rather than glowing —
				// and being dark it stays below the bloom threshold, so it never blooms.
				Vector2 shadowOffset = new Vector2(3f, 3f);
				base.SpriteBatch.DrawString(font, menuEntries[i], position + shadowOffset, new Color(0, 0, 0, 160), 0f, entryOrigin, entryScale, (SpriteEffects)0, 0f);
				if (i == selectedEntry)
				{
					// Selection aura: a violet halo behind the bright core, a neon
					// highlight that brightens the whole row. It reads as an
					// arcade selection once bloom amplifies it. Built from stacked
					// translucent copies of the glyph string in two rings (straight alpha,
					// so each pass layers up a soft glow); the bright core lands on top. The
					// outer ring sits past the bloom corona so the purple actually
					// shows instead of washing out. Strength breathes with the scale pulse.
					float glowPulse = brainPulsate.Evaluate(MyMath.Mod((float)gameTime.TotalGameTime.TotalSeconds / 2f, 1f)); // same phase as the scale pulse
					byte ga = (byte)(70f + 45f * glowPulse);
					Color glow = MenuTheme.WithAlpha(MenuTheme.Glow, ga);
					foreach (Vector2 off in SelectionGlowRing)
					{
						base.SpriteBatch.DrawString(font, menuEntries[i], position + off, glow, 0f, entryOrigin, entryScale, (SpriteEffects)0, 0f);
					}
				}
				// Stage 13: the entry's main text gets the chrome sheen; the drop shadow + the
					// selection glow rings (above) stay as the frame. Per-entry RT composite => each
					// row's sheen is local to itself, so stacked rows read identically regardless of
					// height. The MenuTheme colour is preserved (the sheen modulates it).
					base.SpriteBatch.DrawMetalStringCached(menuEntries[i], position, color, 0f, entryOrigin, entryScale, time);
				position.Y += (float)font.LineSpacing;
			}
		}
	}

	public override void Draw(GameTime gameTime)
	{
		base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
		base.SpriteBatch.Flush();
		EnsureRenderTarget();
		base.GraphicsDevice.SetRenderTarget(0, myRenderTarget);
		((Texture2D)myRenderTarget).GraphicsDevice.Clear(new Color(new Vector4(0f, 0f, 0f, 0f)));
		// Mouse hit boxes are rebuilt every frame by the DrawMenu pass below (HandleMouse
		// reads them next Update). Clear here so a removed/relaid-out entry can't linger.
		entryHitBounds.Clear();
		DrawMenu(gameTime, 0f);
		ReportBackTipOverlap();
		base.SpriteBatch.Flush();
		base.GraphicsDevice.SetRenderTarget(0, (RenderTarget2D)null);
		float scale = 1f;
		float fade = 1f;
		switch (state)
		{
		case SubMenuState.entry:
			scale = MathHelper.SmoothStep(1f, 0f, fadeTimer.Normalized);
			break;
		case SubMenuState.exit:
			scale = MyMath.PowerCurve(1f, 8f, 2f, 1f - fadeTimer.Normalized);
			fade = MyMath.PowerCurve(1f, 0f, 2f, 1f - fadeTimer.Normalized);
			break;
		}
		// Stage 10: the RT is render-sized, so composite it 1:1 into the scene via the
		// identity-transform DrawPresent (the design->render scale would double up here).
		// Centre it on screen (render-space centre) and apply the entry/exit scale+fade
		// about that centre — same visual as the old design-space center:true blit.
		base.SpriteBatch.DrawPresent(myRenderTarget,
			new Vector2((float)RenderScale.Width / 2f, (float)RenderScale.Height / 2f),
			new Vector2((float)((Texture2D)myRenderTarget).Width / 2f, (float)((Texture2D)myRenderTarget).Height / 2f),
			scale, new Color(new Vector4(fade, fade, fade, fade)));
	}

	public void RemoveInstantly()
	{
		Collection.Remove((GameComponent)(object)this);
	}

	public void Remove()
	{
		if (fadeTimer.Normalized >= 0.2f)
		{
			RemoveInstantly();
			return;
		}
		state = SubMenuState.exit;
		fadeTimer.Reset();
		fadeTimer.Start();
	}

	internal void Show()
	{
		Collection.Add((GameComponent)(object)this);
		if (menuEntries.Count != 0)
		{
			while (unLockableDataEntries[selectedEntry].isUnlockable && !Unlockables.GetInstance().IsUnlocked(unLockableDataEntries[selectedEntry].item))
			{
				selectedEntry = MyMath.Mod(selectedEntry + 1, menuEntries.Count);
			}
		}
	}

	// The vertical centre of the visible row list, in 800x600 design space. MenuScene
	// parks (and tweens) the HUD ring around whichever menu is active, so each menu
	// reports its own centre. This base version mirrors the base DrawMenu layout
	// (origin (400,300), yoffset 0, locked entries skipped); MenuSubWithSkull overrides
	// it for the framed main menu (which sits at a different vertical offset).
	public virtual Vector2 GetListCentre()
	{
		if (font == null)
			return origin;
		if (isScrolling)
			return new Vector2(origin.X, origin.Y); // selected entry hovers near origin
		int visible = 0;
		for (int i = 0; i < menuEntries.Count; i++)
		{
			if (!unLockableDataEntries[i].isUnlockable || Unlockables.GetInstance().IsUnlocked(unLockableDataEntries[i].item))
				visible++;
		}
		float curY0 = origin.Y - (float)(font.LineSpacing * menuEntries.Count) / 3f;
		float centreY = curY0 + (visible > 0 ? (visible - 1) / 2f * font.LineSpacing : 0f);
		return new Vector2(origin.X, centreY);
	}

	// The design-space Y of a caption line parked just BELOW the last drawn row, for a menu
	// drawing its list at `yoffset`. Card d1a0559b needed it for the pause menu's "Listed
	// online" line (which used to sit at a hard-coded y=400, i.e. across the rows); card
	// 0d6ffe70 needs the same thing for the host menu's status line, so the expression lives
	// here instead of being copied. Derived from GetListCentre and counting VISIBLE (unlocked)
	// entries exactly as DrawMenu does, so it tracks a font or entry-count change on its own.
	public float GetBelowListY(float yoffset)
	{
		if (font == null)
		{
			return origin.Y;
		}
		int visible = 0;
		for (int i = 0; i < menuEntries.Count; i++)
		{
			if (!unLockableDataEntries[i].isUnlockable || Unlockables.GetInstance().IsUnlocked(unLockableDataEntries[i].item))
			{
				visible++;
			}
		}
		float lastRowY = GetListCentre().Y + yoffset + (visible > 0 ? (visible - 1) / 2f * (float)font.LineSpacing : 0f);
		return lastRowY + (float)font.LineSpacing * 0.9f;
	}
}
