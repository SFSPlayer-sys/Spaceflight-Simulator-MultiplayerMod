# Multiplayer Mod

A (WIP) multiplayer mod for the game Spaceflight Simulator.



### Server Setup & Game Guide

#### Hosting a Server

1. Download the server package for your operating system.
   - If your system has **.NET 6.0 Runtime** installed, it is recommended to download the **-NonSelfContained** version to reduce file size.
   - Otherwise, download the **-SelfContained** version which includes the runtime.

2. Run the server executable (`Server.exe` or `Server`).
   - This will generate a configuration file named `Multiplayer.cfg` in the server directory.

3. Open `Multiplayer.cfg` with a text editor and modify the following settings as needed:
   - `worldSavePath` – Set this to the actual path of your game save folder.
   - Other settings – Adjust port, passwords, player limits, etc., according to your preferences.

4. Save the configuration file and run the server executable again.
   - The server will now start with your custom settings.

---

### Joining the Game
1. Copy `Lidgren.Network.dll` and `Mod.dll` into your SFS **Mods** folder.
   - **Note:** This mod requires `UITools` to be installed as well.
2. Launch Spaceflight Simulator.
3. From the main menu, select the **"Multiplayer"** option.
4. Enter the server address:
   - For a local server, enter `127.0.0.1`.
   - For a remote server, enter the server's IP address or domain name.
5. Click **"Connect"** to join the game.

---

## Commands
| Command | Description |
|---------|-------------|
| `/help [command]` | Show detailed help for a specific command. |
| `/list` | List all available commands. |
| `/admin [password]` | Gain or revoke admin privileges (requires the admin password set in `Multiplayer.cfg`). |
| `/destroy -a` | Destroy **all** rockets in the current world. |
| `/destroy -p [planet]` | Destroy all rockets on a specific planet (e.g., `/destroy -p Earth`). |
| `/stats` | Display server statistics (players, rockets, uptime, etc.). |
| `/broadcast [message] [player] [type] [color]` | Send a broadcast message. Parameters: |
| | - `message` – The content of the message. |
| | - `player` – Target player name, or `all` for everyone (default: `all`, optional). |
| | - `type` – Display type: `message` (chat) or `toast` (popup) (default: `message`, optional). |
| | - `color` – Color in `#RRGGBB` format (default: white, optional). |
| | **Example:** `/broadcast Hello everyone all message #FF0000` |
| `/cheat [cheat] [true/false]` | Enable or disable a cheat feature. Available cheats: |
| | - `infinitefuel` – Infinite fuel |
| | - `noatmosphericdrag` – Disable atmospheric drag |
| | - `nobreakableparts` – Parts cannot break |
| | - `nogravity` – Disable gravity |
| | - `noheatdamage` – Disable heat damage |
| | - `noburnmarks` – Disable burn marks |
| | - `infinitebuildarea` – Unlimited build area |
| | - `partclipping` – Allow part clipping |
| | **Example:** `/cheat infinitefuel true` |
| `/ban [IP/PLAYER] [time]` | Ban a player by name or IP address. `time` is in **hours** (optional; empty = permanent). |
| | **Example:** `/ban 192.168.1.5 24` or `/ban PlayerName` |
| `/unban [IP/PLAYER]` | Remove a ban for the given name or IP address. |
| `/banlist` | List all currently banned players. |


Based On
This mod is based on the repository MultiplayerSFS (GitHub - AstroTheRabbit/Multiplayer-SFS) by Astro The Rabbit.


# How to make a plugin?

## 1. Overview
You can create plugins to extend the server's functionality.

[Example](ExamplePlugin.cs)

## 2. Plugin Structure
Every plugin must implement the IPlugin interface. Below is an example:
```csharp
    using System;
    using Lidgren.Network;
    using MultiplayerSFS.Server;

    namespace MultiplayerSFS.Plugins
    {
        public class MyPlugin : IPlugin
        {
            // Required properties
            public string ID => "myplugin";          // Unique identifier; different IDs are treated as different plugins
            public string Name => "My Plugin";       // Display name
            public string Author => "Your Name";     // Author
            public string Version => "1.0.0";        // Plugin version
            public string MinimumServerVersion => "0.4.2"; // Minimum server version required

            // Lifecycle methods
            public void OnLoad()
            {
                // Called when the plugin loads
            }

            public void OnUnload()
            {
                // Called when the plugin unloads
            }

            public void OnTick()
            {
                // Called once per server main loop tick
            }
        }
    }
```
## 3. Event Subscriptions
You can subscribe to server lifecycle events through static events on the Plugin class.

### Available events:
- Plugin.OnPlayerJoined – Triggered when a player successfully connects and is ready
- Plugin.OnPlayerLeft – Triggered when a player disconnects
- Plugin.OnChatMessage – Triggered when a player sends a chat message
- Plugin.OnPanelReply – Triggered when a player replies from a custom UI panel
- Plugin.OnPacketReceived – Triggered when any packet arrives

