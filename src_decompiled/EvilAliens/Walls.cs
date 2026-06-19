using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class Walls : GameEvent
{
	private int variation;

	private Wall wall;

	public Walls(Game game, int variation)
		: base(game, 0f)
	{
		this.variation = variation;
	}

	public override void Reset()
	{
		base.Reset();
		wall = null;
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);
		if (wall == null)
		{
			wall = Wall.NewWall(collectionHelper, game);
			wall.Setup(variation);
			collectionHelper.Add((GameComponent)(object)wall);
			wall.OnDeath += wall_OnDeath;
		}
	}

	private void wall_OnDeath(object sender)
	{
		wall = null;
		Terminate();
	}
}
