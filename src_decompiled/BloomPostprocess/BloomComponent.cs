using System;
using EvilAliens;
using EvilAliens.Constants;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace BloomPostprocess;

public class BloomComponent : DrawableGameComponent, IBloomService
{
	public enum IntermediateBuffer
	{
		PreBloom,
		BlurredHorizontally,
		BlurredBothWays,
		FinalResult
	}

	private SpriteBatchWrapper batch;

	private ContentManager content;

	private SpriteBatch spriteBatch;

	private Effect bloomExtractEffect;

	private Effect bloomCombineEffect;

	private Effect gaussianBlurEffect;

	private ResolveTexture2D resolveTarget;

	private RenderTarget2D renderTarget1;

	private RenderTarget2D renderTarget2;

	private Effect sketchEffect;

	private static BloomSettings settings = BloomSettings.PresetSettings[0];

	private IntermediateBuffer showBuffer = IntermediateBuffer.FinalResult;

	private float[] sampleWeights = new float[15];

	private Vector2[] sampleOffsets = (Vector2[])(object)new Vector2[15];

	public BloomSettings Settings
	{
		get
		{
			return settings;
		}
		set
		{
			settings = value;
		}
	}

	public IntermediateBuffer ShowBuffer
	{
		get
		{
			return showBuffer;
		}
		set
		{
			showBuffer = value;
		}
	}

	BloomComponent IBloomService.BloomComponent => this;

	public static void setSettings(BloomSettings val)
	{
		settings = val;
	}

	public BloomComponent(Game game)
		: base(game)
	{
		//IL_004c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0056: Expected O, but got Unknown
		if (game == null)
		{
			throw new ArgumentNullException("game");
		}
		content = new ContentManager((IServiceProvider)game.Services, General.Path + "Bloom");
		((DrawableGameComponent)this).DrawOrder = 950;
		batch = ServiceHelper.Get<ISpriteBatchWrapperService>().SpriteBatchWrapper;
	}

	protected override void LoadContent()
	{
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Expected O, but got Unknown
		//IL_008a: Unknown result type (might be due to invalid IL or missing references)
		//IL_008f: Unknown result type (might be due to invalid IL or missing references)
		//IL_009a: Unknown result type (might be due to invalid IL or missing references)
		//IL_009b: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a5: Expected O, but got Unknown
		//IL_00b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c2: Expected O, but got Unknown
		//IL_00cc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d7: Expected O, but got Unknown
		((DrawableGameComponent)this).LoadContent();
		spriteBatch = new SpriteBatch(((DrawableGameComponent)this).GraphicsDevice);
		bloomExtractEffect = content.Load<Effect>("BloomExtract");
		bloomCombineEffect = content.Load<Effect>("BloomCombine");
		gaussianBlurEffect = content.Load<Effect>("GaussianBlur");
		sketchEffect = content.Load<Effect>("sketch");
		PresentationParameters presentationParameters = ((DrawableGameComponent)this).GraphicsDevice.PresentationParameters;
		int backBufferWidth = presentationParameters.BackBufferWidth;
		int backBufferHeight = presentationParameters.BackBufferHeight;
		SurfaceFormat backBufferFormat = presentationParameters.BackBufferFormat;
		resolveTarget = new ResolveTexture2D(((DrawableGameComponent)this).GraphicsDevice, backBufferWidth, backBufferHeight, 1, backBufferFormat);
		backBufferWidth /= 2;
		backBufferHeight /= 2;
		renderTarget1 = new RenderTarget2D(((DrawableGameComponent)this).GraphicsDevice, backBufferWidth, backBufferHeight, 1, backBufferFormat);
		renderTarget2 = new RenderTarget2D(((DrawableGameComponent)this).GraphicsDevice, backBufferWidth, backBufferHeight, 1, backBufferFormat);
	}

