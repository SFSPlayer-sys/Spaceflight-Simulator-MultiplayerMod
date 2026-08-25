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
        //数据包接收：返回 true 阻止默认处理
        public static event Func<NetConnection, PacketType, NetIncomingMessage, bool> OnPacketReceived;
        //数据包发送时触发（null connection = 广播给所有人）
        public static event Action<NetConnection?, Packet> OnPacketSent;
        //收到未处理的包类型时触发
        public static event Action<PacketType, NetConnection> OnUnhandledPacket;

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
        internal static bool TryHandlePacketReceived(NetConnection connection, PacketType type, NetIncomingMessage msg)
        {
            if (OnPacketReceived == null) return false;
            foreach (Func<NetConnection, PacketType, NetIncomingMessage, bool> handler in OnPacketReceived.GetInvocationList())
            {
                if (handler(connection, type, msg)) return true;
            }
            return false;
        }
        internal static void TriggerPacketSent(NetConnection? connection, Packet packet)
        {
            OnPacketSent?.Invoke(connection, packet);
        }
        internal static void TriggerUnhandledPacket(PacketType type, NetConnection connection)
        {
            OnUnhandledPacket?.Invoke(type, connection);
        }
    }
}
