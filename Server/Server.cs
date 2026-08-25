using System;
using System.Net;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using Lidgren.Network;

#if NET48
using MultiplayerSFS.Common;
using UnityEngine;
#else
using MultiplayerSFS.ServerCommon;
#endif

namespace MultiplayerSFS.Server
{
    public static class Server
	{
		public static NetServer server;
		public static ServerSettings settings;
		public static WorldState world;
		public static Dictionary<IPEndPoint, ConnectedPlayer> connectedPlayers;
		public static bool isRunning = false;
		public static byte[] planetsPackData;
		public static string planetsPackName = "";
		public static string planetsPackHash = "";

		public static void Initialize(ServerSettings settings)
		{
			Server.settings = settings;
			isRunning = true;
			int port = settings.port;
			while (true)
			{
				try
				{
					NetPeerConfiguration npc = new NetPeerConfiguration("multiplayersfs")
					{
						Port = port,
						MaximumConnections = settings.maxConnections,
					};
					npc.EnableMessageType(NetIncomingMessageType.StatusChanged);
					npc.EnableMessageType(NetIncomingMessageType.ConnectionApproval);
					npc.EnableMessageType(NetIncomingMessageType.ConnectionLatencyUpdated);
					npc.EnableMessageType(NetIncomingMessageType.VerboseDebugMessage);
					if (settings.discoveryEnabled)
						npc.EnableMessageType(NetIncomingMessageType.DiscoveryRequest);
					server = new NetServer(npc);
					server.Start();
					break;
				}
				catch
				{
					port++;
				}
			}
			if (port != settings.port)
				Logger.Info($"Port {settings.port} was unavailable, using port {port} instead.", true);

			try
			{
				world = new WorldState(settings.worldSavePath);
			}
			catch (Exception ex)
			{
				Logger.Error($"Failed to initialize world state: {ex.Message}");
				Logger.Error($"World save path: {settings.worldSavePath}");
				Logger.Error($"Please ensure the path exists and the application has proper permissions.");
				throw;
			}
			connectedPlayers = new Dictionary<IPEndPoint, ConnectedPlayer>();
			BanManager.Load();
			lastWorldSave = DateTime.Now;
			// 加载星球包
			planetsPackData = null;
			planetsPackName = "";
			planetsPackHash = "";
			if (!string.IsNullOrWhiteSpace(settings.planetsPackPath))
			{
				try
				{
					planetsPackData = PlanetsPackTool.Pack(settings.planetsPackPath);
					planetsPackName = new DirectoryInfo(settings.planetsPackPath).Name;
					planetsPackHash = PlanetsPackTool.ComputeHash(planetsPackData);
					Logger.Info($"Loaded planets pack '{planetsPackName}' ({planetsPackData.Length} bytes, hash {planetsPackHash}).", true);
				}
				catch (Exception ex)
				{
					Logger.Error($"Failed to load planets pack: {ex.Message}");
				}
			}
		}

		public static void Run()
		{
			try
			{
				Logger.Info($"Multiplayer SFS server v{Ver.ServerVersion} started , listening for connections on port {server.Port}...", true);
				Plugin.TriggerEvent("ServerStarted", null);
				
				while (isRunning)
				{
					Listen();
					ProcessSingleMessage(server.WaitMessage(10));
					
					if ((DateTime.Now - lastAuthorityUpdate).TotalMilliseconds >= AuthorityUpdateIntervalMs)
					{
						UpdatePlayerAuthorities();
						lastAuthorityUpdate = DateTime.Now;
					}
					
					if ((DateTime.Now - lastWorldSave).TotalSeconds >= settings.worldSaveInterval)
					{
						_ = world.SaveWorld();
						lastWorldSave = DateTime.Now;
					}
					if ((DateTime.Now - lastBanCleanup).TotalSeconds >= 300)
					{
						BanManager.CleanupExpired();
						lastBanCleanup = DateTime.Now;
					}
				}
				Plugin.TriggerEvent("ServerStopped", null);
			}
			catch (Exception e)
			{
				Logger.Error(e);
			}
		}

		public static void Stop()
		{
			isRunning = false;
			server?.Shutdown("Server stopped");
		}

		private static DateTime lastAuthorityUpdate = DateTime.MinValue;
        private const int AuthorityUpdateIntervalMs = 200;
        
        private static DateTime lastWorldSave = DateTime.MinValue;
        
        private static DateTime lastBanCleanup = DateTime.MinValue;
        private static HashSet<int> controlledRocketsCache = new HashSet<int>();
        private static Dictionary<int, Double2> playerPositionsCache = new Dictionary<int, Double2>();

		/// <summary>
		/// Processes a single incoming message
		/// </summary>
		static void ProcessSingleMessage(NetIncomingMessage msg)
		{
			if (msg == null) return; // WaitMessage 超时返回 null
			switch (msg.MessageType)
			{
				case NetIncomingMessageType.StatusChanged:
					OnStatusChanged(msg);
					break;
				case NetIncomingMessageType.ConnectionApproval:
					OnPlayerConnectionAttempt(msg);
					break;
				case NetIncomingMessageType.ConnectionLatencyUpdated:
					OnLatencyUpdated(msg);
					break;
				case NetIncomingMessageType.DiscoveryRequest:
					OnDiscoveryRequest(msg);
					break;
				case NetIncomingMessageType.Data:
					OnIncomingPacket(msg);
					break;
				case NetIncomingMessageType.DebugMessage:
				case NetIncomingMessageType.VerboseDebugMessage:
					Logger.Info($"Lidgren Debug - \"{msg.ReadString()}\".", true);
					break;
				case NetIncomingMessageType.WarningMessage:
					Logger.Warning($"Lidgren Warning - \"{msg.ReadString()}\".");
					break;
				case NetIncomingMessageType.ErrorMessage:
					Logger.Error($"Lidgren Error - \"{msg.ReadString()}\".");
					break;
				default:
					Logger.Warning($"Unhandled message type: {msg.MessageType} - {msg.DeliveryMethod} - {msg.LengthBytes} bytes.");
					break;
			}
			server.Recycle(msg);
		}

