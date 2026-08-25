using System;
using System.Globalization;
using UnityEngine;
using UITools;
using SFS.UI;
using SFS.UI.ModGUI;
using MultiplayerSFS.Server;
using Type = SFS.UI.ModGUI.Type;

namespace MultiplayerSFS.Mod
{
    public static class HostPanel
    {
        public static readonly int windowID = Builder.GetRandomID();
        public static GameObject holder_window;
        public static Window window;
        static TextInput input_serverName;
        static TextInput input_port;
        static TextInput input_username;
        static TextInput input_serverPassword;
        static TextInput input_adminPassword;
        static SFS.UI.ModGUI.Button button_blockDuplicate;
        static TextInput input_chatCooldown;
        static TextInput input_updatePeriod;
        static TextInput input_loadRange;
        static SFS.UI.ModGUI.Button button_start;
        static JoinInfo joinInfo;
        static string serverName = "HOST Game";
        static string serverPassword = "";
        static string adminPassword = "";
        static bool blockDuplicateNames = false;
        static double chatCooldown = 3;
        static double updatePeriod = 20;
        static double loadRange = 7500;

        public static void CreateUI()
        {
            if (holder_window != null)
                return;
            joinInfo = new JoinInfo();
            joinInfo.password = "";

            holder_window = Builder.CreateHolder(Builder.SceneToAttach.CurrentScene, "Multiplayer SFS - Open to LAN");
            window = UIToolsBuilder.CreateClosableWindow
            (
                holder_window.transform,
                windowID,
                500,
                700,
                draggable: true,
                titleText: "Open to LAN"
            );
            window.CreateLayoutGroup(Type.Vertical, padding: new RectOffset(5, 5, 5, 5));

            Container settings = Builder.CreateContainer(window);
            settings.CreateLayoutGroup(Type.Horizontal);
            Container settingLabels = Builder.CreateContainer(settings);
            Container settingsValues = Builder.CreateContainer(settings);
            settingLabels.CreateLayoutGroup(Type.Vertical, childAlignment: TextAnchor.MiddleLeft);
            settingsValues.CreateLayoutGroup(Type.Vertical, childAlignment: TextAnchor.MiddleLeft);

            Builder.CreateLabel(settingLabels, 150, 40, text: "Server Name").TextAlignment = TMPro.TextAlignmentOptions.MidlineLeft;
            input_serverName = Builder.CreateTextInput(settingsValues, 300, 40, text: serverName,
                onChange: input =>
                {
                    serverName = input.Trim();
                    input_serverName.FieldColor = Color.white;
                }
            );

            Builder.CreateLabel(settingLabels, 150, 40, text: "Port").TextAlignment = TMPro.TextAlignmentOptions.MidlineLeft;
            input_port = Builder.CreateTextInput(settingsValues, 300, 40, text: joinInfo.port.ToString(),
                onChange: input =>
                {
                    if (int.TryParse(input, out int result))
                    {
                        joinInfo.port = result;
                        input_port.FieldColor = Color.white;
                    }
                    else
                    {
                        input_port.FieldColor = Color.red;
                    }
                }
            );

            Builder.CreateLabel(settingLabels, 150, 40, text: "UserName").TextAlignment = TMPro.TextAlignmentOptions.MidlineLeft;
            input_username = Builder.CreateTextInput(settingsValues, 300, 40, text: joinInfo.username,
                onChange: input =>
                {
                    joinInfo.username = input.Trim();
                    input_username.FieldColor = Color.white;
                }
            );

            Builder.CreateLabel(settingLabels, 150, 40, text: "Server Password").TextAlignment = TMPro.TextAlignmentOptions.MidlineLeft;
            input_serverPassword = Builder.CreateTextInput(settingsValues, 300, 40, text: serverPassword,
                onChange: input =>
                {
                    serverPassword = input;
                    input_serverPassword.FieldColor = Color.white;
                }
            );

            Builder.CreateLabel(settingLabels, 150, 40, text: "Admin Password").TextAlignment = TMPro.TextAlignmentOptions.MidlineLeft;
            input_adminPassword = Builder.CreateTextInput(settingsValues, 300, 40, text: adminPassword,
                onChange: input =>
                {
                    adminPassword = input;
                    input_adminPassword.FieldColor = Color.white;
                }
            );

            Builder.CreateLabel(settingLabels, 150, 40, text: "Block Dup Names").TextAlignment = TMPro.TextAlignmentOptions.MidlineLeft;
            button_blockDuplicate = Builder.CreateButton(settingsValues, 300, 40, text: blockDuplicateNames.ToString(),
                onClick: () =>
                {
                    blockDuplicateNames = !blockDuplicateNames;
                    SetText(button_blockDuplicate, blockDuplicateNames.ToString());
                }
            );

            Builder.CreateLabel(settingLabels, 150, 40, text: "Chat Cooldown").TextAlignment = TMPro.TextAlignmentOptions.MidlineLeft;
            input_chatCooldown = Builder.CreateTextInput(settingsValues, 300, 40, text: chatCooldown.ToString(CultureInfo.InvariantCulture),
                onChange: input =>
                {
                    if (TryParseDouble(input, out double result))
                    {
                        chatCooldown = result;
                        input_chatCooldown.FieldColor = Color.white;
                    }
                    else
                    {
                        input_chatCooldown.FieldColor = Color.red;
                    }
                }
            );

            Builder.CreateLabel(settingLabels, 150, 40, text: "Update Period").TextAlignment = TMPro.TextAlignmentOptions.MidlineLeft;
            input_updatePeriod = Builder.CreateTextInput(settingsValues, 300, 40, text: updatePeriod.ToString(CultureInfo.InvariantCulture),
                onChange: input =>
                {
                    if (TryParseDouble(input, out double result))
                    {
                        updatePeriod = result;
                        input_updatePeriod.FieldColor = Color.white;
                    }
                    else
                    {
                        input_updatePeriod.FieldColor = Color.red;
                    }
                }
            );

            Builder.CreateLabel(settingLabels, 150, 40, text: "Load Range").TextAlignment = TMPro.TextAlignmentOptions.MidlineLeft;
            input_loadRange = Builder.CreateTextInput(settingsValues, 300, 40, text: loadRange.ToString(CultureInfo.InvariantCulture),
                onChange: input =>
                {
                    if (TryParseDouble(input, out double result))
                    {
                        loadRange = result;
                        input_loadRange.FieldColor = Color.white;
                    }
                    else
                    {
                        input_loadRange.FieldColor = Color.red;
                    }
                }
            );

            button_start = Builder.CreateButton(window, 470, 60, text: "Start", onClick: OnStartClicked);
        }

