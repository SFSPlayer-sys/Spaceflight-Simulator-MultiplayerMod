using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
#if NET48
using UnityEngine;
#endif
//插件管理器
namespace MultiplayerSFS.Server
{
    public static class PluginManager
    {
        static List<IPlugin> plugins = new List<IPlugin>();
        public static IReadOnlyList<IPlugin> Plugins => plugins;
        public static void LoadPlugins()
        {
            string dir = GetPluginDirectory();
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                return;
            }
            foreach (string file in Directory.GetFiles(dir, "*.dll"))
            {
                string pluginName = Path.GetFileNameWithoutExtension(file);
                string pluginVersion = "";
                try
                {
                    Assembly asm = Assembly.LoadFrom(file);
                    foreach (Type type in asm.GetTypes())
                    {
                        if (!type.IsClass || type.IsAbstract || !typeof(IPlugin).IsAssignableFrom(type)) continue;
                        try
                        {
                            IPlugin plugin = (IPlugin)Activator.CreateInstance(type);
                            pluginName = plugin.Name;
                            pluginVersion = plugin.Version;
                            if (Version.TryParse(plugin.MinimumServerVersion, out Version min) && Version.TryParse(Ver.ServerVersion, out Version cur) && min > cur)
                                throw new Exception($"requires server version {min}, current {cur}");
                            plugin.OnLoad();
                            plugins.Add(plugin);
                            Logger.Info($"Successfully loaded plugin: {pluginName} ({pluginVersion})", true);
                        }
                        catch (Exception e)
                        {
                            Logger.Error($"Failed to load plugin: {pluginName} ({pluginVersion}) : {e.Message}");
                        }
                    }
                }
                catch (Exception e)
                {
                    Logger.Error($"Failed to load assembly: {pluginName} : {e.Message}");
                }
            }
        }
        public static void UnloadPlugins()
        {
            foreach (IPlugin plugin in plugins)
            {
                try { plugin.OnUnload(); }
                catch (Exception e) { Logger.Error($"Failed to unload plugin: {plugin.Name} ({plugin.Version}) : {e.Message}"); }
            }
            plugins.Clear();
        }
        static string GetPluginDirectory()
        {
#if NET48
            return Path.Combine(Application.dataPath, "..", "plugins");
#else
            return Path.Combine(AppContext.BaseDirectory, "plugins");
#endif
        }
        public static void TriggerTick()
        {
            foreach (IPlugin plugin in plugins)
            {
                try { plugin.OnTick(); }
                catch (Exception e) { Logger.Error($"Error in plugin tick: {plugin.Name} ({plugin.Version}) : {e.Message}"); }
            }
        }
    }
}
