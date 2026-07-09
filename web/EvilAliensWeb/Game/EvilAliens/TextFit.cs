namespace EvilAliens;

// Cards 623f16d9 / 1ec619b3: shared "shrink to fit" helper for one-line UI text drawn
// centered at a fixed base scale (the "X Unlocked!" popup, the awardment-unlocked
// notification blade). A long string ("Evil Aliens Classic", "I Don't Get The Spider
// Boss") could overflow its playfield/box at the original hard-coded scale; this scales
// the draw DOWN just enough to fit `maxWidth`, and never scales UP past `baseScale` (a
// short string like "Turbo" keeps its original size). Callers measure with
// SpriteFont.MeasureString (design-size -- the menufont atlas's 3x supersample is
// already divided out, see CLAUDE.md's "Custom font" bullet) and pass the resulting
// unscaled width in; centering (origin = MeasureString/2) is unaffected since the
// returned scale is applied uniformly to the same unscaled origin the caller already
// computes.
internal static class TextFit
{
	public static float FitScale(float measuredWidthAtScaleOne, float baseScale, float maxWidth)
	{
		if (baseScale <= 0f || maxWidth <= 0f || measuredWidthAtScaleOne <= 0f)
		{
			return baseScale;
		}
		float widthAtBaseScale = measuredWidthAtScaleOne * baseScale;
		if (widthAtBaseScale <= maxWidth)
		{
			return baseScale;
		}
		return baseScale * (maxWidth / widthAtBaseScale);
	}
}
