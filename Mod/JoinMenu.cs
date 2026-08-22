using System;
using System.Net;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;
using SFS.UI;
using SFS.Input;
using SFS.UI.ModGUI;
using Type = SFS.UI.ModGUI.Type;

namespace MultiplayerSFS.Mod
{
    public class JoinInfo
    {
        public IPAddress address = IPAddress.Parse("127.0.0.1");
        public int port = 9806;
        public string username = "DEFAULT_USERNAME";
        public string password = "DEFAULT_PASSWORD";
    }

    public class JoinMenu : BasicMenu
    {
        public static JoinMenu main;
        public static Window window;
        public static GameObject windowHolder;
        static readonly int windowID = Builder.GetRandomID();
        static readonly Vector2Int windowSize = new Vector2Int(1000, 500);
        protected override CloseMode OnEscape => CloseMode.Current;

        public JoinInfo joinInfo = new JoinInfo();
        Color defaultTextInputColor;
        TextInput input_address;
        TextInput input_port;
        TextInput input_username;
        TextInput input_password;

        public static void OpenMenu() {
            windowHolder = Builder.CreateHolder(Builder.SceneToAttach.CurrentScene, "MultiplayerSFS - Join Menu Holder");
            main = windowHolder.AddComponent<JoinMenu>();
            main.OnOpen();
        }

        public override void OnOpen()
        {
            if (ScreenManager.main.CurrentScreen != this)
            {
                ScreenManager.main.OpenScreen(() => this);
                windowHolder.SetActive(true);
                ClientManager.multiplayerEnabled.Value = true;
                window = Builder.CreateClosableWindow
                (
                    windowHolder.transform,
                    windowID,
                    windowSize.x,
                    windowSize.y,
                    0,
                    windowSize.y / 2,
                    draggable: false,
                    savePosition: false,
                    titleText: "Multiplayer SFS - Join Menu"
                );
                CreateUI();
                ClientManager.LoadServerHistory();
                ScanLAN();
            }

        }

        public override void Close()
        {
            if (ScreenManager.main.CurrentScreen == this && windowHolder != null)
            {
                serverListActive = false;
                DestroyServerList();
                ClientManager.multiplayerEnabled.Value = false;
                ScreenManager.main.CloseCurrent();
                windowHolder.SetActive(false);
            }
        }

        void CreateUI()
        {
            window.CreateLayoutGroup(Type.Vertical, padding: new RectOffset(5,5,5,5));
            Container settings = Builder.CreateContainer(window);
            settings.CreateLayoutGroup(Type.Horizontal);

            Container settingLabels = Builder.CreateContainer(settings);
            Container settingsValues = Builder.CreateContainer(settings);
            settingLabels.CreateLayoutGroup(Type.Vertical, childAlignment: TextAnchor.MiddleLeft);
            settingsValues.CreateLayoutGroup(Type.Vertical, childAlignment: TextAnchor.MiddleLeft);

            Builder.CreateLabel(settingLabels, 300, 50, text: "Address").TextAlignment = TMPro.TextAlignmentOptions.MidlineLeft;
            input_address = Builder.CreateTextInput(settingsValues, 620, 50, text: joinInfo.address.ToString(),
                onChange: async input =>
                {
                    if (await TryParseAddress(input) is IPAddress result)
                    {
                        input_address.FieldColor = defaultTextInputColor;
                        joinInfo.address = result;
                    }
                    else
                    {
                        input_address.FieldColor = Color.red;
                    }
                }
            );
            defaultTextInputColor = input_address.FieldColor;
            

            Builder.CreateLabel(settingLabels, 300, 50, text: "Port").TextAlignment = TMPro.TextAlignmentOptions.MidlineLeft;
            input_port = Builder.CreateTextInput(settingsValues, 620, 50, text: joinInfo.port.ToString(),
                onChange: input =>
                {
                    if (int.TryParse(input, out int result))
                    {
                        input_port.FieldColor = defaultTextInputColor;
                        joinInfo.port = result;
                    }
                    else
                    {
                        input_port.FieldColor = Color.red;
                    }
                }
            );

            Builder.CreateLabel(settingLabels, 300, 50, text: "Username").TextAlignment = TMPro.TextAlignmentOptions.MidlineLeft;
            input_username = Builder.CreateTextInput(settingsValues, 620, 50, text: joinInfo.username,
                onChange: input =>
                {
                    
                    input_username.Text = joinInfo.username = input.Trim();
                    input_username.FieldColor = defaultTextInputColor;
                }
            );

            Builder.CreateLabel(settingLabels, 300, 50, text: "Password").TextAlignment = TMPro.TextAlignmentOptions.MidlineLeft;
            input_password = Builder.CreateTextInput(settingsValues, 620, 50, text: joinInfo.password,
            onChange: input =>
            {
                    joinInfo.password = input;
                    input_password.FieldColor = defaultTextInputColor;
                }
            );

            Container backJoinButtons = Builder.CreateContainer(window);
            backJoinButtons.CreateLayoutGroup(Type.Horizontal, childAlignment: TextAnchor.MiddleLeft);
            Builder.CreateButton(backJoinButtons, 300, 100, text: "Back", onClick: Close);
            Builder.CreateButton(backJoinButtons, 300, 100, text: "Join", onClick: CheckAndJoin);
        }

