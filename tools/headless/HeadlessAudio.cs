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
//     version (1.18.1) -- bumping past it is coupled to a KNI upgrade, since the nkast.*
//     platform package pins the binary. What covers that box instead is BringUp(), below.
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
// NO AUDIO DEVICE (card 72297923). Losing the null backend cost the one box ALSOFT_DRIVERS
// would have covered: one with no sound card at all (CI container, SSH session, driverless VM).
// MEASURED on main before this card, by making OpenAL Soft fail to open a device for real (an
// alsoft.ini naming a backend that does not exist -- what --fake-no-audio-device now writes):
// the run does NOT die. KNI throws NoAudioHardwareException out of ConcreteAudioService's ctor,
// inside Content.Load<SoundEffect>, and SoundManager.GetEffect catches every exception there and
// caches the miss -- so a 90-sim-second Level 2 soak completed clean, every cue silently absent.
// The defect was never "the run dies", then; it was that NOTHING SAID SO, and that the three
// causes of an unreadable gain (device open failed / OpenAL not up yet / library did not resolve)
// were indistinguishable, so probes/silence.txt failed with the wrong diagnosis.
//
// BringUp() is the fix: ask for AudioService.Current once, at boot, in a try/catch, and REPORT
// the outcome as device= (ok / none / nolib). It never fails the run -- a device-less box says
// device=none and plays on, deaf. EAGER rather than lazy was a measured choice, not a default:
// all six committed probes stay green with the device opened at boot, and it buys two things a
// lazy bring-up cannot -- the mixer mute lands BEFORE the first sound instead of just after it,
// and alGain is readable from frame 0, which is what makes <unreadable> mean something. If a
// future change makes eager bring-up untenable, the fallback is the same try/catch on the first
// Pump(); you lose those two properties and keep the reporting.
//
// PLATFORM LIBRARY NAMES. The P/Invokes below used to name soft_oal.dll outright, which made the
// gain readback -- the only part of the mute that is EVIDENCE rather than a request -- Windows-
// only: everywhere else Pump took the DllNotFoundException path, the mixer mute never landed, and
// probes/silence.txt could not pass at all. They now go through a DllImportResolver over KNI's
// OWN candidate list in KNI's own order, the four strings lifted from Kni.Platform.dll:
// soft_oal.dll, libopenal.so.1, libopenal.1.dylib, openal. Note the third -- an earlier version
// of this comment said libopenal.dylib, which is not what the platform package ships. Whichever
// candidate answers is reported as lib=, so "the resolver found something" is assertable on any
// platform, which is all the coverage a Windows-only dev box can honestly give this. Nobody has
// run eahl off Windows yet; if you do and it still cannot read the gain, add the name HERE rather
// than relaxing the probe.
// ---------------------------------------------------------------------------
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Platform.Audio;

namespace EvilAliensWeb.Headless
{
    internal static class HeadlessAudio
    {
        private const int AL_GAIN = 0x100A;

        // The logical name the P/Invokes below are declared against. It is deliberately not a real
        // file name: nothing must ever resolve it by accident if the resolver stops being called.
        private const string OpenAlLibrary = "eahl:openal";

        // KNI's own candidate list, in KNI's own order (the four strings are in Kni.Platform.dll).
        private static readonly string[] OpenAlCandidates =
            { "soft_oal.dll", "libopenal.so.1", "libopenal.1.dylib", "openal" };

        private static IntPtr _openAl;

        internal static bool Silenced { get; private set; }

        // Which candidate answered, or null if none did. Reported as lib= so a run on a platform
        // nobody here can test still says whether the readback ever had a chance.
        internal static string ResolvedLibrary { get; private set; }

        internal enum DeviceState { NotTried, Ok, None, NoLibrary }

        internal static DeviceState Device { get; private set; } = DeviceState.NotTried;

        static HeadlessAudio()
        {
            // Registered from the static ctor, which the CLR runs before the first call to any
            // static member of this type -- including the P/Invokes themselves, since a type with
            // a static ctor is not beforefieldinit.
            NativeLibrary.SetDllImportResolver(typeof(HeadlessAudio).Assembly, ResolveOpenAl);
        }

        // IntPtr.Zero for anything that is not ours, which hands the name straight back to the
        // default resolution every other DllImport in this exe relies on.
        private static IntPtr ResolveOpenAl(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (libraryName != OpenAlLibrary)
                return IntPtr.Zero;
            if (_openAl != IntPtr.Zero)
                return _openAl;
            foreach (string candidate in OpenAlCandidates)
            {
                if (NativeLibrary.TryLoad(candidate, assembly, searchPath, out _openAl))
                {
                    ResolvedLibrary = candidate;
                    return _openAl;
                }
            }
            return IntPtr.Zero;   // -> DllNotFoundException at the call site; every caller handles it
        }

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

