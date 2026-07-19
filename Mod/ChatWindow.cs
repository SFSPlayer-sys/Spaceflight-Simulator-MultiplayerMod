using System.Timers;
using System.Threading.Tasks;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SFS.UI;
using SFS.UI.ModGUI;
using MultiplayerSFS.Common;
using MultiplayerSFS.Mod.Patches;
using System.Collections.Generic;

namespace MultiplayerSFS.Mod
{
    public static class ChatWindow
    {
        public static readonly int windowID = Builder.GetRandomID();
        public static readonly int maxMessagesCount = 100;
        public static Color defaultInputColor;
        public static bool InputSelected { get; private set; }

        internal const int WindowWidth = 500;
        internal const int WindowHeight = 700;
        internal const int InnerWidth = WindowWidth - 20;
        internal const int MessageWidth = InnerWidth - 10;

        public static GameObject holder_window;
        public static Window window;

        public static Container container_colorPicker;
        // TODO: I wanted this to be a `Slider`, but they seem quite broken so I have to use UI Tools' `NumberInput` instead.
        public static TextInput input_colorPicker;
        public static Label label_colorPicker;
        public static SFS.UI.ModGUI.Button button_colorPicker;

        public static Window window_messages;
        public static readonly List<ChatMessage> messages = new List<ChatMessage>();
        public static int LastSenderId 
        { 
            get 
            {
                int lastId = int.MinValue;
                foreach (var message in messages)
                {
                    if (message.label_message != null)
                    {
                        lastId = message.senderId;
                    }
                }
                return lastId;
            }
        }

        public static Timer cooldownTimer;
        public static bool canSendMessage = true;
        public static TextInput input_sendMessage;

        public static async void CreateUI(string sceneName)
        {
            if (holder_window != null || !ClientManager.multiplayerEnabled)
                return;

            while (LocalManager.Player == null)
            {
                if (ClientManager.multiplayerEnabled)
                    return;
                await Task.Yield();
            }

            holder_window = Builder.CreateHolder(Builder.SceneToAttach.CurrentScene, "Multiplayer SFS - Chat Window Holder");

            window = Builder.CreateWindow
            (
                holder_window.transform,
                windowID,
                WindowWidth,
                WindowHeight,
                draggable: true,
                titleText: "Multiplayer Chat"
            );
            window.CreateLayoutGroup(Type.Vertical);
            // window.RegisterPermanentSaving($"multiplayer-sfs.chat-window.{sceneName}"); 
            int RemainingHeight = WindowHeight - 80;

            container_colorPicker = Builder.CreateContainer(window);
            container_colorPicker.CreateLayoutGroup(Type.Horizontal);
            
            Color.RGBToHSV(LocalManager.Player.iconColor, out float hue, out _, out _);
            hue *= 100;

            input_colorPicker = Builder.CreateTextInput(container_colorPicker, InnerWidth / 3, 50);
            input_colorPicker.Text = hue.ToString();
            label_colorPicker = Builder.CreateLabel(container_colorPicker, InnerWidth / 6, 50, text: "▲");
            button_colorPicker = Builder.CreateButton(container_colorPicker, InnerWidth / 3, 50, onClick: OnColorPickerSubmit, text: "Change");
            
            TMP_FontAsset chineseFont = ChatMessage.GetChineseFont();
            
            TMP_InputField field = input_colorPicker.field;
            field.onSelect.AddListener(_ => InputSelected = true);
            field.onDeselect.AddListener(_ => InputSelected = false);
            field.textComponent.font = chineseFont;
            field.textComponent.ForceMeshUpdate();

            input_colorPicker.field.onValueChanged.AddListener(OnColorPickerChange);
            OnColorPickerChange(hue.ToString());

            // * 2 * -60 for both the color picker and the chat input.
            RemainingHeight -= 60 + 60;

            window_messages = Builder.CreateWindow
            (
                window,
                Builder.GetRandomID(),
                InnerWidth,
                RemainingHeight,
                savePosition: false
            );
            window_messages.CreateLayoutGroup(Type.Vertical, TextAnchor.LowerLeft, 5, new RectOffset(5, 5, 5, 5));
            window_messages.EnableScrolling(Type.Vertical);

            foreach (ChatMessage message in messages)
            {
                message.CreateUI();
            }

            input_sendMessage = Builder.CreateTextInput(window, InnerWidth, 50);
            defaultInputColor = input_sendMessage.FieldColor;
            input_sendMessage.field.onSubmit.AddListener(OnMessageSubmit);
            input_sendMessage.field.onSelect.AddListener(_ => InputSelected = true);
            input_sendMessage.field.onDeselect.AddListener(_ => InputSelected = false);
            input_sendMessage.field.textComponent.alignment = TextAlignmentOptions.Left;
            input_sendMessage.field.textComponent.fontSize = 20;
            input_sendMessage.field.textComponent.font = chineseFont;
            input_sendMessage.field.textComponent.ForceMeshUpdate();
            ChangeCooldownStatus(canSendMessage);
        }

