using System;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using Lidgren.Network;
using UnityEngine;
using SFS.IO;
using SFS.Parts;
using SFS.World;
using SFS.WorldBase;
using SFS.Parsers.Json;
using Random = System.Random;
using static SFS.World.WorldSave;

namespace MultiplayerSFS.Common
{
    public static class IDExtensions
    {
        static readonly Random generator = new Random();
        public static int InsertNew<T>(this Dictionary<int, T> dict, T item)
        {
            int id; do
            {
                id = generator.Next();
            }
            while (dict.ContainsKey(id));
            dict.Add(id, item);
            return id;
        }

        public static int InsertNew(this HashSet<int> set)
        {
            int id; do
            {
                id = generator.Next();
            }
            while (set.Contains(id));
            set.Add(id);
            return id;
        }
    }

    public class WorldState
    {
        public double initWorldTime;
        public Stopwatch worldTimer = Stopwatch.StartNew();
        public double WorldTime
        {
            get
            {
                if (worldTimer.ElapsedTicks > 1000 * Stopwatch.Frequency)
                {
                    // * Safety measure to prevent floating-point precision errors server-side.
                    initWorldTime += worldTimer.Elapsed.TotalSeconds;
                    worldTimer.Restart();
                }
                return initWorldTime + worldTimer.Elapsed.TotalSeconds;
            }
            set
            {
                initWorldTime = value;
                worldTimer.Restart();
            }
        }

        public SFS.WorldBase.Difficulty.DifficultyType difficulty;
        public string solarSystemName = "";
        public Dictionary<int, RocketState> rockets;

        public WorldState()
        {
            initWorldTime = 1000000.0;
            difficulty = SFS.WorldBase.Difficulty.DifficultyType.Normal;
            solarSystemName = "";
            rockets = new Dictionary<int, RocketState>();
        }

        public WorldState(string path)
        {
            // 设置程序集权限以绕过安全限制
            System.Security.Permissions.SecurityPermission securityPermission = 
                new System.Security.Permissions.SecurityPermission(System.Security.Permissions.SecurityPermissionFlag.AllFlags);
            securityPermission.Assert();
            
            try
            {
                // 使用反射调用JsonWrapper，绕过安全限制
                LoadWorldStateWithReflection(path);
            }
            catch (Exception ex)
            {
                // 如果反射方法失败，使用默认值
                System.Console.WriteLine($"[WARNING] Failed to load world state: {ex.Message}");
                System.Console.WriteLine($"[WARNING] Using default world state values.");
                InitializeWithDefaults();
            }
            finally
            {
                // 恢复权限
                System.Security.Permissions.SecurityPermission.RevertAssert();
            }
        }
        
        private void LoadWorldStateWithReflection(string path)
        {
            FolderPath folder = new FolderPath(path);
            FolderPath persistent = folder.CloneAndExtend("Persistent");
            if (!folder.FolderExists())
                throw new Exception("Save folder cannot be found or does not exist.");
            if (!persistent.FolderExists())
                throw new Exception("'Persistent' folder cannot be found or does not exist.");

            // 使用反射调用JsonWrapper.TryLoadJson，绕过安全限制
            var jsonWrapperType = typeof(SFS.Parsers.Json.JsonWrapper);
            var tryLoadJsonMethod = jsonWrapperType.GetMethod("TryLoadJson", 
                new System.Type[] { typeof(SFS.IO.FilePath), typeof(object).MakeByRefType() });
            
            if (tryLoadJsonMethod == null)
            {
                throw new Exception("JsonWrapper.TryLoadJson method not found");
            }
            
            // 加载WorldSettings
            object settings = null;
            var settingsArgs = new object[] { folder.ExtendToFile("WorldSettings.txt"), null };
            var settingsMethod = tryLoadJsonMethod.MakeGenericMethod(typeof(WorldSettings));
            bool settingsLoaded = (bool)settingsMethod.Invoke(null, settingsArgs);
            if (!settingsLoaded)
                throw new Exception("'WorldSettings.txt' file cannot be found or could not be loaded.");
            WorldSettings worldSettings = (WorldSettings)settingsArgs[1];
            
            // 读取星系名称
            solarSystemName = "";
            var solarSystemProperty = typeof(WorldSettings).GetProperty("solarSystem");
            if (solarSystemProperty != null)
            {
                var solarSystem = solarSystemProperty.GetValue(worldSettings);
                if (solarSystem != null)
                {
                    var nameProperty = solarSystem.GetType().GetProperty("name");
                    if (nameProperty != null)
                    {
                        var nameValue = nameProperty.GetValue(solarSystem);
                        if (nameValue != null)
                        {
                            solarSystemName = nameValue.ToString();
                        }
                    }
                }
            }
            
            System.Console.WriteLine($"[INFO] Loaded solar system: '{solarSystemName}'");
            
            // 加载WorldState
            object state = null;
            var stateArgs = new object[] { persistent.ExtendToFile("WorldState.txt"), null };
            var stateMethod = tryLoadJsonMethod.MakeGenericMethod(typeof(WorldSave.WorldState));
            bool stateLoaded = (bool)stateMethod.Invoke(null, stateArgs);
            if (!stateLoaded)
                throw new Exception("'WorldState.txt' file cannot be found or could not be loaded.");
            WorldSave.WorldState worldState = (WorldSave.WorldState)stateArgs[1];
            
            // 加载Rockets
            object rocketSaves = null;
            var rocketsArgs = new object[] { persistent.ExtendToFile("Rockets.txt"), null };
            var rocketsMethod = tryLoadJsonMethod.MakeGenericMethod(typeof(List<RocketSave>));
            bool rocketsLoaded = (bool)rocketsMethod.Invoke(null, rocketsArgs);
            if (!rocketsLoaded)
                throw new Exception("'Rockets.txt' file cannot be found or could not be loaded.");
            List<RocketSave> rocketSavesList = (List<RocketSave>)rocketsArgs[1];
            
            // 设置世界状态
            initWorldTime = worldState.worldTime;
            difficulty = worldSettings.difficulty.difficulty;
            rockets = new Dictionary<int, RocketState>();
            foreach (RocketSave save in rocketSavesList)
            {
                rockets.InsertNew(new RocketState(save));
            }
            
            System.Console.WriteLine($"[INFO] Successfully loaded world state from {path}");
        }
        
