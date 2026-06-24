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

	protected SpriteFont font;

	private RenderTarget2D myRenderTarget;

	private Timer fadeTimer = new Timer(400f, repeating: false);

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
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
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
		bool flag = false;
		if (firstUpdate)
		{
			firstUpdate = false;
			return;
		}
		bool flag2 = false;
		for (int i = 0; i < 4; i++)
		{
			if (!controller.HasValue || controlDeviceToInt(controller.Value) == i)
			{
				flag2 |= base.InputHandler.PadPressed(PadKeys.Back, i) || base.InputHandler.PadPressed(PadKeys.B, i);
			}
		}
		if (!controller.HasValue || controller.Value == ControlDevice.Keyboard)
		{
			flag2 |= base.InputHandler.Pressed(MyKeys.Esc);
		}
		if (flag2 && allowNormalExit)
		{
			if (this.OnExit != null)
			{
				this.OnExit(this);
			}
			flag = true;
		}
		if (menuEntries.Count <= 0)
		{
			return;
		}
		bool flag3 = false;
		if (!controller.HasValue || controller.Value == ControlDevice.Keyboard)
		{
			flag3 |= base.InputHandler.Pressed(MyKeys.Up) | base.InputHandler.Pressed(MyKeys.Left);
		}
		for (int j = 0; j < 4; j++)
		{
			if (!controller.HasValue || controlDeviceToInt(controller.Value) == j)
			{
				flag3 |= base.InputHandler.PadPressed(PadKeys.Up, j);
				flag3 |= base.InputHandler.PadPressed(PadKeys.Left, j);
			}
		}
		if (flag3)
		{
			selectPrevious();
			flag = true;
		}
		bool flag4 = false;
		if (!controller.HasValue || controller.Value == ControlDevice.Keyboard)
		{
			flag4 |= base.InputHandler.Pressed(MyKeys.Down) | base.InputHandler.Pressed(MyKeys.Right);
		}
		for (int k = 0; k < 4; k++)
		{
			if (!controller.HasValue || controlDeviceToInt(controller.Value) == k)
			{
				flag4 |= base.InputHandler.PadPressed(PadKeys.Right, k);
				flag4 |= base.InputHandler.PadPressed(PadKeys.Down, k);
			}
		}
		if (flag4)
		{
			selectNext();
			flag = true;
		}
		bool flag5 = false;
		if (!controller.HasValue || controller.Value == ControlDevice.Keyboard)
		{
			flag5 |= base.InputHandler.Pressed(MyKeys.Enter) | base.InputHandler.Pressed(MyKeys.Generic_Start);
		}
		for (int l = 0; l < 4; l++)
		{
			if (!controller.HasValue || controlDeviceToInt(controller.Value) == l)
			{
				flag5 |= base.InputHandler.PadPressed(PadKeys.Start, l);
				flag5 |= base.InputHandler.PadPressed(PadKeys.A, l);
			}
		}
		if (flag5)
		{
			if (ItemSelectedEvents[selectedEntry] != null)
			{
				ItemSelectedEvents[selectedEntry](this);
			}
			flag = true;
		}
		if (flag)
		{
			timeouttimer.Reset();
		}
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
		//IL_005c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0066: Expected O, but got Unknown
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
		//IL_0100: Unknown result type (might be due to invalid IL or missing references)
		//IL_0105: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0151: Unknown result type (might be due to invalid IL or missing references)
		//IL_019b: Unknown result type (might be due to invalid IL or missing references)
		//IL_019c: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a2: Unknown result type (might be due to invalid IL or missing references)
		Vector2 position = default(Vector2);
		if (isScrolling)
		{
			(position) = new Vector2(origin.X, yoffset + origin.Y - (float)(selectedEntry * font.LineSpacing));
		}
		else
		{
			(position) = new Vector2(origin.X, yoffset + origin.Y - (float)(font.LineSpacing * menuEntries.Count) / 3f);
		}
		Vector2 val = default(Vector2);
		// Stage 13: metal-sheen glint clock — shared by every entry so the rows glint in sync.
		float time = (float)gameTime.TotalGameTime.TotalSeconds;
		for (int i = 0; i < menuEntries.Count; i++)
		{
			Color color;
			float num4;
			if (i == selectedEntry)
			{
				float num = 15f / font.MeasureString(menuEntries[i]).X;
				float num2 = (float)gameTime.TotalGameTime.TotalSeconds;
				float num3 = MyMath.Mod(num2 / 2f, 1f);
				color = MenuTheme.Selected;
				num4 = 1f + num * brainPulsate.Evaluate(num3);
			}
			else
			{
				color = MenuTheme.Idle;
				num4 = 1f;
			}
			if (!unLockableDataEntries[i].isUnlockable || Unlockables.GetInstance().IsUnlocked(unLockableDataEntries[i].item))
			{
				float x = font.MeasureString(menuEntries[i]).X;
				// Centre each entry on origin.X (was left-aligned at origin.X-75); the centre
				// origin keeps the selected-row pulse symmetric. Matches the framed main menu
				// so the HUD ring (which centres on the menu) lines up for the submenus too.
				(val) = new Vector2(x / 2f, (float)(font.LineSpacing / 2));
				// Polish: a soft drop shadow under every entry lifts the text off the busy
				// starfield/planet backdrop (the flat gray items in particular were reading
				// weakly). Same glyph string, offset a few design-space px in translucent
				// black, drawn first so the coloured text lands on top. Straight-alpha
				// (NonPremultiplied) so it darkens the scene behind rather than glowing —
				// and being dark it stays below the bloom threshold, so it never blooms.
				Vector2 shadowOffset = new Vector2(3f, 3f);
				base.SpriteBatch.DrawString(font, menuEntries[i], position + shadowOffset, new Color(0, 0, 0, 160), 0f, val, num4, (SpriteEffects)0, 0f);
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
					foreach (float r in new float[] { 4f, 8f })
					{
						float d = r * 0.7071f;
						Vector2[] ring = new Vector2[]
						{
							new Vector2(r, 0f), new Vector2(-r, 0f), new Vector2(0f, r), new Vector2(0f, -r),
							new Vector2(d, d), new Vector2(-d, d), new Vector2(d, -d), new Vector2(-d, -d)
						};
						foreach (Vector2 off in ring)
						{
							base.SpriteBatch.DrawString(font, menuEntries[i], position + off, glow, 0f, val, num4, (SpriteEffects)0, 0f);
						}
					}
				}
				// Stage 13: the entry's main text gets the chrome sheen; the drop shadow + the
					// selection glow rings (above) stay as the frame. Per-entry RT composite => each
					// row's sheen is local to itself, so stacked rows read identically regardless of
					// height. The MenuTheme colour is preserved (the sheen modulates it).
					base.SpriteBatch.DrawMetalString(menuEntries[i], position, color, 0f, val, num4, time);
				position.Y += (float)font.LineSpacing;
			}
		}
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_011d: Unknown result type (might be due to invalid IL or missing references)
		//IL_012d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0132: Unknown result type (might be due to invalid IL or missing references)
		base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
		base.SpriteBatch.Flush();
		EnsureRenderTarget();
		base.GraphicsDevice.SetRenderTarget(0, myRenderTarget);
		((Texture2D)myRenderTarget).GraphicsDevice.Clear(new Color(new Vector4(0f, 0f, 0f, 0f)));
		DrawMenu(gameTime, 0f);
		base.SpriteBatch.Flush();
		base.GraphicsDevice.SetRenderTarget(0, (RenderTarget2D)null);
		float scale = 1f;
		float num = 1f;
		switch (state)
		{
		case SubMenuState.entry:
			scale = MathHelper.SmoothStep(1f, 0f, fadeTimer.Normalized);
			break;
		case SubMenuState.exit:
			scale = MyMath.PowerCurve(1f, 8f, 2f, 1f - fadeTimer.Normalized);
			num = MyMath.PowerCurve(1f, 0f, 2f, 1f - fadeTimer.Normalized);
			break;
		}
		// Stage 10: the RT is render-sized, so composite it 1:1 into the scene via the
		// identity-transform DrawPresent (the design->render scale would double up here).
		// Centre it on screen (render-space centre) and apply the entry/exit scale+fade
		// about that centre — same visual as the old design-space center:true blit.
		base.SpriteBatch.DrawPresent(myRenderTarget,
			new Vector2((float)RenderScale.Width / 2f, (float)RenderScale.Height / 2f),
			new Vector2((float)((Texture2D)myRenderTarget).Width / 2f, (float)((Texture2D)myRenderTarget).Height / 2f),
			scale, new Color(new Vector4(num, num, num, num)));
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
}
