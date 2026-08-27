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
        static JoinInfo hostJoinInfo;
        static MultiplayerSFS.Server.ServerSettings hostSettings;

        public static void OnStart(JoinInfo joinInfo, MultiplayerSFS.Server.ServerSettings settings)
        {
            if (isHosting)
                return;
            isHosting = true;
            hostJoinInfo = joinInfo;
            hostSettings = settings;
            hostSettings.worldSavePath = Base.worldBase.paths?.path.ToString();
            SceneLoader.ExitToMainMenu();
            SceneHelper.OnHomeSceneLoaded += OnHomeLoaded;
        }

        static void OnHomeLoaded()
        {
            SceneHelper.OnHomeSceneLoaded -= OnHomeLoaded;
            if (!isHosting)
                return;
            if (!InitializeServer())
            {
                isHosting = false;
                return;
            }
            serverThread = new Thread(HostServer.Run) { IsBackground = true };
            serverThread.Start();
            JoinSelf();
        }

        static bool InitializeServer()
        {
            try
            {
                HostServer.isOpenToLan = true;
                HostServer.Initialize(hostSettings);
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