		/// <summary>
		/// Returns `true` if a refresh of the players' update authorities is required.
		/// </summary>
		static bool Listen()
		{
			NetIncomingMessage msg;
			bool requiresRefresh = false;
			while ((msg = server.ReadMessage()) != null)
			{
				Plugin.TriggerEvent(msg.MessageType.ToString(), msg);
				switch (msg.MessageType)
				{
					case NetIncomingMessageType.StatusChanged:
						requiresRefresh |= OnStatusChanged(msg);
						break;
					case NetIncomingMessageType.ConnectionApproval:
						OnPlayerConnectionAttempt(msg);
						break;
					case NetIncomingMessageType.ConnectionLatencyUpdated:
						OnLatencyUpdated(msg);
						break;
					case NetIncomingMessageType.DiscoveryRequest:
						OnDiscoveryRequest(msg);
						break;
					case NetIncomingMessageType.Data:
						requiresRefresh |= OnIncomingPacket(msg);
						break;

					case NetIncomingMessageType.DebugMessage:
					case NetIncomingMessageType.VerboseDebugMessage:
						Logger.Info($"Lidgren Debug - \"{msg.ReadString()}\".", true);
						break;
					case NetIncomingMessageType.WarningMessage:
						Logger.Warning($"Lidgren Warning - \"{msg.ReadString()}\".");
						break;
					case NetIncomingMessageType.ErrorMessage:
						Logger.Error($"Lidgren Error - \"{msg.ReadString()}\".");
						break;
					default:
						Logger.Warning($"Unhandled message type: {msg.MessageType} - {msg.DeliveryMethod} - {msg.LengthBytes} bytes.");
						break;
				}
				server.Recycle(msg);
			}
			return requiresRefresh;
		}

		public static ConnectedPlayer FindPlayer(NetConnection connection)
		{
			if (connectedPlayers.TryGetValue(connection.RemoteEndPoint, out ConnectedPlayer res))
				return res;
			return null;
		}

		static string FormatUsername(this string username)
		{
            return string.IsNullOrWhiteSpace(username) ? "???" : $"'{username}'";
        }

		public static void SendPacketToPlayer(NetConnection connection, Packet packet, NetDeliveryMethod method = NetDeliveryMethod.ReliableOrdered)
		{
			if (connection == null)
			{
				Logger.Warning("Attempted to send packet to null connection.");
				return;
			}
			ConnectedPlayer player = FindPlayer(connection);
			if (player != null && BanManager.IsBannedQuick(player.username, connection.RemoteEndPoint))
				return; 
			// Logger.Debug($"Sending packet of type '{packet.Type}'.");
			NetOutgoingMessage msg = server.CreateMessage();
			msg.Write((byte) packet.Type);
			msg.Write(packet);
			server.SendMessage(msg, connection, method);
		}

		public static void SendPacketToAll(Packet packet, NetConnection except = null, NetDeliveryMethod method = NetDeliveryMethod.ReliableOrdered)
		{
			// Logger.Debug($"Sending packet of type '{packet.Type}' to all.");
			NetOutgoingMessage msg = server.CreateMessage();
			msg.Write((byte) packet.Type);
			msg.Write(packet);
			server.SendToAll(msg, except, method, 0);
		}

		/// <summary>
		/// 响应局域网发现请求
		/// </summary>
		static void OnDiscoveryRequest(NetIncomingMessage msg)
		{
			NetOutgoingMessage response = server.CreateMessage();
			response.Write(settings.serverName);
			response.Write(connectedPlayers.Count);
			response.Write(settings.maxConnections);
			response.Write(settings.allowedGameVersions);
			response.Write(settings.serverPassword != "");
			response.Write(Ver.ProtocolVersion);
			server.SendDiscoveryResponse(response, msg.SenderEndPoint);
		}

		/// <summary>
		/// Returns `true` if a refresh of the players' update authorities is required.
		/// </summary>
		static bool OnStatusChanged(NetIncomingMessage msg)
		{
			NetConnectionStatus status = (NetConnectionStatus) msg.ReadByte();
			string reason = msg.ReadString();
			string playerName = FindPlayer(msg.SenderConnection)?.username.FormatUsername();
			Logger.Info($"Status of {playerName} @ {msg.SenderEndPoint} changed to {status} - \"{reason}\".");

			switch (status)
			{
				case NetConnectionStatus.Disconnected:
					OnPlayerDisconnect(msg.SenderConnection);
					return true;
				case NetConnectionStatus.Connected:
					OnPlayerSuccessfulConnect(msg.SenderConnection);
					return false;
				default:
					return false;
			}
		}

