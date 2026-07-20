using System;
using System.Text;
using System.Reflection;
using System.Globalization;

namespace MultiplayerSFS.Server
{
	public class ServerConfigVariable : Attribute
	{
		public readonly string[] Comment;

		public ServerConfigVariable(params string[] comment)
		{
			Comment = comment;
		}
	}

	public class ServerSettings
	{

		[ServerConfigVariable(
			"The file path to the multiplayer world's save folder."
		)]
		public string worldSavePath = "./World";

		[ServerConfigVariable(
			"Port used by the server. Generally should not be changed, as this is also the default port for the client's join menu."
		)]
		public int port = 9806;

		[ServerConfigVariable(
			"Password required by players to access the multiplayer server.",
			"WARNING: Leaving blank can & will allow any player who knows the server's IP address to join!"
		)]
		public string serverPassword = "";

		[ServerConfigVariable(
			"Password used by a player to gain admin privileges, which allows the use of various powerful commands through the in-game multiplayer chat.",
			"Leaving this blank will prevent the use of admin privileges by any connected player.",
			"WARNING: These commands include the ability to destroy any & all rockets, kick any player, and more, so be careful with sharing this password!"
		)]
		public string adminPassword = "ADMIN123";

		[ServerConfigVariable(
			"The maximum number of connected players allowed at any one time."
		)]
		public int maxConnections = 16;

		[ServerConfigVariable(
			"Prevents players from joining if their username is already in use on the server."
		)]
		public bool blockDuplicatePlayerNames = false;

		[ServerConfigVariable(
			"Cooldown time (in seconds) during which a player cannot send another message in the multiplayer chat.",
			"Used to reduce spam and similar issues. Set to 0 if you want to disable the cooldown."
		)]
		public double chatMessageCooldown = 3;

		[ServerConfigVariable(
			"Comma-separated list of allowed game versions. Players with versions not in this list will be rejected.",
			"Leave empty to allow all versions. Example: '1.5.10.2'",
			"Only major versions need to match (e.g., 1.5.x.x are considered the same major version)."
		)]
		public string allowedGameVersions = "";

		[ServerConfigVariable(
			"Message of the day displayed to players when they join.",
			"Leave empty to disable."
		)]
		public string motd = "Welcome to the server!";

		[ServerConfigVariable(
			"Rocket update interval in milliseconds. Updates per second = 1000 / updateRocketsPeriod",
			"Do not adjust unless you know what you are doing."
		)]
		public double updateRocketsPeriod = 20;

		[ServerConfigVariable(
			"Distance for player authority over nearby rockets. Should be above game's load distance.",
			"Do not adjust unless you know what you are doing."
		)]
		public double loadRange = 7500;

		[ServerConfigVariable(
			"Authority update interval in milliseconds. Lower values increase CPU usage.",
			"Do not adjust unless you know what you are doing."
		)]
		public int authorityUpdateInterval = 200;

		[ServerConfigVariable(
			"Maximum time warp scale allowed on the server. Set to 0 for unlimited.",
			"Do not adjust unless you know what you are doing."
		)]
		public float timeWarpMaxScale = 0f;

		[ServerConfigVariable(
			"Time warp voting timeout in seconds. Set to 0 to disable.",
			"Do not adjust unless you know what you are doing."
		)]
		public int timeWarpVoteTimeout = 30;

		[ServerConfigVariable(
			"Network timeout in seconds for client connections.",
			"Do not adjust unless you know what you are doing."
		)]
		public int networkTimeout = 15;

		[ServerConfigVariable(
			"Maximum packet size in bytes.",
			"Do not adjust unless you know what you are doing."
		)]
		public int maxPacketSize = 65536;

		[ServerConfigVariable(
			"Enable network compression to reduce bandwidth.",
			"Do not adjust unless you know what you are doing."
		)]
		public bool enableNetworkCompression = false;

		[ServerConfigVariable(
			"Server tick rate in milliseconds. Minimum is 1ms.",
			"Do not adjust unless you know what you are doing."
		)]
		public int serverTickRate = 1;

		[ServerConfigVariable(
			"Enable Server GC mode for better performance on multi-core systems.",
			"Uses more memory but reduces GC pause times.",
			"Do not adjust unless you know what you are doing."
		)]
		public bool enableServerGC = true;

		[ServerConfigVariable(
			"Enable concurrent garbage collection to reduce pause times.",
			"Do not adjust unless you know what you are doing."
		)]
		public bool gcConcurrent = true;

		[ServerConfigVariable(
			"Number of GC heaps. 0 = auto (usually equals CPU core count).",
			"Do not adjust unless you know what you are doing."
		)]
		public int gcHeapCount = 0;

		public string Serialize()
		{
			StringBuilder result = new StringBuilder();
			
			foreach (var field in GetType().GetFields())
			{
				var attr = field.GetCustomAttribute<ServerConfigVariable>();

				if (attr == null) continue;

				if (attr.Comment.Length != 0)
				{
					foreach (string line in attr.Comment)
					{
						result.AppendLine("# " + line);
					}
				}
				
				var val = field.GetValue(this);
				var strVal = val is IFormattable formattable
					? formattable.ToString(null, CultureInfo.InvariantCulture)
					: val?.ToString();
				
				result.Append(field.Name + "=" + strVal + "\n\n");
			}
			
			return result.ToString().TrimEnd('\n');
		}

		public static ServerSettings Deserialize(string input)
		{
			try
			{
				var result = new ServerSettings();

				string[] lines = input.Split('\n');
				
				foreach (var line in lines)
				{
					string trimLine = line.TrimStart(' ').Replace("\r", "");

					if (trimLine.Length == 0) continue;
					if (trimLine.StartsWith("#")) continue;

					int eqIndex = trimLine.IndexOf('=');

					if (eqIndex == -1) throw new Exception("= character not found.");

					string key = trimLine.Substring(0, eqIndex);
					string value = trimLine.Length > eqIndex + 1 ? trimLine.Substring(eqIndex + 1) : "";

					try
					{
                        FieldInfo field = result.GetType().GetField(key);
						field.SetValue(result, Convert.ChangeType(value, field.FieldType, CultureInfo.InvariantCulture));
						Logger.Info(key + ": " + field.GetValue(result));
					}
					catch (Exception ex)
					{
						throw new Exception($"Variable deserialization error ({key})", ex);
					}
				}
				
				return result;
			}
			catch (Exception ex)
			{
				throw new Exception("Config deserialization failed", ex);
			}
		}
	}
}
