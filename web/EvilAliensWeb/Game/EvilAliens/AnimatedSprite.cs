using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

public class AnimatedSprite
{
	private Texture2D texture;

	private List<AnimationFrame> frames = new List<AnimationFrame>();

	// Divides the effective draw scale so a supersampled (higher-resolution) sheet keeps its
	// original on-screen size -- the AnimatedSprite analogue of AlienDrawableGameComponent's
	// DesignFrameWidth factor. A sheet re-rendered at Nx by tools/models/build_models.py is
	// constructed with supersample: N; every existing caller uses the default 1 (no-op).
	private readonly float supersample;

	public int Frames => frames.Count;

	public AnimatedSprite(string filename, float supersample = 1f)
	{
		this.supersample = (supersample > 0f) ? supersample : 1f;
		loadTexture(filename);
		loadData(filename + ".dat");
	}

	private void loadTexture(string name)
	{
		texture = ServiceHelper.Get<IContentManagerService>().ContentManager.Load<Texture2D>(name);
	}

	private void loadData(string filename)
	{
		// Web port: there is no filesystem in WASM. The Stage-3 unpacker copied the
		// animation .dat files into wwwroot/Content (lowercased), so stream them via
		// TitleContainer (the same root WebContentManager uses) instead of File.OpenRead.
		// Keep the "Content/" root capitalised (case-sensitive GitHub Pages); only the
		// filename under it is lowercased to match the on-disk lowercase names.
		// using: a malformed .dat throws mid-parse below; without it the stream + reader leak.
		using Stream input = TitleContainer.OpenStream("Content/" + filename.Replace('\\', '/').ToLowerInvariant());
		using BinaryReader binaryReader = new BinaryReader(input);
		binaryReader.ReadInt32();
		binaryReader.ReadString();
		int num = binaryReader.ReadInt32();
		for (int i = 0; i < num; i++)
		{
			binaryReader.ReadString();
			binaryReader.ReadBoolean();
			binaryReader.ReadBoolean();
			_ = (float)binaryReader.ReadInt32() / 65536f;
			byte b = binaryReader.ReadByte();
			for (int j = 0; j < b; j++)
			{
				AnimationFrame item = default(AnimationFrame);
				item.originalWidth = binaryReader.ReadInt16();
				item.originalHeight = binaryReader.ReadInt16();
				item.minX = binaryReader.ReadInt16();
				item.minY = binaryReader.ReadInt16();
				item.maxX = binaryReader.ReadInt16();
				item.maxY = binaryReader.ReadInt16();
				item.xPos = binaryReader.ReadInt16();
				item.yPos = binaryReader.ReadInt16();
				frames.Add(item);
			}
		}
	}

	public void Draw(int frame, Vector2 position, Color color, float scale, bool center, SpriteEffects e)
	{
		AnimationFrame animationFrame = frames[frame];
		SpriteBatchWrapper spriteBatchWrapper = ServiceHelper.Get<ISpriteBatchWrapperService>().SpriteBatchWrapper;
		// The frame coords (minX/maxX, originalWidth, xPos/yPos) are in supersampled atlas
		// pixels; drawing at scale/supersample maps them back to design space, so an Nx sheet
		// keeps its original on-screen size. supersample == 1 leaves every existing sprite exact.
		float s = scale / supersample;
		switch ((int)e)
		{
		default:
			if ((int)e == 256)
			{
			}
			break;
		case 1:
			position.X += (float)(animationFrame.originalWidth - animationFrame.maxX) * s;
			position.Y += (float)animationFrame.minY * s;
			break;
		case 0:
			position.X += (float)animationFrame.minX * s;
			position.Y += (float)animationFrame.minY * s;
			break;
		}
		Vector2 zero = Vector2.Zero;
		if (center)
		{
			zero.X = (float)(animationFrame.originalWidth / 2) * s;
			zero.Y = (float)(animationFrame.originalHeight / 2) * s;
		}
		spriteBatchWrapper.Draw(texture, new Rectangle((int)animationFrame.xPos, (int)animationFrame.yPos, animationFrame.maxX - animationFrame.minX, animationFrame.maxY - animationFrame.minY), position - zero, 0f, s, center: false, color, e);
	}

	public void Draw(int frame, Vector2 position, Color color, float scale, bool center)
	{
		Draw(frame, position, color, scale, center, (SpriteEffects)0);
	}
}
