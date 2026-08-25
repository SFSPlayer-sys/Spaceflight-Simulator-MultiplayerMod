using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;
using Lidgren.Network;
using SFS;
using SFS.UI;
using SFS.World;
using SFS.Variables;
using SFS.WorldBase;
using MultiplayerSFS.Common;

namespace MultiplayerSFS.Mod
{
    public static class ClientManager
    {
        public static Bool_Local multiplayerEnabled = new Bool_Local() { Value = false };
        public static NetClient client;
        public static WorldState world;
        /// <summary>
        /// Id of the local player.
        /// </summary>
        public static int playerId;
        public static bool allowLaunchOnOccupiedPad;
        static readonly List<string> localPlanetsPackHashes = new List<string>();
        class PlanetsPackDownload
        {
            public string packName;
            public string expectedHash;
            public int chunkCount;
            public byte[][] chunks;
            public int received;
        }
        static PlanetsPackDownload planetsPackDownload;
        /// <summary>
        /// 服务器回复的连接信息
        /// </summary>
        static Packet_JoinResponse joinResponse;

        public static async Task TryConnect(JoinInfo info)
        {
            if (client != null && client.Status != NetPeerStatus.NotRunning)
                client.Shutdown("Re-attempting join request");
            NetPeerConfiguration npc = new NetPeerConfiguration("multiplayersfs")
            {
                ConnectionTimeout = 5,
            };
            npc.EnableMessageType(NetIncomingMessageType.StatusChanged);
			npc.EnableMessageType(NetIncomingMessageType.UnconnectedData);
			npc.EnableMessageType(NetIncomingMessageType.VerboseDebugMessage);
            // TODO: ping readout UI?
			// npc.EnableMessageType(NetIncomingMessageType.ConnectionLatencyUpdated);
            client = new NetClient(npc);
            client.Start();
            NetOutgoingMessage hail = client.CreateMessage();
            string solarSystemName = "";
            localPlanetsPackHashes.Clear();
            try
            {
                string recordFile = Main.main != null
                    ? System.IO.Path.Combine(Main.main.ModFolder, ".PlanetsPackPersistent")
                    : System.IO.Path.Combine(Application.persistentDataPath, "MultiplayerSFS_PlanetsPacks.txt");
                Dictionary<string, string> recordedHashes = new Dictionary<string, string>();
                if (System.IO.File.Exists(recordFile))
                {
                    foreach (string line in System.IO.File.ReadAllLines(recordFile))
                    {
                        int eq = line.IndexOf('=');
                        if (eq > 0)
                            recordedHashes[line.Substring(0, eq)] = line.Substring(eq + 1);
                    }
                }
                string customSolarSystemsPath = System.IO.Path.Combine(Application.dataPath, "Custom Solar Systems");
                if (System.IO.Directory.Exists(customSolarSystemsPath))
                {
                    string[] solarSystemDirectories = System.IO.Directory.GetDirectories(customSolarSystemsPath);
                    if (solarSystemDirectories.Length > 0)
                    {
                        solarSystemName = System.IO.Path.GetFileName(solarSystemDirectories[0]);
                        Debug.Log($"Found local solar system: {solarSystemName}");
                    }
                    foreach (string dir in solarSystemDirectories)
                    {
                        string name = System.IO.Path.GetFileName(dir);

                        if (recordedHashes.TryGetValue(name, out string recorded))
                        {
                            localPlanetsPackHashes.Add(recorded);
                            continue;
                        }
                        try
                        {
                            localPlanetsPackHashes.Add(PlanetsPackTool.ComputeHash(PlanetsPackTool.Pack(dir)));
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"Failed to hash planets pack '{dir}': {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to check local solar systems: {ex.Message}");
            }
            string gameVersion = Application.version;
            Debug.Log($"Client game version: {gameVersion}");
            
            hail.Write
            (
                new Packet_JoinRequest()
                {
                    Username = info.username,
                    Password = Packet_JoinRequest.GetPasswordHash(info.password),
                    SolarSystemName = solarSystemName,
                    GameVersion = gameVersion,
                    ProtocolVersion = Ver.ProtocolVersion,
                    PlanetsPackHashes = localPlanetsPackHashes,
                }
            );
            client.Connect(new IPEndPoint(info.address, info.port), hail);

            Menu.loading.Open("Waiting for server response...");
            try
            {
                ServerInfo serverInfo = await GetServerInfo(new IPEndPoint(info.address, info.port));
                if (serverInfo != null && serverInfo.protocolVersion != Ver.ProtocolVersion)
                    MsgDrawer.main.Log("Client's network protocol version does not match. This may affect gameplay experience.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Protocol check failed: {ex.Message}");
            }
            string denialReason = "Connection timed out";
            while (true)
            {
                NetIncomingMessage msg;
                while ((msg = client.ReadMessage()) == null)
                {
                    await Task.Yield();
                }

                switch (msg.MessageType)
                {
                    case NetIncomingMessageType.Error:
                        Debug.LogError("Lidgren Error: Corrupted packet!");
                        break;
                    case NetIncomingMessageType.ErrorMessage:
                        Debug.LogError($"Lidgren Error: \"{msg.ReadString()}\".");
                        break;
                    case NetIncomingMessageType.WarningMessage:
                        Debug.LogWarning($"Lidgren Warning: \"{msg.ReadString()}\".");
                        break;
                    case NetIncomingMessageType.DebugMessage:
                    case NetIncomingMessageType.VerboseDebugMessage:
                        Debug.Log($"Lidgren Debug: \"{msg.ReadString()}\".");
                        break;
                    case NetIncomingMessageType.StatusChanged:
                        NetConnectionStatus status = (NetConnectionStatus) msg.ReadByte();
                        string reason = msg.ReadString();
                        if (status == NetConnectionStatus.Connected)
                            goto ConnectionApproved;
                        else if (status == NetConnectionStatus.Disconnected)
                        {
                            if (!string.IsNullOrWhiteSpace(reason) && reason.IndexOf("banned", StringComparison.OrdinalIgnoreCase) >= 0)
                                denialReason = "You have been banned from the server.";
                            else if (!string.IsNullOrWhiteSpace(reason))
                                denialReason = $"Unable to connect to server: {reason}";
                            goto ConnectionDenied;
                        }
                        break;
                    default:
                        Debug.LogWarning($"Recieved unhandled message type ({msg.MessageType}) when attempting to connect to server.");
                        break;
                }
            }

            ConnectionApproved:
                Menu.loading.Close();
                client.RegisterReceivedCallback(new SendOrPostCallback(Listen));
                LoadWorld();
                return;
            
            ConnectionDenied:
                Menu.loading.Close();
                MsgDrawer.main.Log(denialReason);
                return;
        }

        /// <summary>
        /// 局域网内发现的服务器信息
        /// </summary>
        public class ServerInfo
        {
            public IPEndPoint endpoint;
            public string name;
            public int playerCount;
            public int maxPlayers;
            public string allowedVersions;
            public bool hasPassword;
            public int protocolVersion;
            /// <summary>
            /// 是否为历史加入记录
            /// </summary>
            public bool isHistory;
            /// <summary>
            /// 上次向该服务器询问信息的时间
            /// </summary>
            public float lastQueried;
        }
        /// <summary>
        /// 加入过的服务器列表
        /// </summary>
        public static List<ServerInfo> serverHistory = new List<ServerInfo>();

        /// <summary>
        /// 历史服务器文件的保存路径
        /// </summary>
        static string GetHistoryFilePath()
        {
            return Path.Combine(Main.historyPersistentFolder.ToString(), "ServerHistory.txt");
        }

        public static void LoadServerHistory()
        {
            serverHistory.Clear();
            try
            {
                string path = GetHistoryFilePath();
                if (!File.Exists(path))
                    return;
                // 每行一条：地址|端口|名字
                foreach (string line in File.ReadAllLines(path))
                {
                    string[] parts = line.Split('|');
                    if (parts.Length < 3)
                        continue;
                    if (!IPAddress.TryParse(parts[0], out IPAddress address))
                        continue;
                    if (!int.TryParse(parts[1], out int port))
                        continue;
                    serverHistory.Add(new ServerInfo()
                    {
                        endpoint = new IPEndPoint(address, port),
                        name = parts[2],
                        isHistory = true,
                    });
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to load server history: {e.Message}");
            }
        }

        public static void SaveServerHistory()
        {
            try
            {
                List<string> lines = new List<string>();
                foreach (ServerInfo server in serverHistory)
                    lines.Add($"{server.endpoint.Address}|{server.endpoint.Port}|{server.name}");
                Directory.CreateDirectory(Main.historyPersistentFolder.ToString());
                File.WriteAllLines(GetHistoryFilePath(), lines);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to save server history: {e.Message}");
            }
        }

        /// <summary>
        /// 记录加入过的服务器
        /// </summary>
        public static void AddToServerHistory(IPEndPoint endpoint, string name)
        {
            foreach (ServerInfo server in serverHistory)
            {
                if (server.endpoint.Equals(endpoint))
                {
                    server.name = name;
                    SaveServerHistory();
                    return;
                }
            }
            serverHistory.Add(new ServerInfo()
            {
                endpoint = endpoint,
                name = name,
                isHistory = true,
            });
            SaveServerHistory();
        }

        public static void RemoveFromServerHistory(ServerInfo server)
        {
            serverHistory.Remove(server);
            SaveServerHistory();
        }

        /// <summary>
        /// 通过UDP广播扫描局域网内的服务器
        /// </summary>
        public static async Task<List<ServerInfo>> DiscoverLAN(int port)
        {
            NetPeerConfiguration npc = new NetPeerConfiguration("multiplayersfs");
            npc.EnableMessageType(NetIncomingMessageType.DiscoveryResponse);
            NetClient discoveryClient = new NetClient(npc);
            discoveryClient.Start();
            for (int p = port; p < port + 10; p++)
                discoveryClient.DiscoverLocalPeers(p);

            List<ServerInfo> servers = new List<ServerInfo>();
            DateTime endTime = DateTime.Now.AddSeconds(2);
            while (DateTime.Now < endTime)
            {
                NetIncomingMessage msg;
                while ((msg = discoveryClient.ReadMessage()) != null)
                {
                    if (msg.MessageType == NetIncomingMessageType.DiscoveryResponse)
                    {
                        int actualPort = msg.ReadInt32();
                        servers.Add(new ServerInfo()
                        {
                            endpoint = new IPEndPoint(msg.SenderEndPoint.Address, actualPort),
                            name = msg.ReadString(),
                            playerCount = msg.ReadInt32(),
                            maxPlayers = msg.ReadInt32(),
                            allowedVersions = msg.ReadString(),
                            hasPassword = msg.ReadBoolean(),
                            protocolVersion = msg.ReadInt32(),
                        });
                    }
                }
                await Task.Delay(50);
            }
            discoveryClient.Shutdown("Discovery complete");
            return servers;
        }

        /// <summary>
        /// 向指定服务器发送发现请求并获取信息，无响应返回null
        /// </summary>
        public static async Task<ServerInfo> GetServerInfo(IPEndPoint endpoint)
        {
            NetPeerConfiguration npc = new NetPeerConfiguration("multiplayersfs");
            npc.EnableMessageType(NetIncomingMessageType.DiscoveryResponse);
            NetClient infoClient = new NetClient(npc);
            infoClient.Start();
            infoClient.DiscoverKnownPeer(endpoint);

            DateTime endTime = DateTime.Now.AddSeconds(2);
            while (DateTime.Now < endTime)
            {
                NetIncomingMessage msg;
                while ((msg = infoClient.ReadMessage()) != null)
                {
                    if (msg.MessageType == NetIncomingMessageType.DiscoveryResponse && msg.SenderEndPoint.Equals(endpoint))
                    {
                        int actualPort = msg.ReadInt32();
                        ServerInfo info = new ServerInfo()
                        {
                            endpoint = new IPEndPoint(msg.SenderEndPoint.Address, actualPort),
                            name = msg.ReadString(),
                            playerCount = msg.ReadInt32(),
                            maxPlayers = msg.ReadInt32(),
                            allowedVersions = msg.ReadString(),
                            hasPassword = msg.ReadBoolean(),
                            protocolVersion = msg.ReadInt32(),
                        };
                        infoClient.Shutdown("Query complete");
                        return info;
                    }
                }
                await Task.Delay(50);
            }
            infoClient.Shutdown("Query timeout");
            return null;
        }

        public static void LoadWorld()
        {
            Menu.loading.Open("Loading multiplayer world...");
            
            Packet_JoinResponse response = client.ServerConnection.RemoteHailMessage.Read<Packet_JoinResponse>();
            joinResponse = response;
            playerId = response.PlayerId;
            AddToServerHistory(client.ServerConnection.RemoteEndPoint, response.ServerName);
            LocalManager.updateRocketsPeriod = response.UpdateRocketsPeriod;
            LocalManager.Initialize();

            ChatWindow.CreateCooldownTimer(response.ChatMessageCooldown);
            allowLaunchOnOccupiedPad = response.AllowLaunchOnOccupiedPad;
            
            world = new WorldState()
            {
                initWorldTime = response.WorldTime,
                difficulty = response.Difficulty,
                solarSystemName = response.SolarSystemName,
            };

            // 比对服务器星球包，匹配则直接进入世界
            if (string.IsNullOrEmpty(response.PlanetsPackHash) || localPlanetsPackHashes.Contains(response.PlanetsPackHash))
            {
                SendPacket(new Packet_ClientReady());
                LoadWorldScene();
            }
            else
            {
                // 服务器星球包与本地不匹配，下载星球包
                Menu.loading.Open($"Downloading planets pack '{response.PlanetsPackName}'...");
                planetsPackDownload = new PlanetsPackDownload() { packName = response.PlanetsPackName, expectedHash = response.PlanetsPackHash };
                SendPacket(new Packet_PlanetsPackRequest() { PackName = response.PlanetsPackName });
            }
        }

        static void LoadWorldScene()
        {
            Packet_JoinResponse response = joinResponse;

            // 检查本地是否存在该星系包
            bool solarSystemExists = false;
            if (!string.IsNullOrEmpty(response.SolarSystemName))
            {
                string solarSystemPath = System.IO.Path.Combine(Application.dataPath, "Custom Solar Systems", response.SolarSystemName);
                solarSystemExists = System.IO.Directory.Exists(solarSystemPath);
                Debug.Log($"Solar system '{response.SolarSystemName}' exists locally: {solarSystemExists}");
            }

            WorldSettings settings = new WorldSettings
            (
                new SolarSystemReference(response.SolarSystemName),
                new Difficulty() { difficulty = world.difficulty },
                new WorldMode(WorldMode.Mode.Sandbox) { allowQuicksaves = false },
                new WorldPlaytime(),
                new SandboxSettings.Data()
            );

            typeof(WorldBaseManager).GetMethod("EnterWorld", BindingFlags.NonPublic | BindingFlags.Instance).Invoke
            (
                Base.worldBase,
                new object[]
                {
                    null,
                    settings,
                    (Action) Base.sceneLoader.LoadHubScene
                }
            );
        }

        public static void Listen(object peer)
        {
            NetIncomingMessage msg;
            while ((msg = client.ReadMessage()) != null)
            {
                // Debug.Log("Recieved message...");
                switch (msg.MessageType)
                {
                    case NetIncomingMessageType.Error:
                        Debug.LogError("Lidgren Error: Corrupted packet!");
                        break;
                    case NetIncomingMessageType.ErrorMessage:
                        Debug.LogError($"Lidgren Error: \"{msg.ReadString()}\".");
                        break;
                    case NetIncomingMessageType.WarningMessage:
                        Debug.LogWarning($"Lidgren Warning: \"{msg.ReadString()}\".");
                        break;
                    case NetIncomingMessageType.DebugMessage:
                    case NetIncomingMessageType.VerboseDebugMessage:
                        Debug.Log($"Lidgren Debug: \"{msg.ReadString()}\".");
                        break;
                    case NetIncomingMessageType.Data:
                        HandlePacket(msg);
                        break;
                    case NetIncomingMessageType.StatusChanged:
                        NetConnectionStatus status = (NetConnectionStatus) msg.ReadByte();
                        string reason = msg.ReadString();
                        if (status == NetConnectionStatus.Disconnected)
                        {
                            if (!string.IsNullOrWhiteSpace(reason) && reason.IndexOf("banned", StringComparison.OrdinalIgnoreCase) >= 0)
                                ToastHelper.ShowToast("You have been banned from the server.");
                            HostManager.OnStop();
                            SceneLoader.ExitToMainMenu();
                            client.Shutdown("Disconnected by server.");
                        }
                        break;
                    default:
                        Debug.LogWarning($"Unhandled message type ({msg.MessageType})!");
                        break;
                }
                client.Recycle(msg);
            }
        }

        public static void HandlePacket(NetIncomingMessage msg)
        {
            PacketType packetType = (PacketType) msg.ReadByte();
            if (Packet.ShouldDebug(packetType))
                Debug.Log($"Recieved packet of type {packetType}.");
            switch (packetType)
            {
                // * Player/server Info Packets
                case PacketType.PlayerConnected:
                    OnPacket_PlayerConnected(msg);
                    break;
                case PacketType.PlayerDisconnected:
                    OnPacket_PlayerDisconnected(msg);
                    break;
                case PacketType.UpdatePlayerControl:
                    OnPacket_UpdatePlayerControl(msg);
                    break;
                case PacketType.UpdatePlayerAuthority:
                    OnPacket_UpdatePlayerAuthority(msg);
                    break;
                case PacketType.UpdateWorldTime:
                    OnPacket_UpdateWorldTime(msg);
                    break;
                case PacketType.UpdatePlayerColor:
                    OnPacket_UpdatePlayerColor(msg);
                    break;
                case PacketType.SendChatMessage:
                    OnPacket_SendChatMessage(msg);
                    break;
                case PacketType.ShowToastMessage:
                    OnPacket_ShowToastMessage(msg);
                    break;
                case PacketType.UpdateCheatStatus:
                    OnPacket_UpdateCheatStatus(msg);
                    break;

                // * Time Warp Packets
                case PacketType.TimeWarpVote:
                    OnPacket_TimeWarpVote(msg);
                    break;
                case PacketType.TimeWarpResult:
                    OnPacket_TimeWarpResult(msg);
                    break;

                // * Rocket Packets
                case PacketType.CreateRocket:
                    OnPacket_CreateRocket(msg);
                    break;
                case PacketType.DestroyRocket:
                    OnPacket_DestroyRocket(msg);
                    break;
                case PacketType.UpdateRocketPrimary:
                    OnPacket_UpdateRocketPrimary(msg);
                    break;
                case PacketType.UpdateRocketSecondary:
                    OnPacket_UpdateRocketSecondary(msg);
                    break;

                // * Part & Staging Packets
                case PacketType.DestroyPart:
                    OnPacket_DestroyPart(msg);
                    break;
                case PacketType.UpdateStaging:
                    OnPacket_UpdateStaging(msg);
                    break;
                case PacketType.UpdatePart_EngineModule:
                    OnPacket_UpdatePart_EngineModule(msg);
                    break;
                case PacketType.UpdatePart_WheelModule:
                    OnPacket_UpdatePart_WheelModule(msg);
                    break;
                case PacketType.UpdatePart_BoosterModule:
                    OnPacket_UpdatePart_BoosterModule(msg);
                    break;
                case PacketType.UpdatePart_ParachuteModule:
                    OnPacket_UpdatePart_ParachuteModule(msg);
                    break;
                case PacketType.UpdatePart_MoveModule:
                    OnPacket_UpdatePart_MoveModule(msg);
                    break;
                case PacketType.UpdatePart_ResourceModule:
                    OnPacket_UpdatePart_ResourceModule(msg);
                    break;
                
                // * Planets Pack Packets
                case PacketType.PlanetsPackData:
                    OnPacket_PlanetsPackData(msg);
                    break;

                // * Invalid Packets
                case PacketType.JoinResponse:
                    Debug.LogWarning($"Recieved server info packet outside of connection attempt.");
                    break;
                case PacketType.JoinRequest:
                case PacketType.PlanetsPackRequest:
                case PacketType.ClientReady:
                    Debug.LogWarning($"Recieved packet (of type {packetType}) intended for the server.");
                    break;
                default:
                    Debug.LogWarning($"Unhandled packet type ({packetType})!");
                    break;
            }
        }

        static void OnPacket_PlanetsPackData(NetIncomingMessage msg)
        {
            Packet_PlanetsPackData packet = msg.Read<Packet_PlanetsPackData>();
            if (planetsPackDownload == null || packet.PackName != planetsPackDownload.packName)
                return;
            if (planetsPackDownload.chunks == null)
            {
                planetsPackDownload.chunkCount = packet.ChunkCount;
                planetsPackDownload.chunks = new byte[packet.ChunkCount][];
            }
            planetsPackDownload.chunks[packet.ChunkIndex] = packet.Data;
            planetsPackDownload.received++;
            if (planetsPackDownload.received < planetsPackDownload.chunkCount)
                return;

            // 重组
            int total = 0;
            foreach (byte[] chunk in planetsPackDownload.chunks)
                total += chunk.Length;
            byte[] data = new byte[total];
            int offset = 0;
            foreach (byte[] chunk in planetsPackDownload.chunks)
            {
                Array.Copy(chunk, 0, data, offset, chunk.Length);
                offset += chunk.Length;
            }

            // 校验哈希
            if (PlanetsPackTool.ComputeHash(data) != planetsPackDownload.expectedHash)
            {
                Menu.loading.Close();
                MsgDrawer.main.Log("Planets pack download failed: hash mismatch");
                planetsPackDownload = null;
                return;
            }

            // 存入 Custom Solar Systems\{PackName}
            try
            {
                string dest = System.IO.Path.Combine(Application.dataPath, "Custom Solar Systems", planetsPackDownload.packName);
                PlanetsPackTool.Unpack(data, dest);
                localPlanetsPackHashes.Add(planetsPackDownload.expectedHash);
                // 记录哈希到本地，下次连接无需重新下载
                string recordFile = Main.main != null
                    ? System.IO.Path.Combine(Main.main.ModFolder, ".PlanetsPackPersistent")
                    : System.IO.Path.Combine(Application.persistentDataPath, "MultiplayerSFS_PlanetsPacks.txt");
                System.IO.File.AppendAllText(recordFile, $"{planetsPackDownload.packName}={planetsPackDownload.expectedHash}\n");
                Debug.Log($"Installed planets pack '{planetsPackDownload.packName}'.");
            }
            catch (Exception ex)
            {
                Menu.loading.Close();
                MsgDrawer.main.Log($"Failed to install planets pack: {ex.Message}");
                planetsPackDownload = null;
                return;
            }
            planetsPackDownload = null;

            // 通知服务器就绪并进入世界
            SendPacket(new Packet_ClientReady());
            LoadWorldScene();
        }

        public static void SendPacket(Packet packet, NetDeliveryMethod method = NetDeliveryMethod.ReliableOrdered)
        {
            if (Packet.ShouldDebug(packet.Type))
                Debug.Log($"Sending packet of type {packet.Type}.");

            NetOutgoingMessage msg = client.CreateMessage();
            msg.Write((byte) packet.Type);
            msg.Write(packet);
            client.SendMessage(msg, method);
        }

        static void OnPacket_PlayerConnected(NetIncomingMessage msg)
        {
            Packet_PlayerConnected packet = msg.Read<Packet_PlayerConnected>();
            if (LocalManager.players.ContainsKey(packet.PlayerId))
            {
                // Update existing player
                LocalManager.players[packet.PlayerId] = new LocalPlayer(packet.Username, packet.IconColor);
            }
            else
            {
                // Add new player
                LocalManager.players.Add(packet.PlayerId, new LocalPlayer(packet.Username, packet.IconColor));
            }
            if (packet.PrintMessage)
            {
                string message = $"{packet.Username} connected";
                MsgDrawer.main.Log(message);
                ChatWindow.AddMessage(new ChatMessage(message));
            }
        }

        static void OnPacket_PlayerDisconnected(NetIncomingMessage msg)
        {
            Packet_PlayerDisconnected packet = msg.Read<Packet_PlayerDisconnected>();
            if (LocalManager.players.TryGetValue(packet.PlayerId, out LocalPlayer player))
            {
                string message = $"{player.username} disconnected";
                MsgDrawer.main.Log(message);
                ChatWindow.AddMessage(new ChatMessage(message));
                LocalManager.players.Remove(packet.PlayerId);
            }
        }

        static void OnPacket_UpdatePlayerControl(NetIncomingMessage msg)
        {
            Packet_UpdatePlayerControl packet = msg.Read<Packet_UpdatePlayerControl>();
            if (LocalManager.players.TryGetValue(packet.PlayerId, out LocalPlayer player))
            {
                player.controlledRocket.Value = packet.RocketId;
            }
            else
            {
                Debug.LogError("Missing player while trying to update controlled rocket!");
            }

        }
        static void OnPacket_UpdatePlayerAuthority(NetIncomingMessage msg)
        {
            Packet_UpdatePlayerAuthority packet = msg.Read<Packet_UpdatePlayerAuthority>();
            LocalManager.updateAuthority = packet.RocketIds;

            foreach (int id in LocalManager.updateAuthority)
            {
                if (LocalManager.syncedRockets.TryGetValue(id, out LocalRocket rocket))
                {

                }
            }
        }

        static void OnPacket_UpdateWorldTime(NetIncomingMessage msg)
        {
            Packet_UpdateWorldTime packet = msg.Read<Packet_UpdateWorldTime>();
            if (WorldTime.main != null)
            {
                world.WorldTime = Math.Max(world.WorldTime, packet.WorldTime);
                WorldTime.main.worldTime = world.WorldTime;
            }
        }
        
        static void OnPacket_UpdatePlayerColor(NetIncomingMessage msg)
        {
            Packet_UpdatePlayerColor packet = msg.Read<Packet_UpdatePlayerColor>();
            if (LocalManager.players.TryGetValue(packet.PlayerId, out LocalPlayer player))
            {
                player.iconColor = packet.Color;
                ChatWindow.OnPlayerColorChange(packet.PlayerId, packet.Color);
            }
        }

        static void OnPacket_SendChatMessage(NetIncomingMessage msg)
        {
            Packet_SendChatMessage packet = msg.Read<Packet_SendChatMessage>();
            const int maxLen = 45;
            for (int i = 0; i < packet.Message.Length; i += maxLen)
            {
                int len = Math.Min(maxLen, packet.Message.Length - i);
                ChatWindow.AddMessage(new ChatMessage(packet.Message.Substring(i, len), packet.SenderId, packet.Color));
            }
        }

        static void OnPacket_ShowToastMessage(NetIncomingMessage msg)
        {
            Packet_ShowToastMessage packet = msg.Read<Packet_ShowToastMessage>();
            ToastHelper.ShowToast(packet.Message);
        }

        static void OnPacket_UpdateCheatStatus(NetIncomingMessage msg)
        {
            Packet_UpdateCheatStatus packet = msg.Read<Packet_UpdateCheatStatus>();
            lastCheatStatus = packet;
            ApplyCheatStatus(packet);
        }

        static Packet_UpdateCheatStatus lastCheatStatus;
        /// <summary>
        /// 应用作弊设置，世界场景未加载时缓存待加载后应用
        /// </summary>
        static void ApplyCheatStatus(Packet_UpdateCheatStatus packet)
        {
            if (SFS.World.SandboxSettings.main == null)
                return;
            SFS.World.SandboxSettings.main.settings.infiniteFuel = packet.InfiniteFuel;
            SFS.World.SandboxSettings.main.settings.noAtmosphericDrag = packet.NoAtmosphericDrag;
            SFS.World.SandboxSettings.main.settings.unbreakableParts = packet.UnbreakableParts;
            SFS.World.SandboxSettings.main.settings.noGravity = packet.NoGravity;
            SFS.World.SandboxSettings.main.settings.noHeatDamage = packet.NoHeatDamage;
            SFS.World.SandboxSettings.main.settings.noBurnMarks = packet.NoBurnMarks;
            SFS.World.SandboxSettings.main.settings.infiniteBuildArea = packet.InfiniteBuildArea;
            SFS.World.SandboxSettings.main.settings.partClipping = packet.PartClipping;
            SFS.World.SandboxSettings.main.UpdateUI(false);
            Debug.Log($"Cheat status updated: InfiniteFuel={packet.InfiniteFuel}, NoAtmosphericDrag={packet.NoAtmosphericDrag}");
        }

        /// <summary>
        /// 世界场景加载后应用缓存的作弊设置
        /// </summary>
        public static void ApplyCachedCheatStatus()
        {
            if (lastCheatStatus != null)
                ApplyCheatStatus(lastCheatStatus);
        }

        static void OnPacket_TimeWarpVote(NetIncomingMessage msg)
        {
            Packet_TimeWarpVote packet = msg.Read<Packet_TimeWarpVote>();
            TimeWarpVoting.OnVoteRequestReceived(packet);
        }

        static void OnPacket_TimeWarpResult(NetIncomingMessage msg)
        {
            Packet_TimeWarpResult packet = msg.Read<Packet_TimeWarpResult>();
            TimeWarpVoting.OnVoteResultReceived(packet);
        }

        static void OnPacket_CreateRocket(NetIncomingMessage msg)
        {
            Packet_CreateRocket packet = msg.Read<Packet_CreateRocket>();
            world.rockets[packet.GlobalId] = packet.Rocket;
            LocalManager.OnPacket_CreateRocket(packet);
        }

        static void OnPacket_DestroyRocket(NetIncomingMessage msg)
        {
            Packet_DestroyRocket packet = msg.Read<Packet_DestroyRocket>();
            world.rockets.Remove(packet.RocketId);
            LocalManager.TrueDestructionReason = packet.Reason;
            LocalManager.DestroyLocalRocket(packet.RocketId);
        }

        static void OnPacket_UpdateRocketPrimary(NetIncomingMessage msg)
        {
            Packet_UpdateRocketPrimary packet = msg.Read<Packet_UpdateRocketPrimary>();
            if (world.rockets.TryGetValue(packet.RocketId, out RocketState state))
            {
                state.UpdateRocketPrimary(packet);
                Interpolator.AddPacketToQueue(packet, packet.RocketId, packet.WorldTime);
                // Debug.Log($"Update rocket!!! {} => {}");
            }
            else
            {
                Debug.Log("Missing rocket from world state!!!");
            }
        }

        static void OnPacket_UpdateRocketSecondary(NetIncomingMessage msg)
        {
            Packet_UpdateRocketSecondary packet = msg.Read<Packet_UpdateRocketSecondary>();
            if (world.rockets.TryGetValue(packet.RocketId, out RocketState state))
            {
                state.UpdateRocketSecondary(packet);
                Interpolator.AddPacketToQueue(packet, packet.RocketId, packet.WorldTime);
            }
        }

        static void OnPacket_DestroyPart(NetIncomingMessage msg)
        {
            Packet_DestroyPart packet = msg.Read<Packet_DestroyPart>();
            if (world.rockets.TryGetValue(packet.RocketId, out RocketState state))
            {
                state.RemovePart(packet.PartId);
                Interpolator.AddPacketToQueue(packet, packet.PartId, packet.WorldTime);
            }
        }

        static void OnPacket_UpdateStaging(NetIncomingMessage msg)
        {
            Packet_UpdateStaging packet = msg.Read<Packet_UpdateStaging>();
            if (world.rockets.TryGetValue(packet.RocketId, out RocketState state))
            {
                state.stages = packet.Stages;
                Interpolator.AddPacketToQueue(packet, packet.RocketId, packet.WorldTime);
            }
        }

        static void OnPacket_UpdatePart_EngineModule(NetIncomingMessage msg)
        {
            Packet_UpdatePart_EngineModule packet = msg.Read<Packet_UpdatePart_EngineModule>();
            if (world.rockets.TryGetValue(packet.RocketId, out RocketState rocketState))
            {
                if (rocketState.parts.TryGetValue(packet.PartId, out PartState partState))
				{
					partState.part.TOGGLE_VARIABLES["engine_on"] = packet.EngineOn;
				}
                Interpolator.AddPacketToQueue(packet, packet.RocketId, packet.WorldTime);
            }
        }

        static void OnPacket_UpdatePart_WheelModule(NetIncomingMessage msg)
        {
            Packet_UpdatePart_WheelModule packet = msg.Read<Packet_UpdatePart_WheelModule>();
            if (world.rockets.TryGetValue(packet.RocketId, out RocketState rocketState))
            {
                if (rocketState.parts.TryGetValue(packet.PartId, out PartState partState))
				{
					partState.part.TOGGLE_VARIABLES["wheel_on"] = packet.WheelOn;
				}
                Interpolator.AddPacketToQueue(packet, packet.RocketId, packet.WorldTime);
            }
        }

        static void OnPacket_UpdatePart_BoosterModule(NetIncomingMessage msg)
        {
            Packet_UpdatePart_BoosterModule packet = msg.Read<Packet_UpdatePart_BoosterModule>();
            if (world.rockets.TryGetValue(packet.RocketId, out RocketState rocketState))
            {
                if (rocketState.parts.TryGetValue(packet.PartId, out PartState partState))
				{
					partState.part.NUMBER_VARIABLES["fuel_percent"] = packet.FuelPercent;
				}
                Interpolator.AddPacketToQueue(packet, packet.RocketId, packet.WorldTime);
            }
        }

        static void OnPacket_UpdatePart_ParachuteModule(NetIncomingMessage msg)
        {
            Packet_UpdatePart_ParachuteModule packet = msg.Read<Packet_UpdatePart_ParachuteModule>();
            if (world.rockets.TryGetValue(packet.RocketId, out RocketState rocketState))
            {
                if (rocketState.parts.TryGetValue(packet.PartId, out PartState partState))
				{
					partState.part.NUMBER_VARIABLES["animation_state"] = packet.State;
					partState.part.NUMBER_VARIABLES["deploy_state"] = packet.TargetState;
				}
                Interpolator.AddPacketToQueue(packet, packet.RocketId, packet.WorldTime);
            }
        }

        static void OnPacket_UpdatePart_MoveModule(NetIncomingMessage msg)
        {
            Packet_UpdatePart_MoveModule packet = msg.Read<Packet_UpdatePart_MoveModule>();
            if (world.rockets.TryGetValue(packet.RocketId, out RocketState rocketState))
            {
                if (rocketState.parts.TryGetValue(packet.PartId, out PartState partState))
				{
					partState.part.NUMBER_VARIABLES["state"] = packet.Time;
					partState.part.NUMBER_VARIABLES["state_target"] = packet.TargetTime;
				}
                Interpolator.AddPacketToQueue(packet, packet.RocketId, packet.WorldTime);
            }
        }

        static void OnPacket_UpdatePart_ResourceModule(NetIncomingMessage msg)
        {
            Packet_UpdatePart_ResourceModule packet = msg.Read<Packet_UpdatePart_ResourceModule>();
            if (world.rockets.TryGetValue(packet.RocketId, out RocketState rocketState))
            {
                foreach (int partId in packet.PartIds)
                {
                    if (rocketState.parts.TryGetValue(partId, out PartState partState))
                    {
                        // TODO! A lot of these save variable names will most likely be different for non-vanilla parts, but currently idk what the best way to properly get them is.
                        // TODO! I might need some form of register that associates a part's name and module variable names to their save variable names.
                        partState.part.NUMBER_VARIABLES["fuel_percent"] = packet.ResourcePercent;
                    }
                }
                Interpolator.AddPacketToQueue(packet, packet.RocketId, packet.WorldTime);
            }
        }
    }
}