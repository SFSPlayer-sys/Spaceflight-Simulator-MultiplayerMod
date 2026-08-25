using System;
using System.Linq;
using System.Timers;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using SFS;
using SFS.UI;
using SFS.Parts;
using SFS.World;
using SFS.WorldBase;
using SFS.Variables;
using SFS.Parts.Modules;
using ModLoader.Helpers;
using MultiplayerSFS.Common;
using Object = UnityEngine.Object;

namespace MultiplayerSFS.Mod
{
    public static class LocalManager
    {
        /// <summary>
        /// Players that are currently connected to the server, including the local player.
        /// </summary>
        public static Dictionary<int, LocalPlayer> players;
        /// <summary>
        /// This client's `LocalPlayer` instance.
        /// </summary>
        public static LocalPlayer Player
        {
            get
            {
                if (players.TryGetValue(ClientManager.playerId, out LocalPlayer player))
                    return player;
                else
                    return null;
            }
        }

        /// <summary>
        /// Rockets that are synced with the server and are visible to all other players.
        /// </summary>
        public static Dictionary<int, LocalRocket> syncedRockets;
        /// <summary>
        /// Rockets that have been created locally, and are awaiting synchronisation with the server.
        /// </summary>
        public static Dictionary<int, LocalRocket> unsyncedRockets;
        /// <summary>
        /// The local ids of the rockets that this player should be controlling after their launched rockets has been synced with the server.
        /// </summary>
        public static HashSet<int> updateAuthority;
        /// <summary>
        /// Local id of the rocket that this player will switch to after it has been synced with the server.
        /// </summary>
        public static int unsyncedToControl = -1;

        /// <summary>
        /// A custom `DestructionReason` used to indicate that a part or rocket's destruction was requested by the multiplayer mod (usually as a result of a packet from the server).
        /// </summary>
        public const DestructionReason CustomDestructionReason = (DestructionReason) 4;
        /// <summary>
        /// The true reason for a part or rocket's destruction when `RocketManager.DestroyRocket` or `Part.DestroyPart` is called in multiplayer using `LocalManager.CustomDestructionReason`.
        /// This is set before those methods are called so that their related patches in `WorldEventSyncing` can correctly pass the true reason on.
        /// </summary>
        public static DestructionReason TrueDestructionReason = DestructionReason.Intentional;

        /// <summary>
        /// The rate (in milliseconds) at which `UpdateRocket` packets will be sent to the server.
        /// </summary>
        public static double updateRocketsPeriod = 5;
        static Timer updateTimer;
        /// <summary>
        /// `prevResourcePercents` is used to determine when `UpdatePart_ResourceModule` packets should be sent.
        /// </summary>
        static readonly Dictionary<ResourceModule, double> prevResourcePercents = new Dictionary<ResourceModule, double>();

        static Rocket RocketPrefab => AccessTools.StaticFieldRefAccess<Rocket>(typeof(RocketManager), "prefab");

        private static System.Collections.Queue mainThreadActions = new System.Collections.Queue();
        private static object mainThreadActionsLock = new object();

        public static void Initialize()
        {
            if (updateTimer == null)
            {
                players = new Dictionary<int, LocalPlayer>();
                syncedRockets = new Dictionary<int, LocalRocket>();
                unsyncedRockets = new Dictionary<int, LocalRocket>();
                updateAuthority = new HashSet<int>();
                unsyncedToControl = -1;

                updateTimer = new Timer()
                {
                    Interval = updateRocketsPeriod,
                    AutoReset = true,
                    Enabled = true,
                };
                updateTimer.Elapsed += (source, e) => 
                {
                    // 将任务添加到主线程执行队列
                    lock (mainThreadActionsLock)
                    {
                        mainThreadActions.Enqueue(new System.Action(() => SendUpdatePackets()));
                    }
                };
                SceneHelper.OnHomeSceneLoaded += DisableUpdateTimer;
            }
        }

