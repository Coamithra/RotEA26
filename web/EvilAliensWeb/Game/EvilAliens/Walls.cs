using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class Walls : GameEvent
{
	private int variation;

	// Debug (?wallpoptest): when set, the wall loads this grid file (Content/Levels/<levelFile>)
	// via Wall.SetupFromFile instead of the hard-coded `variation` grid. Null in normal play.
	private string levelFile;

	private Wall wall;

	public Walls(Game game, int variation)
		: base(game, 0f)
	{
		this.variation = variation;
	}

	public Walls(Game game, string levelFile)
		: base(game, 0f)
	{
		this.levelFile = levelFile;
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
			if (levelFile != null)
			{
				wall.SetupFromFile(levelFile);
			}
			else
			{
				wall.Setup(variation);
			}
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
