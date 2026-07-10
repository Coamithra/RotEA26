// ---------------------------------------------------------------------------
// CursorInterop — thin C# -> JS bridge that sets the OS/CSS cursor over the game
// canvas (card 51276dcd "In-game cursor fixes").
//
// KNI's BlazorGL backend does NOT apply Game.IsMouseVisible to the DOM (its
// _isMouseHidden flag is computed but never written to canvas.style.cursor, and
// Mouse.PlatformSetCursor throws), so the OS arrow is ALWAYS shown over the canvas
// unless we drive canvas.style.cursor ourselves. This routes to window.eaCursor.set
// in index.html so MousePointer can pick the mode per scene:
//   "menu"    -> normal OS arrow (menus / non-aiming scenes)
//   "hidden"  -> cursor:none (while the gameplay intro sprite animates)
//   "reticle" -> cursor:url(reticle/<px>.png) — the zero-lag aiming reticle IS the OS
//                cursor during gameplay (no trailing game-loop sprite)
//
// A CSS cursor image is a fixed pixel size and cannot be scaled, so the reticle ships
// as a LADDER of pre-drawn sizes (tools/cursor/build_cursor.py) and MousePointer picks
// the rung matching the current letterbox — hence SetReticle's px argument.
// ---------------------------------------------------------------------------
using System;
using Microsoft.JSInterop;

namespace EvilAliensWeb.Compat
{
    public static class CursorInterop
    {
        private static IJSInProcessRuntime _js;

        // Called once from Index.razor.cs after the JS runtime is available.
        public static void Init(IJSRuntime js)
        {
            _js = js as IJSInProcessRuntime;
        }

        // mode: "menu" | "hidden". Best-effort — swallow if the game isn't wired to JS
        // yet (mirrors the other Compat interops).
        public static void Set(string mode)
        {
            try { _js?.InvokeVoid("eaCursor.set", mode); }
            catch (Exception) { }
        }

        // px: which rung of wwwroot/reticle/<px>.png to show. The hotspot is the image
        // centre, so JS derives it as px/2.
        public static void SetReticle(int px)
        {
            try { _js?.InvokeVoid("eaCursor.set", "reticle", px); }
            catch (Exception) { }
        }
    }
}