        static void OnPlayerConnectionAttempt(NetIncomingMessage msg)
		{
			Packet_JoinRequest request = msg.SenderConnection.RemoteHailMessage.Read<Packet_JoinRequest>();
            NetConnection connection = msg.SenderConnection;
			Logger.Info($"Recieved join request from {request.Username.FormatUsername()} @ {connection.RemoteEndPoint}.", true);

			string reason = "Connection approved!";
			if (connectedPlayers.Count >= settings.maxConnections && settings.maxConnections != 0)
			{
				reason = $"Server is full ({connectedPlayers.Count}/{settings.maxConnections}).";
				goto ConnectionDenied;
			}
			if (string.IsNullOrWhiteSpace(request.Username))
			{
				reason = $"Username cannot be empty";
				goto ConnectionDenied;
			}
			if (settings.blockDuplicatePlayerNames && connectedPlayers.Values.Select(player => player.username).Contains(request.Username))
			{
				reason = $"Username '{request.Username}' is already in use";
				goto ConnectionDenied;
			}
			if (BanManager.IsBanned(request.Username, connection.RemoteEndPoint, out string banReason))
			{
				reason = banReason;
				goto ConnectionDenied;
			}
			if (request.Password != Packet_JoinRequest.GetPasswordHash(settings.serverPassword) && settings.serverPassword != "")
			{
				reason = $"Invalid password";
				goto ConnectionDenied;
			}
			// 版本校验
			if (!IsVersionAllowed(request.GameVersion, out string versionReason))
			{
				reason = versionReason;
				goto ConnectionDenied;
			}
			if (request.ProtocolVersion != Ver.ProtocolVersion)
				Logger.Warning($"Client protocol version mismatch (client {request.ProtocolVersion}, server {Ver.ProtocolVersion}).");

			Logger.Info($"Approved join request, sending world info...", true);
			
            ConnectedPlayer newPlayer = new ConnectedPlayer(request.Username);
			newPlayer.solarSystemName = request.SolarSystemName;
			connectedPlayers.Add(connection.RemoteEndPoint, newPlayer);

			NetOutgoingMessage joinResponse = server.CreateMessage();
			joinResponse.Write
			(
				new Packet_JoinResponse()
				{
					PlayerId = newPlayer.id,
					UpdateRocketsPeriod = settings.updateRocketsPeriod,
					ChatMessageCooldown = settings.chatMessageCooldown,
					WorldTime = world.WorldTime,
					Difficulty = world.difficulty,
					SolarSystemName = world.solarSystemName,
					ServerName = settings.serverName,
					AllowLaunchOnOccupiedPad = settings.allowLaunchOnOccupiedPad,
					PlanetsPackName = planetsPackName,
					PlanetsPackHash = planetsPackHash,
				}
			);
			connection.Approve(joinResponse);
			return;

			ConnectionDenied:
				Logger.Info($"Denied join request - {reason}", true);
				connection.Deny(reason);
		}

		static bool IsVersionAllowed(string clientVersion, out string reason)
		{
			reason = "";
			
			// 配置为空时允许所有版本
			if (string.IsNullOrWhiteSpace(settings.allowedGameVersions))
			{
				return true;
			}

			// 客户端未发送版本信息时拒绝连接
			if (string.IsNullOrWhiteSpace(clientVersion))
			{
				reason = "Client version not provided";
				return false;
			}

			// 解析允许的版本列表
			string[] allowedVersions = settings.allowedGameVersions.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
			
			foreach (string allowedVersion in allowedVersions)
			{
				string trimmedVersion = allowedVersion.Trim();
				
				// 完全匹配
				if (clientVersion == trimmedVersion)
				{
					return true;
				}

				// 前缀匹配
				if (clientVersion.StartsWith(trimmedVersion + "."))
				{
					return true;
				}
			}

			reason = $"Game version {clientVersion} is not allowed. Allowed versions: {settings.allowedGameVersions}";
			return false;
		}

		static void OnPlayerSuccessfulConnect(NetConnection connection)
		{
			ConnectedPlayer player = FindPlayer(connection);
			if (player == null)
			{
				Logger.Warning("Missing new player while sending join response!");
				return;
			}

			SendPacketToAll
			(
				new Packet_PlayerConnected()
				{
					PlayerId = player.id,
					Username = player.username,
					IconColor = player.iconColor,
					PrintMessage = true,
				},
				connection
			);
			// 世界数据等客户端就绪（ClientReady）后发送
			Plugin.TriggerPlayerJoined(player);
		}

		static void SendWorldDataToPlayer(NetConnection connection)
		{
			ConnectedPlayer player = FindPlayer(connection);
			if (player == null)
				return;
			foreach (KeyValuePair<int, RocketState> kvp in world.rockets)
			{
				SendPacketToPlayer
				(
					connection,
					new Packet_CreateRocket()
					{
						GlobalId = kvp.Key,
						Rocket = kvp.Value,
					}
				);
			}
			foreach (KeyValuePair<IPEndPoint, ConnectedPlayer> kvp in connectedPlayers)
			{
				SendPacketToPlayer
				(
					connection,
					new Packet_PlayerConnected()
					{
						PlayerId = kvp.Value.id,
						Username = kvp.Value.username,
						IconColor = kvp.Value.iconColor,
						PrintMessage = false,
					}
				);
				SendPacketToPlayer
				(
					connection,
					new Packet_UpdatePlayerControl()
					{
						PlayerId = kvp.Value.id,
						RocketId = kvp.Value.controlledRocket,
					}
				);
			}

			// 发送当前作弊状态给新玩家
			SendPacketToPlayer
			(
				connection,
				new Packet_UpdateCheatStatus()
				{
					InfiniteFuel = world.infiniteFuel,
					NoAtmosphericDrag = world.noAtmosphericDrag,
					UnbreakableParts = world.unbreakableParts,
					NoGravity = world.noGravity,
					NoHeatDamage = world.noHeatDamage,
					NoBurnMarks = world.noBurnMarks,
					InfiniteBuildArea = world.infiniteBuildArea,
					PartClipping = world.partClipping,
				}
			);

			// 发送 MOTD 给新玩家
			if (!string.IsNullOrWhiteSpace(settings.motd))
			{
				Color motdColor = new Color(0, 0, 1, 1);
				if (!string.IsNullOrWhiteSpace(settings.motdColor))
				{
					string colorStr = settings.motdColor;
					int r = 0, g = 0, b = 0, a = 255;
					
					if (colorStr.Length == 6 || colorStr.Length == 8)
					{
						try
						{
							r = Convert.ToInt32(colorStr.Substring(0, 2), 16);
							g = Convert.ToInt32(colorStr.Substring(2, 2), 16);
							b = Convert.ToInt32(colorStr.Substring(4, 2), 16);
							if (colorStr.Length == 8)
							{
								a = Convert.ToInt32(colorStr.Substring(6, 2), 16);
							}
							motdColor = new Color(r / 255f, g / 255f, b / 255f, a / 255f);
						}
						catch {}
					}
				}
				
				SendPacketToPlayer
				(
					connection,
					new Packet_SendChatMessage()
					{
						SenderId = -1,
						Message = settings.motd,
						Color = motdColor,
					}
				);
			}

			// 发送当前时间加速状态给新玩家
			if (isTimeWarping)
			{
				SendPacketToPlayer
				(
					connection,
					new Packet_TimeWarpResult()
					{
						VoteId = currentVoteId,
						Approved = true,
						TimeScale = currentTimeScale,
						PhysicsWarp = currentPhysicsWarp,
					}
				);
			}
		}

