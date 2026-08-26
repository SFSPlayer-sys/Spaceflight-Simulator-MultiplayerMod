using System;
using Lidgren.Network;
#if NET48
using MultiplayerSFS.Common;
#else
using MultiplayerSFS.ServerCommon;
#endif
//插件接口

namespace MultiplayerSFS.Server
{
    public static class Plugin
    {
        public static event Func<NetConnection, string, string, string, bool> OnPanelReply;
        public static event Action<ConnectedPlayer> OnPlayerJoined;
        public static event Action<ConnectedPlayer> OnPlayerLeft;
        public static event Action<ConnectedPlayer, string> OnChatMessage;
        public static event Func<NetConnection, string, bool> OnMessageHandled;
        public static event Action<string, object> OnAnyEvent;
        public static event Func<NetConnection, PacketType, NetIncomingMessage, bool> OnPacketReceived;
        public static event Action<NetConnection, Packet> OnPacketSent;
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
        internal static bool TryHandlePanelReply(NetConnection connection, string message)
        {
            const string start = "#UI_REPLY_START#";
            const string end = "#UI_REPLY_END#";
            int si = message.IndexOf(start, StringComparison.Ordinal);
            int ei = message.IndexOf(end, StringComparison.Ordinal);
            if (si < 0 || ei < 0 || ei <= si + start.Length)
                return false;
            string body = message.Substring(si + start.Length, ei - si - start.Length);
            if (OnPanelReply == null)
                return true;
            string panelId = "";
            string action = "";
            string value = "";
            foreach (string line in body.Split('\n'))
            {
                string t = line.Trim();
                if (t.StartsWith("\"panel_id\":", StringComparison.Ordinal))
                    panelId = ExtractValue(t);
                else if (t.StartsWith("\"action\":", StringComparison.Ordinal))
                    action = ExtractValue(t);
                else if (t.StartsWith("\"value\":", StringComparison.Ordinal))
                    value = ExtractValue(t);
            }
            foreach (Func<NetConnection, string, string, string, bool> handler in OnPanelReply.GetInvocationList())
            {
                if (handler(connection, panelId, action, value))
                    return true;
            }
            return true;
        }
        static string ExtractValue(string line)
        {
            int i = line.IndexOf(':');
            if (i < 0) return "";
            string v = line.Substring(i + 1).Trim();
            return v.StartsWith("\"") && v.EndsWith("\"") && v.Length >= 2
                ? v.Substring(1, v.Length - 2)
                : v;
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
        internal static void TriggerPacketSent(NetConnection connection, Packet packet)
        {
            OnPacketSent?.Invoke(connection, packet);
        }
        internal static void TriggerUnhandledPacket(PacketType type, NetConnection connection)
        {
            OnUnhandledPacket?.Invoke(type, connection);
        }
    }
}
