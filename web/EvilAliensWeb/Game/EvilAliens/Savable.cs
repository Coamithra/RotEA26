using System;
using Microsoft.Xna.Framework.Storage;

namespace EvilAliens;

public abstract class Savable
{
	private bool wantsToSave;

	public static object syncObj = new object();

	public void Update()
	{
		if (wantsToSave)
		{
			SaveNoThread();
		}
	}

	public void Load()
	{
		if (!Storage.StorageEnabled)
		{
			return;
		}
		lock (syncObj)
		{
			wantsToSave = false;
			StorageContainer val = null;
			try
			{
				val = Storage.StorageDeviceManager.Device.OpenContainer("EvilAliens");
				loadData(val);
				if (!checkData())
				{
					throw new Exception("Invalid data");
				}
			}
			catch (Exception)
			{
				onLoadError();
				Storage.ShowLoadError("");
			}
			finally
			{
				if (val != null)
				{
					val.Dispose();
				}
			}
		}
	}

	public void SaveThreaded()
	{
		// Web port: browsers have no background threads (WASM is single-threaded),
		// so the original background save now runs synchronously on the game loop.
		SaveNoThread();
	}

	public void SaveNoThread()
	{
		if (SuppressSave)
		{
			// Debug-only, and it must clear wantsToSave too: leaving it set would have
			// Update() retry the save on the very next tick, forever.
			wantsToSave = false;
			return;
		}
		wantsToSave = true;
		if (Storage.StorageEnabled)
		{
			wantsToSave = false;
			SaveInner();
		}
	}

	// Save even when SuppressSave is on. For the ERASE direction only: suppression exists to
	// stop a debug flag's unlocks reaching the disk, and wiping progress to a clean slate is
	// the opposite of that. Without this the in-game "reset all progress" was half-applied
	// under ?unlockall -- it really deleted the screenshots and saved Settings, but silently
	// skipped Achievements/Unlockables, so every unlock resurrected on the next reload.
	public void SaveIgnoringSuppression()
	{
		wantsToSave = true;
		if (Storage.StorageEnabled)
		{
			wantsToSave = false;
			SaveInner();
		}
	}

	// A debug flag has mutated this savable in memory, so writing it out would persist a
	// state the player never earned. Card 36db5d75: ?unlockall says "session-only -- a
	// normal reload reverts it" and that was FALSE, because a scatter of unrelated call sites
	// (finishing a level, menu actions, UnlockEvent, the awardment banner, a co-op unlock
	// grant) persist the singletons it mutates -- grep SaveThreaded rather than trusting a
	// list here. Suppressing the WRITE makes the documented contract true by
	// construction, and it is far narrower than the ?invuln fix's shape (never write the
	// flag, read DebugFlags at the point of use) -- Invulnerability had two read sites,
	// whereas IsUnlocked/GetAwardmentIsUnlocked are read all over the menu.
	//
	// Only the two savables ?unlockall actually touches override this; Settings and the
	// screenshot blobs save normally.
	//
	// NOT debug-build-only -- DebugFlags parses the real URL query in Release too, so a player
	// who loads the live site with ?unlockall (a bookmarked or shared debug URL) gets this. For
	// that session NOTHING in Achievements persists, and that file carries per-level hiscore /
	// isFinished / difficulty as well as the ten awardment bools -- so a run with the flag on
	// silently keeps no progress at all. That is the intended trade (it is what makes the
	// session-only promise true), and Game1 prints a warning at boot when the flag is set so it
	// is not silent. It also retires the undocumented trick of using ?unlockall on the live site
	// to make unlocks permanent.
	protected virtual bool SuppressSave => false;

	private void SaveInner()
	{
		lock (syncObj)
		{
			StorageContainer val = null;
			try
			{
				val = Storage.StorageDeviceManager.Device.OpenContainer("EvilAliens");
				saveData(val);
			}
			catch (Exception)
			{
				Storage.ShowSaveError("");
			}
			finally
			{
				if (val != null)
				{
					val.Dispose();
				}
			}
		}
	}

	protected abstract void saveData(StorageContainer c);

	protected abstract void loadData(StorageContainer c);

	protected abstract void onLoadError();

	protected abstract bool checkData();
}
