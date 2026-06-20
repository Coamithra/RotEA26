using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

public class EffectHandler
{
	private InterpolateEffect interpolateEffect;

	private Effect interpolateEffectFile;

	private LightenEffect lightenEffect;

	private Effect lightenEffectFile;

	private ColorizeEffect colorizeEffect;

	private Effect colorizeEffectFile;

	private OutlineEffect outlineEffect;

	private Effect outlineEffectFile;

	private FadeEffect fadeEffect;

	private Effect fadeEffectFile;

	private StaticAlphaEffect staticAlphaEffect;

	private Effect staticAlphaEffectFile;

	private Effect colorize_lightenEffectFile;

	private Effect colorize_fadeEffectFile;

	private Effect colorize_fade_interpolateEffectFile;

	private Effect colorize_lighten_interpolateEffectFile;

	private Effect colorize_interpolateEffectFile;

	private Effect fade_interpolateEffectFile;

	private Effect lighten_interpolateEffectFile;

	private Effect colorize_lighten_interpolate_fadeEffectFile;

	private Effect lighten_interpolate_fadeEffectFile;

	private Effect currentEffect;

	private Texture3D conversionHSVtoRGB;

	private Texture3D conversionRGBtoHSV;

	public InterpolateEffect InterpolateEffect => interpolateEffect;

	public LightenEffect LightenEffect => lightenEffect;

	public ColorizeEffect ColorizeEffect => colorizeEffect;

	public OutlineEffect OutlineEffect => outlineEffect;

	public FadeEffect FadeEffect => fadeEffect;

	public StaticAlphaEffect StaticAlphaEffect => staticAlphaEffect;

	public EffectHandler()
	{
		interpolateEffect = new InterpolateEffect();
		lightenEffect = new LightenEffect();
		colorizeEffect = new ColorizeEffect();
		outlineEffect = new OutlineEffect();
		fadeEffect = new FadeEffect();
		staticAlphaEffect = new StaticAlphaEffect();
		currentEffect = null;
	}

	public bool HasChanged()
	{
		bool flag = false;
		flag |= lightenEffect.hasStateChanged();
		flag |= colorizeEffect.hasStateChanged();
		flag |= outlineEffect.hasStateChanged();
		flag |= fadeEffect.hasStateChanged();
		flag |= interpolateEffect.hasStateChanged();
		return flag | staticAlphaEffect.hasStateChanged();
	}

	public void UnloadEffects()
	{
		if (currentEffect != null)
		{
			currentEffect.CurrentTechnique.Passes[0].End();
			currentEffect.End();
			currentEffect = null;
		}
	}