		static void OnPlayerDisconnect(NetConnection connection)
        {
            if (FindPlayer(connection) is ConnectedPlayer player)
			{
				SendPacketToAll(new Packet_PlayerDisconnected() { PlayerId = player.id });
				Plugin.TriggerPlayerLeft(player);
				connectedPlayers.Remove(connection.RemoteEndPoint);
				UpdatePlayerAuthorities();
			}
        }
		
		static void OnLatencyUpdated(NetIncomingMessage msg)
		{
			if (FindPlayer(msg.SenderConnection) is ConnectedPlayer player)
			{
				string username = player.username.FormatUsername();
				player.avgTripTime = msg.SenderConnection.AverageRoundtripTime;
				Logger.Info($"Average roundtrip time updated for {username} @ {msg.SenderEndPoint} - {1000 * player.avgTripTime}ms.");

				SendPacketToPlayer
				(
					msg.SenderConnection,
					new Packet_UpdateWorldTime()
					{
						WorldTime = world.WorldTime + (player.avgTripTime / 2),
					}
				);
			}
		}


		static void UpdatePlayerAuthorities()
		{
			if (connectedPlayers.Count == 0)
			{
				return;
			}

			foreach (ConnectedPlayer player in connectedPlayers.Values)
			{
				player.updateAuthority.Clear();
			}

			if (world.rockets.Count == 0)
			{
				return;
			}

			controlledRocketsCache.Clear();
			playerPositionsCache.Clear();
			
			foreach (ConnectedPlayer player in connectedPlayers.Values)
			{
				if (world.rockets.TryGetValue(player.controlledRocket, out RocketState controlledRocket))
				{
					player.updateAuthority.Add(player.controlledRocket);
					controlledRocketsCache.Add(player.controlledRocket);
					playerPositionsCache[player.id] = controlledRocket.location.position;
				}
			}

			foreach (int rocketId in world.rockets.Keys)
			{
				if (!controlledRocketsCache.Contains(rocketId))
				{
					RocketState rocket = world.rockets[rocketId];
					Double2 rocketPos = rocket.location.position;
					
					ConnectedPlayer closestPlayer = null;
					double minDistSq = double.MaxValue;
					
					foreach (ConnectedPlayer player in connectedPlayers.Values)
					{
						if (playerPositionsCache.TryGetValue(player.id, out Double2 playerPos))
						{
							double dx = rocketPos.x - playerPos.x;
							double dy = rocketPos.y - playerPos.y;
							double distSq = dx * dx + dy * dy;
							
							if (distSq < minDistSq)
							{
								minDistSq = distSq;
								closestPlayer = player;
							}
						}
					}
					
					if (closestPlayer != null)
					{
						closestPlayer.updateAuthority.Add(rocketId);
					}
				}
			}

			foreach (KeyValuePair<IPEndPoint, ConnectedPlayer> kvp in connectedPlayers)
			{
				SendPacketToPlayer
				(
					server.GetConnection(kvp.Key),
					new Packet_UpdatePlayerAuthority()
					{
						RocketIds = kvp.Value.updateAuthority,
					}
				);
			}
		}

		/// <summary>
		/// Returns `true` if a refresh of the players' update authorities is required.
		/// </summary>
        static bool OnIncomingPacket(NetIncomingMessage msg)
        {
            PacketType packetType = (PacketType) msg.ReadByte();
			Plugin.TriggerEvent("Packet_" + packetType, msg);
			// if (Packet.ShouldDebug(packetType))
			// 	Logger.Debug($"Recieved packet of type '{packetType}'.");
			switch (packetType)
			{
				case PacketType.UpdatePlayerControl:
					OnPacket_UpdatePlayerControl(msg);
					return true;
				case PacketType.UpdatePlayerColor:
                    OnPacket_UpdatePlayerColor(msg);
                    return false;
				case PacketType.SendChatMessage:
                    OnPacket_SendChatMessage(msg);
                    return false;

				case PacketType.CreateRocket:
					return OnPacket_CreateRocket(msg);
				case PacketType.DestroyRocket:
					OnPacket_DestroyRocket(msg);
					return true;
				case PacketType.UpdateRocketPrimary:
					OnPacket_UpdateRocketPrimary(msg);
					return false;
				case PacketType.UpdateRocketSecondary:
					OnPacket_UpdateRocketSecondary(msg);
					return false;

				case PacketType.DestroyPart:
					OnPacket_DestroyPart(msg);
					return false;
				case PacketType.UpdateStaging:
					OnPacket_UpdateStaging(msg);
					return false;
				case PacketType.UpdatePart_EngineModule:
					OnPacket_UpdatePart_EngineModule(msg);
					return false;
				case PacketType.UpdatePart_WheelModule:
					OnPacket_UpdatePart_WheelModule(msg);
					return false;
				case PacketType.UpdatePart_BoosterModule:
					OnPacket_UpdatePart_BoosterModule(msg);
					return false;
				case PacketType.UpdatePart_ParachuteModule:
					OnPacket_UpdatePart_ParachuteModule(msg);
					return false;
				case PacketType.UpdatePart_MoveModule:
                    OnPacket_UpdatePart_MoveModule(msg);
                    return false;
				case PacketType.UpdatePart_ResourceModule:
                    OnPacket_UpdatePart_ResourceModule(msg);
                    return false;
				
				// * Time Warp Packets
				case PacketType.TimeWarpRequest:
					OnPacket_TimeWarpRequest(msg);
					return false;
				case PacketType.TimeWarpVoteResponse:
					OnPacket_TimeWarpVoteResponse(msg);
					return false;
				
				// * Planets Pack Packets
				case PacketType.PlanetsPackRequest:
					OnPacket_PlanetsPackRequest(msg);
					return false;
				case PacketType.ClientReady:
					OnPacket_ClientReady(msg);
					return false;
				
				case PacketType.JoinRequest:
					Logger.Warning("Recieved join request outside of connection attempt.");
					return false;

				case PacketType.PlayerConnected:
				case PacketType.PlayerDisconnected:
				case PacketType.JoinResponse:
				case PacketType.UpdatePlayerAuthority:
					Logger.Warning($"Recieved packet (of type {packetType}) intended for clients.");
					return false;

				default:
					Logger.Error($"Unhandled packet type: {packetType}, {msg.LengthBytes} bytes.");
					return false;
			}
        }

