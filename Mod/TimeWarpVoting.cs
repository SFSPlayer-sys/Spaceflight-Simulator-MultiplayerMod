using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using SFS.UI;
using ModGUI = SFS.UI.ModGUI;
using SFS.World;
using ModLoader.Helpers;
using MultiplayerSFS.Common;

namespace MultiplayerSFS.Mod
{
    /// <summary>
    /// 时间加速投票系统
    /// </summary>
    public static class TimeWarpVoting
    {
        // 当前时间加速状态
        public static bool IsTimeWarping { get; private set; } = false;
        public static float CurrentTimeScale { get; private set; } = 1f;
        public static bool CurrentPhysicsWarp { get; private set; } = false;

        private static int currentVoteId = 0;
        private static bool isVoting = false;
        private static float requestedTimeScale = 1f;
        private static bool requestedPhysicsWarp = false;

        private static ModGUI.Window voteWindow = null;
        private static ModGUI.Button physicsWarpToggleBtn = null;
        private static bool usePhysicsWarp = true; 
        /// <summary>
        /// 玩家点击时间加速按钮
        /// </summary>
        public static void OnTimeWarpRequested()
        {
            if (!ClientManager.multiplayerEnabled.Value)
            {
                WorldTime.main.AccelerateTime();
                return;
            }
            
            // 已经在时间加速中，停止当前加速
            if (IsTimeWarping)
            {
                StopTimeWarp();
                return;
            }
            ShowTimeScaleSelection();
        }
        
        /// <summary>
        /// 显示倍率选择窗口
        /// </summary>
        private static void ShowTimeScaleSelection()
        {
            if (voteWindow != null)
                return;
            
            var holder = ModGUI.Builder.CreateHolder(ModGUI.Builder.SceneToAttach.CurrentScene, "TimeWarp Selection");
            voteWindow = ModGUI.Builder.CreateWindow
            (
                holder.transform,
                ModGUI.Builder.GetRandomID(),
                300,
                350,
                0,
                0,
                true,
                true,
                0.95f,
                "Request Timewarp"
            );
            voteWindow.CreateLayoutGroup
            (
                ModGUI.Type.Vertical,
                TextAnchor.UpperLeft,
                spacing: 8f,
                padding: new RectOffset(10, 10, 10, 10)
            );
            
            // 说明文字
            ModGUI.Builder.CreateLabel(voteWindow, 280, 20, 0, 0, "Select Speed");
            
            // 倍率按钮 - 第一行
            var row1Container = ModGUI.Builder.CreateContainer(voteWindow);
            row1Container.CreateLayoutGroup
            (
                ModGUI.Type.Horizontal,
                TextAnchor.UpperLeft,
                spacing: 5f
            );
            
            float[] scales1 = new float[] { 2f, 4f, 8f, 16f, 32f };
            foreach (float scale in scales1)
            {
                float s = scale;
                ModGUI.Builder.CreateButton
                (
                    row1Container, 
                    52, 
                    30, 
                    0, 
                    0, 
                    () => RequestTimeWarp(s),
                    $"{scale}x"
                );
            }
            
            // 倍率按钮 - 第二行
            var row2Container = ModGUI.Builder.CreateContainer(voteWindow);
            row2Container.CreateLayoutGroup
            (
                ModGUI.Type.Horizontal,
                TextAnchor.UpperLeft,
                spacing: 5f
            );
            
            float[] scales2 = new float[] { 5f, 10f, 15f, 30f, 60f };
            foreach (float scale in scales2)
            {
                float s = scale;
                ModGUI.Builder.CreateButton
                (
                    row2Container, 
                    52, 
                    30, 
                    0, 
                    0, 
                    () => RequestTimeWarp(s),
                    $"{scale}x"
                );
            }
            // 自定义倍率
            ModGUI.Builder.CreateLabel(voteWindow, 280, 20, 0, 0, "Custom:");
            string inputText = "5";
            var textInput = ModGUI.Builder.CreateTextInput
            (
                voteWindow, 
                280, 
                30, 
                0, 
                0, 
                inputText,
                (string newText) => { inputText = newText; }
            );
            // 物理加速
            physicsWarpToggleBtn = ModGUI.Builder.CreateButton
            (
                voteWindow, 
                280, 
                30, 
                0, 
                0, 
                TogglePhysicsWarp,
                usePhysicsWarp ? "Physics: ON" : "Physics: OFF"
            );
            // 确认
            ModGUI.Builder.CreateButton
            (
                voteWindow, 
                280, 
                35, 
                0, 
                0, 
                () => 
                {
                    if (float.TryParse(inputText, out float scale) && scale >= 0f)
                    {
                        RequestTimeWarp(scale);
                    }
                    else
                    {
                        MsgDrawer.main.Log("Invalid time scale value");
                    }
                },
                "Request Timewarp"
            );
            // 取消
            ModGUI.Builder.CreateButton
            (
                voteWindow, 
                280, 
                35, 
                0, 
                0, 
                CloseVoteWindow,
                "Cancel"
            );
        }
        
