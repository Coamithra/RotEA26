using System;
using System.Collections.Generic;
using EvilAliens.Constants;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Storage;

namespace EvilAliens;

public class StorageDeviceManager : GameComponent
{
	private const string ReselectStorageDeviceText = "No storage device was selected. Would you like to re-select the storage device?";

	private const string DisconnectReselectDeviceText = "An active storage device has been disconnected. Would you like to select a new storage device?";

	private const string ForceReselectDeviceText = "No storage device was selected. A storage device is required to continue. Select Ok to choose a storage device.";

	private const string ForceDisconnectReselectText = "An active storage device has been disconnected. A storage device is required to continue. Select Ok to choose a storage device.";

	private const float TIME_BETWEEN_DISCONNECT_CHECKS = 5f;

	private bool selectorShown;

	private bool wasDeviceConnected;

	private bool showDeviceSelector;

	private bool promptToReSelectDevice;

	private bool promptToForceReselect;

	private bool promptForDisconnect;

	private bool promptForDisconnectForced;

	private readonly StorageDeviceEventArgs eventArgs = new StorageDeviceEventArgs();

	private readonly StorageDevicePromptEventArgs promptEventArgs = new StorageDevicePromptEventArgs();

	private float updateTimer = 5f;

	private float storageShownResetTimer = 5f;

	public StorageDevice Device { get; private set; }

	public PlayerIndex? Player { get; private set; }

	public PlayerIndex PlayerToPrompt { get; set; }

	public int RequiredBytes { get; private set; }

	public event EventHandler DeviceSelected;

	public event EventHandler<StorageDeviceEventArgs> DeviceSelectorCancelled;

	public event EventHandler<StorageDevicePromptEventArgs> DevicePromptClosed;

	public event EventHandler<StorageDeviceEventArgs> DeviceDisconnected;

	public StorageDeviceManager(Game game)
		: this(game, (PlayerIndex?)null, 0)
	{
	}

	public StorageDeviceManager(Game game, PlayerIndex player)
		: this(game, player, 0)
	{
	}//IL_0002: Unknown result type (might be due to invalid IL or missing references)


	public StorageDeviceManager(Game game, int requiredBytes)
		: this(game, (PlayerIndex?)null, requiredBytes)
	{
	}

	public StorageDeviceManager(Game game, PlayerIndex player, int requiredBytes)
		: this(game, (PlayerIndex?)player, requiredBytes)
	{
	}//IL_0002: Unknown result type (might be due to invalid IL or missing references)


	private StorageDeviceManager(Game game, PlayerIndex? player, int requiredBytes)
		: base(game)
	{
		Player = player;
		RequiredBytes = requiredBytes;
		PlayerToPrompt = (PlayerIndex)0;
	}

	public void PromptForDevice()
	{
		showDeviceSelector = true;
		updateTimer = 0f;
	}

