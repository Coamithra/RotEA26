using System;
using System.Threading;
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
		wantsToSave = true;
		if (Storage.StorageEnabled)
		{
			wantsToSave = false;
			Thread thread = new Thread(threadedSave);
			thread.Start();
		}
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

	private void threadedSave()
	{
		// Thread.CurrentThread.SetProcessorAffinity(new int[1] { 3 }); // Xbox 360 only
		Thread.CurrentThread.Priority = ThreadPriority.Normal;
		SaveInner();
	}

	private void SaveInner()
	{
		lock (syncObj)
		{
			Thread.Sleep(100);
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
