// ---------------------------------------------------------------------------
// MusicInterop — thin C# -> JS bridge to the WebAudio music layer (Stage 6).
//
// Music can't go through KNI's SoundEffect path: XNA/KNI looping replays the
// whole buffer, but these tracks need seamless loop POINTS (an authored intro
// then a looping body, or a pymusiclooper seam — see tools/audio/build_audio.py
// and Content/music/music.json). So music is played by a small WebAudio layer in
// index.html (`window.eaMusic`), driven from here.
//
// SoundManager.PlayMusic/StopMusic/SetMusicRate call these. The cue name (e.g.
// "kylikova") is the music.json key; the JS side owns the file + loop points.
// SFX/speech stay on the native KNI SoundEffect path (no interop).
// ---------------------------------------------------------------------------
using Microsoft.JSInterop;

namespace EvilAliensWeb.Compat
{
    public static class MusicInterop
    {
        private static IJSInProcessRuntime _js;
        private static string _lastRateCue;       // throttle per-frame SetRate spam
        private static double _lastRate = double.NaN;

        // Called once from Index.razor.cs after the JS runtime is available.
        public static void Init(IJSRuntime js)
        {
            _js = js as IJSInProcessRuntime;
        }

        public static void Play(string cue)
        {
            _lastRate = double.NaN;               // a new track resets the rate
            _js?.InvokeVoid("eaMusic.play", cue);
        }

        public static void Stop()
        {
            _js?.InvokeVoid("eaMusic.stop");
        }

        // rate is the game's XACT "Pitch" value (~50 = normal); the JS layer maps
        // it to AudioBufferSourceNode.playbackRate. Called every frame by the
        // BrainBoss HP sweep, so skip redundant calls.
        public static void SetRate(string cue, double rate)
        {
            if (rate == _lastRate && cue == _lastRateCue)
                return;
            _lastRate = rate;
            _lastRateCue = cue;
            _js?.InvokeVoid("eaMusic.setRate", rate);
        }
    }
}
