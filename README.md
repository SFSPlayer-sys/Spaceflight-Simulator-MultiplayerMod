
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

## Chat Commands

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