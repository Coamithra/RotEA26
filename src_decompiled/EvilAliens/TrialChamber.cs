using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

public class TrialChamber : DrawableGameComponent
{
	private enum State
	{
		FirstWait,
		InitialFadeOut,
		Invisible,
		SlowFade,
		Flash,
		Flash2,
		Flash3
	}

	private Texture2D texture;

	private ContentManager content;

	private SpriteBatchWrapper spriteBatch;

	private float alpha;

	private Color color = Color.White;

	private State state;

	private Timer stateTimer = new Timer(1f, repeating: false);

	public TrialChamber(Game game)
		: base(game)
	{
	}//IL_0001: Unknown result type (might be due to invalid IL or missing references)
	//IL_0006: Unknown result type (might be due to invalid IL or missing references)


	public override void Initialize()
	{
		((DrawableGameComponent)this).Initialize();
		alpha = 1f;
		setState(State.FirstWait);
	}

	private void setState(State state)
	{
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		//IL_005c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0061: Unknown result type (might be due to invalid IL or missing references)
		this.state = state;
		switch (state)
		{
		case State.FirstWait:
			color = Color.White;
			alpha = 1f;
			stateTimer.Duration = 2000f;
			break;
		case State.InitialFadeOut:
			color = Color.White;
			alpha = 1f;
			stateTimer.Duration = 1000f;
			break;
		case State.Invisible:
			alpha = 0f;
			break;
		case State.SlowFade:
			randomColor();
			alpha = 0f;
			stateTimer.Duration = 1100f;
			break;
		case State.Flash:
			randomColor();
			alpha = 0f;
			stateTimer.Duration = 400f;
			break;
		case State.Flash2:
			randomColor();
			alpha = 0f;
			stateTimer.Duration = 600f;
			break;
		case State.Flash3:
			randomColor();
			alpha = 0f;
			stateTimer.Duration = 200f;
			break;
		}
		stateTimer.Reset();
		stateTimer.Start();
	}

	private void randomColor()
	{
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		//IL_004c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_0061: Unknown result type (might be due to invalid IL or missing references)
		//IL_0066: Unknown result type (might be due to invalid IL or missing references)
		//IL_006e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		switch (RandomHelper.Random.Next(6))
		{
		case 0:
			color = Powerup.PowerUpColor(Powerup.PowerupType.Blast);
			break;
		case 1:
			color = Powerup.PowerUpColor(Powerup.PowerupType.FirePower);
			break;
		case 2:
			color = Powerup.PowerUpColor(Powerup.PowerupType.Linker);
			break;
		case 3:
			color = Powerup.PowerUpColor(Powerup.PowerupType.OneUp);
			break;
		case 4:
			color = Powerup.PowerUpColor(Powerup.PowerupType.Option);
			break;
		case 5:
			color = Powerup.PowerUpColor(Powerup.PowerupType.Range);
			break;
		}
	}

	protected override void LoadContent()
	{
		((DrawableGameComponent)this).LoadContent();
		content = ServiceHelper.Get<IContentManagerService>().ContentManager;
		spriteBatch = ServiceHelper.Get<ISpriteBatchWrapperService>().SpriteBatchWrapper;
		texture = content.Load<Texture2D>("GFX/Tutorial/TwistedCheckerboard");
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		((DrawableGameComponent)this).Draw(gameTime);
		spriteBatch.BlendMode = (SpriteBlendMode)2;
		spriteBatch.Draw(texture, new Vector2(0f, -100f), new Color(color, alpha));
		spriteBatch.BlendMode = (SpriteBlendMode)1;
	}

	public override void Update(GameTime gameTime)
	{
		((GameComponent)this).Update(gameTime);
		stateTimer.Update(gameTime);
		switch (state)
		{
		case State.FirstWait:
			nextState(State.InitialFadeOut);
			break;
		case State.InitialFadeOut:
			alpha = stateTimer.Normalized;
			nextState(State.Invisible);
			break;
		case State.Invisible:
			if (RandomHelper.RandomFromAverage(0.1f, gameTime))
			{
				switch (RandomHelper.Random.Next(4))
				{
				case 0:
					setState(State.SlowFade);
					break;
				case 1:
					setState(State.Flash);
					break;
				case 2:
					setState(State.Flash2);
					break;
				case 3:
					setState(State.Flash3);
					break;
				}
			}
			break;
		case State.SlowFade:
			alpha = stateTimer.Normalized * 2f;
			if (alpha > 1f)
			{
				alpha = 1f - (alpha - 1f);
			}
			alpha = MathHelper.SmoothStep(0f, 1f, alpha);
			alpha *= 0.6f;
			nextState(State.Invisible);
			break;
		case State.Flash:
			if (stateTimer.Normalized < 0.13f || stateTimer.Normalized > 0.3f)
			{
				alpha = 0.4f;
			}
			else
			{
				alpha = 0f;
			}
			nextState(State.Invisible);
			break;
		case State.Flash2:
			alpha = 0f;
			if (stateTimer.Normalized < 0.1f)
			{
				alpha = 0.3f;
			}
			if (stateTimer.Normalized > 0.23f && stateTimer.Normalized < 0.75f)
			{
				alpha = 0.55f;
			}
			if (stateTimer.Normalized > 0.89f)
			{
				alpha = 0.34f;
			}
			nextState(State.Invisible);
			break;
		case State.Flash3:
			alpha = 0.4f;
			nextState(State.Invisible);
			break;
		}
	}

	private void nextState(State nextState)
	{
		if (stateTimer.Finished)
		{
			setState(nextState);
		}
	}
}
