using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class MessageEvent : GameEvent
{
	private string message;

	private AnimatedMessage.MessageType type;

	private SoundManager.Texts speechText;

	private float angle;

	private bool displayed;

	public MessageEvent(Game game)
		: base(game, 0.1f)
	{
		message = "Wave Completed!";
		speechText = SoundManager.Texts.WaveCompleted;
		type = AnimatedMessage.MessageType.starwarsblue;
	}

	public MessageEvent(Game game, string alternateMessage, SoundManager.Texts speechText)
		: base(game, 0.1f)
	{
		message = alternateMessage;
		this.speechText = speechText;
		type = AnimatedMessage.MessageType.starwarsblue;
	}

	public MessageEvent(Game game, string alternateMessage, SoundManager.Texts speechText, float duration)
		: base(game, duration)
	{
		type = AnimatedMessage.MessageType.starwarsblue;
		this.speechText = speechText;
		message = alternateMessage;
	}

	public void SetupAsWarning(float angle)
	{
		this.angle = angle;
		type = AnimatedMessage.MessageType.redwarning;
	}

	public override void Reset()
	{
		displayed = false;
		base.Reset();
	}

	public override void Update(GameTime gameTime)
	{
		if (!displayed)
		{
			AnimatedMessage animatedMessage = AnimatedMessage.NewAnimatedMessage(collectionHelper, game);
			animatedMessage.Setup(message, speechText, type);
			switch (type)
			{
			case AnimatedMessage.MessageType.redwarning:
				animatedMessage.SetWarningDirection(angle);
				break;
			}
			collectionHelper.Add((GameComponent)(object)animatedMessage);
			displayed = true;
		}
		base.Update(gameTime);
	}
}