        static bool TryParseDouble(string input, out double result)
        {
            return double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }

        static void OnStartClicked()
        {
            if (!HostManager.isHosting)
            {
                joinInfo.username = string.IsNullOrWhiteSpace(joinInfo.username) ? "HOST" : joinInfo.username;
                joinInfo.password = serverPassword;
                ServerSettings settings = new ServerSettings()
                {
                    port = joinInfo.port,
                    serverName = string.IsNullOrWhiteSpace(serverName) ? "HOST Game" : serverName,
                    serverPassword = serverPassword,
                    adminPassword = adminPassword,
                    blockDuplicatePlayerNames = blockDuplicateNames,
                    chatMessageCooldown = chatCooldown,
                    updateRocketsPeriod = updatePeriod,
                    loadRange = loadRange,
                };
                HostManager.OnStart(joinInfo, settings);
                SetText(button_start, "Stop");
            }
            else
            {
                HostManager.OnStop();
                SetText(button_start, "Start");
            }
        }

        static void SetText(SFS.UI.ModGUI.Button button, string text)
        {
            TextAdapter textAdapter = button.gameObject.GetComponentInChildren<TextAdapter>();
            if (textAdapter != null)
            {
                textAdapter.Text = text;
            }
        }

        public static void DestroyUI()
        {
            if (holder_window != null)
            {
                UnityEngine.Object.Destroy(holder_window);
            }
            holder_window = null;
            window = null;
            input_serverName = null;
            input_port = null;
            input_username = null;
            input_serverPassword = null;
            input_adminPassword = null;
            button_blockDuplicate = null;
            input_chatCooldown = null;
            input_updatePeriod = null;
            input_loadRange = null;
            button_start = null;
        }
    }
}