        // 在主线程执行队列中的任务
        public static void Update()
        {
            // 本地连续推进世界时间，避免星球只在服务器同步时跳变
            if (WorldTime.main != null)
            {
                double increment = WorldTime.main.realtimePhysics.Value
                    ? Time.deltaTime
                    : WorldTime.main.timewarpSpeed * Time.deltaTime;
                ClientManager.world.WorldTime += increment;
            }
            while (true)
            {
                System.Action action = null;
                lock (mainThreadActionsLock)
                {
                    if (mainThreadActions.Count > 0)
                    {
                        action = (System.Action)mainThreadActions.Dequeue();
                    }
                    else
                    {
                        break;
                    }
                }
                action?.Invoke();
            }
        }

        public static void DisableUpdateTimer()
        {
            updateTimer?.Close();
            SceneHelper.OnHomeSceneLoaded -= DisableUpdateTimer;
        }

        public static void SendUpdatePackets()
        {
            foreach (var module in prevResourcePercents.Keys.ToList())
            {
                if (module == null)
                {
                    prevResourcePercents.Remove(module);
                }
            }
            foreach (int id in updateAuthority.ToList())
            {
                if (syncedRockets.TryGetValue(id, out LocalRocket localRocket) && localRocket.rocket is Rocket rocket && rocket.rb2d != null)
                {
                    Packet_UpdateRocketPrimary primary = rocket.ToUpdatePacketPrimary(id);
                    Packet_UpdateRocketSecondary secondary = rocket.ToUpdatePacketSecondary(id);
                    if (ClientManager.world.rockets.TryGetValue(id, out RocketState state))
                    {
                        state.UpdateRocketPrimary(primary);
                        state.UpdateRocketSecondary(secondary);
                    }
                    else
                    {
                        Debug.LogError("Missing rocket state while trying to send update packets!");
                    }
                    ClientManager.SendPacket(primary);
                    ClientManager.SendPacket(secondary);

                    // * `ResourceModule` updates
                    if (rocket.physics.PhysicsMode)
                    {
                        foreach (ResourceModule module in rocket.resources.localGroups)
                        {
                            if (prevResourcePercents.TryGetValue(module, out double prevPercent))
                            {
                                if (prevPercent != module.resourcePercent.Value)
                                {
                                    ClientManager.SendPacket
                                    (
                                        new Packet_UpdatePart_ResourceModule()
                                        {
                                            WorldTime = ClientManager.world.WorldTime,
                                            RocketId = id,
                                            PartIds = module.children
                                                .Select(r => r.GetComponentInParent<Part>())
                                                .Select(p => localRocket.GetPartID(p))
                                                .ToHashSet(),
                                            ResourcePercent = module.resourcePercent.Value,
                                        }
                                    );
                                }
                            }
                            prevResourcePercents[module] = module.resourcePercent.Value;
                        }
                    }
                }
                else
                {
                    syncedRockets.Remove(id);
                    updateAuthority.Remove(id);
                }
            }
        }

        static Dictionary<Rocket, int> rocketToSyncedID = new Dictionary<Rocket, int>();
        static Dictionary<Rocket, int> rocketToUnsyncedID = new Dictionary<Rocket, int>();

        /// <summary>
        /// Returns the id of the provided synced rocket, otherwise returns -1 if not found.
        /// </summary>
        public static int GetSyncedRocketID(Rocket rocket)
        {
            if (rocket == null)
                return -1;
            if (rocketToSyncedID.TryGetValue(rocket, out int id) && syncedRockets.TryGetValue(id, out LocalRocket lr) && lr.rocket == rocket)
                return id;
            rocketToSyncedID.Clear();
            foreach (KeyValuePair<int, LocalRocket> kvp in syncedRockets)
            {
                if (kvp.Value.rocket != null)
                    rocketToSyncedID[kvp.Value.rocket] = kvp.Key;
            }
            return rocketToSyncedID.TryGetValue(rocket, out id) ? id : -1;
        }

