using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EvilAliens.Constants;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Storage;

namespace EvilAliens;

public class ScreenshotSaver
{
	private static Vector2 SIZE = new Vector2(300f, 225f);

	private static List<Levels> levels = Game1.GetEnumValues<Levels>();

	private static Texture2D[] screenshots = (Texture2D[])(object)new Texture2D[levels.Count];

	public static void Init()
	{
		ContentManager contentManager = ServiceHelper.Get<IContentManagerService>().ContentManager;
		for (int i = 0; i < screenshots.Length; i++)
		{
			if (screenshots[i] != null)
			{
				((GraphicsResource)screenshots[i]).Dispose();
			}
			screenshots[i] = null;
		}
		lock (Savable.syncObj)
		{
			foreach (Levels level in levels)
			{
				if (General.ScreenshotEnabled(level))
				{
					LoadScreenshot(level);
				}
			}
		}
		contentManager.Load<Texture2D>("GFX/Screenshots/level1empty");
		contentManager.Load<Texture2D>("GFX/Screenshots/level2empty");
		contentManager.Load<Texture2D>("GFX/Screenshots/level3empty");
		contentManager.Load<Texture2D>("GFX/Screenshots/SpaceDodge");
		contentManager.Load<Texture2D>("GFX/Screenshots/ss1");
		contentManager.Load<Texture2D>("GFX/Screenshots/classicss");
		contentManager.Load<Texture2D>("GFX/Screenshots/Paratrooper");
		contentManager.Load<Texture2D>("GFX/Screenshots/OwnLevel");
		contentManager.Load<Texture2D>("GFX/Screenshots/crazygamess");
		contentManager.Load<Texture2D>("GFX/Screenshots/InsaneBossI");
		contentManager.Load<Texture2D>("GFX/Screenshots/teamchallengess");
	}

	public static Texture2D GetScreenshot(Levels level)
	{
		return screenshots[(int)level];
	}

	public static void SaveScreenShot(Texture2D Screenshot, Levels level)
	{
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		//IL_006a: Expected O, but got Unknown
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_009f: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a4: Unknown result type (might be due to invalid IL or missing references)
		GraphicsDevice graphicsDevice = ServiceHelper.Get<IGraphicsDeviceService>().GraphicsDevice;
		lock (Savable.syncObj)
		{
			string text = level.ToString();
			StorageDevice device = Storage.StorageDeviceManager.Device;
			SpriteBatchWrapper spriteBatchWrapper = ServiceHelper.Get<ISpriteBatchWrapperService>().SpriteBatchWrapper;
			spriteBatchWrapper.Flush();
			RenderTarget2D val = new RenderTarget2D(graphicsDevice, (int)SIZE.X, (int)SIZE.Y, false, graphicsDevice.PresentationParameters.BackBufferFormat, DepthFormat.None);
			graphicsDevice.SetRenderTarget(0, val);
			graphicsDevice.Clear(Color.White);
			spriteBatchWrapper.BlendMode = (SpriteBlendMode)0;
			spriteBatchWrapper.Draw(Screenshot, new Rectangle(0, 0, (int)SIZE.X, (int)SIZE.Y), Color.White);
			spriteBatchWrapper.Flush();
			spriteBatchWrapper.BlendMode = (SpriteBlendMode)1;
			graphicsDevice.SetRenderTarget(0, (RenderTarget2D)null);
			Texture2D texture = val.GetTexture();
			uint[] array = new uint[texture.Width * texture.Height];
			texture.GetData<uint>(array);
			if (Storage.StorageEnabled)
			{
				StorageContainer val2 = device.OpenContainer("EvilAliens");
				string path = Path.Combine(val2.Path, text + ".dat");
				FileStream fileStream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
				BinaryWriter binaryWriter = new BinaryWriter(fileStream, Encoding.UTF8);
				binaryWriter.Write(array.Length);
				for (int i = 0; i < array.Length; i++)
				{
					binaryWriter.Write(array[i]);
				}
				binaryWriter.Close();
				fileStream.Close();
				val2.Dispose();
			}
			if (screenshots[(int)level] != null)
			{
				((GraphicsResource)screenshots[(int)level]).Dispose();
			}
			screenshots[(int)level] = texture;
		}
	}

	public static void LoadScreenshot(Levels level)
	{
		//IL_00f2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fd: Expected O, but got Unknown
		if (screenshots[(int)level] != null)
		{
			((GraphicsResource)screenshots[(int)level]).Dispose();
		}
		screenshots[(int)level] = null;
		if (!Storage.StorageEnabled)
		{
			return;
		}
		GraphicsDevice graphicsDevice = ServiceHelper.Get<IGraphicsDeviceService>().GraphicsDevice;
		string text = level.ToString();
		StorageDevice device = Storage.StorageDeviceManager.Device;
		StorageContainer val = null;
		Texture2D val2;
		try
		{
			val = device.OpenContainer("EvilAliens");
			string path = Path.Combine(val.Path, text + ".dat");
			if (!File.Exists(path))
			{
				val.Dispose();
				throw new FileNotFoundException();
			}
			FileStream fileStream = File.Open(path, FileMode.OpenOrCreate, FileAccess.Read);
			BinaryReader binaryReader = new BinaryReader(fileStream, Encoding.UTF8);
			uint[] array = new uint[binaryReader.ReadInt32()];
			for (int i = 0; i < array.Length; i++)
			{
				array[i] = binaryReader.ReadUInt32();
			}
			binaryReader.Close();
			val2 = new Texture2D(graphicsDevice, (int)SIZE.X, (int)SIZE.Y, false, graphicsDevice.PresentationParameters.BackBufferFormat);
			val2.SetData<uint>(array);
			fileStream.Close();
		}
		catch (Exception)
		{
			val2 = null;
		}
		if (val != null)
		{
			val.Dispose();
		}
		screenshots[(int)level] = val2;
	}

	internal static void DeleteScreenshots()
	{
		if (Storage.StorageEnabled)
		{
			StorageContainer val = null;
			lock (Savable.syncObj)
			{
				try
				{
					val = Storage.StorageDeviceManager.Device.OpenContainer("EvilAliens");
					foreach (Levels level in levels)
					{
						File.Delete(val.Path + level.ToString() + ".dat");
					}
				}
				catch (Exception)
				{
				}
				val.Dispose();
			}
		}
		Init();
	}
}
