// ---------------------------------------------------------------------------
// HeadlessAudio — makes a headless run silent, and survivable on a box with no sound card.
//
// The browser build has no XNA audio at all: music goes through WebAudio via
// Compat/MusicInterop (a no-op here, see HeadlessJsRuntime) and SFX through KNI's
// Blazor audio backend. On desktop, KNI's SDL2 platform drives SoundEffect through
// OpenAL Soft (soft_oal.dll, shipped in the platform package), so a headless run would
// otherwise blast the game's sound effects out of the user's speakers while it soaks —
// which is at best startling when a run is left going in the background.
//
// Two problems, one fix. OpenAL Soft picks its output backend from the ALSOFT_DRIVERS
// environment variable, and "null" is a real backend that accepts and discards
// everything. Selecting it means:
//   * silence, without touching a single line of game code — SoundManager still loads
//     its cues and still "plays" them, so any code path that depends on a SoundEffect
//     existing behaves exactly as it does in a real run; and
//   * no audio DEVICE is opened, so a machine with no sound card (a CI container, an
//     SSH session, a VM) doesn't fail or stall in audio init. That is the same class of
//     problem --software solves for graphics.
//
// Setting the volume to zero instead would be worse on both counts: it still opens a
// device, and the game owns those volumes (SoundManager writes them from Settings), so
// it would be overwritten mid-run.
//
// MUST run before the audio device is created, i.e. before the Game is constructed.
// --audio opts back in when hearing the game is the actual point.
// ---------------------------------------------------------------------------
using System;

namespace EvilAliensWeb.Headless
{
    internal static class HeadlessAudio
    {
        internal static bool Silenced { get; private set; }

        internal static void Silence()
        {
            // OpenAL Soft: discard-everything output backend.
            Environment.SetEnvironmentVariable("ALSOFT_DRIVERS", "null");
            // Belt and braces for any path that goes through SDL's own audio subsystem
            // rather than OpenAL.
            Environment.SetEnvironmentVariable("SDL_AUDIODRIVER", "dummy");
            Silenced = true;
        }
    }
}