        /// <summary>
        /// Returns the id of the provided unsynced rocket, otherwise returns -1 if not found.
        /// </summary>
        public static int GetUnsyncedRocketID(Rocket rocket)
        {
            if (rocket == null)
                return -1;
            if (rocketToUnsyncedID.TryGetValue(rocket, out int id) && unsyncedRockets.TryGetValue(id, out LocalRocket lr) && lr.rocket == rocket)
                return id;
            rocketToUnsyncedID.Clear();
            foreach (KeyValuePair<int, LocalRocket> kvp in unsyncedRockets)
            {
                if (kvp.Value.rocket != null)
                    rocketToUnsyncedID[kvp.Value.rocket] = kvp.Key;
            }
            return rocketToUnsyncedID.TryGetValue(rocket, out id) ? id : -1;
        }

        /// <summary>
        /// Returns the id of the provided part on the rocket with the provided id, otherwise returns -1 if not found.
        /// </summary>
        public static int GetLocalPartID(int rocketId, Part part)
        {
            if (syncedRockets.TryGetValue(rocketId, out LocalRocket lr))
                return lr.GetPartID(part);
            return -1;
        }

        public static Packet_UpdateRocketPrimary ToUpdatePacketPrimary(this Rocket rocket, int id)
        {
            return new Packet_UpdateRocketPrimary()
            {
                WorldTime = ClientManager.world.WorldTime,
                RocketId = id,
                Location = rocket.location.Value.ToNetLocation(),
                Rotation = rocket.rb2d.transform.eulerAngles.z,
                AngularVelocity = rocket.rb2d.angularVelocity,
            };
        }

        public static Packet_UpdateRocketSecondary ToUpdatePacketSecondary(this Rocket rocket, int id)
        {
            return new Packet_UpdateRocketSecondary()
            {
                WorldTime = ClientManager.world.WorldTime,
                RocketId = id,
                Input_Turn = rocket.arrowkeys.turnAxis,
                Input_Raw = rocket.arrowkeys.rawArrowkeysAxis,
                Input_Horizontal = rocket.arrowkeys.horizontalAxis,
                Input_Vertical = rocket.arrowkeys.verticalAxis,
                ThrottlePercent = rocket.throttle.throttlePercent,
                ThrottleOn = rocket.throttle.throttleOn,
                RCS = rocket.arrowkeys.rcs,
            };
        }

        // ? Similar to `SFS.World.RocketManager.LoadRocket(RocketSave, ...)`.
        public static LocalRocket SpawnLocalRocket(RocketState state)
        {
            Rocket rocket = Object.Instantiate(RocketPrefab);
            rocket.rocketName = state.rocketName;
            rocket.throttle.throttleOn.Value = state.throttleOn;
            rocket.throttle.throttlePercent.Value = state.throttlePercent;
            rocket.arrowkeys.rcs.Value = state.RCS;

            Dictionary<int, Part> parts = new Dictionary<int, Part>(state.parts.Count);
            foreach (KeyValuePair<int, PartState> kvp in state.parts)
            {
                Part part = PartsLoader.CreatePart(kvp.Value.part, null, null, OnPartNotOwned.Allow, out _);
                parts.Add(kvp.Key, part);
            }

            List<PartJoint> joints = new List<PartJoint>(state.joints.Count);
            foreach (JointState joint in state.joints)
            {
                if (joint.id_A == -1 || joint.id_B == -1 || !parts.ContainsKey(joint.id_A) || !parts.ContainsKey(joint.id_B))
                {
                    continue;
                }
                Part part_A = parts[joint.id_A];
                Part part_B = parts[joint.id_B];
                joints.Add(new PartJoint(part_A, part_B, part_B.Position - part_A.Position));
            }
            rocket.SetJointGroup(new JointGroup(joints, parts.Values.ToList()));

            rocket.rb2d.transform.eulerAngles = new Vector3(0f, 0f, state.rotation);

            rocket.physics.SetLocationAndState(state.location.ToVanillaLocation(), true);
            rocket.rb2d.angularVelocity = state.angularVelocity;

            foreach (StageState stage in state.stages)
            {
                List<Part> stageParts = stage.partIDs.Where(parts.ContainsKey).Select(id => parts[id]).ToList();
                rocket.staging.InsertStage(new Stage(stage.stageID, stageParts), false);
            }
            foreach (KeyValuePair<int, PartState> kvp in state.parts)
            {
                Part part = parts[kvp.Key];
                if (kvp.Value.part.TOGGLE_VARIABLES.TryGetValue("engine_on", out bool engineOn))
                {
                    foreach (var engine in part.GetModules<EngineModule>())
                    {
                        engine.engineOn.Value = engineOn;
                    }
                }
                if (kvp.Value.part.TOGGLE_VARIABLES.TryGetValue("wheel_on", out bool wheelOn))
                {
                    foreach (var wheel in part.GetModules<WheelModule>())
                    {
                        wheel.on.Value = wheelOn;
                    }
                }
            }

            return new LocalRocket(rocket, parts);
        }

