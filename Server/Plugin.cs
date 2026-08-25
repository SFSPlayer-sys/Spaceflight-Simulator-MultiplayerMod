using System;
using Lidgren.Network;
namespace MultiplayerSFS.Server
{
    public static class Plugin
    {
        public static event Action<ConnectedPlayer> OnPlayerJoined;
        public static event Action<ConnectedPlayer> OnPlayerLeft;
        public static event Action<ConnectedPlayer, string> OnChatMessage;
        public static event Func<NetConnection, string, bool> OnMessageHandled;
        public static event Action<string, object> OnAnyEvent;
        internal static void TriggerEvent(string eventName, object data) => OnAnyEvent?.Invoke(eventName, data);
        internal static void TriggerPlayerJoined(ConnectedPlayer player) => OnPlayerJoined?.Invoke(player);
        internal static void TriggerPlayerLeft(ConnectedPlayer player) => OnPlayerLeft?.Invoke(player);
        internal static void TriggerChatMessage(ConnectedPlayer player, string message) => OnChatMessage?.Invoke(player, message);
        internal static bool TryHandleMessage(NetConnection connection, string message)
        {
            if (OnMessageHandled == null) return false;
            foreach (Func<NetConnection, string, bool> handler in OnMessageHandled.GetInvocationList())
            {
                if (handler(connection, message)) return true;
            }
            return false;
        }
    }
}
