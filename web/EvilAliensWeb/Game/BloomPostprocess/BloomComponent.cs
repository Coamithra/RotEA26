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

	// Perf batch 2: the Gaussian weights depend only on BlurAmount and the two directional
	// offset sets depend only on the render size, yet the original recomputed all of them
	// (15 exp/sqrt) AND re-marshalled the weight array to the GPU twice every frame. Cache
	// them and rebuild only when BlurAmount or the render size actually changes; the weights
	// (identical for both passes) are pushed once on change, the two offset arrays alternate.
	private Vector2[] sampleOffsetsH = (Vector2[])(object)new Vector2[15];

	private Vector2[] sampleOffsetsV = (Vector2[])(object)new Vector2[15];

	private float cachedBlurAmount = float.NaN;

	private int cachedBlurW = -1;

	private int cachedBlurH = -1;

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
		EnsureTargets();
	}

	// Stage 10: the bloom targets must match the unified scene target (Game1.sceneTarget
	// = RenderScale.Width x Height), NOT 800x600 and NOT the window back buffer — so the
	// ResolveBackBuffer copy and the combine align 1:1 with the scene. The window (hence
	// the render size) can change after LoadContent, so (re)create on size change; called
	// from LoadContent and at the top of every Draw.
	private void EnsureTargets()
	{
		int w = EvilAliensWeb.Compat.RenderScale.Width;
		int h = EvilAliensWeb.Compat.RenderScale.Height;
		if (resolveTarget != null && ((Texture2D)resolveTarget).Width == w && ((Texture2D)resolveTarget).Height == h)
		{
			return;
		}
		if (resolveTarget != null)
		{
			((GraphicsResource)resolveTarget).Dispose();
			((Texture2D)renderTarget1).Dispose();
			((Texture2D)renderTarget2).Dispose();
		}
		SurfaceFormat backBufferFormat = base.GraphicsDevice.PresentationParameters.BackBufferFormat;
		int halfW = System.Math.Max(1, w / 2);
		int halfH = System.Math.Max(1, h / 2);
		resolveTarget = new ResolveTexture2D(base.GraphicsDevice, w, h, 1, backBufferFormat);
		renderTarget1 = new RenderTarget2D(base.GraphicsDevice, halfW, halfH, false, backBufferFormat, DepthFormat.None);
		renderTarget2 = new RenderTarget2D(base.GraphicsDevice, halfW, halfH, false, backBufferFormat, DepthFormat.None);
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
		EnsureTargets();
		EnsureBlurKernel();
		base.GraphicsDevice.ResolveBackBuffer(resolveTarget);
		bloomExtractEffect.Parameters["BloomThreshold"].SetValue(Settings.BloomThreshold);
		DrawFullscreenQuad((Texture2D)(object)resolveTarget, renderTarget1, bloomExtractEffect, IntermediateBuffer.PreBloom);
		gaussianBlurEffect.Parameters["SampleOffsets"].SetValue(sampleOffsetsH);
		DrawFullscreenQuad(renderTarget1.GetTexture(), renderTarget2, gaussianBlurEffect, IntermediateBuffer.BlurredHorizontally);
		gaussianBlurEffect.Parameters["SampleOffsets"].SetValue(sampleOffsetsV);
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
		// Combine fills the bound scene target (sceneTarget, redirected above) at the render
		// resolution; use RenderScale directly rather than relying on the viewport equalling it.
		DrawFullscreenQuad(renderTarget1.GetTexture(), EvilAliensWeb.Compat.RenderScale.Width, EvilAliensWeb.Compat.RenderScale.Height, bloomCombineEffect, IntermediateBuffer.FinalResult);
	}

	private void DrawFullscreenQuad(Texture2D texture, RenderTarget2D renderTarget, Effect effect, IntermediateBuffer currentBuffer)
	{
		// Perf batch 2: only bind the intermediate target and draw — don't restore the scene
		// target here. Each of the three intermediate passes is immediately followed by another
		// SetRenderTarget (the next pass's target, or the explicit rebind before the combine
		// pass in Draw), so the old trailing restore was three redundant rebinds per frame.
		base.GraphicsDevice.SetRenderTarget(0, renderTarget);
		DrawFullscreenQuad(texture, ((Texture2D)renderTarget).Width, ((Texture2D)renderTarget).Height, effect, currentBuffer);
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

	// Recompute the blur weights (BlurAmount) + the horizontal/vertical offset arrays (render
	// size) only when one of those inputs changes, and push the invariant weights to the GPU
	// once per change. The per-pass offset arrays are set in Draw (they alternate on the same
	// effect, so they must be re-marshalled each pass regardless).
	private void EnsureBlurKernel()
	{
		//IL_0066: Unknown result type (might be due to invalid IL or missing references)
		int w = ((Texture2D)renderTarget1).Width;
		int h = ((Texture2D)renderTarget1).Height;
		float blur = Settings.BlurAmount;
		if (blur == cachedBlurAmount && w == cachedBlurW && h == cachedBlurH)
		{
			return;
		}
		cachedBlurAmount = blur;
		cachedBlurW = w;
		cachedBlurH = h;
		EffectParameter weightsParam = gaussianBlurEffect.Parameters["SampleWeights"];
		int count = weightsParam.Elements.Count;
		MyDebug.Assert(count == 15);
		sampleWeights[0] = ComputeGaussian(0f);
		float num = sampleWeights[0];
		for (int i = 0; i < count / 2; i++)
		{
			float num2 = ComputeGaussian(i + 1);
			sampleWeights[i * 2 + 1] = num2;
			sampleWeights[i * 2 + 2] = num2;
			num += num2 * 2f;
		}
		for (int j = 0; j < sampleWeights.Length; j++)
		{
			sampleWeights[j] /= num;
		}
		FillSampleOffsets(sampleOffsetsH, 1f / (float)w, 0f, count);
		FillSampleOffsets(sampleOffsetsV, 0f, 1f / (float)h, count);
		weightsParam.SetValue(sampleWeights);
	}

	private void FillSampleOffsets(Vector2[] dst, float dx, float dy, int count)
	{
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		ref Vector2 reference = ref dst[0];
		reference = new Vector2(0f);
		for (int i = 0; i < count / 2; i++)
		{
			float num3 = (float)(i * 2) + 1.5f;
			Vector2 val3 = new Vector2(dx, dy) * num3;
			dst[i * 2 + 1] = val3;
			ref Vector2 reference2 = ref dst[i * 2 + 2];
			reference2 = -val3;
		}
	}

	private float ComputeGaussian(float n)
	{
		float blurAmount = Settings.BlurAmount;
		return (float)(1.0 / Math.Sqrt(Math.PI * 2.0 * (double)blurAmount) * Math.Exp((0f - n * n) / (2f * blurAmount * blurAmount)));
	}
}
