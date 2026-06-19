using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

public class Quad
{
	private bool alreadyloaded;

	private Game game;

	private Texture2D top;

	private Texture2D middle;

	private Texture2D bottom;

	private BasicEffect effect;

	private BasicEffect topeffect;

	private BasicEffect bottomeffect;

	private VertexDeclaration vertexdecl;

	private Vector3 origin;

	private Vector3 upperLeft;

	private Vector3 lowerLeft;

	private Vector3 upperRight;

	private Vector3 lowerRight;

	private Vector3 direction;

	private Vector3 left;

	public float width;

	public float height;

	public float lead;

	private static Vector3 normal = Vector3.Backward;

	private VertexPositionNormalTexture[] vertices;

	private VertexPositionNormalTexture[] topvertices;

	private VertexPositionNormalTexture[] bottomvertices;

	public int[] indices;

	public void LoadContent()
	{
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_009a: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a4: Expected O, but got Unknown
		//IL_00aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ba: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f6: Expected O, but got Unknown
		//IL_00fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_010c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0118: Unknown result type (might be due to invalid IL or missing references)
		//IL_013e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0148: Expected O, but got Unknown
		//IL_014e: Unknown result type (might be due to invalid IL or missing references)
		//IL_015e: Unknown result type (might be due to invalid IL or missing references)
		//IL_016a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0194: Unknown result type (might be due to invalid IL or missing references)
		//IL_019e: Expected O, but got Unknown
		if (!alreadyloaded)
		{
			alreadyloaded = true;
			Matrix view = Matrix.CreateLookAt(new Vector3(0f, 0f, 1f), Vector3.Zero, Vector3.Up);
			Matrix projection = Matrix.CreateOrthographic(800f, 600f, 0f, 1f);
			GraphicsDevice graphicsDevice = ServiceHelper.Get<IGraphicsDeviceService>().GraphicsDevice;
			ContentManager contentManager = ServiceHelper.Get<IContentManagerService>().ContentManager;
			top = contentManager.Load<Texture2D>("GFX/Sprites/lazertop");
			bottom = contentManager.Load<Texture2D>("GFX/Sprites/lazerbottom");
			middle = contentManager.Load<Texture2D>("GFX/Sprites/lazermiddle");
			effect = new BasicEffect(graphicsDevice);
			effect.World = Matrix.Identity;
			effect.View = view;
			effect.Projection = projection;
			effect.TextureEnabled = true;
			effect.Texture = middle;
			topeffect = new BasicEffect(graphicsDevice);
			topeffect.World = Matrix.Identity;
			topeffect.View = view;
			topeffect.Projection = projection;
			topeffect.TextureEnabled = true;
			topeffect.Texture = top;
			bottomeffect = new BasicEffect(graphicsDevice);
			bottomeffect.World = Matrix.Identity;
			bottomeffect.View = view;
			bottomeffect.Projection = projection;
			bottomeffect.TextureEnabled = true;
			bottomeffect.Texture = bottom;
			vertexdecl = VertexPositionNormalTexture.VertexDeclaration;
		}
	}

	public void UnloadGraphics()
	{
		alreadyloaded = false;
	}

	public Quad(Game game, Vector2 origin, float direction, float width, float height, float lead)
	{
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0062: Unknown result type (might be due to invalid IL or missing references)
		//IL_0082: Unknown result type (might be due to invalid IL or missing references)
		//IL_0087: Unknown result type (might be due to invalid IL or missing references)
		this.game = game;
		vertices = (VertexPositionNormalTexture[])(object)new VertexPositionNormalTexture[4];
		topvertices = (VertexPositionNormalTexture[])(object)new VertexPositionNormalTexture[4];
		bottomvertices = (VertexPositionNormalTexture[])(object)new VertexPositionNormalTexture[4];
		indices = new int[6];
		this.origin = new Vector3(origin.X - 400f, 300f - origin.Y, 0f);
		this.height = height;
		this.lead = lead;
		this.width = width;
		this.direction = convertToVector3(direction);
		calculatePoints();
		FillVertices();
	}