	public override void Update(GameTime gameTime)
	{
		//IL_0192: Unknown result type (might be due to invalid IL or missing references)
		//IL_0181: Unknown result type (might be due to invalid IL or missing references)
		//IL_010b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0202: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f1: Unknown result type (might be due to invalid IL or missing references)
		//IL_0272: Unknown result type (might be due to invalid IL or missing references)
		//IL_0261: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d6: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c5: Unknown result type (might be due to invalid IL or missing references)
		if (General.IsTrial)
		{
			return;
		}
		storageShownResetTimer -= (float)gameTime.ElapsedGameTime.TotalSeconds;
		if (storageShownResetTimer <= 0f)
		{
			storageShownResetTimer = 5f;
			selectorShown = false;
		}
		updateTimer -= (float)gameTime.ElapsedGameTime.TotalSeconds;
		if (!(updateTimer <= 0f) && !showDeviceSelector && !promptToReSelectDevice && !promptForDisconnect && !promptToForceReselect && !promptForDisconnectForced)
		{
			return;
		}
		updateTimer = 5f;
		bool flag = false;
		if (Device != null)
		{
			flag = Device.IsConnected;
		}
		if (Device != null && !flag && wasDeviceConnected)
		{
			FireDeviceDisconnectedEvent();
		}
		try
		{
			if (!Guide.IsVisible)
			{
				if (showDeviceSelector)
				{
					if (Player.HasValue)
					{
						if (!selectorShown)
						{
							Guide.BeginShowStorageDeviceSelector(Player.Value, RequiredBytes, 0, (AsyncCallback)deviceSelectorCallback, (object)null);
							selectorShown = true;
						}
					}
					else if (!selectorShown)
					{
						Guide.BeginShowStorageDeviceSelector(RequiredBytes, 0, (AsyncCallback)deviceSelectorCallback, (object)null);
						selectorShown = true;
					}
				}
				else if (promptToReSelectDevice)
				{
					Guide.BeginShowMessageBox(Player.HasValue ? Player.Value : PlayerToPrompt, "Reselect Storage Device?", "No storage device was selected. Would you like to re-select the storage device?", (IEnumerable<string>)new string[2] { "Yes. Select new device.", "No. Continue without device." }, 0, (MessageBoxIcon)0, (AsyncCallback)reselectPromptCallback, (object)null);
				}
				else if (promptForDisconnect)
				{
					Guide.BeginShowMessageBox(Player.HasValue ? Player.Value : PlayerToPrompt, "Storage Device Disconnected", "An active storage device has been disconnected. Would you like to select a new storage device?", (IEnumerable<string>)new string[2] { "Yes. Select new device.", "No. Continue without device." }, 0, (MessageBoxIcon)0, (AsyncCallback)reselectPromptCallback, (object)null);
				}
				else if (promptToForceReselect)
				{
					Guide.BeginShowMessageBox(Player.HasValue ? Player.Value : PlayerToPrompt, "Reselect Storage Device", "No storage device was selected. A storage device is required to continue. Select Ok to choose a storage device.", (IEnumerable<string>)new string[1] { "Ok" }, 0, (MessageBoxIcon)0, (AsyncCallback)forcePromptCallback, (object)null);
				}
				else if (promptForDisconnectForced)
				{
					Guide.BeginShowMessageBox(Player.HasValue ? Player.Value : PlayerToPrompt, "Storage Device Disconnected", "An active storage device has been disconnected. A storage device is required to continue. Select Ok to choose a storage device.", (IEnumerable<string>)new string[1] { "Ok" }, 0, (MessageBoxIcon)0, (AsyncCallback)forcePromptCallback, (object)null);
				}
			}
		}
		catch (Exception)
		{
		}
		wasDeviceConnected = Device != null && flag;
	}

	private void forcePromptCallback(IAsyncResult ar)
	{
		promptToForceReselect = false;
		promptForDisconnectForced = false;
		Guide.EndShowMessageBox(ar);
		showDeviceSelector = true;
		updateTimer = 0f;
	}

	private void reselectPromptCallback(IAsyncResult ar)
	{
		promptForDisconnect = false;
		promptToReSelectDevice = false;
		int? num = Guide.EndShowMessageBox(ar);
		showDeviceSelector = num.HasValue && num.Value == 0;
		promptEventArgs.PromptForDevice = showDeviceSelector;
		if (this.DevicePromptClosed != null)
		{
			this.DevicePromptClosed(this, promptEventArgs);
		}
		updateTimer = 0f;
	}

	private void deviceSelectorCallback(IAsyncResult ar)
	{
		selectorShown = false;
		showDeviceSelector = false;
		Device = Guide.EndShowStorageDeviceSelector(ar);
		if (Device != null)
		{
			if (this.DeviceSelected != null)
			{
				this.DeviceSelected(this, EventArgs.Empty);
			}
		}
		else
		{
			eventArgs.EventResponse = StorageDeviceSelectorEventResponse.Prompt;
			if (this.DeviceSelectorCancelled != null)
			{
				this.DeviceSelectorCancelled(this, eventArgs);
			}
			HandleEventArgResults();
		}
		updateTimer = 0f;
	}

	private void FireDeviceDisconnectedEvent()
	{
		eventArgs.EventResponse = StorageDeviceSelectorEventResponse.Prompt;
		if (this.DeviceDisconnected != null)
		{
			this.DeviceDisconnected(this, eventArgs);
		}
		HandleEventArgResults();
		updateTimer = 0f;
	}

	private void HandleEventArgResults()
	{
		Device = null;
		switch (eventArgs.EventResponse)
		{
		case StorageDeviceSelectorEventResponse.Prompt:
			promptForDisconnect = true;
			break;
		case StorageDeviceSelectorEventResponse.Force:
			promptToForceReselect = true;
			break;
		default:
			promptForDisconnect = false;
			promptForDisconnectForced = false;
			promptToForceReselect = false;
			showDeviceSelector = false;
			break;
		}
		updateTimer = 0f;
	}
}