        public static void DestroyLocalRocket(int id)
        {
            if (syncedRockets.TryGetValue(id, out LocalRocket rocket) && rocket.rocket != null)
            {
                TrueDestructionReason = DestructionReason.Intentional;
                RocketManager.DestroyRocket(rocket.rocket, CustomDestructionReason);
            }
            syncedRockets.Remove(id);
            updateAuthority.Remove(id);
        }

        public static void OnLoadWorld()
        {
            unsyncedRockets.Clear();
            foreach (KeyValuePair<int, RocketState> kvp in ClientManager.world.rockets)
            {
                DestroyLocalRocket(kvp.Key);
                LocalRocket rocket = SpawnLocalRocket(kvp.Value);
                syncedRockets.Add(kvp.Key, rocket);
            }
        }

        public static Location ToVanillaLocation(this NetLocation loc)
        {
            if (string.IsNullOrEmpty(loc.address))
                return new Location(Base.planetLoader.spaceCenter.Planet, loc.position, loc.velocity);
            return new Location(loc.address.GetPlanet(), loc.position, loc.velocity);
        }

        public static NetLocation ToNetLocation(this Location loc)
        {
            return new NetLocation
            (
                loc.position,
                loc.velocity,
                loc.planet.codeName
            );
        }

        public static void OnPacket_CreateRocket(Packet_CreateRocket packet)
        {
            // 检查是否是已存在的火箭
            if (syncedRockets.TryGetValue(packet.GlobalId, out LocalRocket existingRocket))
            {
                // 销毁旧火箭并重新创建
                DestroyLocalRocket(packet.GlobalId);
                
                // 使用新状态重新创建火箭
                LocalRocket synced = SpawnLocalRocket(packet.Rocket);
                syncedRockets.Add(packet.GlobalId, synced);
                
                // 设置新创建标志
                if (synced.interpolator != null)
                {
                    synced.interpolator.updateBuffer.Clear();
                    synced.interpolator.packetBuffer.Clear();
                    synced.interpolator.currentUpdate = synced.rocket.ToUpdatePacketPrimary(packet.GlobalId);
                    synced.interpolator.isNewlyCreated = true;
                }
                
                if (Player.controlledRocket == packet.GlobalId)
                {
                    PlayerController.main.player.Value = synced.rocket;
                }
                return;
            }
            
            DestroyLocalRocket(packet.GlobalId);
            if (unsyncedRockets.TryGetValue(packet.LocalId, out LocalRocket unsynced))
            {
                unsyncedRockets.Remove(packet.LocalId);
                syncedRockets.Add(packet.GlobalId, unsynced);

                if (packet.LocalId == unsyncedToControl)
                {
                    unsyncedToControl = -1;
                    PlayerController.main.SmoothChangePlayer(unsynced.rocket);
                    GameCamerasManager.main.InstantlyRotateCamera();
                    // * This loading screen is opened in the `SceneLoader_LoadWorldScene` patch.
                    // TODO: idk if the player can even see this loading screen, but oh well...
                    Menu.loading.Close();
                }
            }
            else if (GameManager.main != null)
            {
                LocalRocket synced = SpawnLocalRocket(packet.Rocket);
                syncedRockets.Add(packet.GlobalId, synced);
                
                // 新创建的火箭立即清除插值缓冲区并设置精确位置
                if (synced.interpolator != null)
                {
                    synced.interpolator.updateBuffer.Clear();
                    synced.interpolator.packetBuffer.Clear();
                    synced.interpolator.currentUpdate = synced.rocket.ToUpdatePacketPrimary(packet.GlobalId);
                    synced.interpolator.isNewlyCreated = true;
                    synced.rocket.physics.SetLocationAndState(packet.Rocket.location.ToVanillaLocation(), true);
                    synced.rocket.rb2d.transform.eulerAngles = new Vector3(0f, 0f, packet.Rocket.rotation);
                    synced.rocket.rb2d.angularVelocity = packet.Rocket.angularVelocity;
                }
                
                if (Player.controlledRocket == packet.GlobalId)
                {
                    // * Complete resync was sent for this client's rocket.
                    PlayerController.main.player.Value = synced.rocket;
                }
            }
        }
    }