	public void LoadEffects()
	{
		//IL_028a: Unknown result type (might be due to invalid IL or missing references)
		//IL_02cc: Unknown result type (might be due to invalid IL or missing references)
		//IL_01be: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c8: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_022b: Unknown result type (might be due to invalid IL or missing references)
		//IL_03be: Unknown result type (might be due to invalid IL or missing references)
		//IL_03df: Unknown result type (might be due to invalid IL or missing references)
		//IL_03e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_040a: Unknown result type (might be due to invalid IL or missing references)
		//IL_064b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0655: Unknown result type (might be due to invalid IL or missing references)
		//IL_0676: Unknown result type (might be due to invalid IL or missing references)
		//IL_051d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0527: Unknown result type (might be due to invalid IL or missing references)
		//IL_0548: Unknown result type (might be due to invalid IL or missing references)
		//IL_06e6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0707: Unknown result type (might be due to invalid IL or missing references)
		//IL_0873: Unknown result type (might be due to invalid IL or missing references)
		//IL_0894: Unknown result type (might be due to invalid IL or missing references)
		//IL_089e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0774: Unknown result type (might be due to invalid IL or missing references)
		//IL_097c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0986: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a76: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a80: Unknown result type (might be due to invalid IL or missing references)
		//IL_0abf: Unknown result type (might be due to invalid IL or missing references)
		//IL_0afb: Unknown result type (might be due to invalid IL or missing references)
		// Stage 5 (shaders): effect files aren't loaded yet (LoadGraphicsContent is a
		// no-op until the shaders are ported), so there is nothing to apply. Bail out
		// before dereferencing the null *EffectFile fields; sprites render unshaded.
		if (staticAlphaEffectFile == null)
		{
			return;
		}
		lightenEffect.SaveState();
		colorizeEffect.SaveState();
		outlineEffect.SaveState();
		fadeEffect.SaveState();
		interpolateEffect.SaveState();
		staticAlphaEffect.SaveState();
		GraphicsDevice graphicsDevice = ServiceHelper.Get<IGraphicsDeviceService>().GraphicsDevice;
		if (staticAlphaEffect.Enabled)
		{
			currentEffect = staticAlphaEffectFile;
			currentEffect.Parameters[0].SetValue(staticAlphaEffect.Alpha);
		}
		else if (outlineEffect.Enabled)
		{
			currentEffect = outlineEffectFile;
			currentEffect.Parameters[0].SetValue(outlineEffect.LineThickness);
		}
		else if (colorizeEffect.Enabled && lightenEffect.Enabled && interpolateEffect.Enabled && fadeEffect.Enabled)
		{
			currentEffect = colorize_lighten_interpolate_fadeEffectFile;
			graphicsDevice.Textures[1] = (Texture)(object)conversionRGBtoHSV;
			graphicsDevice.SamplerStates[1].AddressU = (TextureAddressMode)3;
			graphicsDevice.SamplerStates[1].AddressV = (TextureAddressMode)3;
			graphicsDevice.SamplerStates[1].AddressW = (TextureAddressMode)3;
			graphicsDevice.Textures[2] = (Texture)(object)conversionHSVtoRGB;
			graphicsDevice.SamplerStates[2].AddressU = (TextureAddressMode)3;
			graphicsDevice.SamplerStates[2].AddressV = (TextureAddressMode)3;
			graphicsDevice.SamplerStates[2].AddressW = (TextureAddressMode)3;
			currentEffect.Parameters[0].SetValue(colorizeEffect.RangeTarget / 360f);
			currentEffect.Parameters[1].SetValue(interpolateEffect.Offset);
			currentEffect.Parameters[2].SetValue(interpolateEffect.Delta);
			currentEffect.Parameters[3].SetValue(fadeEffect.Value);
		}
		else if (lightenEffect.Enabled && interpolateEffect.Enabled && fadeEffect.Enabled)
		{
			currentEffect = lighten_interpolate_fadeEffectFile;
			currentEffect.Parameters[0].SetValue(interpolateEffect.Offset);
			currentEffect.Parameters[1].SetValue(interpolateEffect.Delta);
			currentEffect.Parameters[2].SetValue(fadeEffect.Value);
		}
		else if (colorizeEffect.Enabled && fadeEffect.Enabled && interpolateEffect.Enabled)
		{
			currentEffect = colorize_fade_interpolateEffectFile;
			graphicsDevice.Textures[1] = (Texture)(object)conversionRGBtoHSV;
			graphicsDevice.SamplerStates[1].AddressU = (TextureAddressMode)3;
			graphicsDevice.SamplerStates[1].AddressV = (TextureAddressMode)3;
			graphicsDevice.SamplerStates[1].AddressW = (TextureAddressMode)3;
			graphicsDevice.Textures[2] = (Texture)(object)conversionHSVtoRGB;
			graphicsDevice.SamplerStates[2].AddressU = (TextureAddressMode)3;
			graphicsDevice.SamplerStates[2].AddressV = (TextureAddressMode)3;
			graphicsDevice.SamplerStates[2].AddressW = (TextureAddressMode)3;
			currentEffect.Parameters[0].SetValue(fadeEffect.Value);
			currentEffect.Parameters[1].SetValue(colorizeEffect.RangeTarget / 360f);
			currentEffect.Parameters[2].SetValue(interpolateEffect.Offset);
			currentEffect.Parameters[3].SetValue(interpolateEffect.Delta);
		}
		else if (colorizeEffect.Enabled && lightenEffect.Enabled && interpolateEffect.Enabled)
		{
			currentEffect = colorize_lighten_interpolateEffectFile;
			graphicsDevice.Textures[1] = (Texture)(object)conversionRGBtoHSV;
			graphicsDevice.SamplerStates[1].AddressU = (TextureAddressMode)3;
			graphicsDevice.SamplerStates[1].AddressV = (TextureAddressMode)3;
			graphicsDevice.SamplerStates[1].AddressW = (TextureAddressMode)3;
			graphicsDevice.Textures[2] = (Texture)(object)conversionHSVtoRGB;
			graphicsDevice.SamplerStates[2].AddressU = (TextureAddressMode)3;
			graphicsDevice.SamplerStates[2].AddressV = (TextureAddressMode)3;
			graphicsDevice.SamplerStates[2].AddressW = (TextureAddressMode)3;
			currentEffect.Parameters[0].SetValue(colorizeEffect.RangeTarget / 360f);
			currentEffect.Parameters[1].SetValue(interpolateEffect.Offset);
			currentEffect.Parameters[2].SetValue(interpolateEffect.Delta);
		}
		else if (colorizeEffect.Enabled && interpolateEffect.Enabled)
		{
			currentEffect = colorize_interpolateEffectFile;
			graphicsDevice.Textures[1] = (Texture)(object)conversionRGBtoHSV;
			graphicsDevice.SamplerStates[1].AddressU = (TextureAddressMode)3;
			graphicsDevice.SamplerStates[1].AddressV = (TextureAddressMode)3;
			graphicsDevice.SamplerStates[1].AddressW = (TextureAddressMode)3;
			graphicsDevice.Textures[2] = (Texture)(object)conversionHSVtoRGB;
			graphicsDevice.SamplerStates[2].AddressU = (TextureAddressMode)3;
			graphicsDevice.SamplerStates[2].AddressV = (TextureAddressMode)3;
			graphicsDevice.SamplerStates[2].AddressW = (TextureAddressMode)3;
			currentEffect.Parameters[0].SetValue(colorizeEffect.RangeTarget / 360f);
			currentEffect.Parameters[1].SetValue(interpolateEffect.Offset);
			currentEffect.Parameters[2].SetValue(interpolateEffect.Delta);
		}
		else if (fadeEffect.Enabled && interpolateEffect.Enabled)
		{
			currentEffect = fade_interpolateEffectFile;
			currentEffect.Parameters[0].SetValue(fadeEffect.Value);
			currentEffect.Parameters[1].SetValue(interpolateEffect.Offset);
			currentEffect.Parameters[2].SetValue(interpolateEffect.Delta);
		}
		else if (lightenEffect.Enabled && interpolateEffect.Enabled)
		{
			currentEffect = lighten_interpolateEffectFile;
			currentEffect.Parameters[0].SetValue(interpolateEffect.Offset);
			currentEffect.Parameters[1].SetValue(interpolateEffect.Delta);
		}
		else if (colorizeEffect.Enabled & fadeEffect.Enabled)
		{
			graphicsDevice.Textures[1] = (Texture)(object)conversionRGBtoHSV;
			graphicsDevice.SamplerStates[1].AddressU = (TextureAddressMode)3;
			graphicsDevice.SamplerStates[1].AddressV = (TextureAddressMode)3;
			graphicsDevice.SamplerStates[1].AddressW = (TextureAddressMode)3;
			graphicsDevice.Textures[2] = (Texture)(object)conversionHSVtoRGB;
			graphicsDevice.SamplerStates[2].AddressU = (TextureAddressMode)3;
			graphicsDevice.SamplerStates[2].AddressV = (TextureAddressMode)3;
			graphicsDevice.SamplerStates[2].AddressW = (TextureAddressMode)3;
			currentEffect = colorize_fadeEffectFile;
			currentEffect.Parameters[0].SetValue(fadeEffect.Value);
			currentEffect.Parameters[1].SetValue(colorizeEffect.RangeTarget / 360f);
		}
		else if (lightenEffect.Enabled & colorizeEffect.Enabled)
		{
			graphicsDevice.Textures[1] = (Texture)(object)conversionRGBtoHSV;
			graphicsDevice.SamplerStates[1].AddressU = (TextureAddressMode)3;
			graphicsDevice.SamplerStates[1].AddressV = (TextureAddressMode)3;
			graphicsDevice.SamplerStates[1].AddressW = (TextureAddressMode)3;
			graphicsDevice.Textures[2] = (Texture)(object)conversionHSVtoRGB;
			graphicsDevice.SamplerStates[2].AddressU = (TextureAddressMode)3;
			graphicsDevice.SamplerStates[2].AddressV = (TextureAddressMode)3;
			graphicsDevice.SamplerStates[2].AddressW = (TextureAddressMode)3;
			currentEffect = colorize_lightenEffectFile;
			currentEffect.Parameters[0].SetValue(colorizeEffect.RangeTarget / 360f);
		}
		else if (lightenEffect.Enabled)
		{
			currentEffect = lightenEffectFile;
		}
		else if (colorizeEffect.Enabled)
		{
			graphicsDevice.Textures[1] = (Texture)(object)conversionRGBtoHSV;
			graphicsDevice.SamplerStates[1].AddressU = (TextureAddressMode)3;
			graphicsDevice.SamplerStates[1].AddressV = (TextureAddressMode)3;
			graphicsDevice.SamplerStates[1].AddressW = (TextureAddressMode)3;
			graphicsDevice.Textures[2] = (Texture)(object)conversionHSVtoRGB;
			graphicsDevice.SamplerStates[2].AddressU = (TextureAddressMode)3;
			graphicsDevice.SamplerStates[2].AddressV = (TextureAddressMode)3;
			graphicsDevice.SamplerStates[2].AddressW = (TextureAddressMode)3;
			currentEffect = colorizeEffectFile;
			currentEffect.Parameters[0].SetValue(colorizeEffect.RangeTarget / 360f);
		}
		else if (fadeEffect.Enabled)
		{
			currentEffect = fadeEffectFile;
			currentEffect.Parameters[0].SetValue(fadeEffect.Value);
		}
		else if (interpolateEffect.Enabled)
		{
			currentEffect = interpolateEffectFile;
			currentEffect.Parameters[0].SetValue(InterpolateEffect.Offset);
			currentEffect.Parameters[1].SetValue(InterpolateEffect.Delta);
		}
		else
		{
			UnloadEffects();
		}
		if (currentEffect != null)
		{
			currentEffect.Begin();
			currentEffect.CurrentTechnique.Passes[0].Begin();
		}
	}

