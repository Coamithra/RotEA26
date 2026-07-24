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
	Remote,
	// Coverage-gaps follow-up: a client-side puppet for one of the HOST's AI "friend" ships
	// (Mechanical Friends cheat). Network-driven like Remote (NetSession.DriveFriendShip), but
	// there can be several, one per host friend slot. Host-side friends stay ControlDevice.AI.
	// APPEND-ONLY.
	RemoteFriend
}
