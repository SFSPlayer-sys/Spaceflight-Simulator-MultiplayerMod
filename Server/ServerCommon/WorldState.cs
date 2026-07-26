using System;
using System.Diagnostics;
using System.Collections.Generic;
using Lidgren.Network;

namespace MultiplayerSFS.ServerCommon
{
    public static class IDExtensions
    {
        static readonly Random generator = new Random();
        public static int InsertNew<T>(this Dictionary<int, T> dict, T item) where T : class
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
        
        private string savePath;

        public WorldState()
        {
            InitializeWithDefaults();
        }

        public WorldState(string path)
        {
            savePath = path;
            try
            {
                string settingsPath = System.IO.Path.Combine(path, "WorldSettings.txt");
                if (!System.IO.File.Exists(settingsPath))
                {
                    Logger.Info($"WorldSettings.txt not found at: {path}", true);
                    Logger.Info("Creating new world structure...", true);
                    CreateNewWorld(path);
                }
                
                LoadWorldStateFromFile(path);
                Logger.Info($"World state loaded from {path}", true);
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to load world state: {ex.Message}");
                Logger.Warning("Using default world state values.");
                InitializeWithDefaults();
            }
        }
        
        public void SaveWorld()
        {
            if (string.IsNullOrEmpty(savePath))
            {
                Logger.Warning("Cannot save world: save path is not set");
                return;
            }
            
            try
            {
                Logger.Info("Saving world state...", true);
                
                long currentTicks = DateTime.Now.Ticks;
                double worldTime = WorldTime;
                
                string worldSettingsJson = $"{{\"solarSystem\":{{\"name\":\"{solarSystemName}\"}},\"mode\":{{\"mode\":0,\"allowQuicksaves\":true}},\"difficulty\":{{\"difficulty\":{difficulty}}},\"playtime\":{{\"lastPlayedTime_Ticks\":{currentTicks},\"totalPlayTime_Seconds\":{worldTime}}},\"cheats\":{{\"infiniteFuel\":{infiniteFuel.ToString().ToLower()},\"noAtmosphericDrag\":{noAtmosphericDrag.ToString().ToLower()},\"unbreakableParts\":{unbreakableParts.ToString().ToLower()},\"noGravity\":{noGravity.ToString().ToLower()},\"noHeatDamage\":{noHeatDamage.ToString().ToLower()},\"noBurnMarks\":{noBurnMarks.ToString().ToLower()},\"infiniteBuildArea\":{infiniteBuildArea.ToString().ToLower()},\"partClipping\":{partClipping.ToString().ToLower()}}}}}";
                
                string persistentPath = System.IO.Path.Combine(savePath, "Persistent");
                
                System.IO.File.WriteAllText(System.IO.Path.Combine(savePath, "WorldSettings.txt"), worldSettingsJson);
                System.IO.File.WriteAllText(System.IO.Path.Combine(persistentPath, "WorldState.txt"), $"{{\"worldTime\":{worldTime},\"timewarpPhase\":0,\"mapView\":false,\"mapPosition\":{{\"x\":0.0,\"y\":0.0,\"z\":0.0}},\"mapAddress\":\"\",\"targetAddress\":\"null\",\"playerAddress\":\"null\",\"cameraDistance\":0.0}}");
                
                string rocketsJson = SerializeRocketsToJson();
                System.IO.File.WriteAllText(System.IO.Path.Combine(persistentPath, "Rockets.txt"), rocketsJson);
                
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
            foreach (var kvp in rockets)
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
                foreach (var partKvp in rocket.parts)
                {
                    if (!firstPart)
                        sb.Append(",");
                    firstPart = false;
                    
                    PartState part = partKvp.Value;
                    sb.Append("{");
                    sb.Append($"\"name\":\"{EscapeJson(part.part.name)}\",");
                    sb.Append($"\"position\":{{\"x\":{part.part.position.x},\"y\":{part.part.position.y}}},");
                    sb.Append($"\"orientation\":{{\"x\":{part.part.orientation.x},\"y\":{part.part.orientation.y},\"z\":{part.part.orientation.z}}},");
                    sb.Append($"\"temperature\":{part.part.temperature},");
                    sb.Append("\"NUMBER_VARIABLES\":{");
                    bool firstNumVar = true;
                    foreach (var numVar in part.part.NUMBER_VARIABLES)
                    {
                        if (!firstNumVar)
                            sb.Append(",");
                        firstNumVar = false;
                        sb.Append($"\"{EscapeJson(numVar.Key)}\":{numVar.Value}");
                    }
                    sb.Append("},");
                    sb.Append("\"TOGGLE_VARIABLES\":{");
                    bool firstToggleVar = true;
                    foreach (var toggleVar in part.part.TOGGLE_VARIABLES)
                    {
                        if (!firstToggleVar)
                            sb.Append(",");
                        firstToggleVar = false;
                        sb.Append($"\"{EscapeJson(toggleVar.Key)}\":{toggleVar.Value.ToString().ToLower()}");
                    }
                    sb.Append("},");
                    sb.Append("\"TEXT_VARIABLES\":{");
                    bool firstTextVar = true;
                    foreach (var textVar in part.part.TEXT_VARIABLES)
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
                foreach (var joint in rocket.joints)
                {
                    if (!firstJoint)
                        sb.Append(",");
                    firstJoint = false;
                    sb.Append($"{{\"partIndex_A\":{joint.id_A},\"partIndex_B\":{joint.id_B}}}");
                }
                sb.Append("],");
                
                sb.Append("\"stages\":[");
                bool firstStage = true;
                foreach (var stage in rocket.stages)
                {
                    if (!firstStage)
                        sb.Append(",");
                    firstStage = false;
                    sb.Append($"{{\"stageId\":{stage.stageID},\"partIndexes\":[");
                    bool firstPartIdx = true;
                    foreach (var partIdx in stage.partIDs)
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
        
        private void CreateNewWorld(string path)
        {
            Logger.Info("\n=== World Creation ===", true);
            Logger.Info("Please enter world settings:", true);
            
            Console.Write("Difficulty (0=Normal, 1=Hard, 2=Realistic, default: 0): ");
            string difficultyInput = Console.ReadLine();
            int difficulty = 0;
            if (!string.IsNullOrWhiteSpace(difficultyInput) && int.TryParse(difficultyInput, out int parsedDifficulty))
            {
                difficulty = Math.Clamp(parsedDifficulty, 0, 2);
            }
            
            Console.Write("Configure cheat settings? (y/n, default: n): ");
            string configureCheatsInput = Console.ReadLine();
            bool configureCheats = !string.IsNullOrWhiteSpace(configureCheatsInput) && configureCheatsInput.Trim().ToLower() == "y";
            
            bool infiniteFuel = false;
            bool noAtmosphericDrag = false;
            bool unbreakableParts = false;
            bool noGravity = false;
            bool noHeatDamage = false;
            bool noBurnMarks = false;
            bool infiniteBuildArea = false;
            bool partClipping = false;
            
            if (configureCheats)
            {
                Logger.Info("\n=== Cheat Settings ===", true);
                Console.Write("Infinite fuel (true/false, default: false): ");
                string infiniteFuelInput = Console.ReadLine();
                infiniteFuel = !string.IsNullOrWhiteSpace(infiniteFuelInput) && bool.TryParse(infiniteFuelInput, out bool parsedInfiniteFuel) && parsedInfiniteFuel;
                
                Console.Write("No atmospheric drag (true/false, default: false): ");
                string noAtmosphericDragInput = Console.ReadLine();
                noAtmosphericDrag = !string.IsNullOrWhiteSpace(noAtmosphericDragInput) && bool.TryParse(noAtmosphericDragInput, out bool parsedNoAtmosphericDrag) && parsedNoAtmosphericDrag;
                
                Console.Write("Unbreakable parts (true/false, default: false): ");
                string unbreakablePartsInput = Console.ReadLine();
                unbreakableParts = !string.IsNullOrWhiteSpace(unbreakablePartsInput) && bool.TryParse(unbreakablePartsInput, out bool parsedUnbreakableParts) && parsedUnbreakableParts;
                
                Console.Write("No gravity (true/false, default: false): ");
                string noGravityInput = Console.ReadLine();
                noGravity = !string.IsNullOrWhiteSpace(noGravityInput) && bool.TryParse(noGravityInput, out bool parsedNoGravity) && parsedNoGravity;
                
                Console.Write("No heat damage (true/false, default: false): ");
                string noHeatDamageInput = Console.ReadLine();
                noHeatDamage = !string.IsNullOrWhiteSpace(noHeatDamageInput) && bool.TryParse(noHeatDamageInput, out bool parsedNoHeatDamage) && parsedNoHeatDamage;
                
                Console.Write("No burn marks (true/false, default: false): ");
                string noBurnMarksInput = Console.ReadLine();
                noBurnMarks = !string.IsNullOrWhiteSpace(noBurnMarksInput) && bool.TryParse(noBurnMarksInput, out bool parsedNoBurnMarks) && parsedNoBurnMarks;
                
                Console.Write("Infinite build area (true/false, default: false): ");
                string infiniteBuildAreaInput = Console.ReadLine();
                infiniteBuildArea = !string.IsNullOrWhiteSpace(infiniteBuildAreaInput) && bool.TryParse(infiniteBuildAreaInput, out bool parsedInfiniteBuildArea) && parsedInfiniteBuildArea;
                
                Console.Write("Part clipping (true/false, default: false): ");
                string partClippingInput = Console.ReadLine();
                partClipping = !string.IsNullOrWhiteSpace(partClippingInput) && bool.TryParse(partClippingInput, out bool parsedPartClipping) && parsedPartClipping;
            }
            
            long currentTicks = DateTime.Now.Ticks;
            
            string worldSettingsJson = $"{{\"solarSystem\":{{\"name\":\"\"}},\"mode\":{{\"mode\":0,\"allowQuicksaves\":true}},\"difficulty\":{{\"difficulty\":{difficulty}}},\"playtime\":{{\"lastPlayedTime_Ticks\":{currentTicks},\"totalPlayTime_Seconds\":0.0}},\"cheats\":{{\"infiniteFuel\":{infiniteFuel.ToString().ToLower()},\"noAtmosphericDrag\":{noAtmosphericDrag.ToString().ToLower()},\"unbreakableParts\":{unbreakableParts.ToString().ToLower()},\"noGravity\":{noGravity.ToString().ToLower()},\"noHeatDamage\":{noHeatDamage.ToString().ToLower()},\"noBurnMarks\":{noBurnMarks.ToString().ToLower()},\"infiniteBuildArea\":{infiniteBuildArea.ToString().ToLower()},\"partClipping\":{partClipping.ToString().ToLower()}}}}}";
            
            try
            {
                System.IO.Directory.CreateDirectory(path);
                
                string persistentPath = System.IO.Path.Combine(path, "Persistent");
                System.IO.Directory.CreateDirectory(persistentPath);
                
                System.IO.File.WriteAllText(System.IO.Path.Combine(path, "WorldSettings.txt"), worldSettingsJson);
                
                System.IO.File.WriteAllText(System.IO.Path.Combine(persistentPath, "Achievements.txt"), "[]");
                System.IO.File.WriteAllText(System.IO.Path.Combine(persistentPath, "Branches.txt"), "{}");
                System.IO.File.WriteAllText(System.IO.Path.Combine(persistentPath, "Challenges.txt"), "{}");
                System.IO.File.WriteAllText(System.IO.Path.Combine(persistentPath, "Rockets.txt"), "{}");
                System.IO.File.WriteAllText(System.IO.Path.Combine(persistentPath, "Version.txt"), "\"1.5.10.2\"");
                System.IO.File.WriteAllText(System.IO.Path.Combine(persistentPath, "WorldState.txt"), "{}");
                
                Logger.Info($"World structure created at: {path}", true);
                Logger.Info($"Difficulty: {difficulty} (0=Normal, 1=Hard, 2=Realistic)", true);
                
                this.solarSystemName = "";
                this.difficulty = difficulty;
                this.infiniteFuel = infiniteFuel;
                this.noAtmosphericDrag = noAtmosphericDrag;
                this.unbreakableParts = unbreakableParts;
                this.noGravity = noGravity;
                this.noHeatDamage = noHeatDamage;
                this.noBurnMarks = noBurnMarks;
                this.infiniteBuildArea = infiniteBuildArea;
                this.partClipping = partClipping;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to create world structure: {ex.Message}");
                throw;
            }
        }

        private void LoadWorldStateFromFile(string path)
        {
            rockets = new Dictionary<int, RocketState>();
            
            try
            {
                string settingsPath = System.IO.Path.Combine(path, "WorldSettings.txt");
                if (!System.IO.File.Exists(settingsPath))
                {
                    Logger.Warning($"WorldSettings.txt not found at: {settingsPath}");
                    return;
                }
                
                string jsonContent = System.IO.File.ReadAllText(settingsPath);
                Logger.Info($"Reading WorldSettings.txt from: {settingsPath}");
                
                solarSystemName = ExtractSolarSystemNameFromJson(jsonContent);
                Logger.Info($"Loaded solar system: '{solarSystemName}'");
                
                difficulty = ExtractDifficultyFromJson(jsonContent);
                Logger.Info($"Loaded difficulty: {difficulty} (0=Normal, 1=Hard, 2=Realistic)", true);
                
                ExtractCheatSettingsFromJson(jsonContent);
                Logger.Info("Loaded cheat settings", true);
                
                string persistentPath = System.IO.Path.Combine(path, "Persistent");
                string worldStatePath = System.IO.Path.Combine(persistentPath, "WorldState.txt");
                if (System.IO.File.Exists(worldStatePath))
                {
                    string worldStateJson = System.IO.File.ReadAllText(worldStatePath);
                    double loadedWorldTime = ExtractWorldTimeFromJson(worldStateJson);
                    if (loadedWorldTime > 0)
                    {
                        WorldTime = loadedWorldTime;
                        Logger.Info($"Loaded world time: {loadedWorldTime}", true);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load world settings: {ex.Message}");
                throw;
            }
        }
        
        private double ExtractWorldTimeFromJson(string json)
        {
            try
            {
                int worldTimeIndex = json.IndexOf("\"worldTime\"");
                if (worldTimeIndex == -1)
                {
                    Logger.Warning("'worldTime' field not found in WorldState.txt");
                    return 0;
                }
                
                int colonIndex = json.IndexOf(":", worldTimeIndex);
                if (colonIndex == -1)
                {
                    Logger.Warning("Could not find worldTime value colon");
                    return 0;
                }
                
                int valueStart = colonIndex + 1;
                while (valueStart < json.Length && (json[valueStart] == ' ' || json[valueStart] == '\n' || json[valueStart] == '\r'))
                {
                    valueStart++;
                }
                
                int valueEnd = valueStart;
                while (valueEnd < json.Length && (char.IsDigit(json[valueEnd]) || json[valueEnd] == '.'))
                {
                    valueEnd++;
                }
                
                if (double.TryParse(json.Substring(valueStart, valueEnd - valueStart), out double parsedWorldTime))
                {
                    return parsedWorldTime;
                }
                
                Logger.Warning("Could not parse worldTime value");
                return 0;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to parse world time: {ex.Message}");
                return 0;
            }
        }
        
        private int ExtractDifficultyFromJson(string json)
        {
            try
            {
                int difficultyIndex = json.IndexOf("\"difficulty\"");
                if (difficultyIndex == -1)
                {
                    Logger.Warning("'difficulty' field not found in WorldSettings.txt");
                    return 0;
                }
                
                int objectStart = json.IndexOf("{", difficultyIndex);
                if (objectStart == -1)
                {
                    Logger.Warning("Could not find difficulty object");
                    return 0;
                }
                
                int valueIndex = json.IndexOf("\"difficulty\"", objectStart);
                if (valueIndex == -1)
                {
                    Logger.Warning("'difficulty' value field not found in difficulty object");
                    return 0;
                }
                
                int colonIndex = json.IndexOf(":", valueIndex);
                if (colonIndex == -1)
                {
                    Logger.Warning("Could not find difficulty value colon");
                    return 0;
                }
                
                int valueStart = colonIndex + 1;
                while (valueStart < json.Length && (json[valueStart] == ' ' || json[valueStart] == '\n' || json[valueStart] == '\r'))
                {
                    valueStart++;
                }
                
                int valueEnd = valueStart;
                while (valueEnd < json.Length && char.IsDigit(json[valueEnd]))
                {
                    valueEnd++;
                }
                
                if (int.TryParse(json.Substring(valueStart, valueEnd - valueStart), out int parsedDifficulty))
                {
                    return parsedDifficulty;
                }
                
                Logger.Warning("Could not parse difficulty value");
                return 0;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to parse difficulty: {ex.Message}");
                return 0;
            }
        }
        
        private void ExtractCheatSettingsFromJson(string json)
        {
            try
            {
                int cheatsIndex = json.IndexOf("\"cheats\"");
                if (cheatsIndex == -1)
                {
                    Logger.Warning("'cheats' field not found in WorldSettings.txt");
                    return;
                }
                
                int objectStart = json.IndexOf("{", cheatsIndex);
                if (objectStart == -1)
                {
                    Logger.Warning("Could not find cheats object");
                    return;
                }
                
                int objectEnd = FindMatchingBrace(json, objectStart);
                if (objectEnd == -1)
                {
                    Logger.Warning("Could not find end of cheats object");
                    return;
                }
                
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
                Logger.Error($"Failed to parse cheat settings: {ex.Message}");
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
        
        private string ExtractSolarSystemNameFromJson(string json)
        {
            try
            {
                int solarSystemIndex = json.IndexOf("\"solarSystem\"");
                if (solarSystemIndex == -1)
                {
                    Logger.Warning("'solarSystem' field not found in WorldSettings.txt");
                    return "";
                }
                
                int objectStart = json.IndexOf("{", solarSystemIndex);
                if (objectStart == -1)
                {
                    Logger.Warning("Could not find solarSystem object");
                    return "";
                }
                
                int nameIndex = json.IndexOf("\"name\"", objectStart);
                if (nameIndex == -1)
                {
                    Logger.Warning("'name' field not found in solarSystem object");
                    return "";
                }
                
                int valueStart = json.IndexOf("\"", nameIndex + 6);
                if (valueStart == -1)
                {
                    Logger.Warning("Could not find name value start");
                    return "";
                }
                
                int valueEnd = json.IndexOf("\"", valueStart + 1);
                if (valueEnd == -1)
                {
                    Logger.Warning("Could not find name value end");
                    return "";
                }
                
                string name = json.Substring(valueStart + 1, valueEnd - valueStart - 1);
                return name;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to parse solar system name: {ex.Message}");
                return "";
            }
        }

        private void InitializeWithDefaults()
        {
            initWorldTime = 1000000.0;
            difficulty = 0;
            solarSystemName = "";
            rockets = new Dictionary<int, RocketState>();
            
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