	private Color RGBtoHSV(Color color)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_018c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0191: Unknown result type (might be due to invalid IL or missing references)
		Vector4 val = (color).ToVector4();
		Vector4 val2 = default(Vector4);
		val2.W = val.W;
		val2.X = 1f;
		val2.Y = 1f;
		val2.Z = 1f;
		float num = MathHelper.Min(val.X, val.Y);
		num = MathHelper.Min(num, val.Z);
		float num2 = MathHelper.Max(val.X, val.Y);
		num2 = (val2.Z = MathHelper.Max(num2, val.Z));
		float num3 = num2 - num;
		if (num2 == 0f)
		{
			val2.Y = 0f;
			val2.X = -1f;
			val2.Z = 0f;
		}
		else
		{
			val2.Y = num3 / num2;
			if (val.X == num2)
			{
				val2.X = (val.Z - val.Y) / num3;
			}
			else if (val.Y == num2)
			{
				val2.X = 2f + (val.Z - val.X) / num3;
			}
			else
			{
				val2.X = 4f + (val.X - val.Y) / num3;
			}
			val2.X *= 60f;
			if (val2.X < 0f)
			{
				val2.X += 360f;
			}
		}
		return new Color(new Vector4(val2.X / 360f, val2.Y, val2.Z, val2.W));
	}

	private Color HSVtoRGB(Color hsv_color)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c3: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c4: Unknown result type (might be due to invalid IL or missing references)
		Vector4 val = (hsv_color).ToVector4();
		val.X *= 360f;
		Vector4 val2 = default(Vector4);
		val2.W = val.W;
		if (val.Y == 0f)
		{
			val2.X = (val2.Y = (val2.Z = val.Z));
		}
		else
		{
			val.X /= 60f;
			int num = (int)val.X;
			float num2 = val.X - (float)num;
			float num3 = val.Z * (1f - val.Y);
			float num4 = val.Z * (1f - val.Y * num2);
			float num5 = val.Z * (1f - val.Y * (1f - num2));
			switch (num)
			{
			case 0:
				val2.X = val.Z;
				val2.Y = num5;
				val2.Z = num3;
				break;
			case 1:
				val2.X = num4;
				val2.Y = val.Z;
				val2.Z = num3;
				break;
			case 2:
				val2.X = num3;
				val2.Y = val.Z;
				val2.Z = num5;
				break;
			case 3:
				val2.X = num3;
				val2.Y = num4;
				val2.Z = val.Z;
				break;
			case 4:
				val2.X = num5;
				val2.Y = num3;
				val2.Z = val.Z;
				break;
			default:
				val2.X = val.Z;
				val2.Y = num3;
				val2.Z = num4;
				break;
			}
		}
		return new Color(val2);
	}

	internal void LoadGraphicsContent(bool loadAllContent)
	{
		//IL_0269: Unknown result type (might be due to invalid IL or missing references)
		//IL_0273: Expected O, but got Unknown
		//IL_0299: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a3: Expected O, but got Unknown
		//IL_0198: Unknown result type (might be due to invalid IL or missing references)
		//IL_019d: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_01dd: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e2: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e7: Unknown result type (might be due to invalid IL or missing references)
		if (!loadAllContent)
		{
			return;
		}
		// Stage 5 (shaders): the ~16 sprite-effect .fx files aren't ported yet, so
		// loading them would throw (and the HSV<->RGB conversion Texture3Ds they feed
		// are unused until then). Leave every *EffectFile field null; LoadEffects()
		// early-outs while they are, so sprites render unshaded. The original loads +
		// conversion-texture build are preserved in src_decompiled/; restore here when
		// porting the shaders.
	}

	internal void UnloadGraphicsContent(bool unloadAllContent)
	{
		if (unloadAllContent && conversionHSVtoRGB != null)
		{
			((GraphicsResource)conversionRGBtoHSV).Dispose();
			((GraphicsResource)conversionHSVtoRGB).Dispose();
			conversionHSVtoRGB = null;
			conversionRGBtoHSV = null;
		}
	}
}
