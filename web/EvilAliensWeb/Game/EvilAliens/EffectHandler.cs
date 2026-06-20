using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

// Stage 5 (shaders): the lost XNA 3.x sprite effects were rewritten as one master
// HLSL shader (tools/shaders/src/sprite.fx) compiled into 13 variants via #defines
// (COLORIZE/LIGHTEN/FADE/INTERPOLATE), plus standalone outline/staticAlpha. The
// HSV colour rotation is done in-shader, so the old RGB<->HSV conversion Texture3D
// lookup tables are gone.
//
// XNA 3.x set the device shader globally (effect.Begin / pass.Begin) and then drew
// sprites; XNA 4.0 / KNI instead passes the Effect to SpriteBatch.Begin and applies
// it during the batch flush. So LoadEffects() now just SELECTS currentEffect and
// sets its parameters; SpriteBatchWrapper reads CurrentEffect and hands it to
// spriteBatch.Begin (see SpriteBatchWrapper._beginDrawing).
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

	public InterpolateEffect InterpolateEffect => interpolateEffect;

	public LightenEffect LightenEffect => lightenEffect;

	public ColorizeEffect ColorizeEffect => colorizeEffect;

	public OutlineEffect OutlineEffect => outlineEffect;

	public FadeEffect FadeEffect => fadeEffect;

	public StaticAlphaEffect StaticAlphaEffect => staticAlphaEffect;

	// The effect SpriteBatchWrapper should apply for the current batch (null = the
	// default sprite shader). Valid after LoadEffects(), cleared by UnloadEffects().
	public Effect CurrentEffect => currentEffect;

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
		// 4.0: nothing to "end" — the effect is applied via SpriteBatch.Begin.
		currentEffect = null;
	}

	// Pick the variant matching the enabled effects (same precedence as the
	// original XNA 3.x if-chain), then push its parameters. No device shader is
	// bound here; SpriteBatchWrapper passes CurrentEffect to SpriteBatch.Begin.
	public void LoadEffects()
	{
		// effects not loaded yet (LoadGraphicsContent uses this as its sentinel)
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

		currentEffect = SelectEffect();
		if (currentEffect == null)
		{
			return;
		}

		// Set only the parameters the chosen variant declares (Set is null-safe).
		// Tinting: the FADE variants receive the sprite colour via FadeValue (the
		// game enables fade for exactly those draws); non-fade variants tint via
		// the vertex colour the normal SpriteBatch way.
		if (staticAlphaEffect.Enabled)
		{
			Set("Alpha", staticAlphaEffect.Alpha);
		}
		if (outlineEffect.Enabled)
		{
			Set("LineThickness", outlineEffect.LineThickness);
		}
		if (colorizeEffect.Enabled)
		{
			Set("ColorizeRange", colorizeEffect.RangeTarget / 360f);
		}
		if (fadeEffect.Enabled)
		{
			Set("FadeValue", fadeEffect.Value);
		}
		if (interpolateEffect.Enabled)
		{
			Set("InterpOffset", interpolateEffect.Offset);
			Set("InterpDelta", interpolateEffect.Delta);
		}
	}

	private Effect SelectEffect()
	{
		if (staticAlphaEffect.Enabled)
		{
			return staticAlphaEffectFile;
		}
		if (outlineEffect.Enabled)
		{
			return outlineEffectFile;
		}
		bool c = colorizeEffect.Enabled;
		bool l = lightenEffect.Enabled;
		bool f = fadeEffect.Enabled;
		bool i = interpolateEffect.Enabled;
		if (c && l && i && f)
		{
			return colorize_lighten_interpolate_fadeEffectFile;
		}
		if (l && i && f)
		{
			return lighten_interpolate_fadeEffectFile;
		}
		if (c && f && i)
		{
			return colorize_fade_interpolateEffectFile;
		}
		if (c && l && i)
		{
			return colorize_lighten_interpolateEffectFile;
		}
		if (c && i)
		{
			return colorize_interpolateEffectFile;
		}
		if (f && i)
		{
			return fade_interpolateEffectFile;
		}
		if (l && i)
		{
			return lighten_interpolateEffectFile;
		}
		if (c && f)
		{
			return colorize_fadeEffectFile;
		}
		if (l && c)
		{
			return colorize_lightenEffectFile;
		}
		if (l)
		{
			return lightenEffectFile;
		}
		if (c)
		{
			return colorizeEffectFile;
		}
		if (f)
		{
			return fadeEffectFile;
		}
		if (i)
		{
			return interpolateEffectFile;
		}
		return null;
	}

	private void Set(string name, float value)
	{
		EffectParameter p = currentEffect.Parameters[name];
		if (p != null)
		{
			p.SetValue(value);
		}
	}

	private void Set(string name, Vector2 value)
	{
		EffectParameter p = currentEffect.Parameters[name];
		if (p != null)
		{
			p.SetValue(value);
		}
	}

	private void Set(string name, Vector3 value)
	{
		EffectParameter p = currentEffect.Parameters[name];
		if (p != null)
		{
			p.SetValue(value);
		}
	}

	private void Set(string name, Vector4 value)
	{
		EffectParameter p = currentEffect.Parameters[name];
		if (p != null)
		{
			p.SetValue(value);
		}
	}

	internal void LoadGraphicsContent(bool loadAllContent)
	{
		if (!loadAllContent)
		{
			return;
		}
		ContentManager contentManager = ServiceHelper.Get<IContentManagerService>().ContentManager;
		lightenEffectFile = contentManager.Load<Effect>("GFX/Effects/lighten");
		colorizeEffectFile = contentManager.Load<Effect>("GFX/Effects/colorize");
		outlineEffectFile = contentManager.Load<Effect>("GFX/Effects/outline");
		fadeEffectFile = contentManager.Load<Effect>("GFX/Effects/fade");
		colorize_lightenEffectFile = contentManager.Load<Effect>("GFX/Effects/colorize_lighten");
		colorize_fadeEffectFile = contentManager.Load<Effect>("GFX/Effects/colorize_fade");
		interpolateEffectFile = contentManager.Load<Effect>("GFX/Effects/interpolate");
		colorize_fade_interpolateEffectFile = contentManager.Load<Effect>("GFX/Effects/colorize_fade_interpolate");
		colorize_lighten_interpolateEffectFile = contentManager.Load<Effect>("GFX/Effects/colorize_lighten_interpolate");
		colorize_interpolateEffectFile = contentManager.Load<Effect>("GFX/Effects/colorize_interpolate");
		fade_interpolateEffectFile = contentManager.Load<Effect>("GFX/Effects/fade_interpolate");
		lighten_interpolateEffectFile = contentManager.Load<Effect>("GFX/Effects/lighten_interpolate");
		colorize_lighten_interpolate_fadeEffectFile = contentManager.Load<Effect>("GFX/Effects/colorize_lighten_interpolate_fade");
		lighten_interpolate_fadeEffectFile = contentManager.Load<Effect>("GFX/Effects/lighten_interpolate_fade");
		// staticAlpha must be loaded LAST: LoadEffects() uses staticAlphaEffectFile
		// != null as the "effects are ready" sentinel.
		staticAlphaEffectFile = contentManager.Load<Effect>("GFX/Effects/staticAlpha");
	}

	internal void UnloadGraphicsContent(bool unloadAllContent)
	{
	}
}
