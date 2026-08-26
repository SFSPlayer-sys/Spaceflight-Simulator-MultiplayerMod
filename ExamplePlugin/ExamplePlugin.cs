using System;
using System.Collections.Generic;
using System.Linq;
using Lidgren.Network;
using MultiplayerSFS.Server;
using ServerClass = MultiplayerSFS.Server.Server;
namespace MultiplayerSFS.Plugins
// Example

{
    public class ExamplePlugin : IPlugin
    {
        public string ID => "exampleplugin";//ID,different IDs will be treated as different plugins
        public string Name => "Example Plugin";// display name
        public string Author => "You";//Author
        public string Version => "1.0.0";//Version
        public string MinimumServerVersion => "0.4.2";//Minimum server version required by the plugin
        
        static readonly Dictionary<ConnectedPlayer, DateTime> joinTimes = new Dictionary<ConnectedPlayer, DateTime>();
        
        public void OnLoad()
        {
            // Called when the plugin loads
            // Register command
            CommandManager.RegisterCommand("players", new PlayerListCommand());
            //CommandManager.RegisterCommand("stop", new StopCommand());
            // Subscribe events: OnPlayerJoined/OnPlayerLeft/OnChatMessage
            Plugin.OnPlayerJoined += player =>
            {
                joinTimes[player] = DateTime.Now;
                NetConnection conn = FindConnection(player);
                if (conn == null) return;
                ServerClass.SendPanel(conn, "{\"id\":\"upload_panel\",\"scene\":\"world\",\"title\":\"Upload\",\"width\":300,\"height\":150,\"closable\":true,\"elements\":[{\"type\":\"text_input\",\"placeholder\":\"Enter text\",\"width\":260,\"height\":40,\"action\":\"upload\"},{\"type\":\"button\",\"text\":\"Upload\",\"width\":260,\"height\":40,\"action\":\"upload\"}]}");
            };
            Plugin.OnPlayerLeft += player => joinTimes.Remove(player);
            Plugin.OnMessageHandled += (conn, msg) =>
            {
                const string start = "#UI_REPLY_START#";
                const string end = "#UI_REPLY_END#";
                int si = msg.IndexOf(start, StringComparison.Ordinal);
                int ei = msg.IndexOf(end, StringComparison.Ordinal);
                if (si < 0 || ei < 0) return false;
                string json = msg.Substring(si + start.Length, ei - si - start.Length).Trim();
                if (json.Contains("\"action\":\"upload\""))
                {
                    ConnectedPlayer p = ServerClass.FindPlayer(conn);
                    string val = "";
                    int vi = json.IndexOf("\"value\":\"", StringComparison.Ordinal);
                    if (vi >= 0)
                    {
                        vi += 9;
                        int ve = json.IndexOf('"', vi);
                        if (ve > vi) val = json.Substring(vi, ve - vi);
                    }
                    Logger.Info($"{p?.username ?? "?"}: {val}");
                    return true;
                }
                return false;
            };
        }
        public void OnUnload()
        {
            // Called when the plugin unloads
        }

        static NetConnection FindConnection(ConnectedPlayer player)
        {
            var kvp = ServerClass.connectedPlayers.FirstOrDefault(kv => kv.Value.id == player.id);
            if (kvp.Key == null) return null;
            return ServerClass.server.GetConnection(kvp.Key);
        }

        // Player list
        class PlayerListCommand : Command
        {
            public override string Description => "List online players and their online time";
            public override string Run(string[] args, Lidgren.Network.NetConnection sender)
            {
                string r = $"Players online:";
                foreach (ConnectedPlayer player in ServerClass.connectedPlayers.Values)
                {
                    TimeSpan online = DateTime.Now - joinTimes[player];
                    r += $"\n{player.username}-{online.Hours}h{online.Minutes}m{online.Seconds}s";
                }
                return r;
            }
        }
        //It is dangerous 
        // // Stop command
        // class StopCommand : Command
        // {
        //     public override string Description => "Stop the server";
        //     public override string Run(string[] args, Lidgren.Network.NetConnection sender)
        //     {
        //         if (!CheckAdmin(sender)) return "Admin only";
        //         ServerClass.Stop();
        //         return "";
        //     }
        // }
    }
}