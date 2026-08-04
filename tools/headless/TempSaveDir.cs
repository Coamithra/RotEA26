// ---------------------------------------------------------------------------
// TempSaveDir — the default save directory, one per PROCESS.
//
// It used to be the single fixed path %TEMP%/eahl-saves, shared by every eahl on the box, and
// HeadlessSaveStore's constructor recursively DELETES that directory on every boot (a test rig
// must start from a known state — card 36db5d75). One process per probe is fine; several at once
// is not, and several at once is the normal condition here: eight parallel worktree agents each
// running a 50-probe suite. So one run's boot deletes another run's save tree mid-write.
//
// That is a measured cause, not a worry (card de82597f). ScreenshotSaver.SaveScreenShot opens
// <saves>/fs/EvilAliens/<Level>.dat with FileMode.Create and FileShare.None; with the directory
// deleted under it, the open throws and screenshot_alpha.txt fails AFTER printing a perfectly
// correct `[shot] Level2 300x225 alphaMin=255` — which is exactly the once-seen failure the card
// records. Reproduced 5/5 by churning the shared directory while a run saved thumbnails, and 0
// failures over the same experiment once the run had a directory of its own.
//
// The fix is that each process claims %TEMP%/eahl-saves/<pid>-<ticks> and removes it on the way
// out. `--saves <dir>` is untouched: an explicit directory is a deliberate persistent profile,
// it is never wiped, and it is the caller's to manage.
//
// Nothing in the repo relied on the old sharing — the runner passes no --saves, every probe is
// its own process, and both READMEs already promise "saves start empty every run". This makes
// that promise true when the box is busy, which is when it was silently false.
// ---------------------------------------------------------------------------
using System;
using System.Globalization;
using System.IO;

namespace EvilAliensWeb.Headless
{
    internal static class TempSaveDir
    {
        // A run that dies without unwinding (a crash, a kill, a --script timeout) leaks its
        // directory. Deliberately swept by AGE rather than by liveness: asking whether a pid is
        // still alive races and would eventually delete a running sibling's saves, which is the
        // very bug this file exists to fix. Six hours is ~72x the runner's own 300 s probe
        // timeout, so nothing that is still running can be near it.
        private const double StaleHours = 6.0;

        private static string _claimed;

        internal static string Base => Path.Combine(Path.GetTempPath(), "eahl-saves");

        // The pid alone is not enough: pids are recycled, and a leaked directory from a dead
        // process with the same pid would be adopted (with its saves) instead of starting clean.
        internal static string Claim()
        {
            Sweep();
            _claimed = Path.Combine(Base, string.Format(CultureInfo.InvariantCulture, "{0}-{1:x}",
                Environment.ProcessId, DateTime.UtcNow.Ticks));
            return _claimed;
        }

        // Best effort, and only what this process claimed. A failure here is not worth reporting:
        // the sweep above collects it on some later run, and an `err` line at exit would look
        // like the run itself failed.
        internal static void Release()
        {
            if (_claimed == null)
                return;
            try
            {
                if (Directory.Exists(_claimed))
                    Directory.Delete(_claimed, true);
            }
            catch (Exception)
            {
            }
            _claimed = null;
        }

        // Collects both leaked per-process directories and the pre-card layout's loose files
        // (Base used to hold Settings.xml.b64 and fs/ directly), since one age rule covers both.
        private static void Sweep()
        {
            try
            {
                if (!Directory.Exists(Base))
                    return;
                DateTime cutoff = DateTime.UtcNow.AddHours(-StaleHours);
                foreach (string dir in Directory.GetDirectories(Base))
                    if (Directory.GetLastWriteTimeUtc(dir) < cutoff)
                        try { Directory.Delete(dir, true); } catch (Exception) { }
                foreach (string file in Directory.GetFiles(Base))
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                        try { File.Delete(file); } catch (Exception) { }
            }
            catch (Exception)
            {
                // An unreadable temp dir is the caller's problem one line later, when the store
                // fails to create its own directory and says so.
            }
        }
    }
}