    public class LocalRocket
    {
        public Rocket rocket;
        public Dictionary<int, Part> parts;
        public Interpolator interpolator;
        Dictionary<Part, int> partIDs = new Dictionary<Part, int>();

        public LocalRocket(Rocket rocket)
        {
            this.rocket = rocket;
            parts = new Dictionary<int, Part>(rocket.partHolder.partsSet.Count);
            foreach (Part part in rocket.partHolder.partsSet)
            {
                parts.InsertNew(part);
            }
            interpolator = rocket.gameObject.AddComponent<Interpolator>();
        }

        public LocalRocket(Rocket rocket, Dictionary<int, Part> parts)
        {
            this.rocket = rocket;
            this.parts = parts;
            interpolator = rocket.gameObject.AddComponent<Interpolator>();
        }

        public RocketState ToState()
        {
            return new RocketState()
            {
                rocketName = rocket.rocketName,
                location = rocket.location.Value.ToNetLocation(),
                rotation = rocket.rb2d.transform.eulerAngles.z,
                angularVelocity = rocket.rb2d.angularVelocity,
                throttleOn = rocket.throttle.throttleOn,
                throttlePercent = rocket.throttle.throttlePercent,
                RCS = rocket.arrowkeys.rcs,

                input_Turn = rocket.arrowkeys.turnAxis,
                input_Raw = rocket.arrowkeys.rawArrowkeysAxis,
                input_Horizontal = rocket.arrowkeys.horizontalAxis,
                input_Vertical = rocket.arrowkeys.verticalAxis,

                parts = parts
                    .Where(kvp => kvp.Value != null)
                    .ToDictionary(kvp => kvp.Key, kvp => new PartState(new PartSave(kvp.Value))),
                joints = rocket.jointsGroup.joints
                    .Where(pj => pj.a != null && pj.b != null)
                    .Select(pj => new JointState(GetPartID(pj.a), GetPartID(pj.b)))
                    .ToList(),
                stages = rocket.staging.stages
                    .Select(s => new StageState(s.stageId, s.parts.Where(p => p != null).Select(GetPartID).ToList()))
                    .ToList(),
            };
        }

        /// <summary>
        /// 性能优化，获取本地零件ID
        /// </summary>
        public int GetPartID(Part part)
        {
            if (partIDs.TryGetValue(part, out int id))
                return id;
            if (partIDs.Count != parts.Count)
            {
                partIDs.Clear();
                foreach (KeyValuePair<int, Part> kvp in parts)
                {
                    partIDs[kvp.Value] = kvp.Key;
                }
                return partIDs.TryGetValue(part, out id) ? id : -1;
            }
            return -1;
        }
    }

    public class LocalPlayer
    {
        public string username;
        /// <summary>
        /// Id of the rocket currently controlled by this player, or -1 if they currently aren't controlling a rocket.
        /// </summary>
        public Int_Local controlledRocket;

        /// <summary>
        /// Color used to distinguish this player from other rockets in map view. Also used to color their username in the chat window.
        /// </summary>
        public Color iconColor;

        public LocalPlayer(string username, Color color)
        {
            this.username = username;
            controlledRocket = new Int_Local() { Value = -1 };
            controlledRocket.OnChange += OnControlledRocketChange;
            iconColor = color;
        }

        public void OnControlledRocketChange(int oldId, int newId)
        {
            // TODO: Name tags above controlled rockets.
        }
    }
}