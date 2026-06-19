// ---------------------------------------------------------------------------
// Stub implementation of Microsoft.Xna.Framework.GamerServices.
//
// The original game targeted XNA 3.x on Xbox 360 and used the Xbox LIVE
// "GamerServices" APIs (sign-in, achievements, trial mode, message boxes,
// presence, the storage-device selector). None of that exists on the web.
//
// These stubs exist so the original game code COMPILES and runs unchanged.
// The key design trick: Gamer.SignedInGamers is always EMPTY, so every
// "foreach signed-in gamer ... set presence / award achievement" loop in the
// game simply does nothing. Guide.* calls are no-ops with sensible defaults
// (IsTrialMode = false => the full game is unlocked on the web).
// ---------------------------------------------------------------------------
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Storage;

namespace Microsoft.Xna.Framework.GamerServices
{
    // Enum values come through the decompiler as raw int casts (e.g. (GamerPresenceMode)14),
    // so the enums only need to EXIST; named members are irrelevant.
    public enum GamerPresenceMode { }
    public enum MessageBoxIcon { None = 0, Error = 1, Warning = 2, Alert = 3 }
    public enum GamerPrivilegeSetting { Blocked = 0, FriendsOnly = 1, Everyone = 2 }

    public class GamerPresence
    {
        public GamerPresenceMode PresenceMode { get; set; }
        public string PresenceValue { get; set; }
    }

    public class GamerPrivileges
    {
        public GamerPrivilegeSetting AllowCommunication => GamerPrivilegeSetting.Blocked;
        public GamerPrivilegeSetting AllowOnlineSessions => GamerPrivilegeSetting.Blocked;
        public bool AllowProfileViewing => false;
        public bool AllowPurchaseContent => false;
    }

    public enum GameDifficulty { Unknown = 0, Easy = 1, Medium = 2, Hard = 3 }

    public class GameDefaults
    {
        public GameDifficulty GameDifficulty => GameDifficulty.Unknown;
    }

    public class FriendGamer : Gamer
    {
        public bool IsFriend => true;
        public bool HasSentFriendRequest => false;
    }

    public class FriendCollection : GamerCollection<FriendGamer>
    {
        public FriendCollection() : base(new System.Collections.Generic.List<FriendGamer>()) { }
    }

    public class Gamer
    {
        public string Gamertag { get; set; } = "Player";
        public GamerPresence Presence { get; } = new GamerPresence();

        private static readonly GamerCollection<SignedInGamer> _signedInGamers
            = new GamerCollection<SignedInGamer>(new List<SignedInGamer>());

        // Always empty on the web: disables all per-gamer game logic by construction.
        public static GamerCollection<SignedInGamer> SignedInGamers => _signedInGamers;
    }

    public class SignedInGamer : Gamer
    {
        public PlayerIndex PlayerIndex { get; set; }
        public bool IsSignedInToLive => false;
        public bool IsGuest => false;
        public GamerPrivileges Privileges { get; } = new GamerPrivileges();
        public GameDefaults GameDefaults { get; } = new GameDefaults();
        public FriendCollection GetFriends() => new FriendCollection();
    }

    // The game both foreach-enumerates this (via GamerCollectionEnumerator<T>) and
    // casts it to ReadOnlyCollection<T> for Count / indexer access, so we derive
    // from ReadOnlyCollection<T> and add a strongly typed GetEnumerator.
    public class GamerCollection<T> : ReadOnlyCollection<T>
    {
        public GamerCollection(IList<T> list) : base(list) { }
        public new GamerCollectionEnumerator<T> GetEnumerator() => new GamerCollectionEnumerator<T>(this);
    }

    public class GamerCollectionEnumerator<T> : IEnumerator<T>
    {
        private readonly IList<T> _list;
        private int _index = -1;
        public GamerCollectionEnumerator(IList<T> list) { _list = list; }
        public T Current => _list[_index];
        object IEnumerator.Current => Current;
        public bool MoveNext() { _index++; return _index < _list.Count; }
        public void Reset() { _index = -1; }
        public void Dispose() { }
    }

    public class GamerServicesComponent : GameComponent
    {
        public GamerServicesComponent(Game game) : base(game) { }
    }

    // A completed-synchronously async result; callbacks fire immediately.
    internal sealed class StubAsyncResult : IAsyncResult
    {
        private readonly ManualResetEvent _handle = new ManualResetEvent(true);
        public StubAsyncResult(object state) { AsyncState = state; }
        public object AsyncState { get; }
        public WaitHandle AsyncWaitHandle => _handle;
        public bool CompletedSynchronously => true;
        public bool IsCompleted => true;
    }

    public static class Guide
    {
        public static bool IsVisible => false;
        public static bool IsTrialMode => false;            // full game unlocked on the web
        public static bool SimulateTrialMode { get; set; } = false;
        public static bool IsScreenSaverEnabled { get; set; } = false;
        public static NotificationPosition NotificationPosition { get; set; }

        public static IAsyncResult BeginShowMessageBox(PlayerIndex player, string title, string text,
            IEnumerable<string> buttons, int focusButton, MessageBoxIcon icon, AsyncCallback callback, object state)
        { var ar = new StubAsyncResult(state); callback?.Invoke(ar); return ar; }

        public static IAsyncResult BeginShowMessageBox(string title, string text,
            IEnumerable<string> buttons, int focusButton, MessageBoxIcon icon, AsyncCallback callback, object state)
        { var ar = new StubAsyncResult(state); callback?.Invoke(ar); return ar; }

        public static int? EndShowMessageBox(IAsyncResult result) => null;

        public static void ShowSignIn(int paneCount, bool onlineOnly) { }
        public static void ShowMarketplace(PlayerIndex player) { }
        public static void ShowComposeMessage(PlayerIndex player, string text, IEnumerable<Gamer> recipients) { }

        public static IAsyncResult BeginShowStorageDeviceSelector(PlayerIndex player, int sizeInBytes,
            int directoryCount, AsyncCallback callback, object state)
        { var ar = new StubAsyncResult(state); callback?.Invoke(ar); return ar; }

        public static IAsyncResult BeginShowStorageDeviceSelector(int sizeInBytes, int directoryCount,
            AsyncCallback callback, object state)
        { var ar = new StubAsyncResult(state); callback?.Invoke(ar); return ar; }

        // Returns the MEMFS-backed stub device (see StorageStub.cs).
        public static StorageDevice EndShowStorageDeviceSelector(IAsyncResult result) => StorageDevice.Default;
    }

    public enum NotificationPosition { TopLeft, TopCenter, TopRight, CenterLeft, Center, CenterRight, BottomLeft, BottomCenter, BottomRight }
}