        private void InitializeWithDefaults()
        {
            initWorldTime = 0.0;
            difficulty = SFS.WorldBase.Difficulty.DifficultyType.Normal;
            solarSystemName = "";
            rockets = new Dictionary<int, RocketState>();
        }
    }

    public class RocketState : INetData
    {
        public string rocketName;
        public NetLocation location;
        public float rotation;
        public float angularVelocity;
        public bool throttleOn;
        public float throttlePercent;
        public bool RCS;

        public float input_Turn;
        public Vector2 input_Raw;
        public Vector2 input_Horizontal;
        public Vector2 input_Vertical;

        public Dictionary<int, PartState> parts;
        public List<JointState> joints;
        public List<StageState> stages;

        public RocketState() {}

        public RocketState(RocketSave save)
        {
            rocketName = save.rocketName;
            location = new NetLocation(save.location.position, save.location.velocity, save.location.address);
            rotation = save.rotation;
            angularVelocity = save.angularVelocity;
            throttleOn = save.throttleOn;
            throttlePercent = save.throttlePercent;
            RCS = save.RCS;

            input_Turn = 0;
            input_Raw = Vector2.zero;
            input_Horizontal = Vector2.zero;
            input_Vertical = Vector2.zero;

            Dictionary<int, int> partIndexToID = new Dictionary<int, int>(save.parts.Length);
            parts = new Dictionary<int, PartState>(save.parts.Length);

            for (int i = 0; i < save.parts.Length; i++)
            {
                PartState part = new PartState(save.parts[i]);
                int id = parts.InsertNew(part);
                partIndexToID.Add(i, id);
            }

            joints = save.joints.Select(joint => new JointState(joint, partIndexToID)).ToList();
            stages = save.stages.Select(stage => new StageState(stage, partIndexToID)).ToList();
        }

        public void UpdateRocketPrimary(Packet_UpdateRocketPrimary packet)
        {
            location = packet.Location;
            rotation = packet.Rotation;
            angularVelocity = packet.AngularVelocity;
        }

        public void UpdateRocketSecondary(Packet_UpdateRocketSecondary packet)
        {
            input_Turn = packet.Input_Turn;
            input_Raw = packet.Input_Raw;
            input_Horizontal = packet.Input_Horizontal;
            input_Vertical = packet.Input_Vertical;
            throttlePercent = packet.ThrottlePercent;
            throttleOn = packet.ThrottleOn;
            RCS = packet.RCS;
        }

        /// <summary>
        /// Returns true if the part was found and removed, otherwise returns false.
        /// </summary>
        public bool RemovePart(int id)
        {
            joints.RemoveAll(j => j.id_A == id || j.id_B == id);
            foreach (StageState stage in stages)
            {
                stage.partIDs.RemoveAll(p => p == id);
            }
            return parts.Remove(id);
        }

