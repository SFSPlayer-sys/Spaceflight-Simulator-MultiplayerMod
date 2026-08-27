using System;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UITools;
using Newtonsoft.Json.Linq;
using SFS.UI;
using SFS.UI.ModGUI;
using MultiplayerSFS.Common;
using GUIElement = SFS.UI.ModGUI.GUIElement;
using Type = SFS.UI.ModGUI.Type;
//UI格式
//#UI_START#
//{UI内容JSON}
//#UI_END#
// {
//   "id": "Example",
//   "title": "Example",
//   "width": 460,
//   "height": 560,
//   "closable": true,
//   "scrollable": true,
//   "elements": [
//     { "type": "label", "text": "Label Text", "width": 400, "height": 30, "font_size": 24, "color": "#00FF00" },
//     { "type": "button", "text": "Button", "width": 400, "height": 40, "action": "demo_button" },
//     { "type": "text_input", "placeholder": "Text Input", "width": 400, "height": 40, "action": "demo_input" },
//     { "type": "container", "layout": "horizontal", "spacing": 10, "padding": 5, "elements": [
//         { "type": "button", "text": "Left", "width": 190, "height": 40, "action": "left" },
//         { "type": "button", "text": "Right", "width": 190, "height": 40, "action": "right" }
//     ]},
//     { "type": "scroll_view", "width": 420, "height": 120, "elements": [
//         { "type": "label", "text": "Scroll Row 1", "width": 380, "height": 24 },
//     ]},
//     { "type": "window", "title": "Nested Window", "width": 420, "height": 140, "closable": true, "elements": [
//         { "type": "label", "text": "Nested Content", "width": 380, "height": 24 },
//         { "type": "button", "text": "Nested Button", "width": 380, "height": 36, "action": "nested_btn" }
//     ]},
//     { "type": "separator" },
//     { "type": "spacer", "width": 10, "height": 20 }
//   ]
// }





namespace MultiplayerSFS.Mod
{
    /// <summary>
    ///服务端UI面板渲染
    /// </summary>
    public static class ServerUI
    {
        static Dictionary<string, PanelInstance> panels = new Dictionary<string, PanelInstance>();
        static Dictionary<string, string> panelInputs = new Dictionary<string, string>();
        static string currentScene = "";

        class PanelInstance
        {
            public string id;
            public string scene;
            public GameObject holder;
            public JObject data;
        }

        /// <summary>
        /// 场景切换时调用，按场景渲染或隐藏面板
        /// </summary>
        public static void OnSceneChanged(string scene)
        {
            currentScene = scene;
            foreach (PanelInstance panel in panels.Values)
            {
                bool match = string.IsNullOrEmpty(panel.scene) || panel.scene == scene;
                if (match)
                {
                    if (panel.holder == null)
                        RenderPanel(panel);
                }
                else if (panel.holder != null)
                {
                    UnityEngine.Object.Destroy(panel.holder);
                    panel.holder = null;
                }
            }
        }

        /// <summary>
        /// 从收到的聊天消息中提取UI协议并渲染
        /// </summary>
        /// <returns>true表示已消费该消息（不进入聊天框）</returns>
        public static bool HandleMessage(string message)
        {
            const string startTag = "#UI_START#";
            const string endTag = "#UI_END#";
            int startIdx = message.IndexOf(startTag, StringComparison.Ordinal);
            int endIdx = message.IndexOf(endTag, StringComparison.Ordinal);
            if (startIdx < 0 || endIdx < 0 || endIdx <= startIdx + startTag.Length)
                return false;

            string json = message.Substring(startIdx + startTag.Length, endIdx - startIdx - startTag.Length).Trim();
            if (string.IsNullOrEmpty(json))
                return true;

            try
            {
                JObject data = JObject.Parse(json);
                string id = (string)data["id"];

                if ((bool?)data["close"] == true)
                {
                    if (panels.TryGetValue(id, out PanelInstance oldClose))
                        DestroyPanel(oldClose);
                    return true;
                }
                if (panels.TryGetValue(id, out PanelInstance old))
                    DestroyPanel(old);

                PanelInstance panel = new PanelInstance()
                {
                    id = id,
                    scene = (string)data["scene"] ?? "",
                    data = data,
                };
                panels[id] = panel;
                if (string.IsNullOrEmpty(panel.scene) || panel.scene == currentScene)
                    RenderPanel(panel);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(ex.Message);
            }
            return true;
        }