        /// <summary>
        /// 切换物理加速
        /// </summary>
        private static void TogglePhysicsWarp()
        {
            usePhysicsWarp = !usePhysicsWarp;
            if (physicsWarpToggleBtn != null)
            {
                physicsWarpToggleBtn.Text = usePhysicsWarp ? "Physics: ON" : "Physics: OFF";
            }
        }
        
        /// <summary>
        /// 请求时间加速
        /// </summary>
        private static void RequestTimeWarp(float timeScale)
        {
            CloseVoteWindow();
            
            ClientManager.SendPacket
            (
                new Packet_TimeWarpRequest()
                {
                    TimeScale = timeScale,
                    RequesterName = LocalManager.Player?.username ?? "Unknown",
                    PhysicsWarp = usePhysicsWarp,
                }
            );
            
            MsgDrawer.main.Log($"Requested time warp to {timeScale}x ({(usePhysicsWarp ? "Physics" : "WorldTime")})");
        }
        
        /// <summary>
        /// 收到服务器的投票请求
        /// </summary>
        public static void OnVoteRequestReceived(Packet_TimeWarpVote packet)
        {
            if (isVoting)
                return; // 已经在投票中
            currentVoteId = packet.VoteId;
            requestedTimeScale = packet.TimeScale;
            requestedPhysicsWarp = packet.PhysicsWarp;
            isVoting = true;
            
            ShowVoteDialog(packet.RequesterName, packet.TimeScale, packet.VoteId, packet.PhysicsWarp);
        }
        
        /// <summary>
        /// 显示投票对话框
        /// </summary>
        private static void ShowVoteDialog(string requesterName, float timeScale, int voteId, bool physicsWarp)
        {
            if (voteWindow != null)
                return;
            
            var holder = ModGUI.Builder.CreateHolder(ModGUI.Builder.SceneToAttach.CurrentScene, "TimeWarp Vote");
            voteWindow = ModGUI.Builder.CreateWindow
            (
                holder.transform,
                ModGUI.Builder.GetRandomID(),
                300,
                180,
                0,
                0,
                true,
                true,
                0.95f,
                "Timewarp Vote"
            );
            voteWindow.CreateLayoutGroup
            (
                ModGUI.Type.Vertical,
                TextAnchor.UpperLeft,
                spacing: 10f,
                padding: new RectOffset(10, 10, 10, 10)
            );
            
            // 显示请求信息
            ModGUI.Builder.CreateLabel
            (
                voteWindow, 
                280, 
                40, 
                0, 
                0, 
                $"{requesterName} requests {timeScale}x timewarp"
            );
            // 显示加速类型
            ModGUI.Builder.CreateLabel
            (
                voteWindow, 
                280, 
                20, 
                0, 
                0, 
                $"Mode: {(physicsWarp ? "Physics" : "WorldTime")}"
            );
            var buttonContainer = ModGUI.Builder.CreateContainer(voteWindow);
            buttonContainer.CreateLayoutGroup
            (
                ModGUI.Type.Horizontal,
                TextAnchor.MiddleCenter,
                spacing: 20f
            );
            
            ModGUI.Builder.CreateButton
            (
                buttonContainer, 
                100, 
                40, 
                0, 
                0, 
                () => Vote(true, voteId),
                "Approve"
            );
            
            ModGUI.Builder.CreateButton
            (
                buttonContainer, 
                100, 
                40, 
                0, 
                0, 
                () => Vote(false, voteId),
                "Reject"
            );
        }
        