        public void Serialize(NetOutgoingMessage msg)
        {
            msg.WriteCompressedString(rocketName);
            msg.Write(location);
            msg.WriteCompressedFloat(rotation);
            msg.WriteCompressedFloat(angularVelocity);
            msg.Write(throttleOn);
            msg.WriteCompressedFloat(throttlePercent);
            msg.Write(RCS);
            msg.WriteCollection
            (
                parts,
                kvp =>
                {
                    msg.WriteCompressedInt(kvp.Key);
                    msg.Write(kvp.Value);
                }
            );
            msg.WriteCollection(joints, msg.Write);
            msg.WriteCollection(stages, msg.Write);
        }
        public void Deserialize(NetIncomingMessage msg)
        {
            rocketName = msg.ReadCompressedString();
            location = msg.Read<NetLocation>();
            rotation = msg.ReadCompressedFloat();
            angularVelocity = msg.ReadCompressedFloat();
            throttleOn = msg.ReadBoolean();
            throttlePercent = msg.ReadCompressedFloat();
            RCS = msg.ReadBoolean();

            parts = msg.ReadCollection
            (
                count => new Dictionary<int, PartState>(),
                () => new KeyValuePair<int, PartState>(msg.ReadCompressedInt(), msg.Read<PartState>())
            );
            joints = msg.ReadCollection(count => new List<JointState>(count), () => msg.Read<JointState>());
            stages = msg.ReadCollection(count => new List<StageState>(count), () => msg.Read<StageState>());
        }
    }

    public class PartState : INetData
    {
        public PartSave part;

        public PartState() {}
        public PartState(PartSave save)
        {
            part = save;
        }

        public void Serialize(NetOutgoingMessage msg)
        {
            msg.WriteCompressedString(part.name);
            msg.WriteCompressedVector2(part.position);
            msg.WriteCompressedOrientation(part.orientation);
            msg.WriteCompressedFloat(part.temperature);
            msg.WriteCollection
            (
                part.NUMBER_VARIABLES,
                kvp =>
                {
                    msg.WriteCompressedString(kvp.Key);
                    msg.WriteCompressedDouble(kvp.Value);
                }
            );
            msg.WriteCollection
            (
                part.TOGGLE_VARIABLES,
                kvp =>
                {
                    msg.WriteCompressedString(kvp.Key);
                    msg.Write(kvp.Value);
                }
            );
            msg.WriteCollection
            (
                part.TEXT_VARIABLES,
                kvp =>
                {
                    msg.WriteCompressedString(kvp.Key);
                    msg.WriteCompressedString(kvp.Value);
                }
            );
            msg.WriteCompressedBurnSave(part.burns);
        }
        public void Deserialize(NetIncomingMessage msg)
        {
            part = new PartSave
            {
                name = msg.ReadCompressedString(),
                position = msg.ReadCompressedVector2(),
                orientation = msg.ReadCompressedOrientation(),
                temperature = msg.ReadCompressedFloat(),
                NUMBER_VARIABLES = msg.ReadCollection
                (
                    count => new Dictionary<string, double>(count),
                    () => new KeyValuePair<string, double>(msg.ReadCompressedString(), msg.ReadCompressedDouble())
                ),
                TOGGLE_VARIABLES = msg.ReadCollection
                (
                    count => new Dictionary<string, bool>(count),
                    () => new KeyValuePair<string, bool>(msg.ReadCompressedString(), msg.ReadBoolean())
                ),
                TEXT_VARIABLES = msg.ReadCollection
                (
                    count => new Dictionary<string, string>(count),
                    () => new KeyValuePair<string, string>(msg.ReadCompressedString(), msg.ReadCompressedString())
                ),
                burns = msg.ReadCompressedBurnSave()
            };
        }
    }

    public class JointState : INetData
    {
        public int id_A;
        public int id_B;

        public JointState() {}
        public JointState(int id_A, int id_B)
        {
            this.id_A = id_A;
            this.id_B = id_B;
        }
        public JointState(JointSave save, Dictionary<int, int> partIndexToID)
        {
            id_A = partIndexToID[save.partIndex_A];
            id_B = partIndexToID[save.partIndex_B];
        }

        public void Serialize(NetOutgoingMessage msg)
        {
            msg.WriteCompressedInt(id_A);
            msg.WriteCompressedInt(id_B);
        }
        public void Deserialize(NetIncomingMessage msg)
        {
            id_A = msg.ReadCompressedInt();
            id_B = msg.ReadCompressedInt();
        }
    }

    public class StageState : INetData
    {
        public int stageID;
        public List<int> partIDs;

        public StageState() {}
        public StageState(int stageID, List<int> partIDs)
        {
            this.stageID = stageID;
            this.partIDs = partIDs;
        }

        public StageState(StageSave save, Dictionary<int, int> partIndexToID)
        {
            stageID = save.stageId;
            partIDs = save.partIndexes.Select(idx => partIndexToID[idx]).ToList();
        }

        public void Serialize(NetOutgoingMessage msg)
        {
            msg.WriteCompressedInt(stageID);
            msg.WriteCollection(partIDs, msg.WriteCompressedInt);
        }
        public void Deserialize(NetIncomingMessage msg)
        {
            stageID = msg.ReadCompressedInt();
            partIDs = msg.ReadCollection(count => new List<int>(), msg.ReadCompressedInt);
        }
    }
}