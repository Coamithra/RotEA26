using Microsoft.JSInterop;

namespace EvilAliensWeb.Compat
{
    // Live FX / feature toggles for the in-browser tuning panel (the #ea-fx buttons in
    // wwwroot/index.html, built only when the URL query contains "fx"). The panel pushes
    // each change via DotNet.invokeMethod('EvilAliensWeb', 'debugFx', key, value).
    //
    // The render hooks read these statics ONLY while Active — and Active flips true the
    // first time the panel pushes anything. So a normal build (no ?fx, panel never built,
    // no push) leaves Active == false and every hook falls back to the game's real
    // behaviour. Dev-only; nothing here is wired into a shipped code path.
    public static class DebugToggles
    {
        public static bool Active;                  // panel engaged (a push has arrived)
        public static bool Bloom;                   // scene bloom post-process
        public static bool BgVeil;                  // Background's oscillating black dim
        public static bool Gamma = true;            // present-blit gamma shader
        public static float StarfieldBrightness = 1f;

        [JSInvokable("debugFx")]
        public static void Set(string key, double value)
        {
            Active = true;
            switch (key)
            {
                case "bloom": Bloom = value != 0.0; break;
                case "veil": BgVeil = value != 0.0; break;
                case "gamma": Gamma = value != 0.0; break;
                case "brightness": StarfieldBrightness = (float)value; break;
            }
        }
    }
}
