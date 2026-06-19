using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

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

	public event ExitMenu OnExit;

	public event TimeOut OnTimeOut;

	public MenuSub1(Game game)
		: base(game)
	{
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		((DrawableGameComponent)this).DrawOrder = 2;
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
		((DrawableGameComponent)this).Initialize();
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
		((DrawableGameComponent)this).LoadContent();
		brainPulsate = Content.Load<Curve>("GFX/Effects/BrainCurve");
		font = Content.Load<SpriteFont>("GFX/Menu/menufont");
		if (myRenderTarget == null)
		{
			PresentationParameters presentationParameters = ((DrawableGameComponent)this).GraphicsDevice.PresentationParameters;
			myRenderTarget = new RenderTarget2D(((DrawableGameComponent)this).GraphicsDevice, presentationParameters.BackBufferWidth, presentationParameters.BackBufferHeight, 1, (SurfaceFormat)1, (RenderTargetUsage)1);
		}
	}

	protected override void UnloadContent()
	{
		((DrawableGameComponent)this).UnloadContent();
		if (myRenderTarget != null)
		{
			((RenderTarget)myRenderTarget).Dispose();
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
			((Vector2)(ref position))._002Ector(origin.X - 75f, yoffset + origin.Y - (float)(selectedEntry * font.LineSpacing));
		}
		else
		{
			((Vector2)(ref position))._002Ector(origin.X - 75f, yoffset + origin.Y - (float)(font.LineSpacing * menuEntries.Count) / 3f);
		}
		Vector2 val = default(Vector2);
		for (int i = 0; i < menuEntries.Count; i++)
		{
			Color color;
			float num4;
			if (i == selectedEntry)
			{
				float num = 15f / font.MeasureString(menuEntries[i]).X;
				float num2 = (float)gameTime.TotalGameTime.TotalSeconds;
				float num3 = MyMath.Mod(num2 / 2f, 1f);
				color = Color.AliceBlue;
				num4 = 1f + num * brainPulsate.Evaluate(num3);
			}
			else
			{
				color = Color.Gray;
				num4 = 1f;
			}
			if (!unLockableDataEntries[i].isUnlockable || Unlockables.GetInstance().IsUnlocked(unLockableDataEntries[i].item))
			{
				float x = font.MeasureString(menuEntries[i]).X;
				float num5 = (x * num4 - x) / 2f;
				((Vector2)(ref val))._002Ector(num5, (float)(font.LineSpacing / 2));
				base.SpriteBatch.DrawString(font, menuEntries[i], position, color, 0f, val, num4, (SpriteEffects)0, 0f);
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
		((DrawableGameComponent)this).GraphicsDevice.SetRenderTarget(0, myRenderTarget);
		((RenderTarget)myRenderTarget).GraphicsDevice.Clear(new Color(new Vector4(0f, 0f, 0f, 0f)));
		DrawMenu(gameTime, 0f);
		base.SpriteBatch.Flush();
		((DrawableGameComponent)this).GraphicsDevice.SetRenderTarget(0, (RenderTarget2D)null);
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
		base.SpriteBatch.Draw(myRenderTarget.GetTexture(), origin, 0f, scale, center: true, new Color(new Vector4(num, num, num, num)));
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
}
