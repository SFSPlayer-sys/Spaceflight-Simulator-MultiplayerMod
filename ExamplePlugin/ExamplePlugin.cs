using System;
using System.Collections.Generic;
using MultiplayerSFS.Server;
using ServerClass = MultiplayerSFS.Server.Server;
namespace MultiplayerSFS.Plugins
// Example

{
    public class ExamplePlugin : IPlugin
    {
        public string ID => "exampleplugin";// Plugin ID,different IDs will be treated as different plugins
        public string Name => "Example Plugin";// Plugin display name
        public string Author => "You";// Plugin author
        public string Version => "1.0.0";// Plugin version
        public string MinimumServerVersion => "0.4.2";// Minimum server version required by the plugin
        
        static readonly Dictionary<ConnectedPlayer, DateTime> joinTimes = new Dictionary<ConnectedPlayer, DateTime>();
        
        public void OnLoad()
        {
            // Called when the plugin loads
            // Register command
            CommandManager.RegisterCommand("player", new PlayerListCommand());
            CommandManager.RegisterCommand("stop", new StopCommand());
            // Subscribe events: OnPlayerJoined/OnPlayerLeft/OnChatMessage
            Plugin.OnPlayerJoined += player => joinTimes[player] = DateTime.Now;
            Plugin.OnPlayerLeft += player => joinTimes.Remove(player);
        }
        public void OnUnload()
        {
            // Called when the plugin unloads
        }
        // Player list command
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