        public static void DestroyUI()
        {
            if (holder_window != null)
            {
                foreach (ChatMessage msg in messages)
                {
                    msg.DestroyUI();
                }
                Object.Destroy(holder_window);
            }
        }

        public static void OnColorPickerChange(string hueText)
        {
            if (float.TryParse(hueText, out float hue))
            {
                float clamped = hue % 100;
                if (clamped < 0)
                {
                    clamped += 100;
                }
                if (clamped != hue)
                {
                    input_colorPicker.Text = clamped.ToString();
                    return;
                }
                if (label_colorPicker != null)
                {
                    label_colorPicker.Color = Color.HSVToRGB(hue / 100, 1, 1);
                }
            }
        }

        public static void OnColorPickerSubmit()
        {
            Color color = LocalManager.Player.iconColor = label_colorPicker.Color;
            ClientManager.SendPacket
            (
                new Packet_UpdatePlayerColor()
                {
                    PlayerId = ClientManager.playerId,
                    Color = color,
                }
            );
            OnPlayerColorChange(ClientManager.playerId, color);
        }

        public static void OnMessageSubmit(string message)
        {
            if (!string.IsNullOrEmpty(message) && canSendMessage)
            {
                AddMessage(new ChatMessage(message, ClientManager.playerId));
                if (cooldownTimer != null)
                {
                    ChangeCooldownStatus(false);
                    cooldownTimer.Stop();
                    cooldownTimer.Start();
                }
                ClientManager.SendPacket
                (
                    new Packet_SendChatMessage()
                    {
                        SenderId = ClientManager.playerId,
                        Message = message,
                        Color = LocalManager.Player.iconColor
                    }
                );
                input_sendMessage.Text = "";
                input_sendMessage.field.ActivateInputField();
            }
        }

        private static System.Collections.Queue mainThreadActions = new System.Collections.Queue();
        private static object mainThreadActionsLock = new object();        private static double cooldownSeconds = 0;

        public static void CreateCooldownTimer(double cooldown)
        {
            if (cooldownTimer == null && cooldown > 0)
            {
                cooldownSeconds = cooldown;
                cooldownTimer = new Timer()
                {
                    Interval = 1000 * cooldown,
                    AutoReset = false,
                };
                cooldownTimer.Elapsed += (s, e) => 
                {
                    // 将任务添加到主线程执行队列
                    lock (mainThreadActionsLock)
                    {
                        mainThreadActions.Enqueue(new System.Action(() => ChangeCooldownStatus(true)));
                    }
                };
            }
        }

        // 在主线程中调用此方法来处理队列中的任务
        public static void Update()
        {
            while (true)
            {
                System.Action action = null;
                lock (mainThreadActionsLock)
                {
                    if (mainThreadActions.Count > 0)
                    {
                        action = (System.Action)mainThreadActions.Dequeue();
                    }
                    else
                    {
                        break;
                    }
                }
                action?.Invoke();
            }
        }

        public static void DestroyCooldownTimer()
        {
            if (cooldownTimer != null)
            {
                cooldownTimer.Dispose();
                cooldownTimer = null;
            }
        }

        public static void ChangeCooldownStatus(bool canSend)
        {
            canSendMessage = canSend;
            if (input_sendMessage != null)
            {
                input_sendMessage.FieldColor = canSend ? defaultInputColor : Color.red;
            }
        }

        public static void AddMessage(ChatMessage message)
        {
            messages.Add(message);
            if (window_messages != null)
            {
                message.CreateUI();

                ScrollToBottom();
            }
            while (messages.Count > maxMessagesCount)
            {
                messages[0].DestroyUI();
                messages.RemoveAt(0);
            }
        }

        // 滚动聊天窗口到底部
        public static void ScrollToBottom()
        {
            if (window_messages != null)
            {
                // 获取滚动组件
                SFS.UI.ScrollElement scrollElement = window_messages.ChildrenHolder.GetComponent<SFS.UI.ScrollElement>();
                if (scrollElement != null)
                {
                    
                    scrollElement.PercentPosition = new Vector2(0.5f, 1f);
                }
            }
        }

        public static void OnPlayerColorChange(int id, Color color)
        {
            foreach (ChatMessage msg in messages)
            {
                if (msg.senderId == id && msg.label_playerName != null)
                {
                    msg.label_playerName.Color = color;
                }
            }
            if (LocalManager.players.TryGetValue(id, out LocalPlayer player))
            {
                if (LocalManager.syncedRockets.TryGetValue(player.controlledRocket.Value, out LocalRocket rocket) && rocket.rocket != null)
                {
                    // * Updates the rocket's map icon color manually.
                    new Traverse(rocket.rocket.mapIcon).Method("UpdateAlpha").GetValue();//
                }
            }
        }
    }