        static void RenderPanel(PanelInstance panel)
        {
            JObject data = panel.data;
            string title = (string)data["title"] ?? "Server Panel";
            int width = (int?)data["width"] ?? 400;
            int height = (int?)data["height"] ?? 300;
            bool closable = (bool?)data["closable"] ?? true;
            bool scrollable = (bool?)data["scrollable"] ?? false;

            panel.holder = Builder.CreateHolder(Builder.SceneToAttach.CurrentScene, $"ServerUI_{panel.id}");
            Window window = closable
                ? UIToolsBuilder.CreateClosableWindow(
                    panel.holder.transform,
                    Builder.GetRandomID(),
                    width, height, 0, 0,
                    true, true, 0.95f, title)
                : Builder.CreateWindow(
                    panel.holder.transform,
                    Builder.GetRandomID(),
                    width, height,
                    savePosition: false);

            window.CreateLayoutGroup(Type.Vertical, TextAnchor.UpperLeft, spacing: 8f, padding: new RectOffset(10, 10, 10, 10));
            if (scrollable)
                window.EnableScrolling(Type.Vertical);

            JArray elements = (JArray)data["elements"];
            if (elements != null)
            {
                foreach (JObject el in elements)
                    RenderElement(window, panel.id, el);
            }
        }

        static void RenderElement(GUIElement parent, string panelId, JObject el)
        {
            string type = (string)el["type"] ?? "";
            switch (type)
            {
                case "label":
                    CreateLabel(parent, el);
                    break;
                case "button":
                    CreateButton(parent, panelId, el);
                    break;
                case "text_input":
                    CreateTextInput(parent, panelId, el);
                    break;
                case "container":
                    CreateContainer(parent, panelId, el);
                    break;
                case "scroll_view":
                    CreateScrollView(parent, panelId, el);
                    break;
                case "window":
                    CreateNestedWindow(parent, panelId, el);
                    break;
                case "separator":
                    Builder.CreateLabel(parent, 200, 1, text: "");
                    break;
                case "spacer":
                    int sw = (int?)el["width"] ?? 10;
                    int sh = (int?)el["height"] ?? 10;
                    Builder.CreateLabel(parent, sw, sh, text: "");
                    break;
                default:
                    Debug.LogWarning($"ServerUI: 未知元素类型 '{type}'");
                    break;
            }
        }

        static void CreateLabel(GUIElement parent, JObject el)
        {
            string text = (string)el["text"] ?? "";
            int width = (int?)el["width"] ?? 200;
            int height = (int?)el["height"] ?? 30;
            int fontSize = (int?)el["font_size"] ?? 0;
            string colorStr = (string)el["color"];

            Label label = Builder.CreateLabel(parent, width, height, text: text);
            if (fontSize > 0)
            {
                TextMeshProUGUI tmp = label.gameObject.GetComponentInChildren<TextMeshProUGUI>();
                if (tmp != null)
                    tmp.fontSize = fontSize;
            }
            if (!string.IsNullOrEmpty(colorStr) && ColorUtility.TryParseHtmlString(colorStr, out Color col))
                label.Color = col;
        }

        static void CreateButton(GUIElement parent, string panelId, JObject el)
        {
            string text = (string)el["text"] ?? "";
            string action = (string)el["action"] ?? "";
            int width = (int?)el["width"] ?? 200;
            int height = (int?)el["height"] ?? 35;

            Builder.CreateButton(parent, width, height, onClick: () => SendReply(panelId, action, panelInputs.TryGetValue(panelId, out string v) ? v : null), text: text);
        }
        static void CreateTextInput(GUIElement parent, string panelId, JObject el)
        {
            string placeholder = (string)el["placeholder"] ?? "";
            string action = (string)el["action"] ?? "";
            int width = (int?)el["width"] ?? 200;
            int height = (int?)el["height"] ?? 40;

            TextInput input = Builder.CreateTextInput(parent, width, height);
            if (!string.IsNullOrEmpty(placeholder))
                input.Text = placeholder;
            input.field.onValueChanged.AddListener(val => panelInputs[panelId] = val);
            input.field.onSubmit.AddListener(val => SendReply(panelId, action, val));
        }

