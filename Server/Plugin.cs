using System;
using System.Text.RegularExpressions;
using System.Collections.Generic;
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
        public static string AskUser(string q)
        {
            if (Server.isOpenToLan)
                return null;
            Console.Write(q);
            return Console.ReadLine();
        }

        // 设置火箭全部状态并广播更新
        public static void SetRocketState(int rocketId, RocketState state)
        {
            if (!Server.world.rockets.TryGetValue(rocketId, out RocketState target))
                return;
            double wt = Server.world.WorldTime;
            target.rocketName = state.rocketName;
            target.location = state.location;
            target.rotation = state.rotation;
            target.angularVelocity = state.angularVelocity;
            target.throttleOn = state.throttleOn;
            target.throttlePercent = state.throttlePercent;
            target.RCS = state.RCS;
            target.input_Turn = state.input_Turn;
            target.input_Raw = state.input_Raw;
            target.input_Horizontal = state.input_Horizontal;
            target.input_Vertical = state.input_Vertical;
            target.parts = state.parts;
            target.joints = state.joints;
            target.stages = state.stages;
            Server.SendPacketToAll(new Packet_UpdateRocketPrimary()
            {
                RocketId = rocketId,
                WorldTime = wt,
                Location = state.location,
                Rotation = state.rotation,
                AngularVelocity = state.angularVelocity,
            });
            Server.SendPacketToAll(new Packet_UpdateRocketSecondary()
            {
                RocketId = rocketId,
                WorldTime = wt,
                ThrottlePercent = state.throttlePercent,
                ThrottleOn = state.throttleOn,
                RCS = state.RCS,
                Input_Turn = state.input_Turn,
                Input_Raw = state.input_Raw,
                Input_Horizontal = state.input_Horizontal,
                Input_Vertical = state.input_Vertical,
            });
            Server.SendPacketToAll(new Packet_UpdateStaging()
            {
                RocketId = rocketId,
                WorldTime = wt,
                Stages = state.stages,
            });
            foreach (KeyValuePair<int, PartState> kvp in state.parts)
            {
                var part = kvp.Value.part;
                if (part == null)
                    continue;
                if (part.TOGGLE_VARIABLES.TryGetValue("engine_on", out bool engineOn))
                {
                    Server.SendPacketToAll(new Packet_UpdatePart_EngineModule()
                    {
                        RocketId = rocketId,
                        PartId = kvp.Key,
                        WorldTime = wt,
                        EngineOn = engineOn,
                    });
                }
                if (part.TOGGLE_VARIABLES.TryGetValue("wheel_on", out bool wheelOn))
                {
                    Server.SendPacketToAll(new Packet_UpdatePart_WheelModule()
                    {
                        RocketId = rocketId,
                        PartId = kvp.Key,
                        WorldTime = wt,
                        WheelOn = wheelOn,
                    });
                }
                if (part.NUMBER_VARIABLES.TryGetValue("animation_state", out double animState) && part.NUMBER_VARIABLES.TryGetValue("deploy_state", out double deployState))
                {
                    Server.SendPacketToAll(new Packet_UpdatePart_ParachuteModule()
                    {
                        RocketId = rocketId,
                        PartId = kvp.Key,
                        WorldTime = wt,
                        State = (float)animState,
                        TargetState = (float)deployState,
                    });
                }
                if (part.NUMBER_VARIABLES.TryGetValue("state", out double moveTime) && part.NUMBER_VARIABLES.TryGetValue("state_target", out double moveTarget))
                {
                    Server.SendPacketToAll(new Packet_UpdatePart_MoveModule()
                    {
                        RocketId = rocketId,
                        PartId = kvp.Key,
                        WorldTime = wt,
                        Time = (float)moveTime,
                        TargetTime = (float)moveTarget,
                    });
                }
                if (part.NUMBER_VARIABLES.TryGetValue("fuel_percent", out double fuel))
                {
                    Server.SendPacketToAll(new Packet_UpdatePart_ResourceModule()
                    {
                        RocketId = rocketId,
                        WorldTime = wt,
                        ResourcePercent = fuel,
                        PartIds = new HashSet<int>() { kvp.Key },
                    });
                }
            }
        }

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
            foreach (Match m in Regex.Matches(body, "\"(\\w+)\":\"((?:[^\"\\\\]|\\\\.)*)\""))
            {
                string key = m.Groups[1].Value;
                string val = m.Groups[2].Value;
                if (key == "panel_id")
                    panelId = val;
                else if (key == "action")
                    action = val;
                else if (key == "value")
                    value = val;
            }
            foreach (Func<NetConnection, string, string, string, bool> handler in OnPanelReply.GetInvocationList())
            {
                if (handler(connection, panelId, action, value))
                    return true;
            }
            return true;
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
