using HarmonyLib;
using SFS.World;
using SFS.Parts.Modules;
namespace MultiplayerSFS.Mod.Patches
{
    public class DisableClientCrashes
    {
        [HarmonyPatch(typeof(Trajectory), nameof(Trajectory.CheckEncounters))]
        public class Trajectory_CheckEncounters
        {
            public static bool Prefix()
            {
                return !ClientManager.multiplayerEnabled.Value;
            }
        }

        [HarmonyPatch(typeof(Trajectory), nameof(Trajectory.GetPathEndTime))]
        public class Trajectory_GetPathEndTime
        {
            public static bool Prefix()
            {
                return !ClientManager.multiplayerEnabled.Value;
            }
        }

        [HarmonyPatch(typeof(Trajectory), nameof(Trajectory.CheckPathTransition))]
        public class Trajectory_CheckPathTransition
        {
            public static bool Prefix()
            {
                return !ClientManager.multiplayerEnabled.Value;
            }
        }

        [HarmonyPatch(typeof(Trajectory), nameof(Trajectory.GetLocation))]
        public class Trajectory_GetLocation
        {
            public static bool Prefix()
            {
                return !ClientManager.multiplayerEnabled.Value;
            }
        }

        [HarmonyPatch(typeof(ParachuteModule), "LateUpdate")]
        public class ParachuteModule_LateUpdate
        {
            public static bool Prefix()
            {
                return !ClientManager.multiplayerEnabled.Value;
            }
        }
    }
}