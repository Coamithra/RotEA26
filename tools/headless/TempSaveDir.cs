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
// records. Measured as a matched A/B, same subject binary, a churner performing the same delete
// a concurrent boot performs at ~780 wipes per trial: churn aimed at the SHARED dir failed 10 of
// 10 runs, churn aimed at a PER-PROCESS dir 0 of 10.
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
        // directory. Six hours is ~72x the runner's own 300 s probe timeout, so no PROBE run can
        // be near it -- but age alone is not a safe rule, because `--repl` is advertised as a
        // session an agent boots once and drives all day. Only writes directly inside the claimed
        // directory refresh its mtime, and the game writes to <claimed>/fs/EvilAliens/, so an
        // idle-but-live repl ages exactly like a leak and a sibling's Claim() would delete its
        // saves -- the very failure this file exists to remove.
        //
        // So a directory is collected only when it is BOTH older than this AND owned by a pid
        // that is gone. Note the asymmetry that makes the liveness test safe here: pids are
        // recycled, so it can wrongly conclude "alive" and SKIP a dead run's directory (which
        // merely leaks, and the name carries the claim time so a later sweep with a different
        // pid table collects it), but it can never conclude "dead" about a process that is
        // running. Liveness as the SOLE criterion would be the racy design -- as an extra veto
        // on top of age it is strictly conservative.
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
                    if (Directory.GetLastWriteTimeUtc(dir) < cutoff && !OwnerAlive(dir))
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

        // Is the pid in "<pid>-<ticks>" still running? Answers TRUE whenever it cannot tell --
        // an unparseable name, a pid it may not query, any surprise -- because every ambiguous
        // answer must be the one that keeps the directory. See StaleHours.
        private static bool OwnerAlive(string dir)
        {
            string name = Path.GetFileName(dir);
            int dash = name.IndexOf('-');
            if (dash <= 0 || !int.TryParse(name.Substring(0, dash), NumberStyles.Integer,
                                           CultureInfo.InvariantCulture, out int pid))
                return true;
            try
            {
                // Throws ArgumentException when no such process exists -- the only answer that
                // licenses a delete.
                using (System.Diagnostics.Process.GetProcessById(pid))
                    return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (Exception)
            {
                return true;
            }
        }
    }
}
