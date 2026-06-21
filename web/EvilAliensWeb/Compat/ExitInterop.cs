// ---------------------------------------------------------------------------
// ExitInterop — the "boss key" bridge (C# -> JS).
//
// A browser tab can't really "quit" the way the Xbox build's Exit did, so the
// main-menu Exit instead navigates to the fake corporate productivity suite that
// lives in wwwroot/office/ (a deliberately separate, dependency-free stack so it
// can never interfere with the game). MenuScene.mainMenu_ExitSelected calls
// Quit() -> window.eaQuit (defined in wwwroot/index.html), which fades the canvas
// to black and navigates to "office/". The office's Start -> Shut Down navigates
// back to the game. See FullscreenInterop for the same Init/IJSInProcessRuntime
// pattern.
// ---------------------------------------------------------------------------
using Microsoft.JSInterop;

namespace EvilAliensWeb.Compat
{
    public static class ExitInterop
    {
        private static IJSInProcessRuntime _js;

        // Called once from Index.razor.cs after the JS runtime is available.
        public static void Init(IJSRuntime js)
        {
            _js = js as IJSInProcessRuntime;
        }

        // "Close" the game: hand off to the decoy productivity suite.
        public static void Quit()
        {
            _js?.InvokeVoid("eaQuit");
        }
    }
}
