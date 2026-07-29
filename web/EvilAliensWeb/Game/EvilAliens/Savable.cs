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

	// A debug flag has mutated this savable in memory, so writing it out would persist a
	// state the player never earned. Card 36db5d75: ?unlockall says "session-only -- a
	// normal reload reverts it" and that was FALSE, because five unrelated call sites
	// (GameScene finishing a level, MenuScene, UnlockEvent, AwardmentBlade x2) persist the
	// singletons it mutates. Suppressing the WRITE makes the documented contract true by
	// construction, and it is far narrower than the ?invuln fix's shape (never write the
	// flag, read DebugFlags at the point of use) -- Invulnerability had two read sites,
	// whereas IsUnlocked/GetAwardmentIsUnlocked are read all over the menu.
	//
	// Only the two savables ?unlockall actually touches override this; Settings and the
	// screenshot blobs save normally. False in every shipped build, so it costs one bool
	// test. The intended consequence: a ?unlockall session cannot persist LEGITIMATE
	// progress either -- correct for a debug session, and the reason this is a per-savable
	// override rather than a blanket one.
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