        /// <summary>
        /// 服务器列表窗口与扫描状态
        /// </summary>
        static GameObject serverListHolder;
        static Window serverListWindow;
        static Window serverListRows;
        static bool serverListActive = false;
        static bool serverListCreated = false;
        /// <summary>
        /// 上一次扫描到的服务器
        /// </summary>
        static List<ClientManager.ServerInfo> lastScannedServers = new List<ClientManager.ServerInfo>();

        /// <summary>
        /// 扫描局域网内服务器并周期性刷新列表
        /// </summary>
        async void ScanLAN()
        {
            if (serverListActive) return;
            serverListActive = true;
            try
            {
                ShowServerList(new List<ClientManager.ServerInfo>());
                serverListCreated = true;
                while (serverListActive)
                {
                    List<ClientManager.ServerInfo> servers = await ClientManager.DiscoverLAN(joinInfo.port);
                    if (!serverListActive) break;
                    if (serverListCreated && serverListHolder == null)
                        break;
                    ShowServerList(servers);
                    await Task.Delay(2000);
                }
            }
            catch (Exception e)
            {
                MsgDrawer.main.Log("An error occured... (Check console)");
                Debug.LogError(e);
            }
            finally
            {
                serverListActive = false;
            }
        }

        /// <summary>
        /// 销毁服务器列表窗口
        /// </summary>
        static void DestroyServerList()
        {
            if (serverListHolder != null)
                UnityEngine.Object.Destroy(serverListHolder);
            serverListHolder = null;
            serverListWindow = null;
            serverListRows = null;
            serverListCreated = false;
        }

        /// <summary>
        /// 显示服务器列表（历史 + 扫描），窗口只建一次，仅更新数据行
        /// </summary>
        static void ShowServerList(List<ClientManager.ServerInfo> servers)
        {
            lastScannedServers = servers;

            // 首次创建窗口与表头
            if (serverListHolder == null)
            {
                serverListHolder = Builder.CreateHolder(Builder.SceneToAttach.CurrentScene, "MultiplayerSFS - Server List");
                serverListWindow = Builder.CreateClosableWindow
                (
                    serverListHolder.transform,
                    Builder.GetRandomID(),
                    950,
                    500,
                    0,
                    0,
                    true,
                    true,
                    0.95f,
                    "Servers"
                );
                serverListWindow.CreateLayoutGroup(Type.Vertical, TextAnchor.UpperLeft, spacing: 3f, padding: new RectOffset(5, 5, 5, 5));
                serverListRows = Builder.CreateClosableWindow
                (
                    serverListWindow,
                    Builder.GetRandomID(),
                    930,
                    400,
                    savePosition: false
                );
                serverListRows.CreateLayoutGroup(Type.Vertical, TextAnchor.UpperLeft, spacing: 3f, padding: new RectOffset(5, 5, 5, 5));
                serverListRows.EnableScrolling(Type.Vertical);

                // 表头放在行窗口内，与数据行共享同一布局容器
                Container header = Builder.CreateContainer(serverListRows);
                header.CreateLayoutGroup(Type.Horizontal, TextAnchor.MiddleLeft, spacing: 3f);
                CreateColumnLabel(header, 160, "IP Address");
                CreateColumnLabel(header, 200, "Name");
                CreateColumnLabel(header, 70, "Players");
                CreateColumnLabel(header, 150, "Version");
                CreateColumnLabel(header, 80, "Password");
                Builder.CreateLabel(header, 90, 24, text: "");
                Builder.CreateLabel(header, 60, 24, text: "");
            }

            // 清空旧行（保留第一个子对象：表头）
            while (serverListRows.ChildrenHolder.transform.childCount > 1)
                UnityEngine.Object.DestroyImmediate(serverListRows.ChildrenHolder.transform.GetChild(1).gameObject);

            // 合并历史记录与扫描结果
            List<ClientManager.ServerInfo> history = new List<ClientManager.ServerInfo>(ClientManager.serverHistory);
            List<ClientManager.ServerInfo> all = new List<ClientManager.ServerInfo>();
            foreach (ClientManager.ServerInfo scanned in servers)
            {
                ClientManager.ServerInfo hit = history.Find(h => h.endpoint.Equals(scanned.endpoint));
                if (hit != null)
                {
                    hit.playerCount = scanned.playerCount;
                    hit.maxPlayers = scanned.maxPlayers;
                    hit.allowedVersions = scanned.allowedVersions;
                    hit.hasPassword = scanned.hasPassword;
                    history.Remove(hit);
                }
                else
                {
                    all.Add(scanned);
                }
            }
            all.InsertRange(0, history);
            foreach (ClientManager.ServerInfo offline in history)
                QueryHistoryServer(offline);

            // 填充新行
            foreach (ClientManager.ServerInfo server in all)
            {
                Container row = Builder.CreateContainer(serverListRows);
                row.CreateLayoutGroup(Type.Horizontal, TextAnchor.MiddleLeft, spacing: 3f);
                CreateColumnLabel(row, 160, server.endpoint.Address.ToString());
                CreateColumnLabel(row, 200, server.name);
                if (server.isHistory)
                {
                    CreateColumnLabel(row, 70, server.maxPlayers > 0 ? $"{server.playerCount}/{server.maxPlayers}" : "-");
                    CreateColumnLabel(row, 150, string.IsNullOrWhiteSpace(server.allowedVersions) ? "-" : server.allowedVersions);
                    CreateColumnLabel(row, 80, server.hasPassword ? "Yes" : "-");
                }
                else
                {
                    CreateColumnLabel(row, 70, $"{server.playerCount}/{server.maxPlayers}");
                    CreateColumnLabel(row, 150, string.IsNullOrWhiteSpace(server.allowedVersions) ? "All" : server.allowedVersions);
                    CreateColumnLabel(row, 80, server.hasPassword ? "Yes" : "No");
                }
                Builder.CreateButton(row, 90, 24, 0, 0, () => SelectServer(server.endpoint), "Join");
                if (server.isHistory)
                {
                    ClientManager.ServerInfo captured = server;
                    Builder.CreateButton(row, 60, 24, 0, 0,
                        () =>
                        {
                            ClientManager.RemoveFromServerHistory(captured);
                            ShowServerList(lastScannedServers);
                        },
                        "X");
                }
                else
                {
                    // 用空标签占位
                    Builder.CreateLabel(row, 60, 24, text: "");
                }
            }
        }

