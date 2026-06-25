// ---------------------------------------------------------------------------
// TrailerInterop — thin C# -> JS bridge to the embedded trailer player (Stage 14).
//
// The original Xbox trailers (Content/VFX/*.wmv, VC-1) can't play in a browser
// and the web port never ported a video loader (Stage 6 was audio-only), so the
// old TrailerScene's Content.Load<Video>("VFX/..") threw and wedged the JS game
// loop. Instead, the Options -> Trailers menu hands off to an embedded
// youtube-nocookie player overlaid on the canvas (built OUTSIDE #app so Blazor's
// mount can't wipe it; music pauses while it's up). MenuScene's trailer handlers
// call Play(youtubeId); the overlay's own Back button / Esc close it (JS-owned).
// See FullscreenInterop for the same Init/IJSInProcessRuntime pattern.
// ---------------------------------------------------------------------------
using Microsoft.JSInterop;

namespace EvilAliensWeb.Compat
{
    public static class TrailerInterop
    {
        private static IJSInProcessRuntime _js;

        // Called once from Index.razor.cs after the JS runtime is available.
        public static void Init(IJSRuntime js)
        {
            _js = js as IJSInProcessRuntime;
        }

        // Open the trailer overlay for the given YouTube video id (e.g. "v732YJ4wHjc").
        public static void Play(string youtubeId)
        {
            _js?.InvokeVoid("eaTrailer", youtubeId);
        }
    }
}
