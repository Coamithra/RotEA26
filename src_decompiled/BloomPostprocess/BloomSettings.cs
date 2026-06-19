namespace BloomPostprocess;

public class BloomSettings
{
	public readonly string Name;

	public readonly float BloomThreshold;

	public readonly float BlurAmount;

	public readonly float BloomIntensity;

	public readonly float BaseIntensity;

	public readonly float BloomSaturation;

	public readonly float BaseSaturation;

	public static BloomSettings[] PresetSettings = new BloomSettings[6]
	{
		new BloomSettings("Default", 0.25f, 4f, 1.25f, 1f, 1f, 1f),
		new BloomSettings("Soft", 0f, 3f, 1f, 1f, 1f, 1f),
		new BloomSettings("Desaturated", 0.5f, 8f, 2f, 1f, 0f, 1f),
		new BloomSettings("Saturated", 0.25f, 4f, 2f, 1f, 2f, 0f),
		new BloomSettings("Blurry", 0f, 2f, 1f, 0.1f, 1f, 1f),
		new BloomSettings("Subtle", 0.5f, 2f, 1f, 1f, 1f, 1f)
	};

	public BloomSettings(string name, float bloomThreshold, float blurAmount, float bloomIntensity, float baseIntensity, float bloomSaturation, float baseSaturation)
	{
		Name = name;
		BloomThreshold = bloomThreshold;
		BlurAmount = blurAmount;
		BloomIntensity = bloomIntensity;
		BaseIntensity = baseIntensity;
		BloomSaturation = bloomSaturation;
		BaseSaturation = baseSaturation;
	}
}