	private void FillVertices()
	{
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0095: Unknown result type (might be due to invalid IL or missing references)
		//IL_0096: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00de: Unknown result type (might be due to invalid IL or missing references)
		//IL_00df: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f1: Unknown result type (might be due to invalid IL or missing references)
		//IL_0102: Unknown result type (might be due to invalid IL or missing references)
		//IL_0104: Unknown result type (might be due to invalid IL or missing references)
		//IL_0115: Unknown result type (might be due to invalid IL or missing references)
		//IL_0116: Unknown result type (might be due to invalid IL or missing references)
		//IL_0127: Unknown result type (might be due to invalid IL or missing references)
		//IL_0128: Unknown result type (might be due to invalid IL or missing references)
		//IL_0139: Unknown result type (might be due to invalid IL or missing references)
		//IL_013a: Unknown result type (might be due to invalid IL or missing references)
		//IL_014b: Unknown result type (might be due to invalid IL or missing references)
		//IL_014d: Unknown result type (might be due to invalid IL or missing references)
		//IL_015e: Unknown result type (might be due to invalid IL or missing references)
		//IL_015f: Unknown result type (might be due to invalid IL or missing references)
		for (int i = 0; i < vertices.Length; i++)
		{
			vertices[i].Normal = Vector3.Backward;
			topvertices[i].Normal = Vector3.Backward;
		}
		calculateVertexPositions();
		Vector2 textureCoordinate = default(Vector2);
		(textureCoordinate) = new Vector2(0f, 0f);
		Vector2 textureCoordinate2 = default(Vector2);
		(textureCoordinate2) = new Vector2(1f, 0f);
		Vector2 textureCoordinate3 = default(Vector2);
		(textureCoordinate3) = new Vector2(0f, 1f);
		Vector2 textureCoordinate4 = default(Vector2);
		(textureCoordinate4) = new Vector2(1f, 1f);
		vertices[0].TextureCoordinate = textureCoordinate3;
		vertices[1].TextureCoordinate = textureCoordinate;
		vertices[2].TextureCoordinate = textureCoordinate4;
		vertices[3].TextureCoordinate = textureCoordinate2;
		topvertices[0].TextureCoordinate = textureCoordinate3;
		topvertices[1].TextureCoordinate = textureCoordinate;
		topvertices[2].TextureCoordinate = textureCoordinate4;
		topvertices[3].TextureCoordinate = textureCoordinate2;
		bottomvertices[0].TextureCoordinate = textureCoordinate3;
		bottomvertices[1].TextureCoordinate = textureCoordinate;
		bottomvertices[2].TextureCoordinate = textureCoordinate4;
		bottomvertices[3].TextureCoordinate = textureCoordinate2;
		indices[0] = 0;
		indices[1] = 1;
		indices[2] = 2;
		indices[3] = 2;
		indices[4] = 1;
		indices[5] = 3;
	}

	private void calculateVertexPositions()
	{
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0040: Unknown result type (might be due to invalid IL or missing references)
		//IL_0052: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Unknown result type (might be due to invalid IL or missing references)
		//IL_0069: Unknown result type (might be due to invalid IL or missing references)
		//IL_006e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Unknown result type (might be due to invalid IL or missing references)
		//IL_0086: Unknown result type (might be due to invalid IL or missing references)
		//IL_0097: Unknown result type (might be due to invalid IL or missing references)
		//IL_009c: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ca: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00eb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fd: Unknown result type (might be due to invalid IL or missing references)
		//IL_0103: Unknown result type (might be due to invalid IL or missing references)
		//IL_0114: Unknown result type (might be due to invalid IL or missing references)
		//IL_0119: Unknown result type (might be due to invalid IL or missing references)
		//IL_011e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0130: Unknown result type (might be due to invalid IL or missing references)
		//IL_0135: Unknown result type (might be due to invalid IL or missing references)
		//IL_0147: Unknown result type (might be due to invalid IL or missing references)
		//IL_014d: Unknown result type (might be due to invalid IL or missing references)
		//IL_015e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0163: Unknown result type (might be due to invalid IL or missing references)
		//IL_0168: Unknown result type (might be due to invalid IL or missing references)
		//IL_017a: Unknown result type (might be due to invalid IL or missing references)
		//IL_017f: Unknown result type (might be due to invalid IL or missing references)
		vertices[0].Position = lowerLeft;
		vertices[1].Position = upperLeft;
		vertices[2].Position = lowerRight;
		vertices[3].Position = upperRight;
		topvertices[0].Position = upperLeft;
		topvertices[1].Position = upperLeft + direction * (width / 2f);
		topvertices[2].Position = upperRight;
		topvertices[3].Position = upperRight + direction * (width / 2f);
		bottomvertices[0].Position = lowerLeft - direction * (width / 2f);
		bottomvertices[1].Position = lowerLeft;
		bottomvertices[2].Position = lowerRight - direction * (width / 2f);
		bottomvertices[3].Position = lowerRight;
	}

