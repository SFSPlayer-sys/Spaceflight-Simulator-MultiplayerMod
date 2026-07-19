using HarmonyLib;
using SFS.UI;
using SFS.World;
using SFS.World.Maps;

namespace MultiplayerSFS.Mod.Patches
{
    /// <summary>
    /// 替换原版时间加速逻辑为投票系统
    /// </summary>
    public class DisableTimewarp
    {
        [HarmonyPatch(typeof(WorldTime), nameof(WorldTime.AccelerateTime))]
        public class WorldTime_AccelerateTime
        {
            public static bool Prefix()
            {
                if (ClientManager.multiplayerEnabled.Value)
                {
                    // 多人模式下弹出倍率选择窗口
                    TimeWarpVoting.OnTimeWarpRequested();
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(WorldTime), nameof(WorldTime.DecelerateTime))]
        public class WorldTime_DecelerateTime
        {
            public static bool Prefix()
            {
                if (ClientManager.multiplayerEnabled.Value)
                {
                    // 多人模式下停止时间加速
                    TimeWarpVoting.OnTimeWarpRequested();
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(TimewarpTo), nameof(TimewarpTo.StartTimewarp))]
        public class TimewarpTo_StartTimewarp
        {
            public static bool Prefix()
            {
                if (ClientManager.multiplayerEnabled.Value)
                {
                    // 多人模式下弹出倍率选择窗口
                    TimeWarpVoting.OnTimeWarpRequested();
                    return false;
                }
                return true;
            }
        }
    }
}

