using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EvilAliens.Constants;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Storage;
using EvilAliensWeb.Compat;

namespace EvilAliens;

public class ScreenshotSaver
{
	private static Vector2 SIZE = new Vector2(300f, 225f);

	private static List<Levels> levels = Game1.GetEnumValues<Levels>();

	private static Texture2D[] screenshots = (Texture2D[])(object)new Texture2D[levels.Count];

	// A player-overlay texture grabbed at the last snapshot instant, composited over
	// the game frame in SaveScreenShot (the webcam challenge — see CaptureWebcamOverlay).
	// Disposed + cleared once composited so it never leaks into a later level's shot.
	private static Texture2D pendingOverlay;

	// The stock level-select art: what the carousel draws for a level the player has no
	// saved screenshot of yet (SubMenuLevelChoice.loadScreenshots falls back to these).
	// ONE list with two consumers -- Init() below, which loads them through the shared
	// content manager, and Game1.QueueMenuWarm, which pre-decodes them during the splash so
	// Init()'s loads are cache hits. They MUST NOT drift: Init() used to hardcode eleven of
	// the twelve, and the one it missed (webcamss, the challenge carousel's last entry) then
	// decoded cold the first time the player opened Challenges.
	//
	// Card 8d6883f3: DERIVED, not spelled out. Every level with bundled art
	// (LevelArt.ScreenshotPath returns non-null) contributes it -- the same lookup
	// SubMenuLevelChoice draws through -- so adding a level to the carousel adds its art here
	// for free. Deduped because two levels sharing one bundled image is legal and must not warm
	// it twice.
	//
	// Card 0d166364: the membership test used to be a SECOND hand list, LevelArt.
	// HasCarouselEntry, which had to agree with ScreenshotPath; a level in the first but missed
	// in the second fell through ScreenshotPath's old level1empty default, the dedupe swallowed
	// the duplicate, and the probe below stayed green. Null IS the membership answer now, so
	// that drift cannot be written. What remains hand-kept is the agreement between
	// ScreenshotPath and MenuScene's AddEntry* calls -- adding a carousel entry for a level with
	// no art there reproduces the original bug, and is what
	// tools/headless/probes/stockshots_warm.txt catches (via the loud fallback in
	// SubMenuLevelChoice.loadScreenshots, not via a cold decode -- see that probe's header).
	//
	// Order is enum order, not the old hand order. It only sets the warm-queue sequence; the
	// whole set is drained before the menu is built either way (see Game1.QueueMenuWarm).
	internal static readonly string[] StockShots = BuildStockShots();

	private static string[] BuildStockShots()
	{
		List<string> paths = new List<string>();
		foreach (Levels level in levels)
		{
			string path = LevelArt.ScreenshotPath(level);
			if (path == null)
			{
				continue;
			}
			if (!paths.Contains(path))
			{
				paths.Add(path);
			}
		}
		return paths.ToArray();
	}

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
		foreach (string stockShot in StockShots)
		{
			contentManager.Load<Texture2D>(stockShot);
		}
	}

	public static Texture2D GetScreenshot(Levels level)
	{
		return screenshots[(int)level];
	}

	// Grab the webcam challenge's player-overlay pixels from JS (WebcamInterop ->
	// eaWebcam.overlayPixels) at the current frame and stash them as a texture for the
	// next SaveScreenShot to composite. Called from WebcamLevel.OnScreenshotResolved at
	// the snapshot instant (the JS overlay is torn down before SaveScreenShot runs). Any
	// failure (no player, interop unavailable) just leaves pendingOverlay null -> the
	// shot is the plain game frame.
	public static void CaptureWebcamOverlay(GraphicsDevice gd)
	{
		if (pendingOverlay != null)
		{
			((GraphicsResource)pendingOverlay).Dispose();
			pendingOverlay = null;
		}
		if (WebcamInterop.GetOverlayPixels((int)SIZE.X, (int)SIZE.Y, out byte[] rgba, out int w, out int h)
			&& rgba != null && w > 0 && h > 0 && rgba.Length >= w * h * 4)
		{
			try
			{
				// SetData wants EXACTLY w*h*4 bytes; the interop returns exactly that, but
				// the guard above only checks >=, so trim any trailing slack defensively
				// (a mismatch would otherwise throw). Any failure -> plain frame.
				int need = w * h * 4;
				if (rgba.Length != need)
				{
					byte[] exact = new byte[need];
					Array.Copy(rgba, exact, need);
					rgba = exact;
				}
				Texture2D overlay = new Texture2D(gd, w, h, false, SurfaceFormat.Color);
				overlay.SetData<byte>(rgba);
				pendingOverlay = overlay;
			}
			catch
			{
				pendingOverlay = null;
			}
		}
	}

	public static void SaveScreenShot(Texture2D Screenshot, Levels level)
	{
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
			// DrawPresent (identity transform) composites the full resolved frame into the fixed
			// SIZE (300x225) thumbnail RT. The plain Draw() path bakes in RenderScale.Matrix, which
			// scales this dest rect up by RenderScale.Scale so only the top-left corner of the field
			// lands in the small target — the "screenshot is just a section / cropped" bug. Screenshot
			// (the resolved scene target) is the full 4:3 playing field, so identity gives the whole
			// field, downscaled to the thumbnail.
			spriteBatchWrapper.BlendMode = (SpriteBlendMode)0;
			spriteBatchWrapper.DrawPresent(Screenshot, new Rectangle(0, 0, (int)SIZE.X, (int)SIZE.Y), Color.White);
			// Composite the player overlay on top of the game frame (webcam challenge).
			// Straight (non-premultiplied) alpha; the overlay is transparent except the
			// segmented person, which draws over the starfield/saucers just like on screen.
			// Gated on WebcamAliens: a stray overlay must never leak the camera image into
			// another level's thumbnail (it's still disposed below either way). Same identity
			// DrawPresent so the overlay lines up 1:1 with the (now un-cropped) game frame.
			if (pendingOverlay != null && level == Levels.WebcamAliens)
			{
				spriteBatchWrapper.BlendMode = (SpriteBlendMode)1;
				spriteBatchWrapper.DrawPresent(pendingOverlay, new Rectangle(0, 0, (int)SIZE.X, (int)SIZE.Y), Color.White);
			}
			spriteBatchWrapper.BlendMode = (SpriteBlendMode)1;
			graphicsDevice.SetRenderTarget(0, (RenderTarget2D)null);
			if (pendingOverlay != null)
			{
				((GraphicsResource)pendingOverlay).Dispose();
				pendingOverlay = null;
			}
			Texture2D texture = val.GetTexture();
			// Render target (never padded): GetData reads the FULL mip, so size the buffer by the
			// ACTUAL Width*Height (LogicalWidth/Height would under-size it for a padded texture).
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
