using System;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lidgren.Network;
using UnityEngine;
using SFS.IO;
using SFS.Parts;
using SFS.World;
using SFS.WorldBase;
using Random = System.Random;
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
        /// <summary>
        /// 时间加速倍率
        /// </summary>
        public double timeWarpScale = 1.0;
        public double WorldTime
        {
            get
            {
                if (worldTimer.ElapsedTicks > 1000 * Stopwatch.Frequency)
                {
                    // * Safety measure to prevent floating-point precision errors server-side.
                    initWorldTime += worldTimer.Elapsed.TotalSeconds * timeWarpScale;
                    worldTimer.Restart();
                }
                return initWorldTime + worldTimer.Elapsed.TotalSeconds * timeWarpScale;
            }
            set
            {
                initWorldTime = value;
                worldTimer.Restart();
            }
        }

        public Difficulty.DifficultyType difficulty;
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

        private string savePath;
        private bool isSaving = false;

        public async Task SaveWorld()
        {
            if (isSaving) return;
            isSaving = true;
            try
            {
                await Task.Run(() => SaveWorldSync());
            }
            finally
            {
                isSaving = false;
            }
        }

        private void SaveWorldSync()
        {
            if (string.IsNullOrEmpty(savePath))
            {
                Logger.Warning("Cannot save world: save path is not set");
                return;
            }
            try
            {
                Logger.Info("Saving world state...", true);
                double worldTime = WorldTime;
                string worldSettingsJson = $"{{\"solarSystem\":{{\"name\":\"{solarSystemName}\"}},\"mode\":{{\"mode\":0,\"allowQuicksaves\":true}},\"difficulty\":{{\"difficulty\":{(int)difficulty}}},\"playtime\":{{\"lastPlayedTime_Ticks\":{DateTime.Now.Ticks},\"totalPlayTime_Seconds\":{worldTime}}},\"cheats\":{{\"infiniteFuel\":{infiniteFuel.ToString().ToLower()},\"noAtmosphericDrag\":{noAtmosphericDrag.ToString().ToLower()},\"unbreakableParts\":{unbreakableParts.ToString().ToLower()},\"noGravity\":{noGravity.ToString().ToLower()},\"noHeatDamage\":{noHeatDamage.ToString().ToLower()},\"noBurnMarks\":{noBurnMarks.ToString().ToLower()},\"infiniteBuildArea\":{infiniteBuildArea.ToString().ToLower()},\"partClipping\":{partClipping.ToString().ToLower()}}}}}";
                string persistentPath = System.IO.Path.Combine(savePath, "Persistent");
                System.IO.File.WriteAllText(System.IO.Path.Combine(savePath, "WorldSettings.txt"), worldSettingsJson);
                System.IO.File.WriteAllText(System.IO.Path.Combine(persistentPath, "WorldState.txt"), $"{{\"worldTime\":{worldTime},\"timewarpPhase\":0,\"mapView\":false,\"mapPosition\":{{\"x\":0.0,\"y\":0.0,\"z\":0.0}},\"mapAddress\":\"\",\"targetAddress\":\"null\",\"playerAddress\":\"null\",\"cameraDistance\":0.0}}");
                System.IO.File.WriteAllText(System.IO.Path.Combine(persistentPath, "Rockets.txt"), SerializeRocketsToJson());
                Logger.Info($"World saved successfully at: {savePath}", true);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to save world: {ex.Message}");
            }
        }

        private string SerializeRocketsToJson()
        {
            if (rockets == null || rockets.Count == 0)
                return "[]";
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("[");
            bool first = true;
            foreach (KeyValuePair<int, RocketState> kvp in rockets)
            {
                if (!first)
                    sb.Append(",");
                first = false;
                RocketState rocket = kvp.Value;
                sb.Append("{");
                sb.Append($"\"rocketName\":\"{EscapeJson(rocket.rocketName)}\",");
                sb.Append($"\"location\":{{\"position\":{{\"x\":{rocket.location.position.x},\"y\":{rocket.location.position.y}}},\"velocity\":{{\"x\":{rocket.location.velocity.x},\"y\":{rocket.location.velocity.y}}},\"address\":\"{EscapeJson(rocket.location.address)}\"}},");
                sb.Append($"\"rotation\":{rocket.rotation},");
                sb.Append($"\"angularVelocity\":{rocket.angularVelocity},");
                sb.Append($"\"throttleOn\":{rocket.throttleOn.ToString().ToLower()},");
                sb.Append($"\"throttlePercent\":{rocket.throttlePercent},");
                sb.Append($"\"RCS\":{rocket.RCS.ToString().ToLower()},");
                sb.Append("\"parts\":[");
                bool firstPart = true;
                foreach (KeyValuePair<int, PartState> partKvp in rocket.parts)
                {
                    if (!firstPart)
                        sb.Append(",");
                    firstPart = false;
                    PartState part = partKvp.Value;
                    sb.Append("{");
                    sb.Append($"\"id\":{partKvp.Key},");
                    sb.Append($"\"name\":\"{EscapeJson(part.part.name)}\",");
                    sb.Append($"\"position\":{{\"x\":{part.part.position.x},\"y\":{part.part.position.y}}},");
                    sb.Append($"\"orientation\":{{\"x\":{part.part.orientation.x},\"y\":{part.part.orientation.y},\"z\":{part.part.orientation.z}}},");
                    sb.Append($"\"temperature\":{part.part.temperature},");
                    sb.Append("\"NUMBER_VARIABLES\":{");
                    bool firstNumVar = true;
                    foreach (KeyValuePair<string, double> numVar in part.part.NUMBER_VARIABLES)
                    {
                        if (!firstNumVar)
                            sb.Append(",");
                        firstNumVar = false;
                        sb.Append($"\"{EscapeJson(numVar.Key)}\":{numVar.Value}");
                    }
                    sb.Append("},");
                    sb.Append("\"TOGGLE_VARIABLES\":{");
                    bool firstToggleVar = true;
                    foreach (KeyValuePair<string, bool> toggleVar in part.part.TOGGLE_VARIABLES)
                    {
                        if (!firstToggleVar)
                            sb.Append(",");
                        firstToggleVar = false;
                        sb.Append($"\"{EscapeJson(toggleVar.Key)}\":{toggleVar.Value.ToString().ToLower()}");
                    }
                    sb.Append("},");
                    sb.Append("\"TEXT_VARIABLES\":{");
                    bool firstTextVar = true;
                    foreach (KeyValuePair<string, string> textVar in part.part.TEXT_VARIABLES)
                    {
                        if (!firstTextVar)
                            sb.Append(",");
                        firstTextVar = false;
                        sb.Append($"\"{EscapeJson(textVar.Key)}\":\"{EscapeJson(textVar.Value)}\"");
                    }
                    sb.Append("},");
                    sb.Append("\"burns\":{");
                    if (part.part.burns != null)
                    {
                        sb.Append($"\"angle\":{part.part.burns.angle},");
                        sb.Append($"\"intensity\":{part.part.burns.intensity},");
                        sb.Append($"\"x\":{part.part.burns.x},");
                        sb.Append($"\"top\":\"{EscapeJson(part.part.burns.top)}\",");
                        sb.Append($"\"bottom\":\"{EscapeJson(part.part.burns.bottom)}\"");
                    }
                    sb.Append("}");
                    sb.Append("}");
                }
                sb.Append("],");
                sb.Append("\"joints\":[");
                bool firstJoint = true;
                foreach (JointState joint in rocket.joints)
                {
                    if (!firstJoint)
                        sb.Append(",");
                    firstJoint = false;
                    sb.Append($"{{\"partIndex_A\":{joint.id_A},\"partIndex_B\":{joint.id_B}}}");
                }
                sb.Append("],");
                sb.Append("\"stages\":[");
                bool firstStage = true;
                foreach (StageState stage in rocket.stages)
                {
                    if (!firstStage)
                        sb.Append(",");
                    firstStage = false;
                    sb.Append($"{{\"stageId\":{stage.stageID},\"partIndexes\":[");
                    bool firstPartIdx = true;
                    foreach (int partIdx in stage.partIDs)
                    {
                        if (!firstPartIdx)
                            sb.Append(",");
                        firstPartIdx = false;
                        sb.Append(partIdx);
                    }
                    sb.Append("]}}");
                }
                sb.Append("]");
                sb.Append("}");
            }
            sb.Append("]");
            return sb.ToString();
        }

        private string EscapeJson(string value)
        {
            if (value == null)
                return "";
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        public WorldState()
        {
            initWorldTime = 1000000.0;
            difficulty = Difficulty.DifficultyType.Normal;
            solarSystemName = "";
            rockets = new Dictionary<int, RocketState>();
        }
        public WorldState(string path)
        {
            savePath = path;
            System.Security.Permissions.SecurityPermission securityPermission = 
                new System.Security.Permissions.SecurityPermission(System.Security.Permissions.SecurityPermissionFlag.AllFlags);
            securityPermission.Assert();
        
            try
            {
                LoadWorldStateWithReflection(path);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load world state from {path}: {ex.Message}");
                Logger.Warning("Using default world state values.");
                InitializeWithDefaults();
            }
            finally
            {
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
            var jsonWrapperType = typeof(SFS.Parsers.Json.JsonWrapper);
            //这里我改了1H
            var fromJsonMethod = jsonWrapperType.GetMethod("FromJson");
            if (fromJsonMethod == null)
            {
                throw new Exception("JsonWrapper.FromJson method not found");
            }
            //加载WorldSettings
            FilePath worldSettingsFile = folder.ExtendToFile("WorldSettings.txt");
            if (!worldSettingsFile.FileExists())
                throw new Exception("'WorldSettings.txt' file cannot be found or could not be loaded.");
            WorldSettings worldSettings = (WorldSettings)fromJsonMethod.MakeGenericMethod(typeof(WorldSettings)).Invoke(null, new object[] { worldSettingsFile.ReadText() });
            if (worldSettings == null)
                throw new Exception("'WorldSettings.txt' file cannot be found or could not be loaded.");
            // 读取星系名称
            solarSystemName = "";
            var solarSystemField = typeof(WorldSettings).GetField("solarSystem");
            if (solarSystemField != null)
            {
                var solarSystem = solarSystemField.GetValue(worldSettings);
                if (solarSystem != null)
                {
                    var nameField = solarSystem.GetType().GetField("name");
                    if (nameField != null)
                    {
                        var nameValue = nameField.GetValue(solarSystem);
                        if (nameValue != null)
                        {
                            solarSystemName = nameValue.ToString();
                        }
                    }
                }
            }
            Console.WriteLine($"[INFO] Loaded solar system: '{solarSystemName}'");
            //加载WorldState
            FilePath worldStateFile = persistent.ExtendToFile("WorldState.txt");
            if (!worldStateFile.FileExists())
                throw new Exception("'WorldState.txt' file cannot be found or could not be loaded.");
            WorldSave.WorldState worldState = (WorldSave.WorldState)fromJsonMethod.MakeGenericMethod(typeof(WorldSave.WorldState)).Invoke(null, new object[] { worldStateFile.ReadText() });
            if (worldState == null)
                throw new Exception("'WorldState.txt' file cannot be found or could not be loaded.");
            //加载Rockets
            FilePath rocketsFile = persistent.ExtendToFile("Rockets.txt");
            if (!rocketsFile.FileExists())
                throw new Exception("'Rockets.txt' file cannot be found or could not be loaded.");
            List<RocketSave> rocketSavesList = (List<RocketSave>)fromJsonMethod.MakeGenericMethod(typeof(List<RocketSave>)).Invoke(null, new object[] { rocketsFile.ReadText() });
            if (rocketSavesList == null)
                throw new Exception("'Rockets.txt' file cannot be found or could not be loaded.");
            // 设置世界状态
            initWorldTime = worldState.worldTime;
            difficulty = worldSettings.difficulty.difficulty;
            rockets = new Dictionary<int, RocketState>();
            foreach (RocketSave save in rocketSavesList)
            {
                rockets.InsertNew(new RocketState(save));
            }
            
            System.Console.WriteLine($"[INFO] Successfully loaded world state from {path}");
            // 读取作弊设置
            try
            {
                ExtractCheatSettingsFromJson(System.IO.File.ReadAllText(System.IO.Path.Combine(path, "WorldSettings.txt")));
            }
            catch {}
        }
        
        private void InitializeWithDefaults()
        {
            initWorldTime = 0.0;
            difficulty = Difficulty.DifficultyType.Normal;
            solarSystemName = "";
            rockets = new Dictionary<int, RocketState>();
        }

        private void ExtractCheatSettingsFromJson(string json)
        {
            try
            {
                int cheatsIndex = json.IndexOf("\"cheats\"");
                if (cheatsIndex == -1)
                    return;
                int objectStart = json.IndexOf("{", cheatsIndex);
                if (objectStart == -1)
                    return;
                int objectEnd = FindMatchingBrace(json, objectStart);
                if (objectEnd == -1)
                    return;
                string cheatsJson = json.Substring(objectStart, objectEnd - objectStart + 1);
                infiniteFuel = ExtractBoolFromJson(cheatsJson, "infiniteFuel");
                noAtmosphericDrag = ExtractBoolFromJson(cheatsJson, "noAtmosphericDrag");
                unbreakableParts = ExtractBoolFromJson(cheatsJson, "unbreakableParts");
                noGravity = ExtractBoolFromJson(cheatsJson, "noGravity");
                noHeatDamage = ExtractBoolFromJson(cheatsJson, "noHeatDamage");
                noBurnMarks = ExtractBoolFromJson(cheatsJson, "noBurnMarks");
                infiniteBuildArea = ExtractBoolFromJson(cheatsJson, "infiniteBuildArea");
                partClipping = ExtractBoolFromJson(cheatsJson, "partClipping");
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to parse cheat settings: {ex.Message}");
            }
        }

        private int FindMatchingBrace(string json, int startIndex)
        {
            int depth = 0;
            for (int i = startIndex; i < json.Length; i++)
            {
                if (json[i] == '{')
                    depth++;
                else if (json[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }
            return -1;
        }

        private bool ExtractBoolFromJson(string json, string key)
        {
            try
            {
                int keyIndex = json.IndexOf($"\"{key}\"");
                if (keyIndex == -1)
                    return false;
                int colonIndex = json.IndexOf(":", keyIndex);
                if (colonIndex == -1)
                    return false;
                int valueStart = colonIndex + 1;
                while (valueStart < json.Length && (json[valueStart] == ' ' || json[valueStart] == '\n' || json[valueStart] == '\r'))
                {
                    valueStart++;
                }
                return json.Substring(valueStart, 4).Equals("true", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
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