```csharp
Subscription example (inside OnLoad):

    Plugin.OnPlayerJoined += player =>
    {
        Logger.Info($"{player.username} joined!");
    };

    Plugin.OnPlayerLeft += player =>
    {
        Logger.Info($"{player.username} left.");
    };

    Plugin.OnChatMessage += (sender, message) =>
    {
        if (message.Contains("badword"))
            return null; // Block the message
        return message;  // Forward unchanged
    };

    Plugin.OnPanelReply += (conn, panelId, action, value) =>
    {
        if (panelId == "my_panel" && action == "submit")
        {
            Logger.Info($"Player replied: {value}");
            return true; // Handled; skip further processing
        }
        return false;
    };
```
## 4. Custom Commands
### Command class definition:
Commands must inherit from the Command class and implement Description and Run methods.
```csharp
    class MyCommand : Command
    {
        public override string Description => "Performs an action";
        public override string Run(string[] args, NetConnection sender)
        {
            ConnectedPlayer player = Server.FindPlayer(sender);
            if (player == null) return "Player not found";
            return "Command executed";
        }
    }
```
### Registering a command (in OnLoad):

    CommandManager.RegisterCommand("mycmd", new MyCommand());

Players can then type `/mycmd arg1 arg2` in chat to trigger it.

### Permission check helper:
```csharp
    private bool IsAdmin(NetConnection conn)
    {
        ConnectedPlayer p = Server.FindPlayer(conn);
        return p != null && p.isAdmin;
    }
```
## 5. Sending Custom GUI
The server sends GUI panels via chat messages. The format is:
```csharp
    //#UI_START#
    //{UI content JSON}
    //#UI_END#
```
The markers `//#UI_START#` and `//#UI_END#` are required.

### Example JSON (the part between the markers):
```csharp
    {
      "id": "Example",
      "title": "Example",
      "width": 460,
      "height": 560,
      "closable": true,
      "scrollable": true,
      "elements": [
        { "type": "label", "text": "Label Text", "width": 400, "height": 30, "font_size": 24, "color": "#00FF00" },
        { "type": "button", "text": "Button", "width": 400, "height": 40, "action": "demo_button" },
        { "type": "text_input", "placeholder": "Text Input", "width": 400, "height": 40, "action": "demo_input" },
        { "type": "container", "layout": "horizontal", "spacing": 10, "padding": 5, "elements": [
            { "type": "button", "text": "Left", "width": 190, "height": 40, "action": "left" },
            { "type": "button", "text": "Right", "width": 190, "height": 40, "action": "right" }
        ]},
        { "type": "scroll_view", "width": 420, "height": 120, "elements": [
            { "type": "label", "text": "Scroll Row 1", "width": 380, "height": 24 }
        ]},
        { "type": "window", "title": "Nested Window", "width": 420, "height": 140, "closable": true, "elements": [
            { "type": "label", "text": "Nested Content", "width": 380, "height": 24 },
            { "type": "button", "text": "Nested Button", "width": 380, "height": 36, "action": "nested_btn" }
        ]},
        { "type": "separator" },
        { "type": "spacer", "width": 10, "height": 20 }
      ]
    }
```

When sending, place the JSON between the markers, e.g.:
```csharp
    string panelJson = "{\"id\":\"Example\",...}";
    Server.SendPanel(conn, panelJson);
```
Or send it as a chat message with the markers.

Closing a panel:
```csharp
    Server.ClosePanel(conn, "Example");
```
Listening for panel replies: Subscribe to Plugin.OnPanelReply in OnLoad and distinguish responses by panelId and action.

## 6. Player and Network Operations
### Getting player objects:
- Server.FindPlayer(NetConnection conn) – Get player by connection
- Server.FindPlayerByName(string username) – Find by username
- Server.FindConnectionByName(string username) – Get connection by username

Sending packets:
Unicast:
```csharp
    Server.SendPacketToPlayer(conn, new Packet_SendChatMessage() { Message = "Hello!", Color = Color.White });
```
Broadcast:
```csharp
    Server.SendPacketToAll(new Packet_SendChatMessage() { Message = "Global announcement" }, exceptConnection: null);
```
Kicking a player:
```csharp
    Server.KickPlayer(conn, "Reason text");
```
Ban management:
```csharp
    // Ban username (permanent)
    BanManager.BanTarget("username", 0);
    // Ban IP (24 hours)
    BanManager.BanTarget("192.168.1.100", 24);
    // Unban
    BanManager.UnbanTarget("username");
    // List bans
    string bans = BanManager.ListBans();
```
## 7. Accessing World State
The server world state is stored in Server.world:

```csharp
    // Get all rockets
    foreach (var kvp in Server.world.rockets)
    {
        int rocketId = kvp.Key;
        RocketState rocket = kvp.Value;
        // Access rocket properties: location, velocity, parts, stages, etc.
    }

    // Get current world time (in-game time)
    double worldTime = Server.world.WorldTime;

    // Read/write cheat flags
    Server.world.infiniteFuel = true;
    Server.world.noGravity = false;
```
## 8. Logging Output
```csharp
    Logger.Info("Info message");       // Console + log file
    Logger.Warning("Warning message"); // Yellow
    Logger.Error("Error message");     // Red
```
## 9. Packet Interception
To intercept or modify specific packets, implement:

```csharp
    Plugin.OnPacketReceived += (conn, packetType, msg) =>
    {
        if (packetType == PacketType.SendChatMessage)
        {
            Packet_SendChatMessage pkt = msg.Read<Packet_SendChatMessage>();
            if (pkt.Message.Contains("spam"))
                return true; // Skip default handling
        }
        return false; // Do not skip default handling
    };
```