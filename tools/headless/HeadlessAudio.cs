// ---------------------------------------------------------------------------
// HeadlessAudio — makes a headless run silent without taking the audio code path away.
//
// The browser build has no XNA audio at all: music goes through WebAudio via
// Compat/MusicInterop (a no-op here, see HeadlessJsRuntime) and SFX through KNI's
// Blazor audio backend. On desktop, KNI's SDL2 platform drives SoundEffect through
// OpenAL Soft (soft_oal.dll, shipped in the platform package), so a headless run would
// otherwise blast the game's sound effects out of the user's speakers while it soaks —
// startling when a run is left going in the background, and pure noise either way since
// an agent cannot hear it.
//
// WHAT IT DOES. SoundEffect.MasterVolume = 0, set once before anything can play. The
// device, the context, the mixer and every source stay real and running; only the gain is
// zero. Nothing in Game/ or Compat/ writes MasterVolume (checked), so it is ours to own —
// SoundManager sets per-INSTANCE Volume, which XNA multiplies by MasterVolume.
//
// WHY NOT SKIP THE AUDIO PATH. The silencing must not be able to hide a future audio-path
// crash, so nothing here may stub SoundManager or SoundEffect: SoundEffect.FromStream still
// decodes every .wav, buffers are still uploaded, sources are still allocated and
// alSourcePlay still runs. Only the samples are inaudible. That is also why the mute is
// applied by the HOST and not by, say, skipping PlayCue.
//
// TWO TRAPS ARE BURIED HERE. Both were paid for; do not re-tread them.
//
// (1) ALSOFT_DRIVERS=null LOOKS like the elegant answer and CRASHES THE RUN. OpenAL Soft's
//     "null" backend is a real discard-everything output, and the original version of this
//     file selected it. Under sustained SFX play it dies with
//     `Fatal error. System.AccessViolationException` inside Content.Load<SoundEffect> —
//     deterministically (3/3 runs, ~90 sim-seconds of Level 2 with ?aiplayer), while the
//     same script on the real mmdevapi backend is clean (3/3). An AccessViolation is a
//     corrupted-state exception, so SoundManager.GetEffect's catch cannot save you and the
//     process is simply gone. Confirmed to be the backend and not the plumbing by setting
//     ALSOFT_DRIVERS=null in the SHELL with the in-process path disabled: still 2/2 crashes.
//     Cost: silence via the null backend also meant no audio device was opened at all, which
//     a box with no sound card would have wanted. It is not available at this OpenAL Soft
//     version (1.18.1); see the Trello follow-up.
//
// (2) Environment.SetEnvironmentVariable CANNOT configure OpenAL Soft, and fails SILENTLY.
//     Left here because it is the reason trap (1) went unnoticed for the whole of eahl's
//     first life: the file set ALSOFT_DRIVERS that way, the docs vouched for silence, and
//     the game played at full volume regardless. soft_oal.dll imports msvcrt.dll and reads
//     the variable with getenv; .NET's SetEnvironmentVariable calls Win32
//     SetEnvironmentVariableW, which updates the process environment BLOCK but not msvcrt's
//     already-initialised _environ TABLE — and the table is what getenv reads. (Writing
//     through msvcrt's own _putenv does reach it. That is how trap (1) was finally
//     observed at all.) The tell in an ALSOFT_LOGLEVEL=3 run is
//     `GetConfigValue: Key drivers not found`. If you ever need to configure OpenAL Soft
//     from here, use an alsoft.ini next to the exe — GetProcPath/ReadALConfig loads it
//     unconditionally — not an environment variable.
//
// Silence() is called BEFORE the Game is constructed (Boot runs a whole frame, and a sound
// played in it would be audible); Pump() applies the mixer half later, when there is a mixer.
// The gain is read back out of OpenAL itself (ListenerGain / alGetListenerf) so silence is
// assertable as DATA rather than by ear — see the `audio` script command and probes/silence.txt.
// --audio opts back in when hearing the game is the actual point.
//
// WINDOWS ONLY, by omission. The DllImports below name soft_oal.dll; elsewhere KNI loads
// libopenal.so.1 / libopenal.dylib, so Pump takes the DllNotFoundException path, the
// mixer-level mute never lands and ListenerGain reports <unreadable> — which fails
// probes/silence.txt rather than passing it. MasterVolume still silences (it is managed and
// platform-agnostic), so a non-Windows run is quiet but cannot PROVE it. Nobody has run eahl
// off Windows yet; if you do, add the platform library names here rather than relaxing the probe.
// ---------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework.Audio;