		static void OnPacket_UpdatePlayerControl(NetIncomingMessage msg)
		{
			Packet_UpdatePlayerControl packet = msg.Read<Packet_UpdatePlayerControl>();
			if (FindPlayer(msg.SenderConnection) is ConnectedPlayer player)
			{
				if (player.id == packet.PlayerId)
				{
					player.controlledRocket = packet.RocketId;
					SendPacketToAll
					(
						packet,
						msg.SenderConnection
					);
					// 更新玩家权限
					UpdatePlayerAuthorities();
				}
				else
				{
					Logger.Warning("Incorrect player id while trying to update controlled rocket!");
				}
			}
			else
			{
				Logger.Error("Missing connected player while trying to update controlled rocket!");
			}
		}

		static void OnPacket_UpdatePlayerColor(NetIncomingMessage msg)
        {
            Packet_UpdatePlayerColor packet = msg.Read<Packet_UpdatePlayerColor>();
			if (FindPlayer(msg.SenderConnection) is ConnectedPlayer player)
            {
                player.iconColor = packet.Color;
				SendPacketToAll(packet, msg.SenderConnection);
            }
        }

		static void OnPacket_SendChatMessage(NetIncomingMessage msg)
        {
            Packet_SendChatMessage packet = msg.Read<Packet_SendChatMessage>();
			// 插件可拦截处理消息
			if (Plugin.TryHandleMessage(msg.SenderConnection, packet.Message))
				return;
			if (CommandManager.TryParse(packet.Message, out string name, out string[] args))
			{
				string message = CommandManager.TryRun(name, args, msg.SenderConnection);
				foreach (string line in message.Split('\n'))
				{
					if (line.Length == 0) continue;
					SendPacketToPlayer
					(
						msg.SenderConnection,
						new Packet_SendChatMessage()
						{
							Message = line.TrimEnd('\r'),
						}
					);
				}
			}
			else
			{
				SendPacketToAll(packet, msg.SenderConnection);
				if (FindPlayer(msg.SenderConnection) is ConnectedPlayer sender)
					Plugin.TriggerChatMessage(sender, packet.Message);
			}
        }

		static bool OnPacket_CreateRocket(NetIncomingMessage msg)
		{
			Packet_CreateRocket packet = msg.Read<Packet_CreateRocket>();
			if (world.rockets.ContainsKey(packet.GlobalId))
            {
				// Logger.Debug($"existing: {packet.Rocket.parts.Count}");
                world.rockets[packet.GlobalId] = packet.Rocket;
            	SendPacketToAll(packet, msg.SenderConnection);
				return true;
            }
            else
            {
				// Logger.Debug($"new: {packet.Rocket.parts.Count}");
                packet.GlobalId = world.rockets.InsertNew(packet.Rocket);
            	SendPacketToAll(packet);
				// * This is to prevent update authority being given to a different player than the one that launched the rocket, which can cause some strange issues.
				// * The update authority is updated when the player has switched to their newly launched rocket, however.
				return !packet.ForLaunch;
            }

		}

		static void OnPacket_DestroyRocket(NetIncomingMessage msg)
		{
			Packet_DestroyRocket packet = msg.Read<Packet_DestroyRocket>();
			if (world.rockets.Remove(packet.RocketId))
            {
                SendPacketToAll(packet, msg.SenderConnection);
            }
		}

		static void OnPacket_UpdateRocketPrimary(NetIncomingMessage msg)
		{
			Packet_UpdateRocketPrimary packet = msg.Read<Packet_UpdateRocketPrimary>();
			if (world.rockets.TryGetValue(packet.RocketId, out RocketState state))
			{
				state.UpdateRocketPrimary(packet);
				SendPacketToAll(packet, msg.SenderConnection);
			}
		}

		static void OnPacket_UpdateRocketSecondary(NetIncomingMessage msg)
		{
			Packet_UpdateRocketSecondary packet = msg.Read<Packet_UpdateRocketSecondary>();
			if (world.rockets.TryGetValue(packet.RocketId, out RocketState state))
			{
				state.UpdateRocketSecondary(packet);
				SendPacketToAll(packet, msg.SenderConnection);
			}
		}

		static void OnPacket_DestroyPart(NetIncomingMessage msg)
		{
			Packet_DestroyPart packet = msg.Read<Packet_DestroyPart>();
			if (world.rockets.TryGetValue(packet.RocketId, out RocketState state))
			{
				if (state.RemovePart(packet.PartId))
					SendPacketToAll(packet, msg.SenderConnection);
			}
		}

		static void OnPacket_UpdateStaging(NetIncomingMessage msg)
		{
			Packet_UpdateStaging packet = msg.Read<Packet_UpdateStaging>();
			if (world.rockets.TryGetValue(packet.RocketId, out RocketState state))
			{
				state.stages = packet.Stages;
				SendPacketToAll(packet, msg.SenderConnection);
			}
		}

		static void OnPacket_UpdatePart_EngineModule(NetIncomingMessage msg)
		{
			Packet_UpdatePart_EngineModule packet = msg.Read<Packet_UpdatePart_EngineModule>();
			if (world.rockets.TryGetValue(packet.RocketId, out RocketState state))
			{
				if (state.parts.TryGetValue(packet.PartId, out PartState part))
				{
					part.part.TOGGLE_VARIABLES["engine_on"] = packet.EngineOn;
					SendPacketToAll(packet, msg.SenderConnection);
				}
			}
		}

