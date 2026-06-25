using Microsoft.Xna.Framework;

namespace EvilAliens;

// Halting event that holds the level event list until the Background's fly-by
// doodad (the hero earth) has finished crossing and left the screen. Lifetime 0
// => infinite (it never times out on its own); it Terminates as soon as
// Background.DoodadActive goes false.
//
// It POLLS DoodadActive rather than subscribing to a one-shot "doodad finished"
// event on purpose: if the earth has ALREADY left by the time this event runs
// (the common case -- the fly-by is slow and the opening waves take a while), a
// one-shot event would have fired before we could subscribe and the list would
// hang forever. Polling the current state instead terminates immediately when the
// doodad is gone, so the gate is a safe no-op in that case.
internal class WaitForDoodadEvent : GameEvent
{
	private readonly Background background;

	public WaitForDoodadEvent(Game game, Background background)
		: base(game, 0f)
	{
		this.background = background;
	}

	public override void Update(GameTime gameTime)
	{
		if (background == null || !background.DoodadActive)
		{
			Terminate();
			return;
		}
		base.Update(gameTime);
	}
}