	protected override void UnloadContent()
	{
		((DrawableGameComponent)this).UnloadContent();
		content.Unload();
		if (resolveTarget != null)
		{
			((GraphicsResource)resolveTarget).Dispose();
			((RenderTarget)renderTarget1).Dispose();
			((RenderTarget)renderTarget2).Dispose();
			resolveTarget = null;
			renderTarget1 = null;
			renderTarget2 = null;
		}
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_0172: Unknown result type (might be due to invalid IL or missing references)
		//IL_0177: Unknown result type (might be due to invalid IL or missing references)
		batch.Flush();
		((DrawableGameComponent)this).GraphicsDevice.ResolveBackBuffer(resolveTarget);
		bloomExtractEffect.Parameters["BloomThreshold"].SetValue(Settings.BloomThreshold);
		DrawFullscreenQuad((Texture2D)(object)resolveTarget, renderTarget1, bloomExtractEffect, IntermediateBuffer.PreBloom);
		SetBlurEffectParameters(1f / (float)((RenderTarget)renderTarget1).Width, 0f);
		DrawFullscreenQuad(renderTarget1.GetTexture(), renderTarget2, gaussianBlurEffect, IntermediateBuffer.BlurredHorizontally);
		SetBlurEffectParameters(0f, 1f / (float)((RenderTarget)renderTarget1).Height);
		DrawFullscreenQuad(renderTarget2.GetTexture(), renderTarget1, gaussianBlurEffect, IntermediateBuffer.BlurredBothWays);
		((DrawableGameComponent)this).GraphicsDevice.SetRenderTarget(0, (RenderTarget2D)null);
		EffectParameterCollection parameters = bloomCombineEffect.Parameters;
		parameters["BloomIntensity"].SetValue(Settings.BloomIntensity);
		parameters["BaseIntensity"].SetValue(Settings.BaseIntensity);
		parameters["BloomSaturation"].SetValue(Settings.BloomSaturation);
		parameters["BaseSaturation"].SetValue(Settings.BaseSaturation);
		((DrawableGameComponent)this).GraphicsDevice.Textures[1] = (Texture)(object)resolveTarget;
		Viewport viewport = ((DrawableGameComponent)this).GraphicsDevice.Viewport;
		DrawFullscreenQuad(renderTarget1.GetTexture(), ((Viewport)(ref viewport)).Width, ((Viewport)(ref viewport)).Height, bloomCombineEffect, IntermediateBuffer.FinalResult);
	}

	private void DrawFullscreenQuad(Texture2D texture, RenderTarget2D renderTarget, Effect effect, IntermediateBuffer currentBuffer)
	{
		((DrawableGameComponent)this).GraphicsDevice.SetRenderTarget(0, renderTarget);
		DrawFullscreenQuad(texture, ((RenderTarget)renderTarget).Width, ((RenderTarget)renderTarget).Height, effect, currentBuffer);
		((DrawableGameComponent)this).GraphicsDevice.SetRenderTarget(0, (RenderTarget2D)null);
	}

	private void DrawFullscreenQuad(Texture2D texture, int width, int height, Effect effect, IntermediateBuffer currentBuffer)
	{
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		spriteBatch.Begin((SpriteBlendMode)0, (SpriteSortMode)0, (SaveStateMode)0);
		if (showBuffer >= currentBuffer)
		{
			effect.Begin();
			effect.CurrentTechnique.Passes[0].Begin();
		}
		spriteBatch.Draw(texture, new Rectangle(0, 0, width, height), Color.White);
		spriteBatch.End();
		if (showBuffer >= currentBuffer)
		{
			effect.CurrentTechnique.Passes[0].End();
			effect.End();
		}
	}

	private void SetBlurEffectParameters(float dx, float dy)
	{
		//IL_0066: Unknown result type (might be due to invalid IL or missing references)
		//IL_006b: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ff: Unknown result type (might be due to invalid IL or missing references)
		//IL_0104: Unknown result type (might be due to invalid IL or missing references)
		EffectParameter val = gaussianBlurEffect.Parameters["SampleWeights"];
		EffectParameter val2 = gaussianBlurEffect.Parameters["SampleOffsets"];
		int count = val.Elements.Count;
		MyDebug.Assert(count == 15);
		sampleWeights[0] = ComputeGaussian(0f);
		ref Vector2 reference = ref sampleOffsets[0];
		reference = new Vector2(0f);
		float num = sampleWeights[0];
		for (int i = 0; i < count / 2; i++)
		{
			float num2 = ComputeGaussian(i + 1);
			sampleWeights[i * 2 + 1] = num2;
			sampleWeights[i * 2 + 2] = num2;
			num += num2 * 2f;
			float num3 = (float)(i * 2) + 1.5f;
			Vector2 val3 = new Vector2(dx, dy) * num3;
			sampleOffsets[i * 2 + 1] = val3;
			ref Vector2 reference2 = ref sampleOffsets[i * 2 + 2];
			reference2 = -val3;
		}
		for (int j = 0; j < sampleWeights.Length; j++)
		{
			sampleWeights[j] /= num;
		}
		val.SetValue(sampleWeights);
		val2.SetValue(sampleOffsets);
	}

	private float ComputeGaussian(float n)
	{
		float blurAmount = Settings.BlurAmount;
		return (float)(1.0 / Math.Sqrt(Math.PI * 2.0 * (double)blurAmount) * Math.Exp((0f - n * n) / (2f * blurAmount * blurAmount)));
	}
}
