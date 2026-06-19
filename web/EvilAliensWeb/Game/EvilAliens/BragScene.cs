using System;
using System.Collections.Generic;
using EvilAliens.Constants;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

public class BragScene : Scene
{
	private enum State
	{
		Intro,
		ChooseFriends,
		Done
	}

	public delegate void ExitEvent();

	private enum FriendError
	{
		None,
		ActivePlayerDoesNotExist,
		FailedToRetrieveFriends,
		NotPrivileged,
		NotSignedIn
	}

	private const string HARD = "I... I did it! I beat Evil Aliens on Hard and you haven't even bought it yet! If you're gonna be like that, why not just get a Wii or something? God, I hate you sometimes.";

	private const string VERYHARD = "That Evil Aliens game I told you about? Well I beat it on Very Hard now. Just saying. Don't be surprised if I don't recognize you at first glance. I tend to forget less significant beings. By the way, your parents called, they want me as their child.";

	private const string INZANE = "Next time you see me, you\u00b4ll remark that I have my head up high, that I have new friends. From now on, you will only speak when spoken to. For today I have become your superior in every fathomable way. Today I have beaten Evil Aliens on INZANE!";

	private Texture2D background;

	private SpriteFont font;

	private Texture2D AButton;

	private Texture2D BButton;

	private List<Gamer> friends = new List<Gamer>();

	private bool finishedHard;

	private bool finishedVeryHard;

	private bool finishedInzane;

	private Settings.DifficultyLevel difficultyLevel;

	private State state;

	private Timer waitTimer = new Timer(500f, repeating: false);

	private MenuSub1 friendsmenu;

	public event ExitEvent OnExit;

	public BragScene(Game game)
		: base(game)
	{
		friendsmenu = new MenuSub1(game);
		friendsmenu.OnExit += friendsmenu_OnExit;
		friendsmenu.SetScrolling();
	}

	private void friendsmenu_OnExit(MenuSub1 sender)
	{
		state = State.Done;
		sender.Remove();
	}

	private void friendsmenu_OnFriendSelected(MenuSub1 sender)
	{
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_0058: Unknown result type (might be due to invalid IL or missing references)
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_006c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0071: Unknown result type (might be due to invalid IL or missing references)
		//IL_009b: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a0: Unknown result type (might be due to invalid IL or missing references)
		//IL_0123: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f0: Unknown result type (might be due to invalid IL or missing references)
		Gamer val = friends[sender.GetSelectedEntry];
		try
		{
			PlayerIndex val2 = (PlayerIndex)0;
			if (base.InputHandler.Pressed(MyKeys.Enter))
			{
				val2 = (PlayerIndex)0;
			}
			for (int i = 0; i < 4; i++)
			{
				if (base.InputHandler.PadPressed(PadKeys.Start, i) || base.InputHandler.PadPressed(PadKeys.A, i))
				{
					val2 = (PlayerIndex)i;
				}
			}
			SignedInGamer val3 = null;
			GamerCollectionEnumerator<SignedInGamer> enumerator = ((GamerCollection<SignedInGamer>)(object)Gamer.SignedInGamers).GetEnumerator();
			try
			{
				while (enumerator.MoveNext())
				{
					SignedInGamer current = enumerator.Current;
					if (current.PlayerIndex == Storage.ActivePlayer)
					{
						val3 = current;
					}
				}
			}
			finally
			{
				((IDisposable)enumerator).Dispose();
			}
			if (val3 == null)
			{
				return;
			}
			if (val3.PlayerIndex == val2)
			{
				string text = "I broke Evil Aliens! I am so awesome :)";
				switch (difficultyLevel)
				{
				case Settings.DifficultyLevel.Medium:
					text = "I\u00b4m testing the Brag to a Friend feature in Evil Aliens! I am the king of Xbox!";
					break;
				case Settings.DifficultyLevel.Hard:
					text = "I... I did it! I beat Evil Aliens on Hard and you haven't even bought it yet! If you're gonna be like that, why not just get a Wii or something? God, I hate you sometimes.";
					break;
				case Settings.DifficultyLevel.Very_Hard:
					text = "That Evil Aliens game I told you about? Well I beat it on Very Hard now. Just saying. Don't be surprised if I don't recognize you at first glance. I tend to forget less significant beings. By the way, your parents called, they want me as their child.";
					break;
				case Settings.DifficultyLevel.Inzane:
					text = "Next time you see me, you\u00b4ll remark that I have my head up high, that I have new friends. From now on, you will only speak when spoken to. For today I have become your superior in every fathomable way. Today I have beaten Evil Aliens on INZANE!";
					break;
				}
				Guide.ShowComposeMessage(val3.PlayerIndex, text, (IEnumerable<Gamer>)(object)new Gamer[1] { val });
			}
			else
			{
				showError("Only the active player (" + ((Gamer)val3).Gamertag + ") can brag to his friends, but we\u00b4re sure he appreciated your help!", val2);
			}
		}
		catch
		{
		}
	}

