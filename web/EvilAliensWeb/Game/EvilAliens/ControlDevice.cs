namespace EvilAliens;

public enum ControlDevice
{
	PadOne,
	PadTwo,
	PadThree,
	PadFour,
	Generic,
	Keyboard,
	AI,
	// Online co-op (Stage 11): a ship owned by the OTHER peer -- driven by the network-fed
	// interpolation buffer (Compat/Net/NetSession.DriveRemoteShip), never by local input.
	// APPEND-ONLY position (existing members' ordinals must not shift).
	Remote
}