		static void OnPacket_UpdatePart_WheelModule(NetIncomingMessage msg)
		{
			Packet_UpdatePart_WheelModule packet = msg.Read<Packet_UpdatePart_WheelModule>();
			if (world.rockets.TryGetValue(packet.RocketId, out RocketState state))
			{
				if (state.parts.TryGetValue(packet.PartId, out PartState part))
				{
					part.part.TOGGLE_VARIABLES["wheel_on"] = packet.WheelOn;
					SendPacketToAll(packet, msg.SenderConnection);
				}
			}
		}

		static void OnPacket_UpdatePart_BoosterModule(NetIncomingMessage msg)
		{
			Packet_UpdatePart_BoosterModule packet = msg.Read<Packet_UpdatePart_BoosterModule>();
			if (world.rockets.TryGetValue(packet.RocketId, out RocketState state))
			{
				if (state.parts.TryGetValue(packet.PartId, out PartState part))
				{
					// TODO: Booster modules seemingly don't save their on/off status? (At least not the RA retro pack)
					// TODO: I'm guessing that's why they get infinite thrust after loading a save when they're activated?
					// TODO: Anyway, I can't save either their "primed" state or their thrust output to the world state rn.
					// TODO: The booster module is only obtainable in vanilla through the RA retro pack, so it shouldn't matter too much for now.
					part.part.NUMBER_VARIABLES["fuel_percent"] = packet.FuelPercent;
					SendPacketToAll(packet, msg.SenderConnection);
				}
			}
		}

		static void OnPacket_UpdatePart_ParachuteModule(NetIncomingMessage msg)
		{
			Packet_UpdatePart_ParachuteModule packet = msg.Read<Packet_UpdatePart_ParachuteModule>();
			if (world.rockets.TryGetValue(packet.RocketId, out RocketState state))
			{
				if (state.parts.TryGetValue(packet.PartId, out PartState part))
				{
					part.part.NUMBER_VARIABLES["animation_state"] = packet.State;
					part.part.NUMBER_VARIABLES["deploy_state"] = packet.TargetState;
					SendPacketToAll(packet, msg.SenderConnection);
				}
			}
		}

		static void OnPacket_UpdatePart_MoveModule(NetIncomingMessage msg)
		{
			Packet_UpdatePart_MoveModule packet = msg.Read<Packet_UpdatePart_MoveModule>();
			if (world.rockets.TryGetValue(packet.RocketId, out RocketState state))
			{
				if (state.parts.TryGetValue(packet.PartId, out PartState part))
				{
					part.part.NUMBER_VARIABLES["state"] = packet.Time;
					part.part.NUMBER_VARIABLES["state_target"] = packet.TargetTime;
					SendPacketToAll(packet, msg.SenderConnection);
				}
			}
		}

		static void OnPacket_UpdatePart_ResourceModule(NetIncomingMessage msg)
		{
			Packet_UpdatePart_ResourceModule packet = msg.Read<Packet_UpdatePart_ResourceModule>();
			if (world.rockets.TryGetValue(packet.RocketId, out RocketState state))
			{
				bool foundPart = false;
				foreach (int partId in packet.PartIds)
                {
                    if (state.parts.TryGetValue(partId, out PartState partState))
                    {
                        // TODO! A lot of these save variable names might be different for non-vanilla parts, but currently idk what the best way to properly get them is.
                        // TODO! I might need some form of register that associates a part's name and module variable names to their save variable names.
                        partState.part.NUMBER_VARIABLES["fuel_percent"] = (float)packet.ResourcePercent;
						foundPart = true;
                    }
                }
				if (foundPart)
				{
					SendPacketToAll(packet, msg.SenderConnection);
				}
			}
		}

		// * Time Warp Voting System
	private static int currentVoteId = 0;
	private static bool isVoting = false;
	private static bool isTimeWarping = false;
	private static float currentTimeScale = 1f;
	private static bool currentPhysicsWarp = false;
	private static Dictionary<int, bool> currentVotes = new Dictionary<int, bool>();
	private static int totalVoters = 0;
	private static float requestedTimeScale = 1f;
	private static string requesterName = "";
	private static bool requestedPhysicsWarp = false;

		static void OnPacket_TimeWarpRequest(NetIncomingMessage msg)
		{
			Packet_TimeWarpRequest packet = msg.Read<Packet_TimeWarpRequest>();
			ConnectedPlayer requester = FindPlayer(msg.SenderConnection);
			
			if (requester == null)
				return;
			if (settings.timeWarpMaxScale > 0f && packet.TimeScale > settings.timeWarpMaxScale)
			{
				Logger.Info($"Time warp request {packet.TimeScale}x exceeds max scale {settings.timeWarpMaxScale}x, rejecting");
				return;
			}

		if (packet.TimeScale == 1f)
		{
			isTimeWarping = false;
			currentTimeScale = 1f;
			currentPhysicsWarp = false;
			SendPacketToAll(new Packet_TimeWarpResult()
			{
				VoteId = -1,
				Approved = true,
				TimeScale = 1f,
				PhysicsWarp = false,
				StopperName = requester.username,
			});
			
			Logger.Info($"Time warp stopped by {requester.username}");
			return;
		}
		if (isVoting)
		{
			Logger.Info($"Vote already in progress, ignoring request from {requester.username}");
			return;
		}
		currentVoteId++;
		isVoting = true;
		requestedTimeScale = packet.TimeScale;
		requesterName = packet.RequesterName;
		requestedPhysicsWarp = packet.PhysicsWarp;
		currentVotes.Clear();
		totalVoters = connectedPlayers.Values.Count(p => p.controlledRocket >= 0);
		if (totalVoters <= 1)
		{
			isVoting = false;
			isTimeWarping = true;
			currentTimeScale = requestedTimeScale;
			currentPhysicsWarp = requestedPhysicsWarp;
			world.timeWarpScale = requestedTimeScale;
			
			SendPacketToAll(new Packet_TimeWarpResult()
			{
				VoteId = currentVoteId,
				Approved = true,
				TimeScale = requestedTimeScale,
				PhysicsWarp = requestedPhysicsWarp,
			});
			
			Logger.Info($"Time warp to {requestedTimeScale}x approved ({(requestedPhysicsWarp ? "Physics" : "WorldTime")})");
			return;
		}
		foreach (KeyValuePair<IPEndPoint, ConnectedPlayer> kvp in connectedPlayers)
		{
			if (kvp.Value.id != requester.id && kvp.Value.controlledRocket >= 0)
			{
				SendPacketToPlayer(server.GetConnection(kvp.Key), new Packet_TimeWarpVote()
				{
					TimeScale = requestedTimeScale,
					RequesterName = requesterName,
					VoteId = currentVoteId,
					PhysicsWarp = requestedPhysicsWarp,
				});
			}
		}
		
		Logger.Info($"Time warp vote started: {requesterName} requests {requestedTimeScale}x ({(requestedPhysicsWarp ? "Physics" : "WorldTime")}, {totalVoters} voters)");
	}

