using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using EvilAliens.Constants;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.GamerServices;

namespace EvilAliens;

public static class Storage
{
	private static bool started = false;

	private static bool busy;

	private static bool storageEnabled;

	public static StorageDeviceManager StorageDeviceManager;

	private static PlayerIndex activePlayer;

	public static bool Busy => busy;

	public static bool StorageEnabled => storageEnabled;

	public static PlayerIndex ActivePlayer => activePlayer;

	public static void Init(Game game, PlayerIndex player)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		activePlayer = player;
		started = true;
		busy = true;
		StorageDeviceManager = new StorageDeviceManager(game, player);
		((Collection<IGameComponent>)(object)game.Components).Add((IGameComponent)(object)StorageDeviceManager);
		StorageDeviceManager.DeviceDisconnected += storageDeviceManager_DeviceDisconnected;
		StorageDeviceManager.DevicePromptClosed += storageDeviceManager_DevicePromptClosed;
		StorageDeviceManager.DeviceSelected += storageDeviceManager_DeviceSelected;
		StorageDeviceManager.DeviceSelectorCancelled += storageDeviceManager_DeviceSelectorCancelled;
		StorageDeviceManager.PromptForDevice();
		if (General.IsTrial)
		{
			storageEnabled = false;
			busy = false;
		}
	}

	private static void storageDeviceManager_DeviceSelectorCancelled(object sender, StorageDeviceEventArgs e)
	{
		e.EventResponse = StorageDeviceSelectorEventResponse.Prompt;
	}

	private static void storageDeviceManager_DeviceSelected(object sender, EventArgs e)
	{
		storageEnabled = true;
		busy = false;
	}

	private static void storageDeviceManager_DevicePromptClosed(object sender, StorageDevicePromptEventArgs e)
	{
		if (!e.PromptForDevice)
		{
			storageEnabled = false;
			busy = false;
		}
	}

	private static void storageDeviceManager_DeviceDisconnected(object sender, StorageDeviceEventArgs e)
	{
		e.EventResponse = StorageDeviceSelectorEventResponse.Prompt;
		busy = true;
	}

	public static void ShowLoadError(string message)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			if (!Guide.IsVisible)
			{
				Guide.BeginShowMessageBox(activePlayer, "Error", "There was an error while loading. Your data may have been lost.\n\n" + message, (IEnumerable<string>)new string[1] { "Ok" }, 0, (MessageBoxIcon)1, (AsyncCallback)null, (object)null);
			}
		}
		catch (Exception)
		{
		}
	}

	public static void ShowSaveError(string message)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			if (!Guide.IsVisible)
			{
				Guide.BeginShowMessageBox(activePlayer, "Error", "There was an error while saving. Your data may have been lost.\n\n" + message, (IEnumerable<string>)new string[1] { "Ok" }, 0, (MessageBoxIcon)1, (AsyncCallback)null, (object)null);
			}
		}
		catch (Exception)
		{
		}
	}

	public static void Reset(Game1 game)
	{
		lock (Savable.syncObj)
		{
			game.Reset();
			started = false;
			storageEnabled = false;
		}
	}

	public static void Update(GameTime gameTime, Game1 game)
	{
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		// Web/PC port: this is the Xbox "active player signed OUT -> reset to start"
		// check. SignedInGamers is empty on the web (no sign-in / no sign-out), so only
		// run it when a gamer actually exists, guarding the indexer against the empty
		// collection (which would otherwise throw every frame once a game has started).
		if (!busy && started && (int)activePlayer < Gamer.SignedInGamers.Count
			&& Gamer.SignedInGamers[(int)activePlayer] == null)
		{
			Reset(game);
		}
	}
}
