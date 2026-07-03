using System;
using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class TutorialMessageEvent : GameEvent
{
	private bool displayed;

	private string text;

	// Optional lazy text, resolved when the message is actually shown (during play)
	// rather than when the event list is built (at boot, before any player exists).
	// Used for control prompts that depend on the tutorial player's input device.
	private Func<string> textResolver;

	private TutorialMessage message;

	public TutorialMessageEvent(Game game, float lifetime, string text)
		: base(game, lifetime)
	{
		this.text = text;
		base.OnFinished += TutorialMessageEvent_OnFinished;
	}

	public TutorialMessageEvent(Game game, float lifetime, Func<string> textResolver)
		: base(game, lifetime)
	{
		this.textResolver = textResolver;
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
			message.Setup(textResolver != null ? textResolver() : text);
			collectionHelper.Add((GameComponent)(object)message);
		}
	}
}