	private void showError(string p, PlayerIndex player)
	{
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		if (Guide.IsVisible)
		{
			return;
		}
		try
		{
			Guide.BeginShowMessageBox(player, "Error", p, (IEnumerable<string>)new string[1] { "Ok" }, 0, (MessageBoxIcon)1, (AsyncCallback)null, (object)null);
		}
		catch (Exception)
		{
		}
	}

	public override void Initialize()
	{
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_0056: Unknown result type (might be due to invalid IL or missing references)
		base.Initialize();
		try
		{
			SignedInGamer val = null;
			GamerCollectionEnumerator<SignedInGamer> enumerator = ((GamerCollection<SignedInGamer>)(object)Gamer.SignedInGamers).GetEnumerator();
			try
			{
				while (enumerator.MoveNext())
				{
					SignedInGamer current = enumerator.Current;
					if (current.PlayerIndex == Storage.ActivePlayer)
					{
						val = current;
					}
				}
			}
			finally
			{
				((IDisposable)enumerator).Dispose();
			}
			if (val == null || !val.IsSignedInToLive || (int)val.Privileges.AllowCommunication == 0)
			{
				state = State.Done;
				return;
			}
		}
		catch
		{
			state = State.Done;
			return;
		}
		state = State.Intro;
		waitTimer.Reset();
		waitTimer.Start();
		difficultyLevel = Settings.DifficultyLevel.Easy;
		if (allLevelsFinishedOn(Settings.DifficultyLevel.Hard) && !finishedHard)
		{
			difficultyLevel = Settings.DifficultyLevel.Hard;
		}
		if (allLevelsFinishedOn(Settings.DifficultyLevel.Very_Hard) && !finishedVeryHard)
		{
			difficultyLevel = Settings.DifficultyLevel.Very_Hard;
		}
		if (allLevelsFinishedOn(Settings.DifficultyLevel.Inzane) && !finishedInzane)
		{
			difficultyLevel = Settings.DifficultyLevel.Inzane;
		}
		if (difficultyLevel == Settings.DifficultyLevel.Easy)
		{
			state = State.Done;
		}
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		font = Content.Load<SpriteFont>("GFX/Menu/menufont");
		background = Content.Load<Texture2D>("GFX/Game/starfield2");
		AButton = Content.Load<Texture2D>("GFX/Preview/small_face_a");
		BButton = Content.Load<Texture2D>("GFX/Preview/small_face_b");
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0082: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b7: Unknown result type (might be due to invalid IL or missing references)
		base.Draw(gameTime);
		base.SpriteBatch.Draw(background, Vector2.Zero);
		switch (state)
		{
		case State.Intro:
			base.SpriteBatch.DrawString("Congratulations!", new Vector2(400f, 100f), Color.AliceBlue, 0f, centered: true, 1.2f, (SpriteEffects)0, 0f);
			base.SpriteBatch.DrawString("You have beaten Evil Aliens!", new Vector2(400f, 150f), Color.AliceBlue, 0f, centered: true, 0.9f, (SpriteEffects)0, 0f);
			base.SpriteBatch.DrawString("Would you like to Brag To A Friend?", new Vector2(400f, 240f), Color.AliceBlue, 0f, centered: true, 0.9f, (SpriteEffects)0, 0f);
			if (!waitTimer.Active)
			{
				drawButtons("Yes!", "I have no friends");
			}
			break;
		case State.ChooseFriends:
			if (!waitTimer.Active)
			{
				drawButtons("Select", "Exit");
			}
			break;
		}
	}

	private void drawButtons(string A, string B)
	{
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_006a: Unknown result type (might be due to invalid IL or missing references)
		//IL_008b: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00db: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_011e: Unknown result type (might be due to invalid IL or missing references)
		//IL_012a: Unknown result type (might be due to invalid IL or missing references)
		//IL_013a: Unknown result type (might be due to invalid IL or missing references)
		//IL_013f: Unknown result type (might be due to invalid IL or missing references)
		float num = 0.5f;
		float num2 = 0.8f;
		float num3 = (General.SafeZone).Left;
		float num4 = (float)(General.SafeZone).Bottom - MathHelper.Max((float)AButton.Height * num, font.MeasureString("yo").Y * num2);
		float num5 = num3 + (float)AButton.Width * num + font.MeasureString(" ").X * num2;
		float num6 = (float)(General.SafeZone).Right - font.MeasureString(A).X * num2;
		float num7 = num6 - (float)BButton.Width * num - font.MeasureString(" ").X * num2;
		SpriteBatchWrapper spriteBatchWrapper = ServiceHelper.Get<ISpriteBatchWrapperService>().SpriteBatchWrapper;
		spriteBatchWrapper.Draw(BButton, new Vector2(num3, num4), 0f, num, center: false, Color.White);
		spriteBatchWrapper.DrawString(B, new Vector2(num5, num4), Color.AliceBlue, 0f, centered: false, num2, (SpriteEffects)0, 1f);
		spriteBatchWrapper.Draw(AButton, new Vector2(num7, num4), 0f, num, center: false, Color.White);
		spriteBatchWrapper.DrawString(A, new Vector2(num6, num4), Color.AliceBlue, 0f, centered: false, num2, (SpriteEffects)0, 1f);
	}

