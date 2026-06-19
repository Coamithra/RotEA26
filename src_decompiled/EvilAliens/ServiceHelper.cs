using Microsoft.Xna.Framework;

namespace EvilAliens;

internal static class ServiceHelper
{
	private static Game game;

	public static Game Game
	{
		set
		{
			game = value;
		}
	}

	public static void Add<T>(T service) where T : class
	{
		game.Services.AddService(typeof(T), (object)service);
	}

	public static void Remove<T>() where T : class
	{
		game.Services.RemoveService(typeof(T));
	}

	public static T Get<T>() where T : class
	{
		return game.Services.GetService(typeof(T)) as T;
	}
}
