using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

public class AwardmentBlade : DrawableGameComponent, IAwardmentBladeService
{
	private enum State
	{
		Enter,
		Show,
		Exit,
		Idle
	}

	private State state;

	private SpriteBatchWrapper batch;

	private Timer bladeTimer = new Timer(1f, repeating: false);

	private Texture2D blade;

	private ContentManager content;

	private string[] awardmentStrings;

	private Awardment currentlyDisplaying;

	private Queue<Awardment> awardmentsQueue;

	public AwardmentBlade(Game game)
		: base(game)
	{
		base.DrawOrder = 2500;
		awardmentStrings = new string[Game1.GetEnumValues<Awardment>().Count];
		awardmentStrings[4] = "Challenger Award";
		awardmentStrings[5] = "Fight Like A Team";
		awardmentStrings[6] = "I Don't Get The Spider Boss";
		awardmentStrings[0] = "Act The First";
		awardmentStrings[9] = "Real Ultimate Power";
		awardmentStrings[8] = "The Insane Award";
		awardmentStrings[7] = "Pacifist";
		awardmentStrings[1] = "Act The Second";
		awardmentStrings[2] = "Act The Third";
		awardmentStrings[3] = "True Ending";
		awardmentsQueue = new Queue<Awardment>();
	}

	public string AwardmentName(Awardment awardment)
	{
		return awardmentStrings[(int)awardment];
	}

	public override void Initialize()
	{
		base.Initialize();
		bladeTimer.Stop();
		state = State.Idle;
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		batch = ServiceHelper.Get<ISpriteBatchWrapperService>().SpriteBatchWrapper;
		content = ServiceHelper.Get<IContentManagerService>().ContentManager;
		blade = content.Load<Texture2D>("GFX/Sprites/awardmentblade");
	}

	public override void Update(GameTime gameTime)
	{
		if (awardmentsQueue.Count > 0 && state == State.Idle)
		{
			currentlyDisplaying = awardmentsQueue.Dequeue();
			if (Achievements.GetInstance().GetAwardmentIsUnlocked((int)currentlyDisplaying))
			{
				return;
			}
			bladeTimer.Duration = 170f;
			bladeTimer.Reset();
			bladeTimer.Start();
			state = State.Enter;
		}
		base.Update(gameTime);
		bladeTimer.Update(gameTime);
		if (!bladeTimer.Finished)
		{
			return;
		}
		switch (state)
		{
		case State.Enter:
			bladeTimer.Duration = 6500f;
			bladeTimer.Reset();
			bladeTimer.Start();
			state = State.Show;
			Achievements.GetInstance().SetAwardmentIsUnlocked((int)currentlyDisplaying, value: true);
			Achievements.GetInstance().SaveThreaded();
			if (!Unlockables.GetInstance().IsUnlocked(Unlockables.Items.Awardments))
			{
				Unlockables.GetInstance().Unlock(Unlockables.Items.Awardments);
				Unlockables.GetInstance().SaveThreaded();
			}
			break;
		case State.Show:
			bladeTimer.Duration = 170f;
			bladeTimer.Reset();
			bladeTimer.Start();
			state = State.Exit;
			break;
		case State.Exit:
			state = State.Idle;
			break;
		case State.Idle:
			break;
		}
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_008f: Unknown result type (might be due to invalid IL or missing references)
		//IL_009b: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ba: Unknown result type (might be due to invalid IL or missing references)
		//IL_00eb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f7: Unknown result type (might be due to invalid IL or missing references)
		//IL_0111: Unknown result type (might be due to invalid IL or missing references)
		//IL_0116: Unknown result type (might be due to invalid IL or missing references)
		//IL_0135: Unknown result type (might be due to invalid IL or missing references)
		//IL_013a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0147: Unknown result type (might be due to invalid IL or missing references)
		//IL_0151: Unknown result type (might be due to invalid IL or missing references)
		//IL_017e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0183: Unknown result type (might be due to invalid IL or missing references)
		//IL_0190: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ff: Unknown result type (might be due to invalid IL or missing references)
		//IL_020d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0227: Unknown result type (might be due to invalid IL or missing references)
		//IL_022c: Unknown result type (might be due to invalid IL or missing references)
		base.Draw(gameTime);
		batch.BlendMode = (SpriteBlendMode)1;
		switch (state)
		{
		case State.Enter:
		{
			float num5 = MathHelper.SmoothStep(0f, 1f, 1f - bladeTimer.Normalized);
			float num6 = MathHelper.SmoothStep(0.5f, 1f, 1f - bladeTimer.Normalized);
			batch.Draw(blade, new Vector2(400f, 450f), 0f, new Vector2(num6, num5), center: true, new Color(new Vector4(1f, 1f, 1f, 0.65f)));
			break;
		}
		case State.Show:
		{
			float num3 = 1f;
			float num4 = 1f;
			batch.Draw(blade, new Vector2(400f, 450f), 0f, new Vector2(num4, num3), center: true, new Color(new Vector4(1f, 1f, 1f, 0.65f)));
			batch.DrawString("Awardment Unlocked!", new Vector2(400f, 433f), Color.AliceBlue, 0f, centered: true, new Vector2(num4, num3) * 0.8f, (SpriteEffects)0, 1f);
			batch.DrawString(awardmentStrings[(int)currentlyDisplaying], new Vector2(400f, 467f), Color.AliceBlue, 0f, centered: true, new Vector2(num4, num3), (SpriteEffects)0, 1f);
			break;
		}
		case State.Exit:
		{
			float num = MathHelper.SmoothStep(1f, 0f, 1f - bladeTimer.Normalized);
			float num2 = MathHelper.SmoothStep(1f, 0.5f, 1f - bladeTimer.Normalized);
			batch.Draw(blade, new Vector2(400f, 450f), 0f, new Vector2(num2, num), center: true, new Color(new Vector4(1f, 1f, 1f, 0.65f)));
			break;
		}
		}
		_ = bladeTimer.Active;
	}

	public void AwardAchievement(Awardment awardment)
	{
		if (!Achievements.GetInstance().GetAwardmentIsUnlocked((int)awardment) && !Settings.GetInstance().CheckForCheats())
		{
			awardmentsQueue.Enqueue(awardment);
		}
	}

	public AwardmentBlade get()
	{
		return this;
	}
}
