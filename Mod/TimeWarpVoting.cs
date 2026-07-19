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
        public static bool isTimeWarping { get; private set; } = false;
        public static float currentTimeScale { get; private set; } = 1f;
        
        // 投票状态
        private static int currentVoteId = 0;
        private static bool isVoting = false;
        private static float requestedTimeScale = 1f;
        
        // UI元素
        private static ModGUI.Window voteWindow = null;
        private static ModGUI.Window timeWarpIndicator = null;
        
        /// <summary>
        /// 玩家点击时间加速按钮时调用
        /// </summary>
        public static void OnTimeWarpRequested()
        {
            if (!ClientManager.multiplayerEnabled.Value)
            {
                // 单人模式直接使用原版逻辑
                WorldTime.main.AccelerateTime();
                return;
            }
            
            // 如果已经在时间加速中，直接停止
            if (isTimeWarping)
            {
                StopTimeWarp();
                return;
            }
            
            // 显示倍率选择窗口
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
                280,
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
            
            // 自定义输入
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
            
            // 确认按钮
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
            
            // 取消按钮
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
        /// 请求时间加速（发送投票请求到服务器）
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
                    PhysicsWarp = true,
                }
            );
            
            MsgDrawer.main.Log($"Requested time warp to {timeScale}x");
        }
        
        /// <summary>
        /// 收到服务器的投票请求
        /// </summary>
        public static void OnVoteRequestReceived(Packet_TimeWarpVote packet)
        {
            if (isVoting)
                return; // 已经在投票中，忽略
            
            currentVoteId = packet.VoteId;
            requestedTimeScale = packet.TimeScale;
            isVoting = true;
            
            ShowVoteDialog(packet.RequesterName, packet.TimeScale, packet.VoteId);
        }
        
        /// <summary>
        /// 显示投票对话框
        /// </summary>
        private static void ShowVoteDialog(string requesterName, float timeScale, int voteId)
        {
            if (voteWindow != null)
                return;
            
            var holder = ModGUI.Builder.CreateHolder(ModGUI.Builder.SceneToAttach.CurrentScene, "TimeWarp Vote");
            voteWindow = ModGUI.Builder.CreateWindow
            (
                holder.transform,
                ModGUI.Builder.GetRandomID(),
                300,
                150,
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
                    isTimeWarping = false;
                    currentTimeScale = 1f;
                    ApplyTimeScale(1f);
                    HideTimeWarpIndicator();
                    
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
                    isTimeWarping = true;
                    currentTimeScale = packet.TimeScale;
                    ApplyTimeScale(packet.TimeScale);
                    MsgDrawer.main.Log($"Time warp started: {packet.TimeScale}x");
                    ShowTimeWarpIndicator(packet.TimeScale);
                }
            }
            else
            {
                // 投票被拒绝
                isTimeWarping = false;
                currentTimeScale = 1f;
                
                // 显示拒绝者名字
                if (!string.IsNullOrEmpty(packet.RejecterName))
                {
                    MsgDrawer.main.Log($"{packet.RejecterName} rejected the time warp request");
                }
                else
                {
                    MsgDrawer.main.Log("Time warp request rejected");
                }
                
                HideTimeWarpIndicator();
                CloseVoteWindow(); // 关闭投票窗口
            }
        }
        
        /// <summary>
        /// 应用时间倍率
        /// </summary>
        private static void ApplyTimeScale(float timeScale)
        {
            // 物理加速：同时调整Time.timeScale和WorldTime.timeScale
            Time.timeScale = timeScale;
            if (WorldTime.main != null)
            {
                // 使用反射设置timeScale字段
                var timeScaleField = typeof(WorldTime).GetField("timeScale", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (timeScaleField != null)
                {
                    timeScaleField.SetValue(WorldTime.main, timeScale);
                }
            }
        }
        
        /// <summary>
        /// 停止时间加速（任何玩家都可以停止）
        /// </summary>
        private static void StopTimeWarp()
        {
            isTimeWarping = false;
            currentTimeScale = 1f;
            ApplyTimeScale(1f);
            HideTimeWarpIndicator();
            
            // 通知服务器
            ClientManager.SendPacket
            (
                new Packet_TimeWarpRequest()
                {
                    TimeScale = 1f,
                    RequesterName = LocalManager.Player?.username ?? "Unknown",
                    PhysicsWarp = true,
                }
            );
            
            MsgDrawer.main.Log("Time warp stopped");
        }
        
        /// <summary>
        /// 显示时间加速指示器（← Xx → 样式，点击箭头停止加速）
        /// </summary>
        private static void ShowTimeWarpIndicator(float timeScale)
        {
            if (timeWarpIndicator != null)
            {
                UnityEngine.Object.Destroy(timeWarpIndicator.gameObject);
                timeWarpIndicator = null;
            }
            
            var holder = ModGUI.Builder.CreateHolder(ModGUI.Builder.SceneToAttach.CurrentScene, "TimeWarp Indicator");
            timeWarpIndicator = ModGUI.Builder.CreateWindow
            (
                holder.transform,
                ModGUI.Builder.GetRandomID(),
                120,
                40,
                Screen.width - 130,
                10,
                true,
                false,
                0.8f,
                ""
            );
            timeWarpIndicator.CreateLayoutGroup
            (
                ModGUI.Type.Horizontal,
                TextAnchor.MiddleCenter,
                spacing: 2f,
                padding: new RectOffset(2, 2, 2, 2)
            );
            
            // 左箭头按钮 - 点击停止时间加速
            ModGUI.Builder.CreateButton
            (
                timeWarpIndicator, 
                30, 
                32, 
                0, 
                0, 
                StopTimeWarp,
                "\u2190"
            );
            
            // 中间显示当前倍率
            ModGUI.Builder.CreateLabel
            (
                timeWarpIndicator, 
                50, 
                32, 
                0, 
                0, 
                $"{timeScale}x"
            );
            
            // 右箭头按钮 - 点击停止时间加速
            ModGUI.Builder.CreateButton
            (
                timeWarpIndicator, 
                30, 
                32, 
                0, 
                0, 
                StopTimeWarp,
                "\u2192"
            );
        }
        
        /// <summary>
        /// 隐藏时间加速指示器
        /// </summary>
        private static void HideTimeWarpIndicator()
        {
            if (timeWarpIndicator != null)
            {
                UnityEngine.Object.Destroy(timeWarpIndicator.gameObject);
                timeWarpIndicator = null;
            }
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
            }
        }
        
        /// <summary>
        /// 重置状态（断开连接时调用）
        /// </summary>
        public static void Reset()
        {
            isTimeWarping = false;
            currentTimeScale = 1f;
            isVoting = false;
            currentVoteId = 0;
            
            CloseVoteWindow();
            HideTimeWarpIndicator();
            
            Time.timeScale = 1f;
            if (WorldTime.main != null)
            {
                // 使用反射设置timeScale字段
                var timeScaleField = typeof(WorldTime).GetField("timeScale", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (timeScaleField != null)
                {
                    timeScaleField.SetValue(WorldTime.main, 1f);
                }
            }
        }
    }
}
