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
		wantsToSave = true;
		if (Storage.StorageEnabled)
		{
			wantsToSave = false;
			SaveInner();
		}
	}

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
