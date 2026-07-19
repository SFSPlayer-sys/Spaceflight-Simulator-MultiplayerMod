using System;
using System.IO;
using Lidgren.Network;

namespace MultiplayerSFS.ServerCommon
{
    public class ServerSettings
    {
        public int port = 7777;
        public string serverName = "Multiplayer SFS Server";
        public string serverPassword = "";
        public int maxConnections = 10;
        public bool blockDuplicatePlayerNames = true;
        public double updateRocketsPeriod = 0.05;
        public double chatMessageCooldown = 1.0;
        public double updateWorldTimePeriod = 1.0;
        public double loadRange = 100000;
        public string worldSavePath = "";
        public string allowedGameVersions = "";
        public int authorityUpdateInterval = 200;
        public float timeWarpMaxScale = 0f;
        public int timeWarpVoteTimeout = 30;
        public int networkTimeout = 15;
        public int maxPacketSize = 65536;
        public bool enableNetworkCompression = false;
        public int serverTickRate = 1;
        public string motd = "";

        public string Serialize()
        {
            // Simple serialization for now
            return $"port={port}\n"
                 + $"serverName={serverName}\n"
                 + $"serverPassword={serverPassword}\n"
                 + $"maxConnections={maxConnections}\n"
                 + $"blockDuplicatePlayerNames={blockDuplicatePlayerNames}\n"
                 + $"updateRocketsPeriod={updateRocketsPeriod}\n"
                 + $"chatMessageCooldown={chatMessageCooldown}\n"
                 + $"updateWorldTimePeriod={updateWorldTimePeriod}\n"
                 + $"loadRange={loadRange}\n"
                 + $"worldSavePath={worldSavePath}\n"
                 + $"allowedGameVersions={allowedGameVersions}\n"
                 + $"authorityUpdateInterval={authorityUpdateInterval}\n"
                 + $"timeWarpMaxScale={timeWarpMaxScale}\n"
                 + $"timeWarpVoteTimeout={timeWarpVoteTimeout}\n"
                 + $"networkTimeout={networkTimeout}\n"
                 + $"maxPacketSize={maxPacketSize}\n"
                 + $"enableNetworkCompression={enableNetworkCompression}\n"
                 + $"serverTickRate={serverTickRate}\n"
                 + $"motd={motd}";
        }

        public static ServerSettings Deserialize(string data)
        {
            ServerSettings settings = new ServerSettings();
            
            foreach (string line in data.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = line.Split('=');
                if (parts.Length != 2)
                    continue;

                string key = parts[0].Trim();
                string value = parts[1].Trim();

                switch (key)
                {
                    case "port":
                        if (int.TryParse(value, out int portValue))
                            settings.port = portValue;
                        break;
                    case "serverName":
                        settings.serverName = value;
                        break;
                    case "serverPassword":
                        settings.serverPassword = value;
                        break;
                    case "maxConnections":
                        if (int.TryParse(value, out int maxConnValue))
                            settings.maxConnections = maxConnValue;
                        break;
                    case "blockDuplicatePlayerNames":
                        if (bool.TryParse(value, out bool blockValue))
                            settings.blockDuplicatePlayerNames = blockValue;
                        break;
                    case "updateRocketsPeriod":
                        if (double.TryParse(value, out double updatePeriodValue))
                            settings.updateRocketsPeriod = updatePeriodValue;
                        break;
                    case "chatMessageCooldown":
                        if (double.TryParse(value, out double cooldownValue))
                            settings.chatMessageCooldown = cooldownValue;
                        break;
                    case "updateWorldTimePeriod":
                        if (double.TryParse(value, out double worldTimeValue))
                            settings.updateWorldTimePeriod = worldTimeValue;
                        break;
                    case "loadRange":
                        if (double.TryParse(value, out double loadRangeValue))
                            settings.loadRange = loadRangeValue;
                        break;
                    case "worldSavePath":
                        settings.worldSavePath = value;
                        break;
                    case "allowedGameVersions":
                        settings.allowedGameVersions = value;
                        break;
                    case "authorityUpdateInterval":
                        if (int.TryParse(value, out int authInterval))
                            settings.authorityUpdateInterval = authInterval;
                        break;
                    case "timeWarpMaxScale":
                        if (float.TryParse(value, out float maxScale))
                            settings.timeWarpMaxScale = maxScale;
                        break;
                    case "timeWarpVoteTimeout":
                        if (int.TryParse(value, out int voteTimeout))
                            settings.timeWarpVoteTimeout = voteTimeout;
                        break;
                    case "networkTimeout":
                        if (int.TryParse(value, out int netTimeout))
                            settings.networkTimeout = netTimeout;
                        break;
                    case "maxPacketSize":
                        if (int.TryParse(value, out int packetSize))
                            settings.maxPacketSize = packetSize;
                        break;
                    case "enableNetworkCompression":
                        if (bool.TryParse(value, out bool compression))
                            settings.enableNetworkCompression = compression;
                        break;
                    case "serverTickRate":
                        if (int.TryParse(value, out int tickRate))
                            settings.serverTickRate = tickRate;
                        break;
                    case "motd":
                        settings.motd = value;
                        break;
                }
            }

            return settings;
        }
    }
}
