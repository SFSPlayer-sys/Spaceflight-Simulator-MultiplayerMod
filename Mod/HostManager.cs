using System;
using System.Net;
using System.Threading;
using SFS;
using ModLoader.Helpers;
using HostServer = MultiplayerSFS.Server.Server;

namespace MultiplayerSFS.Mod
{
    public static class HostManager
    {
        static Thread serverThread;
        public static bool isHosting = false;
        static string worldSavePath;
        static JoinInfo hostJoinInfo;

        public static void OnStart(JoinInfo joinInfo)
        {
            if (isHosting)
                return;
            isHosting = true;
            hostJoinInfo = joinInfo;
            // 记录世界存档路径（退到主界面后 Base.worldBase.paths 会被清空）
            worldSavePath = Base.worldBase.paths?.path.ToString();
            // 保存当前世界存档并按游戏方法退到主界面
            SceneLoader.ExitToMainMenu();
            SceneHelper.OnHomeSceneLoaded += OnHomeLoaded;
        }

        static void OnHomeLoaded()
        {
            SceneHelper.OnHomeSceneLoaded -= OnHomeLoaded;
            if (!isHosting)
                return;
            // 主线程初始化服务器
            if (!InitializeServer())
            {
                isHosting = false;
                return;
            }
            // 后台运行服务器消息循环
            serverThread = new Thread(HostServer.Run) { IsBackground = true };
            serverThread.Start();
            // 加入自己创建的服务器
            JoinSelf();
        }

        static bool InitializeServer()
        {
            try
            {
                string password = string.IsNullOrWhiteSpace(hostJoinInfo.password) || hostJoinInfo.password == "DEFAULT_PASSWORD" ? "" : hostJoinInfo.password;
                MultiplayerSFS.Server.ServerSettings settings = new MultiplayerSFS.Server.ServerSettings()
                {
                    port = hostJoinInfo.port,
                    serverName = "HOST Game",
                    serverPassword = password,
                    adminPassword = password,
                    maxConnections = 16,
                    worldSavePath = worldSavePath,
                };
                HostServer.Initialize(settings);
                return true;
            }
            catch (Exception e)
            {
                Logger.Error(e);
                return false;
            }
        }

        static async void JoinSelf()
        {
            try
            {
                ClientManager.multiplayerEnabled.Value = true;
                hostJoinInfo.address = IPAddress.Loopback;
                await ClientManager.TryConnect(hostJoinInfo);
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }

        public static void OnStop()
        {
            isHosting = false;
            HostServer.Stop();
            ClientManager.client?.Shutdown("Host stopped");
            if (serverThread != null)
            {
                serverThread.Join(1000);
                serverThread = null;
            }
        }
    }
}