        static void CreateContainer(GUIElement parent, string panelId, JObject el)
        {
            string layout = (string)el["layout"] ?? "vertical";
            int spacing = (int?)el["spacing"] ?? 8;
            int padding = (int?)el["padding"] ?? 0;

            Container container = Builder.CreateContainer(parent);
            container.CreateLayoutGroup(
                layout == "horizontal" ? Type.Horizontal : Type.Vertical,
                TextAnchor.UpperLeft,
                spacing: spacing,
                padding: new RectOffset(padding, padding, padding, padding));

            JArray elements = (JArray)el["elements"];
            if (elements != null)
            {
                foreach (JObject child in elements)
                    RenderElement(container, panelId, child);
            }
        }

        static void CreateScrollView(GUIElement parent, string panelId, JObject el)
        {
            int width = (int?)el["width"] ?? 400;
            int height = (int?)el["height"] ?? 200;

            Window scrollWin = Builder.CreateWindow(parent, Builder.GetRandomID(), width, height, savePosition: false);
            scrollWin.CreateLayoutGroup(Type.Vertical, TextAnchor.UpperLeft, spacing: 8f, padding: new RectOffset(5, 5, 5, 5));
            scrollWin.EnableScrolling(Type.Vertical);

            JArray elements = (JArray)el["elements"];
            if (elements != null)
            {
                foreach (JObject child in elements)
                    RenderElement(scrollWin, panelId, child);
            }
        }

        static void CreateNestedWindow(GUIElement parent, string panelId, JObject el)
        {
            string title = (string)el["title"] ?? "Window";
            int width = (int?)el["width"] ?? 300;
            int height = (int?)el["height"] ?? 200;
            bool closable = (bool?)el["closable"] ?? true;

            Window nested = closable
                ? UIToolsBuilder.CreateClosableWindow(
                    parent,
                    Builder.GetRandomID(),
                    width, height, 0, 0,
                    true, true, 0.95f, title)
                : Builder.CreateWindow(parent, Builder.GetRandomID(), width, height, savePosition: false);

            nested.CreateLayoutGroup(Type.Vertical, TextAnchor.UpperLeft, spacing: 8f, padding: new RectOffset(10, 10, 10, 10));

            JArray elements = (JArray)el["elements"];
            if (elements != null)
            {
                foreach (JObject child in elements)
                    RenderElement(nested, panelId, child);
            }
        }

        static void SendReply(string panelId, string action, string value)
        {
            JObject reply = new JObject();
            reply["panel_id"] = panelId;
            reply["action"] = action;
            if (value != null)
                reply["value"] = value;

            string msg = "#UI_REPLY_START#\n" + reply.ToString(Newtonsoft.Json.Formatting.None) + "\n#UI_REPLY_END#";
            ClientManager.SendPacket(new Packet_SendChatMessage()
            {
                SenderId = ClientManager.playerId,
                Message = msg,
                Color = LocalManager.Player != null ? LocalManager.Player.iconColor : Color.white,
            });
        }

        static void DestroyPanel(PanelInstance panel)
        {
            if (panel.holder != null)
                UnityEngine.Object.Destroy(panel.holder);
            panels.Remove(panel.id);
        }

        /// <summary>
        /// 场景卸载时调用，销毁面板显示但不删除定义，便于重进场景重新渲染
        /// </summary>
        public static void DestroyHolders()
        {
            foreach (PanelInstance panel in panels.Values)
            {
                if (panel.holder != null)
                {
                    UnityEngine.Object.Destroy(panel.holder);
                    panel.holder = null;
                }
            }
        }

        /// <summary>
        /// 清理所有面板
        /// </summary>
        public static void ClearAll()
        {
            foreach (PanelInstance panel in panels.Values)
            {
                if (panel.holder != null)
                    UnityEngine.Object.Destroy(panel.holder);
            }
            panels.Clear();
        }
    }
}