namespace EvilAliensWeb.Headless
{
    internal static class HeadlessAudio
    {
        private const int AL_GAIN = 0x100A;

        internal static bool Silenced { get; private set; }

        // "the Pump has finished trying", NOT "the listener is muted" — it is also set when
        // the interop is unavailable, precisely so a hopeless Pump stops retrying every frame.
        // ListenerGain, not this flag, is the evidence that the mute landed.
        private static bool _pumpSettled;

        // Call BEFORE the Game is constructed. MasterVolume is a plain managed static and needs
        // no audio device, while Boot() runs a full Update+Draw frame -- so applying it after
        // Boot would leave exactly one frame in which a sound could be heard.
        internal static void Silence()
        {
            SoundEffect.MasterVolume = 0f;
            Silenced = true;
        }

        internal static float MasterVolume => SoundEffect.MasterVolume;

        // The OpenAL-level belt, and the only part of the mute that can be READ BACK OUT OF
        // OPENAL — which after this card's history is the difference between knowing the run
        // is silent and hoping so.
        //
        // It has to be lazy. KNI initialises OpenAL on the FIRST sound, so at Boot there is no
        // context and alListenerf would go nowhere. That is safe rather than a leak: the
        // MasterVolume above is already in force by then, and KNI folds it into every source's
        // gain when SoundManager sets inst.Volume (which it does on every branch), so the sound
        // that triggers initialisation is itself already silent. This just adds a second,
        // global mute at the mixer as soon as there is a mixer to mute.
        //
        // Cheap: one bool test per Step once it has landed.
        internal static void Pump()
        {
            if (_pumpSettled || !Silenced)
                return;
            try
            {
                if (alcGetCurrentContext() == IntPtr.Zero)
                    return;
                alListenerf(AL_GAIN, 0f);
                _pumpSettled = true;
            }
            catch (DllNotFoundException) { _pumpSettled = true; }      // hopeless; stop retrying
            catch (EntryPointNotFoundException) { _pumpSettled = true; }
        }

        // The gain OpenAL itself reports, read through the same soft_oal.dll the game plays
        // through. null when it CANNOT be read, so a probe says it could not confirm silence
        // instead of passing vacuously — which is a live hazard here, not a hypothetical:
        // OpenAL is initialised lazily on the FIRST sound, so before then there is no current
        // context, alGetListenerf is a no-op, and the caller's variable keeps whatever it held.
        // Seeded with NaN rather than 0 for exactly that reason: a 0 seed made an unreadable
        // gain indistinguishable from a muted one, and every run "passed".
        internal static float? ListenerGain
        {
            get
            {
                try
                {
                    if (alcGetCurrentContext() == IntPtr.Zero)
                        return null;                 // audio not initialised yet
                    float g = float.NaN;
                    alGetListenerf(AL_GAIN, ref g);
                    return float.IsNaN(g) ? (float?)null : g;
                }
                catch (DllNotFoundException) { return null; }
                catch (EntryPointNotFoundException) { return null; }
            }
        }

        // Already loaded in-process by KNI from runtimes/<rid>/native, so resolving it by
        // bare name returns that same module rather than loading a second copy.
        [DllImport("soft_oal.dll", EntryPoint = "alGetListenerf", CallingConvention = CallingConvention.Cdecl)]
        private static extern void alGetListenerf(int param, ref float value);

        [DllImport("soft_oal.dll", EntryPoint = "alcGetCurrentContext", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr alcGetCurrentContext();

        [DllImport("soft_oal.dll", EntryPoint = "alListenerf", CallingConvention = CallingConvention.Cdecl)]
        private static extern void alListenerf(int param, float value);
    }
}
