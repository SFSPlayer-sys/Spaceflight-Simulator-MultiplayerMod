using System;
using System.Diagnostics;
using System.Collections.Generic;
using Lidgren.Network;

namespace MultiplayerSFS.ServerCommon
{
    public static class IDExtensions
    {
        static readonly Random generator = new Random();
        public static int InsertNew<T>(this Dictionary<int, T> dict, T item)
        {
            int id;
            do
            {
                id = generator.Next();
            }
            while (dict.ContainsKey(id));
            dict.Add(id, item);
            return id;
        }

        public static int InsertNew(this HashSet<int> set)
        {
            int id;
            do
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

        public int difficulty;
        public string solarSystemName = "";
        public Dictionary<int, RocketState> rockets;
        
        // Cheat settings
        public bool infiniteFuel;
        public bool noAtmosphericDrag;
        public bool unbreakableParts;
        public bool noGravity;
        public bool noHeatDamage;
        public bool noBurnMarks;
        public bool infiniteBuildArea;
        public bool partClipping;

        public WorldState()
        {
            InitializeWithDefaults();
        }

        public WorldState(string path)
        {
            try
            {
                // Try to load world state from file
                LoadWorldStateFromFile(path);
                Console.WriteLine($"[INFO] World state loaded from {path}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARNING] Failed to load world state: {ex.Message}");
                Console.WriteLine($"[WARNING] Using default world state values.");
                InitializeWithDefaults();
            }
        }

        private void LoadWorldStateFromFile(string path)
        {
            // 设置默认值
            InitializeWithDefaults();
            
            try
            {
                // 读取WorldSettings.txt文件
                string settingsPath = System.IO.Path.Combine(path, "WorldSettings.txt");
                if (!System.IO.File.Exists(settingsPath))
                {
                    Console.WriteLine($"[WARNING] WorldSettings.txt not found at: {settingsPath}");
                    return;
                }
                
                string jsonContent = System.IO.File.ReadAllText(settingsPath);
                Console.WriteLine($"[INFO] Reading WorldSettings.txt from: {settingsPath}");
                
                // 解析JSON获取星系名称
                solarSystemName = ExtractSolarSystemNameFromJson(jsonContent);
                Console.WriteLine($"[INFO] Loaded solar system: '{solarSystemName}'");
                
                // 这里可以添加更多的世界状态加载逻辑
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to load world settings: {ex.Message}");
                throw;
            }
        }
        
        private string ExtractSolarSystemNameFromJson(string json)
        {
            try
            {
                // 简单的JSON解析，查找solarSystem.name字段
                int solarSystemIndex = json.IndexOf("\"solarSystem\"");
                if (solarSystemIndex == -1)
                {
                    Console.WriteLine("[WARNING] 'solarSystem' field not found in WorldSettings.txt");
                    return "";
                }
                
                // 查找solarSystem对象
                int objectStart = json.IndexOf("{", solarSystemIndex);
                if (objectStart == -1)
                {
                    Console.WriteLine("[WARNING] Could not find solarSystem object");
                    return "";
                }
                
                // 查找name字段
                int nameIndex = json.IndexOf("\"name\"", objectStart);
                if (nameIndex == -1)
                {
                    Console.WriteLine("[WARNING] 'name' field not found in solarSystem object");
                    return "";
                }
                
                // 提取name值
                int valueStart = json.IndexOf("\"", nameIndex + 6);
                if (valueStart == -1)
                {
                    Console.WriteLine("[WARNING] Could not find name value start");
                    return "";
                }
                
                int valueEnd = json.IndexOf("\"", valueStart + 1);
                if (valueEnd == -1)
                {
                    Console.WriteLine("[WARNING] Could not find name value end");
                    return "";
                }
                
                string name = json.Substring(valueStart + 1, valueEnd - valueStart - 1);
                return name;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to parse solar system name: {ex.Message}");
                return "";
            }
        }

        private void InitializeWithDefaults()
        {
            initWorldTime = 1000000.0;
            difficulty = 1; // Normal difficulty
            solarSystemName = "";
            rockets = new Dictionary<int, RocketState>();
            
            // Default cheat settings
            infiniteFuel = false;
            noAtmosphericDrag = false;
            unbreakableParts = false;
            noGravity = false;
            noHeatDamage = false;
            noBurnMarks = false;
            infiniteBuildArea = true;
            partClipping = true;
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

        public RocketState()
        {
            parts = new Dictionary<int, PartState>();
            joints = new List<JointState>();
            stages = new List<StageState>();
            location = new NetLocation();
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

        public bool RemovePart(int id)
        {
            joints.RemoveAll(j => j.id_A == id || j.id_B == id);
            foreach (StageState stage in stages)
            {
                stage.partIDs.RemoveAll(p => p == id);
            }
            return parts.Remove(id);
        }
    }

    public class PartState : INetData
    {
        public PartSave part;

        public PartState() {}

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

    public class PartSave : INetData
    {
        public string name;
        public Vector2 position;
        public Orientation orientation;
        public float temperature;
        public Dictionary<string, double> NUMBER_VARIABLES;
        public Dictionary<string, bool> TOGGLE_VARIABLES;
        public Dictionary<string, string> TEXT_VARIABLES;
        public BurnMark.BurnSave burns;

        public PartSave()
        {
            NUMBER_VARIABLES = new Dictionary<string, double>();
            TOGGLE_VARIABLES = new Dictionary<string, bool>();
            TEXT_VARIABLES = new Dictionary<string, string>();
        }

        public void Serialize(NetOutgoingMessage msg)
        {
            msg.WriteCompressedString(name);
            msg.WriteCompressedVector2(position);
            msg.WriteCompressedOrientation(orientation);
            msg.WriteCompressedFloat(temperature);
            msg.WriteCollection
            (
                NUMBER_VARIABLES,
                kvp =>
                {
                    msg.WriteCompressedString(kvp.Key);
                    msg.WriteCompressedDouble(kvp.Value);
                }
            );
            msg.WriteCollection
            (
                TOGGLE_VARIABLES,
                kvp =>
                {
                    msg.WriteCompressedString(kvp.Key);
                    msg.Write(kvp.Value);
                }
            );
            msg.WriteCollection
            (
                TEXT_VARIABLES,
                kvp =>
                {
                    msg.WriteCompressedString(kvp.Key);
                    msg.WriteCompressedString(kvp.Value);
                }
            );
            msg.WriteCompressedBurnSave(burns);
        }

        public void Deserialize(NetIncomingMessage msg)
        {
            name = msg.ReadCompressedString();
            position = msg.ReadCompressedVector2();
            orientation = msg.ReadCompressedOrientation();
            temperature = msg.ReadCompressedFloat();
            NUMBER_VARIABLES = msg.ReadCollection
            (
                count => new Dictionary<string, double>(count),
                () => new KeyValuePair<string, double>(msg.ReadCompressedString(), msg.ReadCompressedDouble())
            );
            TOGGLE_VARIABLES = msg.ReadCollection
            (
                count => new Dictionary<string, bool>(count),
                () => new KeyValuePair<string, bool>(msg.ReadCompressedString(), msg.ReadBoolean())
            );
            TEXT_VARIABLES = msg.ReadCollection
            (
                count => new Dictionary<string, string>(count),
                () => new KeyValuePair<string, string>(msg.ReadCompressedString(), msg.ReadCompressedString())
            );
            burns = msg.ReadCompressedBurnSave();
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

        public StageState()
        {
            partIDs = new List<int>();
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
