// ---------------------------------------------------------------------------
// FullscreenInterop — thin C# -> JS bridge to the browser Fullscreen API (Stage 9).
//
// KNI's BlazorGL backend doesn't drive the DOM Fullscreen API from
// GraphicsDeviceManager.IsFullScreen (and toggling it can be unsupported), so the
// in-menu "Fullscreen" option (MenuScene -> Game1.GoFullScreen) routes through here
// to window.eaFullscreen.set(bool) instead. The canvas already auto-resizes to the
// window and Game1.Draw letterboxes the fixed 800x600 scene, so entering/leaving
// fullscreen needs no graphics changes — just a resize, which KNI handles.
//
// Note: requestFullscreen requires user activation. The corner button in index.html
// is the guaranteed-gesture path; calls from the menu rely on the keypress that
// selected the option still being within the browser's transient-activation window.
// Both are best-effort — the JS side swallows a rejected request.
// ---------------------------------------------------------------------------
using Microsoft.JSInterop;

namespace EvilAliensWeb.Compat
{
    public static class FullscreenInterop
    {
        private static IJSInProcessRuntime _js;

        // Called once from Index.razor.cs after the JS runtime is available.
        public static void Init(IJSRuntime js)
        {
            _js = js as IJSInProcessRuntime;
        }

        // Request (on) or leave (off) browser fullscreen to match the setting.
        public static void Set(bool on)
        {
            _js?.InvokeVoid("eaFullscreen.set", on);
        }
    }
}
