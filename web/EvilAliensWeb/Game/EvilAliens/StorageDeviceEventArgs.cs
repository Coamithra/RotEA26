using System;

namespace EvilAliens;

public class StorageDeviceEventArgs : EventArgs
{
	public StorageDeviceSelectorEventResponse EventResponse { get; set; }
}
