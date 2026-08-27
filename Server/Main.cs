using System;
using System.IO;

#if NET48
using MultiplayerSFS.Common;
#else
using MultiplayerSFS.ServerCommon;
#endif

namespace MultiplayerSFS.Server
{
    public class Program
    {
	    private const string CONFIG_FILENAME = "Multiplayer.cfg";
		/// <summary>
		/// Stops the server's config file from being saved or loaded.
		/// </summary>
		//private static readonly bool DEV_MODE = false;
		public static void Main()
		{
			try
			{
				ServerSettings settings;
				if (!File.Exists(CONFIG_FILENAME))
				{
					Logger.Info($"'{CONFIG_FILENAME}' not found, running with default settings...", true);
					settings = new ServerSettings();
					File.WriteAllText(CONFIG_FILENAME, settings.Serialize());
				}
				else
				{
					Logger.Info($"Loading server settings from '{CONFIG_FILENAME}'...", true);
					settings = ServerSettings.Deserialize(File.ReadAllText(CONFIG_FILENAME));
				}
				PluginManager.LoadPlugins();
				Server.Initialize(settings);
				Server.Run();
				PluginManager.UnloadPlugins();
			}
			catch (Exception e)
			{
				Logger.Error(e);
			}
		}
	}
}