	static void OnPacket_TimeWarpVoteResponse(NetIncomingMessage msg)
	{
		Packet_TimeWarpVoteResponse packet = msg.Read<Packet_TimeWarpVoteResponse>();
		ConnectedPlayer voter = FindPlayer(msg.SenderConnection);
		
		if (voter == null || !isVoting || packet.VoteId != currentVoteId)
			return;
		currentVotes[voter.id] = packet.Agreed;
		Logger.Info($"{voter.username} voted: {(packet.Agreed ? "Agree" : "Reject")}");
		if (!packet.Agreed)
		{
			isVoting = false;
			
			SendPacketToAll(new Packet_TimeWarpResult()
			{
				VoteId = currentVoteId,
				Approved = false,
				TimeScale = 1f,
				PhysicsWarp = false,
				RejecterName = voter.username,
			});
			
			Logger.Info($"Time warp vote rejected by {voter.username}");
			return;
		}

		// 检查是否所有人都同意
		int agreedCount = currentVotes.Values.Count(v => v);
		int rejectedCount = currentVotes.Values.Count(v => !v);
		
		if (agreedCount + rejectedCount >= totalVoters - 1)
		{
			isVoting = false;
			
			if (rejectedCount == 0)
			{
				// 全部同意则开始时间加速
				isTimeWarping = true;
				currentTimeScale = requestedTimeScale;
				currentPhysicsWarp = requestedPhysicsWarp;
				world.timeWarpScale = requestedTimeScale;
				
				SendPacketToAll(new Packet_TimeWarpResult()
				{
					VoteId = currentVoteId,
					Approved = true,
					TimeScale = requestedTimeScale,
					PhysicsWarp = requestedPhysicsWarp,
				});
				
				Logger.Info($"Time warp to {requestedTimeScale}x approved ({(requestedPhysicsWarp ? "Physics" : "WorldTime")})");
			}
			else
			{
				var rejecter = currentVotes.FirstOrDefault(v => !v.Value);
				string rejecterName = "";
				if (rejecter.Key != 0)
				{
					var rejecterPlayer = connectedPlayers.Values.FirstOrDefault(p => p.id == rejecter.Key);
					if (rejecterPlayer != null)
						rejecterName = rejecterPlayer.username;
				}
				
				SendPacketToAll(new Packet_TimeWarpResult()
				{
					VoteId = currentVoteId,
					Approved = false,
					TimeScale = 1f,
					PhysicsWarp = false,
					RejecterName = rejecterName,
				});
				
				Logger.Info($"Time warp vote rejected");
			}
		}
	}
	// * Planets Pack Sync
	static void OnPacket_PlanetsPackRequest(NetIncomingMessage msg)
	{
		Packet_PlanetsPackRequest packet = msg.Read<Packet_PlanetsPackRequest>();
		ConnectedPlayer player = FindPlayer(msg.SenderConnection);
		if (player == null || planetsPackData == null || packet.PackName != planetsPackName)
			return;
		const int chunkSize = 64 * 1024;
		int chunkCount = (planetsPackData.Length + chunkSize - 1) / chunkSize;
		for (int i = 0; i < chunkCount; i++)
		{
			int len = Math.Min(chunkSize, planetsPackData.Length - i * chunkSize);
			byte[] chunk = new byte[len];
			Array.Copy(planetsPackData, i * chunkSize, chunk, 0, len);
			SendPacketToPlayer(msg.SenderConnection, new Packet_PlanetsPackData()
			{
				PackName = planetsPackName,
				Hash = planetsPackHash,
				ChunkIndex = i,
				ChunkCount = chunkCount,
				Data = chunk,
			});
		}
		Logger.Info($"Sent planets pack '{planetsPackName}' to {player.username} ({chunkCount} chunks).", true);
	}

	static void OnPacket_ClientReady(NetIncomingMessage msg)
	{
		msg.Read<Packet_ClientReady>();
		ConnectedPlayer player = FindPlayer(msg.SenderConnection);
		if (player == null || player.ready)
			return;
		player.ready = true;
		Logger.Info($"{player.username} is ready, sending world data...", true);
		SendWorldDataToPlayer(msg.SenderConnection);
	}
	}

	public class ConnectedPlayer
	{
		public int id;
		public bool isAdmin;
		public string username;
		public Color iconColor;
		public float avgTripTime;
		public float loadRange;
		public string solarSystemName = "";

		public int controlledRocket;
		public HashSet<int> updateAuthority;
		/// <summary>
		/// 是否已就绪（收到 ClientReady，可以接收世界数据）
		/// </summary>
		public bool ready;

		static readonly System.Random colorRandom = new System.Random();
		static Color GetRandomColor()
		{
			return Color.HSVToRGB(colorRandom.Next(0, 101) / 100f, 1, 1);
		}

		public ConnectedPlayer(string playerName)
		{
			id = Server.connectedPlayers.Select(kvp => kvp.Value.id).ToHashSet().InsertNew();
			isAdmin = false;
			username = playerName;
			iconColor = GetRandomColor();
			avgTripTime = 0;
			loadRange = (float)Server.settings.loadRange;
			controlledRocket = -1;
			updateAuthority = new HashSet<int>();
		}
	}