        // Open the audio device ONCE, at boot, and never let its failure kill the run. Call it
        // AFTER Boot() (LoadContent has to have run) and before the first Step.
        //
        // KNI would otherwise bring OpenAL up lazily on the first sound, deep inside
        // Content.Load<SoundEffect>, where SoundManager.GetEffect swallows the failure and caches
        // the miss -- so a device-less box played on in total silence with nothing anywhere saying
        // why. Asking here, explicitly, is what turns that into a reported fact (see the header).
        internal static void BringUp()
        {
            if (Device != DeviceState.NotTried)
                return;
            // Even when the device never opens, run the resolver so lib= reports what it found.
            TouchLibrary();
            try
            {
                _ = AudioService.Current;
                Device = DeviceState.Ok;
            }
            catch (NoAudioHardwareException ex)
            {
                // AudioService.Current wraps whatever the strategy ctor threw, so the
                // missing-binaries case arrives as an inner DllNotFoundException, not as itself.
                Device = HasInner<DllNotFoundException>(ex) ? DeviceState.NoLibrary : DeviceState.None;
                return;
            }
            catch (DllNotFoundException)     // in case a future KNI stops wrapping
            {
                Device = DeviceState.NoLibrary;
                return;
            }
            Pump();     // there is a mixer now, so the mute lands before any sound plays
        }

        private static void TouchLibrary()
        {
            try { alcGetCurrentContext(); }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        private static bool HasInner<T>(Exception ex) where T : Exception
        {
            for (Exception e = ex; e != null; e = e.InnerException)
                if (e is T)
                    return true;
            return false;
        }

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
            if (Device == DeviceState.None || Device == DeviceState.NoLibrary)
            {
                _pumpSettled = true;    // there is no mixer, and there never will be
                return;
            }
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

        // Declared against the logical name and resolved by ResolveOpenAl, so these reach OpenAL
        // on every platform KNI ships a binary for, not just Windows (see the header). Whichever
        // candidate wins is already loaded in-process by KNI from runtimes/<rid>/native, so
        // TryLoad returns that same module rather than loading a second copy.
        [DllImport(OpenAlLibrary, EntryPoint = "alGetListenerf", CallingConvention = CallingConvention.Cdecl)]
        private static extern void alGetListenerf(int param, ref float value);

        [DllImport(OpenAlLibrary, EntryPoint = "alcGetCurrentContext", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr alcGetCurrentContext();

        [DllImport(OpenAlLibrary, EntryPoint = "alListenerf", CallingConvention = CallingConvention.Cdecl)]
        private static extern void alListenerf(int param, float value);
    }

    // -----------------------------------------------------------------------------------
    // NoAudioDeviceSim -- makes THIS box look like one with no sound card, for real.
    //
    // The no-device path cannot be reached on a dev box by asking nicely, and a mocked flag would
    // prove nothing about KNI. So --fake-no-audio-device writes an alsoft.ini next to the exe
    // naming a backend that does not exist: OpenAL Soft finds no usable backend, alcOpenDevice
    // returns NULL, and KNI throws the same NoAudioHardwareException a driverless VM would. It is
    // the genuine failure, not a simulation of one, which is what makes probes/no_audio_device.txt
    // worth committing.
    //
    // An ini is the ONE supported way to configure OpenAL Soft from here (GetProcPath/ReadALConfig
    // loads it unconditionally); an environment variable provably cannot, see HeadlessAudio trap 2.
    // Do NOT reach for `drivers = null` instead -- that is the backend that crashes the process
    // (trap 1), and this flag would then be testing the crash rather than the missing device.
    //
    // It refuses rather than clobbers when an alsoft.ini is already there: on a box that has one
    // for a real reason, silently overwriting it would be a nasty way to find out.
    // -----------------------------------------------------------------------------------
    internal static class NoAudioDeviceSim
    {
        private const string FileName = "alsoft.ini";
        private const string Body = "[general]\ndrivers = eahl-no-such-backend\n";

        private static string _installedAt;

        // null on success, else why not.
        internal static string Install()
        {
            string path = Path.Combine(AppContext.BaseDirectory, FileName);
            if (File.Exists(path))
                return "an " + FileName + " already exists at " + path + " -- refusing to overwrite it";
            try
            {
                File.WriteAllText(path, Body);
            }
            catch (Exception ex)
            {
                return ex.GetType().Name + ": " + ex.Message;
            }
            _installedAt = path;
            return null;
        }

        // Only ever removes the file this process wrote. Best-effort: the run's verdict must not
        // hinge on cleanup, and the exe's own directory is build output either way.
        internal static void Remove()
        {
            if (_installedAt == null)
                return;
            try { File.Delete(_installedAt); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            _installedAt = null;
        }
    }
}
