using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class TutorialMessageEvent : GameEvent
{
	private bool displayed;

	private string text;

	private TutorialMessage message;

	public TutorialMessageEvent(Game game, float lifetime, string text)
		: base(game, lifetime)
	{
		this.text = text;
		base.OnFinished += TutorialMessageEvent_OnFinished;
	}

	private void TutorialMessageEvent_OnFinished(GameEvent sender)
	{
		collectionHelper.Remove((GameComponent)(object)message);
	}

	public override void Reset()
	{
		base.Reset();
		displayed = false;
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);
		if (!displayed)
		{
			displayed = true;
			message = TutorialMessage.NewTutorialMessage(collectionHelper, game);
			message.Setup(text);
			collectionHelper.Add((GameComponent)(object)message);
		}
	}
}