	/// <summary>
	/// 封禁记录（按 IP 或玩家名）
	/// </summary>
	public class BanEntry
	{
		public string target;
		public bool isIP;
		public long bannedAt;
		public long durationSeconds;
	}

	/// <summary>
	/// 管理封禁列表，持久化到本地 Bans.txt
	/// </summary>
	public static class BanManager
	{
		public static List<BanEntry> bans = new List<BanEntry>();
		public static readonly string bansFilePath = "./Bans.txt";

		/// <summary>
		/// 从本地文件加载封禁列表
		/// </summary>
		public static void Load()
		{
			bans.Clear();
			try
			{
				if (!File.Exists(bansFilePath))
					return;
				foreach (string line in File.ReadAllLines(bansFilePath))
				{
					string[] parts = line.Split('|');
					if (parts.Length >= 4 && bool.TryParse(parts[0], out bool isIP) && long.TryParse(parts[2], out long bannedAt) && long.TryParse(parts[3], out long duration))
					{
						bans.Add(new BanEntry() { target = parts[1], isIP = isIP, bannedAt = bannedAt, durationSeconds = duration });
					}
				}
			}
			catch (Exception e)
			{
				Logger.Error($"Failed to load bans: {e.Message}");
			}
		}

		/// <summary>
		/// 保存封禁列表到本地文件
		/// </summary>
		public static void Save()
		{
			try
			{
				List<string> lines = new List<string>();
				foreach (BanEntry ban in bans)
					lines.Add($"{ban.isIP}|{ban.target}|{ban.bannedAt}|{ban.durationSeconds}");
				File.WriteAllLines(bansFilePath, lines);
			}
			catch (Exception e)
			{
				Logger.Error($"Failed to save bans: {e.Message}");
			}
		}

		/// <summary>
		/// 检查是否被封禁，过期自动清理
		/// </summary>
		public static bool IsBanned(string username, IPEndPoint endpoint, out string reason)
		{
			long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
			bool changed = false;
			foreach (BanEntry ban in bans.ToList())
			{
				// 封禁到期自动解除
				if (ban.durationSeconds != 0 && now - ban.bannedAt >= ban.durationSeconds)
				{
					bans.Remove(ban);
					changed = true;
					continue;
				}
				bool match = ban.isIP
					? ban.target == endpoint.Address.ToString()
					: string.Equals(ban.target, username, StringComparison.OrdinalIgnoreCase);
				if (match)
				{
					if (ban.durationSeconds == 0)
						reason = "You are banned from this server (permanent).";
					else
						reason = $"You are banned from this server for another {FormatRemaining(ban.durationSeconds - (now - ban.bannedAt))}.";
					if (changed)
						Save();
					return true;
				}
			}
			if (changed)
				Save();
			reason = "";
			return false;
		}

		/// <summary>
		/// 清理已过期的封禁记录
		/// </summary>
		public static void CleanupExpired()
		{
			long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
			if (bans.RemoveAll(b => b.durationSeconds != 0 && now - b.bannedAt >= b.durationSeconds) > 0)
				Save();
		}

		/// <summary>
		/// 只读检查是否被封禁（不过期清理）
		/// </summary>
		public static bool IsBannedQuick(string username, IPEndPoint endpoint)
		{
			return bans.Any(ban => ban.isIP
				? ban.target == endpoint.Address.ToString()
				: string.Equals(ban.target, username, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>
		/// 封禁目标，返回结果消息
		/// </summary>
		public static string BanTarget(string target, long durationHours)
		{
			bool isIP = IPAddress.TryParse(target, out _);
			// 防止重复封禁
			if (bans.Any(b => b.isIP == isIP && string.Equals(b.target, target, StringComparison.OrdinalIgnoreCase)))
				return $"'{target}' is already banned.";
			bans.Add(new BanEntry()
			{
				target = target,
				isIP = isIP,
				bannedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
				durationSeconds = durationHours > 0 ? durationHours * 3600 : 0,
			});
			Save();
			// 踢出所有匹配的在线玩家（同名或同 IP）
			foreach (var kvp in Server.connectedPlayers.ToList())
			{
				bool match = isIP
					? kvp.Key.Address.ToString() == target
					: string.Equals(kvp.Value.username, target, StringComparison.OrdinalIgnoreCase);
				if (match)
					Server.server.GetConnection(kvp.Key)?.Disconnect("You have been banned from this server.");
			}
			return durationHours > 0
				? $"Banned '{target}' for {durationHours} hour(s)."
				: $"Banned '{target}' permanently.";
		}

		/// <summary>
		/// 解除封禁，返回结果消息
		/// </summary>
		public static string UnbanTarget(string target)
		{
			bool isIP = IPAddress.TryParse(target, out _);
			BanEntry match = bans.FirstOrDefault(b => b.isIP == isIP && string.Equals(b.target, target, StringComparison.OrdinalIgnoreCase));
			if (match == null)
				return $"'{target}' is not banned.";
			bans.Remove(match);
			Save();
			return $"Unbanned '{target}'.";
		}

		/// <summary>
		/// 列出所有封禁记录
		/// </summary>
		public static string ListBans()
		{
			if (bans.Count == 0)
				return "No players are banned.";
			List<string> lines = new List<string> { $"Banned players ({bans.Count}):" };
			long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
			foreach (BanEntry ban in bans)
			{
				string type = ban.isIP ? "IP" : "Name";
				string duration = ban.durationSeconds == 0
					? "Permanent"
					: $"Expires in {FormatRemaining(ban.durationSeconds - (now - ban.bannedAt))}";
				lines.Add($"  {type} '{ban.target}' - {duration}");
			}
			return string.Join("\n", lines);
		}

		/// <summary>
		/// 将秒数格式化为 天/小时/分钟
		/// </summary>
		static string FormatRemaining(long seconds)
		{
			long days = seconds / 86400; seconds %= 86400;
			long hours = seconds / 3600; seconds %= 3600;
			long minutes = seconds / 60;
			if (days > 0)
				return $"{days}d {hours}h {minutes}m";
			if (hours > 0)
				return $"{hours}h {minutes}m";
			return $"{minutes}m";
		}
	}
}