        /// <summary>
        /// 投票
        /// </summary>
        private static void Vote(bool agreed, int voteId)
        {
            if (voteId != currentVoteId)
                return;
            
            CloseVoteWindow();
            
            ClientManager.SendPacket
            (
                new Packet_TimeWarpVoteResponse()
                {
                    VoteId = voteId,
                    Agreed = agreed,
                }
            );
            
            MsgDrawer.main.Log(agreed ? "Voted: Agree" : "Voted: Reject");
        }
        /// <summary>
        /// 收到投票结果
        /// </summary>
        public static void OnVoteResultReceived(Packet_TimeWarpResult packet)
        {
            isVoting = false;
            
            if (packet.Approved)
            {
                if (packet.TimeScale == 1f && packet.VoteId == -1)
                {
                    // 停止时间加速
                    IsTimeWarping = false;
                    CurrentTimeScale = 1f;
                    CurrentPhysicsWarp = false;
                    ApplyTimeScale(1f, false);
                    if (!string.IsNullOrEmpty(packet.StopperName))
                    {
                        MsgDrawer.main.Log($"{packet.StopperName} ended the time warp");
                    }
                    else
                    {
                        MsgDrawer.main.Log("Time warp ended");
                    }
                }
                else
                {
                    // 开始时间加速
                    IsTimeWarping = true;
                    CurrentTimeScale = packet.TimeScale;
                    CurrentPhysicsWarp = packet.PhysicsWarp;
                    ApplyTimeScale(packet.TimeScale, packet.PhysicsWarp);
                    MsgDrawer.main.Log($"Time warp started: {packet.TimeScale}x ({(packet.PhysicsWarp ? "Physics" : "WorldTime")})");
                }
            }
            else
            {
                // 投票被拒绝
                IsTimeWarping = false;
                CurrentTimeScale = 1f;
                CurrentPhysicsWarp = false;
                if (!string.IsNullOrEmpty(packet.RejecterName))
                {
                    MsgDrawer.main.Log($"{packet.RejecterName} rejected the time warp request");
                }
                else
                {
                    MsgDrawer.main.Log("Time warp request rejected");
                }
                
                CloseVoteWindow();
            }
        }
        /// <summary>
        /// 应用加速
        /// </summary>
        private static void ApplyTimeScale(float timeScale, bool physicsWarp)
        {
            if (WorldTime.main != null)
            {
                WorldTime.main.SetState(timeScale, physicsWarp, false);
            }
        }
        
        /// <summary>
        /// 停止加速
        /// </summary>
        private static void StopTimeWarp()
        {
            IsTimeWarping = false;
            CurrentTimeScale = 1f;
            CurrentPhysicsWarp = false;
            ApplyTimeScale(1f, false);
            
            // 通知服务器
            ClientManager.SendPacket
            (
                new Packet_TimeWarpRequest()
                {
                    TimeScale = 1f,
                    RequesterName = LocalManager.Player?.username ?? "Unknown",
                    PhysicsWarp = false,
                }
            );
            
            MsgDrawer.main.Log("Time warp stopped");
        }
        
        /// <summary>
        /// 关闭投票窗口
        /// </summary>
        private static void CloseVoteWindow()
        {
            if (voteWindow != null)
            {
                UnityEngine.Object.Destroy(voteWindow.gameObject);
                voteWindow = null;
                physicsWarpToggleBtn = null;
            }
        }
        
        /// <summary>
        /// 重置状态（断开连接时调用）
        /// </summary>
        public static void Reset()
        {
            IsTimeWarping = false;
            CurrentTimeScale = 1f;
            CurrentPhysicsWarp = false;
            isVoting = false;
            currentVoteId = 0;
            
            CloseVoteWindow();
            
            // 使用SetState方法重置时间
            if (WorldTime.main != null)
            {
                WorldTime.main.SetState(1f, false, false);
            }
        }

        /// <summary>
        /// 同步当前时间加速状态（发射火箭后调用）
        /// </summary>
        public static void SyncCurrentTimeWarp()
        {
            if (IsTimeWarping && WorldTime.main != null)
            {
                ApplyTimeScale(CurrentTimeScale, CurrentPhysicsWarp);
            }
        }
    }
}
