using System;
using System.Net;
using System.Linq;
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

		public static void Initialize(ServerSettings settings)
		{
			Server.settings = settings;
            NetPeerConfiguration npc = new NetPeerConfiguration("multiplayersfs")
            {
                Port = settings.port,
				MaximumConnections = settings.maxConnections,
            };
			npc.EnableMessageType(NetIncomingMessageType.StatusChanged);
			npc.EnableMessageType(NetIncomingMessageType.ConnectionApproval);
			npc.EnableMessageType(NetIncomingMessageType.ConnectionLatencyUpdated);
			npc.EnableMessageType(NetIncomingMessageType.VerboseDebugMessage);

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

            server = new NetServer(npc);
			server.Start();
		}

		public static void Run()
		{
			try
			{
				Logger.Info($"Multiplayer SFS server started, listening for connections on port {server.Port}...", true);
				
				while (true)
				{
					Listen();
					
					if ((DateTime.Now - lastAuthorityUpdate).TotalMilliseconds >= AuthorityUpdateIntervalMs)
					{
						UpdatePlayerAuthorities();
						lastAuthorityUpdate = DateTime.Now;
					}
					
					System.Threading.Thread.Sleep(1);
				}
			}
			catch (Exception e)
			{
				Logger.Error(e);
			}
		}

        private static DateTime lastAuthorityUpdate = DateTime.MinValue;
        private const int AuthorityUpdateIntervalMs = 200;
        
        private static HashSet<int> controlledRocketsCache = new HashSet<int>();
        private static Dictionary<int, Double2> playerPositionsCache = new Dictionary<int, Double2>();

		/// <summary>
		/// Processes a single incoming message
		/// </summary>
		static void ProcessSingleMessage(NetIncomingMessage msg)
		{
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
			if (request.Password != settings.serverPassword && settings.serverPassword != "")
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
			
			// 如果配置为空，允许所有版本
			if (string.IsNullOrWhiteSpace(settings.allowedGameVersions))
			{
				return true;
			}

			// 如果客户端没有发送版本信息，拒绝连接
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

				// 前缀匹配：客户端版本以配置版本开头，且后面跟着点号
				// 例如：配置 "1.5"，客户端 "1.5.10.2" → 匹配
				// 例如：配置 "1.5.10"，客户端 "1.5.10.2" → 匹配
				// 例如：配置 "1.5"，客户端 "1.50.0" → 不匹配（不是以 "1.5." 开头）
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
				System.Drawing.Color motdColor = System.Drawing.Color.Blue;
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
							motdColor = System.Drawing.Color.FromArgb(a, r, g, b);
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
					// 玩家切换控制的火箭后，需要更新权限
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
			if (CommandManager.TryParse(packet.Message, out string name, out string[] args))
			{
				string message = CommandManager.TryRun(name, args, msg.SenderConnection);
				SendPacketToPlayer
				(
					msg.SenderConnection,
					new Packet_SendChatMessage()
					{
						Message = message,
					}
				);
			}
			else
			{
				SendPacketToAll(packet, msg.SenderConnection);
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
			
			// 广播给所有玩家停止时间加速
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

		// 如果已经在投票中，忽略新请求
		if (isVoting)
		{
			Logger.Info($"Vote already in progress, ignoring request from {requester.username}");
			return;
		}

		// 开始新投票
		currentVoteId++;
		isVoting = true;
		requestedTimeScale = packet.TimeScale;
		requesterName = packet.RequesterName;
		requestedPhysicsWarp = packet.PhysicsWarp;
		currentVotes.Clear();
		
		// 计算投票人数（所有控制火箭的玩家）
		totalVoters = connectedPlayers.Values.Count(p => p.controlledRocket >= 0);
		
		// 如果只有请求者一人控制火箭，直接通过
		if (totalVoters <= 1)
		{
			isVoting = false;
			isTimeWarping = true;
			currentTimeScale = requestedTimeScale;
			currentPhysicsWarp = requestedPhysicsWarp;
			
			SendPacketToAll(new Packet_TimeWarpResult()
			{
				VoteId = currentVoteId,
				Approved = true,
				TimeScale = requestedTimeScale,
				PhysicsWarp = requestedPhysicsWarp,
			});
			
			Logger.Info($"Time warp to {requestedTimeScale}x approved (single player, {(requestedPhysicsWarp ? "Physics" : "WorldTime")})");
			return;
		}

		// 向所有其他控制火箭的玩家发送投票请求
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

		// 记录投票
		currentVotes[voter.id] = packet.Agreed;
		
		Logger.Info($"{voter.username} voted: {(packet.Agreed ? "Agree" : "Reject")}");

		// 如果有人拒绝，广播给所有玩家
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
				// 所有人都同意，广播开始时间加速
				isTimeWarping = true;
				currentTimeScale = requestedTimeScale;
				currentPhysicsWarp = requestedPhysicsWarp;
				
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

		static readonly System.Random colorRandom = new System.Random();
		static Color GetRandomColor()
		{
			float rand = (float) Math.Round(100 * colorRandom.NextDouble());
			return Color.HSVToRGB(rand / 100, 1, 1);
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
}