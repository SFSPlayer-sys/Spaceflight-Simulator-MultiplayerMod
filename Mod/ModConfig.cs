using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using HarmonyLib;

namespace MultiplayerSFS.Mod
{
    public static class ModConfig
    {
        static string FilePath => Path.Combine(Main.main.ModFolder, "Settings.txt");

        public static ConfigData Data { get; private set; } = new ConfigData();

        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    ConfigData data = JsonConvert.DeserializeObject<ConfigData>(File.ReadAllText(FilePath));
                    if (data != null) Data = data;
                }
                else
                {
                    Save();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"ModConfig.Load: {e.Message}");
            }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Main.main.ModFolder);
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(Data));
            }
            catch (Exception e)
            {
                Debug.LogError($"ModConfig.Save: {e.Message}");
            }
        }

        public class ConfigData
        {
            public HostSettings Host = new HostSettings();
            public List<ServerHistory> ServerHistory = new List<ServerHistory>();
            public Dictionary<string, string> PlanetsPackHashes = new Dictionary<string, string>();
            public Dictionary<string, bool> PanelMinimizedStates = new Dictionary<string, bool>();
        }

        public class HostSettings
        {
            public string serverName = "HOST Game";
            public string serverPassword = "";
            public string adminPassword = "";
            public bool blockDuplicateNames = false;
            public double chatCooldown = 3;
            public double updatePeriod = 20;
            public double loadRange = 7500;
            public string username = "";
            public int port = 9806;
        }

        public class ServerHistory
        {
            public string address;
            public int port;
            public string name;
        }

        //恢复面板最小化状态
        public static void RestorePanelMinimized(object window, string key)
        {
            try
            {
                if (Data.PanelMinimizedStates.TryGetValue(key, out bool m))
                    Traverse.Create(window).Property("Minimized").SetValue(m);
            }
            catch { }
        }

        //保存面板最小化状态
        public static void SavePanelMinimized(object window, string key)
        {
            try
            {
                Data.PanelMinimizedStates[key] = Traverse.Create(window).Property("Minimized").GetValue<bool>();
                Save();
            }
            catch { }
        }
    }
}