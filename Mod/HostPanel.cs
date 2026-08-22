using System;
using UnityEngine;
using SFS.UI;
using SFS.UI.ModGUI;
using Type = SFS.UI.ModGUI.Type;

namespace MultiplayerSFS.Mod
{
    public static class HostPanel
    {
        public static readonly int windowID = Builder.GetRandomID();
        public static GameObject holder_window;
        public static Window window;
        static TextInput input_port;
        static TextInput input_username;
        static TextInput input_password;
        static SFS.UI.ModGUI.Button button_start;
        static JoinInfo joinInfo;

        public static void CreateUI()
        {
            if (holder_window != null)
                return;
            joinInfo = new JoinInfo();

            holder_window = Builder.CreateHolder(Builder.SceneToAttach.CurrentScene, "Multiplayer SFS - Open to LAN");
            window = Builder.CreateClosableWindow
            (
                holder_window.transform,
                windowID,
                500,
                400,
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

            Builder.CreateLabel(settingLabels, 150, 40, text: "Password").TextAlignment = TMPro.TextAlignmentOptions.MidlineLeft;
            input_password = Builder.CreateTextInput(settingsValues, 300, 40, text: joinInfo.password,
                onChange: input =>
                {
                    joinInfo.password = input;
                    input_password.FieldColor = Color.white;
                }
            );

            button_start = Builder.CreateButton(window, 470, 60, text: "Start", onClick: OnStartClicked);
        }

        static void OnStartClicked()
        {
            if (!HostManager.isHosting)
            {
                HostManager.OnStart(joinInfo);
                SetButtonText("Stop");
            }
            else
            {
                HostManager.OnStop();
                SetButtonText("Start");
            }
        }

        static void SetButtonText(string text)
        {
            TextAdapter textAdapter = button_start.gameObject.GetComponentInChildren<TextAdapter>();
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
            input_port = null;
            input_username = null;
            input_password = null;
            button_start = null;
        }
    }
}
