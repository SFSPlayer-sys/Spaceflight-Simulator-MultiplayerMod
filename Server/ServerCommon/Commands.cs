using System;
using System.Collections.Generic;
using System.Linq;
using Lidgren.Network;

namespace MultiplayerSFS.ServerCommon
{
    public static class CommandManager
    {
        private static Dictionary<string, Command> commands = new Dictionary<string, Command>
        {
            { "help", new Command("help", "Shows this help message", Help) },
            { "players", new Command("players", "Lists all connected players", ListPlayers) },
            { "kick", new Command("kick", "Kicks a player by username", KickPlayer, true) },
            { "ban", new Command("ban", "Bans a player by username", BanPlayer, true) },
            { "unban", new Command("unban", "Unbans a player by username", UnbanPlayer, true) },
            { "clear", new Command("clear", "Clears the chat", ClearChat, true) },
            { "say", new Command("say", "Sends a message as the server", SayMessage, true) },
            { "restart", new Command("restart", "Restarts the server", RestartServer, true) },
            { "stop", new Command("stop", "Stops the server", StopServer, true) },
        };

        public static bool TryParse(string message, out string name, out string[] args)
        {
            name = null;
            args = null;

            if (string.IsNullOrWhiteSpace(message) || !message.StartsWith("/"))
                return false;

            string[] parts = message.Substring(1).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return false;

            name = parts[0].ToLower();
            args = parts.Skip(1).ToArray();

            return commands.ContainsKey(name);
        }

        public static string TryRun(string name, string[] args, NetConnection connection)
        {
            if (!commands.TryGetValue(name, out Command command))
                return "Unknown command. Type /help for a list of commands.";

            try
            {
                return command.Action(args, connection);
            }
            catch (Exception ex)
            {
                return $"Error executing command: {ex.Message}";
            }
        }

        private static string Help(string[] args, NetConnection connection)
        {
            string result = "Available commands:\n";
            foreach (var command in commands.Values)
            {
                result += $"/{command.Name}: {command.Description}\n";
            }
            return result;
        }

        private static string ListPlayers(string[] args, NetConnection connection)
        {
            // Implementation would go here
            return "Connected players: None";
        }

        private static string KickPlayer(string[] args, NetConnection connection)
        {
            if (args.Length == 0)
                return "Usage: /kick <username>";
            // Implementation would go here
            return $"Kicked player: {args[0]}";
        }

        private static string BanPlayer(string[] args, NetConnection connection)
        {
            if (args.Length == 0)
                return "Usage: /ban <username>";
            // Implementation would go here
            return $"Banned player: {args[0]}";
        }

        private static string UnbanPlayer(string[] args, NetConnection connection)
        {
            if (args.Length == 0)
                return "Usage: /unban <username>";
            // Implementation would go here
            return $"Unbanned player: {args[0]}";
        }

        private static string ClearChat(string[] args, NetConnection connection)
        {
            // Implementation would go here
            return "Chat cleared";
        }

        private static string SayMessage(string[] args, NetConnection connection)
        {
            if (args.Length == 0)
                return "Usage: /say <message>";
            // Implementation would go here
            return $"Server message sent: {string.Join(" ", args)}";
        }

        private static string RestartServer(string[] args, NetConnection connection)
        {
            // Implementation would go here
            return "Server restarting...";
        }

        private static string StopServer(string[] args, NetConnection connection)
        {
            // Implementation would go here
            return "Server stopping...";
        }
    }

    public class Command
    {
        public string Name { get; }
        public string Description { get; }
        public Func<string[], NetConnection, string> Action { get; }
        public bool RequiresAdmin { get; }

        public Command(string name, string description, Func<string[], NetConnection, string> action, bool requiresAdmin = false)
        {
            Name = name;
            Description = description;
            Action = action;
            RequiresAdmin = requiresAdmin;
        }
    }
}