	public override void Update(GameTime gameTime)
	{
		//IL_0185: Unknown result type (might be due to invalid IL or missing references)
		//IL_0144: Unknown result type (might be due to invalid IL or missing references)
		//IL_0149: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_01bd: Unknown result type (might be due to invalid IL or missing references)
		//IL_01cc: Unknown result type (might be due to invalid IL or missing references)
		//IL_01db: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_011e: Unknown result type (might be due to invalid IL or missing references)
		base.Update(gameTime);
		waitTimer.Update(gameTime);
		switch (state)
		{
		case State.Intro:
		{
			if (waitTimer.Active)
			{
				break;
			}
			bool flag = false;
			bool flag2 = false;
			int num = -1;
			if (base.InputHandler.Pressed(MyKeys.Enter))
			{
				flag2 = true;
				num = 0;
			}
			flag |= base.InputHandler.Pressed(MyKeys.Esc);
			for (int i = 0; i < 4; i++)
			{
				if (base.InputHandler.PadPressed(PadKeys.A, i))
				{
					flag2 = true;
					num = i;
				}
				flag |= base.InputHandler.PadPressed(PadKeys.B, i);
				flag |= base.InputHandler.PadPressed(PadKeys.Back, i);
			}
			if (flag2)
			{
				friends.Clear();
				FriendError friendError = FriendError.None;
				try
				{
					SignedInGamer val = null;
					GamerCollectionEnumerator<SignedInGamer> enumerator = ((GamerCollection<SignedInGamer>)(object)Gamer.SignedInGamers).GetEnumerator();
					try
					{
						while (enumerator.MoveNext())
						{
							SignedInGamer current = enumerator.Current;
							if (current.PlayerIndex == Storage.ActivePlayer)
							{
								val = current;
							}
						}
					}
					finally
					{
						((IDisposable)enumerator).Dispose();
					}
					if (val == null)
					{
						friendError = FriendError.ActivePlayerDoesNotExist;
					}
					else if (!val.IsSignedInToLive)
					{
						friendError = FriendError.NotSignedIn;
					}
					else if ((int)val.Privileges.AllowCommunication == 0)
					{
						friendError = FriendError.NotPrivileged;
					}
					else
					{
						FriendCollection val2 = null;
						try
						{
							val2 = val.GetFriends();
						}
						catch (Exception)
						{
							friendError = FriendError.FailedToRetrieveFriends;
						}
						if (val2 != null)
						{
							GamerCollectionEnumerator<FriendGamer> enumerator2 = ((GamerCollection<FriendGamer>)(object)val2).GetEnumerator();
							try
							{
								while (enumerator2.MoveNext())
								{
									FriendGamer current2 = enumerator2.Current;
									friends.Add((Gamer)(object)current2);
								}
							}
							finally
							{
								((IDisposable)enumerator2).Dispose();
							}
						}
					}
				}
				catch
				{
					friendError = FriendError.FailedToRetrieveFriends;
				}
				PlayerIndex player = (PlayerIndex)num;
				switch (friendError)
				{
				case FriendError.ActivePlayerDoesNotExist:
					showError("The player who started the game is no longer active. Unable to retrieve friends list.", player);
					break;
				case FriendError.FailedToRetrieveFriends:
					showError("Failed to retrieve friends list. Perhaps there is a connection problem.", player);
					break;
				case FriendError.NotPrivileged:
					showError("The player who started the game is not allowed to send any messages.", player);
					break;
				case FriendError.NotSignedIn:
					showError("The player who started the game is not signed in to LIVE. Unable to send messages.", player);
					break;
				}
				if (friendError != 0)
				{
					break;
				}
				state = State.ChooseFriends;
				friendsmenu.RemoveAllEntries();
				if (friends.Count == 0)
				{
					friendsmenu.AddEntry("No friends found");
					friendsmenu.AddEntryEvent(friendsmenu_OnExit);
				}
				foreach (Gamer friend in friends)
				{
					friendsmenu.AddEntry(friend.Gamertag);
					friendsmenu.AddEntryEvent(friendsmenu_OnFriendSelected);
				}
				friendsmenu.Reset();
				Collection.Add((GameComponent)(object)friendsmenu);
			}
			else if (flag)
			{
				state = State.Done;
			}
			break;
		}
		case State.Done:
			if (this.OnExit != null)
			{
				this.OnExit();
			}
			break;
		case State.ChooseFriends:
			break;
		}
	}

	public void StoreCompletionProgress()
	{
		finishedHard = allLevelsFinishedOn(Settings.DifficultyLevel.Hard);
		finishedVeryHard = allLevelsFinishedOn(Settings.DifficultyLevel.Very_Hard);
		finishedInzane = allLevelsFinishedOn(Settings.DifficultyLevel.Inzane);
	}

	private static bool allLevelsFinishedOn(Settings.DifficultyLevel difficulty)
	{
		bool flag = Achievements.GetInstance().Data[Levels.Level1].difficulty >= difficulty;
		flag &= Achievements.GetInstance().Data[Levels.Level2].difficulty >= difficulty;
		return flag & (Achievements.GetInstance().Data[Levels.Level3].difficulty >= difficulty);
	}
}
