using Microsoft.Xna.Framework;

namespace EvilAliens;

// Per-face directional shading for the Level-3 wall tower slices (tools/shaders/src/faceshade.fx).
//
// Enabled only around Wall.DrawTowerShafts' slice loop. The shader classifies each pixel into one of
// the four faces by which triangle of the slice square it lands in (the square's diagonals cut the
// visible border ring into the four faces, mitred at the corners), and scales rgb by that face's
// factor. Factors are darken-only, so the hazy tower base can't clip to white.
//
// WindowOrigin changes once per SLICE (never per block -- every block's window origin is congruent
// mod Window, see faceshade.fx), so a slice pass costs one batch flush per slice rather than one per
// sprite. hasStateChanged() is what drives that flush, via EffectHandler.HasChanged().
public class FaceShadeEffect : MySpriteEffect
{
	private Vector4 factors = Vector4.One;

	private Vector4 oldFactors = Vector4.One;

	private float window;

	private float oldWindow;

	private float sheetSize;

	private float oldSheetSize;

	private float windowOrigin;

	private float oldWindowOrigin;

	private Vector4 sliceTint = Vector4.One;

	private Vector4 oldSliceTint = Vector4.One;

	// (north, south, east, west) rgb multipliers, each <= 1.
	public Vector4 Factors
	{
		get
		{
			return factors;
		}
		set
		{
			factors = value;
		}
	}

	// Sampling-window size and sheet width, in texels.
	public float Window
	{
		get
		{
			return window;
		}
		set
		{
			window = value;
		}
	}

	public float SheetSize
	{
		get
		{
			return sheetSize;
		}
		set
		{
			sheetSize = value;
		}
	}

	// This slice's shared window origin, in texels.
	public float WindowOrigin
	{
		get
		{
			return windowOrigin;
		}
		set
		{
			windowOrigin = value;
		}
	}

	// This slice's tint: rgb = fogged side colour, a = dissolve alpha. A UNIFORM rather than the
	// vertex colour, because every block at one depth shares it -- which frees the vertex colour to
	// carry the per-block face mask. See faceshade.fx.
	public Vector4 SliceTint
	{
		get
		{
			return sliceTint;
		}
		set
		{
			sliceTint = value;
		}
	}

	public override bool hasStateChanged()
	{
		if (!base.hasStateChanged())
		{
			if (base.Enabled)
			{
				return factors != oldFactors || window != oldWindow || sheetSize != oldSheetSize
					|| windowOrigin != oldWindowOrigin || sliceTint != oldSliceTint;
			}
			return false;
		}
		return true;
	}

	public override void SaveState()
	{
		base.SaveState();
		oldFactors = factors;
		oldWindow = window;
		oldSheetSize = sheetSize;
		oldWindowOrigin = windowOrigin;
		oldSliceTint = sliceTint;
	}
}