    public class ChatMessage
    {
        public string message;
        public int senderId;
        public UnityEngine.Color color;

        public Label label_playerName;
        public Label label_message;

        public ChatMessage(string message, int senderId = -1, UnityEngine.Color color = default)
        {
            this.message = message;
            this.senderId = senderId;
            this.color = color == default ? UnityEngine.Color.white : color;
        }

        // 获取支持中文的字体
        private static TMP_FontAsset cachedChineseFont;
        public static TMP_FontAsset GetChineseFont()
        {
            if (cachedChineseFont != null)
                return cachedChineseFont;
            
            // 尝试加载支持中文的字体
            string[] fontPaths = new string[]
            {
                "Fonts & Materials/NotoSansCJK-Regular SDF",
                "Fonts & Materials/NotoSansSC-Regular SDF",
                "Fonts & Materials/SourceHanSans-Regular SDF",
                "Fonts & Materials/NotoSansCJKsc-Regular SDF",
                "Fonts & Materials/NotoSansCJKjp-Regular SDF",
                "Fonts & Materials/NotoSansCJKkr-Regular SDF",
            };
            
            foreach (string path in fontPaths)
            {
                TMP_FontAsset font = Resources.Load<TMP_FontAsset>(path);
                if (font != null)
                {
                    cachedChineseFont = font;
                    Debug.Log($"Successfully loaded Chinese font: {path}");
                    return font;
                }
            }
            
            // 如果没有找到中文字体，使用默认字体
            TMP_FontAsset defaultFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            cachedChineseFont = defaultFont;
            Debug.LogWarning("Chinese font not found, using default font");
            return defaultFont;
        }
        
        public static TMP_FontAsset GetChineseFontForInput()
        {
            return GetChineseFont();
        }

        public void CreateUI()
        {
            TMP_FontAsset chineseFont = GetChineseFont();

            if (ChatWindow.LastSenderId != senderId)
                {
                    if (LocalManager.players.TryGetValue(senderId, out LocalPlayer player))
                    {
                        label_playerName = Builder.CreateLabel(ChatWindow.window_messages, ChatWindow.MessageWidth, 30, text: player.username);
                        TextMeshProUGUI playerNameAdapter = label_playerName.FieldRef<TextMeshProUGUI>("textAdapter");
                        playerNameAdapter.font = chineseFont;
                        playerNameAdapter.ForceMeshUpdate();
                        label_playerName.TextAlignment = TextAlignmentOptions.Left;
                        label_playerName.Color = player.iconColor;
                    }
                    else
                    {
                        label_playerName = Builder.CreateLabel(ChatWindow.window_messages, ChatWindow.MessageWidth, 30, text: "SERVER");
                        TextMeshProUGUI serverNameAdapter = label_playerName.FieldRef<TextMeshProUGUI>("textAdapter");
                        serverNameAdapter.font = chineseFont;
                        serverNameAdapter.ForceMeshUpdate();
                        label_playerName.TextAlignment = TextAlignmentOptions.Left;
                        label_playerName.FontStyle = FontStyles.Bold;
                    }
                }
            label_message = Builder.CreateLabel(ChatWindow.window_messages, ChatWindow.MessageWidth, 25, text: message);
            TextMeshProUGUI textAdapter = label_message.FieldRef<TextMeshProUGUI>("textAdapter");
            textAdapter.enableWordWrapping = true;
            textAdapter.font = chineseFont;
            textAdapter.color = color;
            textAdapter.ForceMeshUpdate();
            label_message.AutoFontResize = false;
            label_message.TextAlignment = TextAlignmentOptions.TopLeft;
            label_message.Size = new Vector2(label_message.Size.x, textAdapter.preferredHeight);
        }

        public void DestroyUI()
        {
            if (label_playerName != null)
            {
                Object.Destroy(label_playerName.gameObject);
                label_playerName = null;
            }
            if (label_message != null)
            {
                Object.Destroy(label_message.gameObject);
                label_message = null;
            }
        }
    }

    public static class ToastHelper
    {
        // 显示Toast消息
        public static string ShowToast(string toast)
        {
            string msg = toast;
            if (MsgDrawer.main == null)
            {
                MsgDrawer.main = GameObject.FindObjectOfType<MsgDrawer>();
            }
            if (MsgDrawer.main != null)
            {
                MsgDrawer.main.Log(msg, false);
                return "Success";
            }
            return "Error: MsgDrawer not available";
        }
    }
}