	public void Draw()
	{
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c3: Unknown result type (might be due to invalid IL or missing references)
		SpriteBatchWrapper spriteBatchWrapper = ServiceHelper.Get<ISpriteBatchWrapperService>().SpriteBatchWrapper;
		spriteBatchWrapper.Flush();
		GraphicsDevice graphicsDevice = ServiceHelper.Get<IGraphicsDeviceService>().GraphicsDevice;
		BlendState oldBlend = graphicsDevice.BlendState;
			graphicsDevice.BlendState = BlendState.Additive;
			foreach (EffectPass pass in effect.CurrentTechnique.Passes)
			{
				pass.Apply();
				graphicsDevice.DrawUserIndexedPrimitives<VertexPositionNormalTexture>(PrimitiveType.TriangleStrip, vertices, 0, 4, indices, 0, 2);
			}
			foreach (EffectPass pass2 in topeffect.CurrentTechnique.Passes)
			{
				pass2.Apply();
				graphicsDevice.DrawUserIndexedPrimitives<VertexPositionNormalTexture>(PrimitiveType.TriangleStrip, topvertices, 0, 4, indices, 0, 2);
			}
			foreach (EffectPass pass3 in bottomeffect.CurrentTechnique.Passes)
			{
				pass3.Apply();
				graphicsDevice.DrawUserIndexedPrimitives<VertexPositionNormalTexture>(PrimitiveType.TriangleStrip, bottomvertices, 0, 4, indices, 0, 2);
			}
			graphicsDevice.BlendState = oldBlend;
	}

	public void SetProperties(Vector2 position, float direction, float length, float lead)
	{
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		origin = new Vector3(position.X - 400f, 300f - position.Y, 0f);
		this.direction = convertToVector3(direction);
		height = length;
		this.lead = lead;
		calculatePoints();
		calculateVertexPositions();
	}

	public void SetLead(float lead)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		Vector3 val = direction * (height - lead);
		this.lead = lead;
		lowerLeft = upperLeft - val;
		lowerRight = upperRight - val;
		calculateVertexPositions();
	}

	private void calculatePoints()
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		//IL_0055: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0068: Unknown result type (might be due to invalid IL or missing references)
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		//IL_0077: Unknown result type (might be due to invalid IL or missing references)
		//IL_007c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0083: Unknown result type (might be due to invalid IL or missing references)
		//IL_0089: Unknown result type (might be due to invalid IL or missing references)
		//IL_009b: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ac: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ce: Unknown result type (might be due to invalid IL or missing references)
		left = Vector3.Cross(normal, direction);
		Vector3 val = direction * height + origin;
		upperLeft = val + left * width / 2f;
		upperRight = val - left * width / 2f;
		lowerLeft = upperLeft - direction * (height - lead);
		lowerRight = upperRight - direction * (height - lead);
	}

	public void SetLength(float length)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		Vector3 val = direction * (length - height);
		height = length;
		upperLeft += val;
		upperRight += val;
		calculateVertexPositions();
	}

	public void MoveTo(Vector2 position)
	{
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		origin = new Vector3(position.X - 400f, 300f - position.Y, 0f);
		calculatePoints();
		calculateVertexPositions();
	}

	public void AimAt(float direction)
	{
		//IL_0003: Unknown result type (might be due to invalid IL or missing references)
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		this.direction = convertToVector3(direction);
		calculatePoints();
		calculateVertexPositions();
	}

	private Vector3 convertToVector3(float direction)
	{
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		Vector2 val = default(Vector2);
		(val) = new Vector2(Convert.ToSingle(Math.Cos(direction)), -1f * Convert.ToSingle(Math.Sin(direction)));
		return new Vector3(val, 0f);
	}
}
