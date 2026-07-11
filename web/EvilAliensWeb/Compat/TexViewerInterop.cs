// ---------------------------------------------------------------------------
// TexViewerInterop — C# half of the ?texviewer texture-format viewer's HTML
// control panel (eaTexViewer in wwwroot/index.html, built outside #app like the
// other tuner panels). The scene (Compat/TexViewerScene.cs) drives the actual
// PNG-vs-DXT rendering; this bridge just seeds the panel's readout and receives
// its button clicks.
//
//   C# -> JS : Show(json) pushes the current asset (name/dims/sizes/pick/view)
//              so the panel re-renders; Hide() tears it down.
//   JS -> C# : the panel calls DotNet 'debugSetTexViewer' (see DebugInput) which
//              enqueues a command string here; the scene drains it each Update.
//
// Save is done entirely JS-side (a fetch POST to the dev-only /api/texdecide
// endpoint on web/DevServer), so no C# is involved in writing textures.config.
// ---------------------------------------------------------------------------
using System.Collections.Generic;
using Microsoft.JSInterop;

namespace EvilAliensWeb.Compat
{
    public static class TexViewerInterop
    {
        private static IJSInProcessRuntime _js;

        // Panel -> scene commands ("next", "prev", "flip:1", "mode:1", "pick:0", "zoom:2.5",
        // "fit"). Blazor WASM is single-threaded, so a plain Queue is safe;
        // the scene drains it fully each Update so nothing is dropped between frames.
        private static readonly Queue<string> commands = new Queue<string>();

        public static void Init(IJSRuntime js)
        {
            _js = js as IJSInProcessRuntime;
        }

        // Seed / refresh the panel from the scene's current state. `json` is built by
        // the scene (see TexViewerScene.PushPanel).
        public static void Show(string json)
        {
            _js?.InvokeVoid("eaTexViewer.show", json);
        }

        public static void Hide()
        {
            _js?.InvokeVoid("eaTexViewer.hide");
        }

        // Called from DebugInput.SetTexViewer (the [JSInvokable] the panel pokes).
        public static void Post(string cmd)
        {
            if (!string.IsNullOrEmpty(cmd))
            {
                commands.Enqueue(cmd);
            }
        }

        // Drain every queued command (scene calls this once per Update).
        public static bool TryDequeue(out string cmd)
        {
            if (commands.Count > 0)
            {
                cmd = commands.Dequeue();
                return true;
            }
            cmd = null;
            return false;
        }
    }
}