        /// <summary>
        /// 向历史服务器定向询问信息并更新显示
        /// </summary>
        static async void QueryHistoryServer(ClientManager.ServerInfo server)
        {
            if (Time.unscaledTime - server.lastQueried < 5f)
                return;
            server.lastQueried = Time.unscaledTime;
            ClientManager.ServerInfo info = await ClientManager.GetServerInfo(server.endpoint);
            // 菜单已关闭或无响应时不做处理
            if (info == null || serverListHolder == null)
                return;
            server.name = string.IsNullOrWhiteSpace(info.name) ? server.name : info.name;
            server.playerCount = info.playerCount;
            server.maxPlayers = info.maxPlayers;
            server.allowedVersions = info.allowedVersions;
            server.hasPassword = info.hasPassword;
            ShowServerList(lastScannedServers);
        }

        /// <summary>
        /// 创建表格列标签
        /// </summary>
        static void CreateColumnLabel(Container parent, int width, string text)
        {
            Builder.CreateLabel(parent, width, 24, text: text).TextAlignment = TMPro.TextAlignmentOptions.MidlineLeft;
        }

        /// <summary>
        /// 选择服务器并填充连接信息
        /// </summary>
        static void SelectServer(IPEndPoint endpoint)
        {
            serverListActive = false;
            main.joinInfo.address = endpoint.Address;
            main.joinInfo.port = endpoint.Port;
            main.input_address.Text = endpoint.Address.ToString();
            main.input_port.Text = endpoint.Port.ToString();
            DestroyServerList();
        }

        async void CheckAndJoin()
        {
            try
            {
                input_address.FieldColor   = defaultTextInputColor;
                input_port.FieldColor      = defaultTextInputColor;
                input_username.FieldColor  = defaultTextInputColor;
                input_password.FieldColor  = defaultTextInputColor;

                if (await TryParseAddress(input_address.Text) is null)
                {
                    input_address.FieldColor = Color.red;
                    MsgDrawer.main.Log("IP address is invalid");
                    return;
                }
                if (!(int.TryParse(input_port.Text, out int n_port) && n_port > 0))
                {
                    input_port.FieldColor = Color.red;
                    MsgDrawer.main.Log("Port is invalid");
                    return;
                }
                if (string.IsNullOrWhiteSpace(input_username.Text))
                {
                    input_username.FieldColor = Color.red;
                    MsgDrawer.main.Log("Username cannot be empty");
                    return;
                }
                MsgDrawer.main.Log("Attempting to connect...");
                
                await ClientManager.TryConnect(joinInfo);
            }
            catch (Exception e)
            {
                if (e is AggregateException ae && ae.InnerException is OperationCanceledException)
                {
                    MsgDrawer.main.Log("");
                }
                else
                {
                    MsgDrawer.main.Log("An error occured... (Check console)");
                    Debug.LogError(e);
                }
            }
        }

        static async Task<IPAddress> TryParseAddress(string input)
        {
            if (IPAddress.TryParse(input, out IPAddress ip))
            {
                return ip;
            }
            try
            {
                if ((await Dns.GetHostAddressesAsync(input)).First() is IPAddress res)
                {
                    return res;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}