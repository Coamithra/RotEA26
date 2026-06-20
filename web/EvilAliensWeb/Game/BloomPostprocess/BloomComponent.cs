using System;
using EvilAliens;
using EvilAliens.Constants;
using EvilAliensWeb.Compat;
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
		// Stage 5: must be the web loader (effects ship as .mgfxo, not .xnb).
		content = new WebContentManager((IServiceProvider)game.Services, General.Path + "Bloom");
		base.DrawOrder = 950;
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
		base.LoadContent();
		spriteBatch = new SpriteBatch(base.GraphicsDevice);
		bloomExtractEffect = content.Load<Effect>("BloomExtract");
		bloomCombineEffect = content.Load<Effect>("BloomCombine");
		gaussianBlurEffect = content.Load<Effect>("GaussianBlur");
		// 'sketch' was loaded by the original but never applied in Draw; its .fx is
		// lost and it's dead, so we don't ship/load it (Stage 5).
		// Stage 5: the scene composites at the fixed 800x600 design resolution (the
		// presenter target), NOT the window-sized back buffer, so the bloom targets
		// must match 800x600 (half-res for the blur). Sizing to the back buffer would
		// mismatch the presenter target and misalign the combine.
		SurfaceFormat backBufferFormat = base.GraphicsDevice.PresentationParameters.BackBufferFormat;
		int sceneWidth = 800;
		int sceneHeight = 600;
		resolveTarget = new ResolveTexture2D(base.GraphicsDevice, sceneWidth, sceneHeight, 1, backBufferFormat);
		renderTarget1 = new RenderTarget2D(base.GraphicsDevice, sceneWidth / 2, sceneHeight / 2, false, backBufferFormat, DepthFormat.None);
		renderTarget2 = new RenderTarget2D(base.GraphicsDevice, sceneWidth / 2, sceneHeight / 2, false, backBufferFormat, DepthFormat.None);
	}

	protected override void UnloadContent()
	{
		base.UnloadContent();
		content.Unload();
		if (resolveTarget != null)
		{
			((GraphicsResource)resolveTarget).Dispose();
			((Texture2D)renderTarget1).Dispose();
			((Texture2D)renderTarget2).Dispose();
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
		base.GraphicsDevice.ResolveBackBuffer(resolveTarget);
		bloomExtractEffect.Parameters["BloomThreshold"].SetValue(Settings.BloomThreshold);
		DrawFullscreenQuad((Texture2D)(object)resolveTarget, renderTarget1, bloomExtractEffect, IntermediateBuffer.PreBloom);
		SetBlurEffectParameters(1f / (float)((Texture2D)renderTarget1).Width, 0f);
		DrawFullscreenQuad(renderTarget1.GetTexture(), renderTarget2, gaussianBlurEffect, IntermediateBuffer.BlurredHorizontally);
		SetBlurEffectParameters(0f, 1f / (float)((Texture2D)renderTarget1).Height);
		DrawFullscreenQuad(renderTarget2.GetTexture(), renderTarget1, gaussianBlurEffect, IntermediateBuffer.BlurredBothWays);
		base.GraphicsDevice.SetRenderTarget(0, (RenderTarget2D)null);
		EffectParameterCollection parameters = bloomCombineEffect.Parameters;
		parameters["BloomIntensity"].SetValue(Settings.BloomIntensity);
		parameters["BaseIntensity"].SetValue(Settings.BaseIntensity);
		parameters["BloomSaturation"].SetValue(Settings.BloomSaturation);
		parameters["BaseSaturation"].SetValue(Settings.BaseSaturation);
		// Bind the original scene as an effect texture parameter (Stage 5): a manual
		// GraphicsDevice.Textures[1] is not preserved by SpriteBatch's custom-effect
		// path, so without this the combine reads a black base and only the blurred
		// bloom shows (everything looks blurry).
		parameters["BaseTexture"].SetValue((Texture2D)(object)resolveTarget);
		Viewport viewport = base.GraphicsDevice.Viewport;
		DrawFullscreenQuad(renderTarget1.GetTexture(), (viewport).Width, (viewport).Height, bloomCombineEffect, IntermediateBuffer.FinalResult);
	}

	private void DrawFullscreenQuad(Texture2D texture, RenderTarget2D renderTarget, Effect effect, IntermediateBuffer currentBuffer)
	{
		base.GraphicsDevice.SetRenderTarget(0, renderTarget);
		DrawFullscreenQuad(texture, ((Texture2D)renderTarget).Width, ((Texture2D)renderTarget).Height, effect, currentBuffer);
		base.GraphicsDevice.SetRenderTarget(0, (RenderTarget2D)null);
	}

	private void DrawFullscreenQuad(Texture2D texture, int width, int height, Effect effect, IntermediateBuffer currentBuffer)
	{
		// Stage 5: XNA 4.0 applies the effect by passing it to SpriteBatch.Begin
		// (the pass is applied during End/DrawBatch). A null effect = the default
		// sprite shader, used when showBuffer says to skip this stage's effect.
		Effect fx = (showBuffer >= currentBuffer) ? effect : null;
		spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone, fx);
		spriteBatch.Draw(texture, new Rectangle(0, 0, width, height), Color.White);
		spriteBatch.End();
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
