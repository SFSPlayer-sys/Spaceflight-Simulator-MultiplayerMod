using System.Collections.Generic;
using Lidgren.Network;

namespace MultiplayerSFS.ServerCommon
{
    public enum PacketType
    {
        // * Player/server Info Packets
        JoinRequest,
        JoinResponse,
        PlayerConnected,
        PlayerDisconnected,
        UpdatePlayerControl,
        UpdatePlayerAuthority,
        UpdateWorldTime,
        UpdatePlayerColor,
        SendChatMessage,
        ShowToastMessage,

        // * Rocket Packets
        CreateRocket,
        DestroyRocket,
        UpdateRocketPrimary,
        UpdateRocketSecondary,

        // * Part & Staging Packets
        DestroyPart,
        UpdateStaging,
        UpdatePart_EngineModule,
        UpdatePart_WheelModule,
        UpdatePart_BoosterModule,
        UpdatePart_ParachuteModule,
        UpdatePart_MoveModule,
        UpdatePart_ResourceModule,
        UpdateCheatStatus,

        // * Time Warp Packets
        TimeWarpRequest,
        TimeWarpVote,
        TimeWarpVoteResponse,
        TimeWarpResult,
    }

    public abstract class Packet : INetData
    {
        public abstract PacketType Type { get; }
        public abstract void Serialize(NetOutgoingMessage msg);
        public abstract void Deserialize(NetIncomingMessage msg);

        static readonly HashSet<PacketType> DoNotDebug = new HashSet<PacketType>
        {
            PacketType.UpdateRocketPrimary,
            PacketType.UpdateRocketSecondary,
            PacketType.UpdatePart_ResourceModule,
        };
        public static bool ShouldDebug(PacketType type) => !DoNotDebug.Contains(type);
    }

    // * Player/server Info Packets
    public class Packet_JoinRequest : Packet
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public string SolarSystemName { get; set; } = "";
        public string GameVersion { get; set; } = "";

        public override PacketType Type => PacketType.JoinRequest;
        public override void Serialize(NetOutgoingMessage msg)
        {
            msg.Write(Username);
            msg.Write(Password);
            msg.Write(SolarSystemName);
            msg.Write(GameVersion);
        }
        public override void Deserialize(NetIncomingMessage msg)
        {
            Username = msg.ReadString();
            Password = msg.ReadString();
            SolarSystemName = msg.ReadString();
            GameVersion = msg.ReadString();
        }
    }

    public class Packet_JoinResponse : Packet
    {
        public int PlayerId { get; set; } = -1;
        public double UpdateRocketsPeriod { get; set; }
        public double ChatMessageCooldown { get; set; }
        public double WorldTime { get; set; } = double.NaN;
        public double SendTime { get; set; }
        public int Difficulty { get; set; }
        public string SolarSystemName { get; set; } = "";

        public override PacketType Type => PacketType.JoinResponse;
        public override void Serialize(NetOutgoingMessage msg)
        {
            msg.Write(PlayerId);
            msg.Write(UpdateRocketsPeriod);
            msg.Write(ChatMessageCooldown);
            msg.Write(WorldTime);
            msg.Write(SendTime);
            msg.Write((byte)Difficulty);
            msg.Write(SolarSystemName);
        }

        public override void Deserialize(NetIncomingMessage msg)
        {
            PlayerId = msg.ReadInt32();
            UpdateRocketsPeriod = msg.ReadDouble();
            ChatMessageCooldown = msg.ReadDouble();
            WorldTime = msg.ReadDouble();
            SendTime = msg.ReadDouble();
            Difficulty = msg.ReadByte();
            SolarSystemName = msg.ReadString();
        }
    }

    public class Packet_PlayerConnected : Packet
    {
        public int PlayerId { get; set; } = -1;
        public string Username { get; set; }
        public Color IconColor { get; set; }
        public bool PrintMessage { get; set; }

        public override PacketType Type => PacketType.PlayerConnected;
        public override void Serialize(NetOutgoingMessage msg)
        {
            msg.WriteCompressedInt(PlayerId);
            msg.WriteCompressedString(Username);
            msg.WriteCompressedColor(IconColor);
            msg.Write(PrintMessage);
        }
        public override void Deserialize(NetIncomingMessage msg)
        {
            PlayerId = msg.ReadCompressedInt();
            Username = msg.ReadCompressedString();
            IconColor = msg.ReadCompressedColor();
            PrintMessage = msg.ReadBoolean();
        }
    }

    public class Packet_PlayerDisconnected : Packet
    {
        public int PlayerId { get; set; } = -1;

        public override PacketType Type => PacketType.PlayerDisconnected;
        public override void Serialize(NetOutgoingMessage msg)
        {
            msg.Write(PlayerId);
        }
        public override void Deserialize(NetIncomingMessage msg)
        {
            PlayerId = msg.ReadInt32();
        }
    }

    public class Packet_UpdatePlayerControl : Packet
    {
        public int PlayerId { get; set; } = -1;
        public int RocketId { get; set; } = -1;
        
        public override PacketType Type => PacketType.UpdatePlayerControl;
        public override void Serialize(NetOutgoingMessage msg)
        {
            msg.Write(PlayerId);
            msg.Write(RocketId);
        }
        public override void Deserialize(NetIncomingMessage msg)
        {
            PlayerId = msg.ReadInt32();
            RocketId = msg.ReadInt32();
        }
    }

    public class Packet_UpdatePlayerAuthority : Packet
    {
        public HashSet<int> RocketIds { get; set; }
        
        public override PacketType Type => PacketType.UpdatePlayerAuthority;
        public override void Serialize(NetOutgoingMessage msg)
        {
            msg.WriteCollection(RocketIds, msg.Write);
        }
        public override void Deserialize(NetIncomingMessage msg)
        {
            RocketIds = msg.ReadCollection(count => new HashSet<int>(count), msg.ReadInt32);
        }
    }

    public class Packet_UpdateWorldTime : Packet
    {
        public double WorldTime { get; set; } = double.NaN;
        
        public override PacketType Type => PacketType.UpdateWorldTime;
        public override void Serialize(NetOutgoingMessage msg)
        {
            msg.Write(WorldTime);
        }
        public override void Deserialize(NetIncomingMessage msg)
        {
            WorldTime = msg.ReadDouble();
        }
    }

    public class Packet_UpdatePlayerColor : Packet
    {
        public int PlayerId { get; set; } = -1;
        public Color Color { get; set; }

        public override PacketType Type => PacketType.UpdatePlayerColor;
        public override void Serialize(NetOutgoingMessage msg)
        {
            msg.WriteCompressedInt(PlayerId);
            msg.WriteCompressedColor(Color);
        }
        public override void Deserialize(NetIncomingMessage msg)
        {
            PlayerId = msg.ReadCompressedInt();
            Color = msg.ReadCompressedColor();
        }
    }

    public class Packet_SendChatMessage : Packet
    {
        public int SenderId { get; set; } = -1;
        public string Message { get; set; }
        public System.Drawing.Color Color { get; set; } = System.Drawing.Color.White; // 默认白色

        public override PacketType Type => PacketType.SendChatMessage;
        public override void Serialize(NetOutgoingMessage msg)
        {
            msg.Write(SenderId);
            msg.Write(Message);
            msg.Write(Color.R / 255f);
            msg.Write(Color.G / 255f);
            msg.Write(Color.B / 255f);
            msg.Write(Color.A / 255f);
        }
        public override void Deserialize(NetIncomingMessage msg)
        {
            SenderId = msg.ReadInt32();
            Message = msg.ReadString();
            float r = msg.ReadFloat();
            float g = msg.ReadFloat();
            float b = msg.ReadFloat();
            float a = msg.ReadFloat();
            Color = System.Drawing.Color.FromArgb((int)(a * 255), (int)(r * 255), (int)(g * 255), (int)(b * 255));
        }
    }

    public class Packet_ShowToastMessage : Packet
    {
        public string Message { get; set; }

        public override PacketType Type => PacketType.ShowToastMessage;
        public override void Serialize(NetOutgoingMessage msg)
        {
            msg.Write(Message);
        }
        public override void Deserialize(NetIncomingMessage msg)
        {
            Message = msg.ReadString();
        }
    }

    // * Rocket Packets
    public class Packet_CreateRocket : Packet
    {
        public double WorldTime { get; set; } = double.NaN;
        public int LocalId { get; set; } = -1;
        public int GlobalId { get; set; } = -1;
        public bool ForLaunch { get; set; } = false;
        public RocketState Rocket { get; set; }

        public override PacketType Type => PacketType.CreateRocket;
        public override void Serialize(NetOutgoingMessage msg)
        {
            msg.WriteCompressedDouble(WorldTime);
            msg.WriteCompressedInt(LocalId);
            msg.WriteCompressedInt(GlobalId);
            msg.Write(ForLaunch);
            msg.Write(Rocket);
        }
        public override void Deserialize(NetIncomingMessage msg)
        {
            WorldTime = msg.ReadCompressedDouble();
            LocalId = msg.ReadCompressedInt();
            GlobalId = msg.ReadCompressedInt();
            ForLaunch = msg.ReadBoolean();
            Rocket = msg.Read<RocketState>();
        }
    }

    public class Packet_DestroyRocket : Packet
    {
        public double WorldTime { get; set; } = double.NaN;
        public int RocketId { get; set; } = -1;
        public int Reason { get; set; }

        public override PacketType Type => PacketType.DestroyRocket;
        public override void Serialize(NetOutgoingMessage msg)
        {
            msg.Write(WorldTime);
            msg.Write(RocketId);
            msg.Write((byte)Reason);
        }
        public override void Deserialize(NetIncomingMessage msg)
        {
            WorldTime = msg.ReadDouble();
            RocketId = msg.ReadInt32();
            Reason = msg.ReadByte();
        }
    }

    public class Packet_UpdateRocketPrimary : Packet
    {
        public double WorldTime { get; set; } = double.NaN;
        public int RocketId { get; set; } = -1;
        public NetLocation Location { get; set; }
        public float Rotation { get; set; }
        public float AngularVelocity { get; set; }

        public override PacketType Type => PacketType.UpdateRocketPrimary;
        public override void Serialize(NetOutgoingMessage msg)
        {
            msg.Write(WorldTime);
            msg.Write(RocketId);
            msg.Write(Location);
            msg.Write(Rotation);
            msg.Write(AngularVelocity);
        }
        public override void Deserialize(NetIncomingMessage msg)
        {
            WorldTime = msg.ReadDouble();
            RocketId = msg.ReadInt32();
            Location = msg.Read<NetLocation>();
            Rotation = msg.ReadFloat();
            AngularVelocity = msg.ReadFloat();
        }
    }

    public class Packet_UpdateRocketSecondary : Packet
    {
        public double WorldTime { get; set; } = double.NaN;
        public int RocketId { get; set; } = -1;
        public float Input_Turn { get; set; }
        public Vector2 Input_Raw { get; set; }
        public Vector2 Input_Horizontal { get; set; }
        public Vector2 Input_Vertical { get; set; }
        public float ThrottlePercent { get; set; }
        public bool ThrottleOn { get; set; }
        public bool RCS { get; set; }

        public override PacketType Type => PacketType.UpdateRocketSecondary;
        public override void Serialize(NetOutgoingMessage msg)
        {
            msg.WriteCompressedDouble(WorldTime);
            msg.WriteCompressedInt(RocketId);
            msg.WriteCompressedFloat(Input_Turn);
            msg.WriteCompressedVector2(Input_Raw);
            msg.WriteCompressedVector2(Input_Horizontal);
            msg.WriteCompressedVector2(Input_Vertical);
            msg.WriteCompressedFloat(ThrottlePercent);
            msg.Write(ThrottleOn);
            msg.Write(RCS);
        }
        public override void Deserialize(NetIncomingMessage msg)
        {
            WorldTime = msg.ReadCompressedDouble();
            RocketId = msg.ReadCompressedInt();
            Input_Turn = msg.ReadCompressedFloat();
            Input_Raw = msg.ReadCompressedVector2();
            Input_Horizontal = msg.ReadCompressedVector2();
            Input_Vertical = msg.ReadCompressedVector2();
            ThrottlePercent = msg.ReadCompressedFloat();
            ThrottleOn = msg.ReadBoolean();
            RCS = msg.ReadBoolean();
        }
    }

    // * Part & Staging Packets
    public class Packet_DestroyPart : Packet
    {
        public double WorldTime { get; set; } = double.NaN;
        public int RocketId { get; set; } = -1;
        public int PartId { get; set; } = -1;
        public bool CreateExplosion { get; set; }
        public int Reason { get; set; }

        public override PacketType Type => PacketType.DestroyPart;
        public override void Serialize(NetOutgoingMessage msg)
        {
            msg.Write(WorldTime);
            msg.Write(RocketId);
            msg.Write(PartId);
            msg.Write(CreateExplosion);
            msg.Write((byte)Reason);
        }
        public override void Deserialize(NetIncomingMessage msg)
        {
            WorldTime = msg.ReadDouble();
            RocketId = msg.ReadInt32();
            PartId = msg.ReadInt32();
            CreateExplosion = msg.ReadBoolean();
            Reason = msg.ReadByte();
        }
    }

    public class Packet_UpdateStaging : Packet
    {
        public double WorldTime { get; set; } = double.NaN;
        public int RocketId { get; set; } = -1;
        public List<StageState> Stages { get; set; }

        public override PacketType Type => PacketType.UpdateStaging;
        public override void Serialize(NetOutgoingMessage msg)
        {
            msg.Write(WorldTime);
            msg.Write(RocketId);
            msg.WriteCollection(Stages, msg.Write);
        }
        public override void Deserialize(NetIncomingMessage msg)
        {
            WorldTime = msg.ReadDouble();
            RocketId = msg.ReadInt32();
            Stages = msg.ReadCollection(count => new List<StageState>(count), () => msg.Read<StageState>());
        }
    }

    public class Packet_UpdatePart_EngineModule : Packet
    {
        public double WorldTime { get; set; } = double.NaN;
        public int RocketId { get; set; } = -1;
        public int PartId { get; set; } = -1;
        public bool EngineOn { get; set; }

        public override PacketType Type => PacketType.UpdatePart_EngineModule;
        public override void Serialize(NetOutgoingMessage msg)
        {
            msg.Write(WorldTime);
            msg.Write(RocketId);
            msg.Write(PartId);
            msg.Write(EngineOn);
        }
        public override void Deserialize(NetIncomingMessage msg)
        {
            WorldTime = msg.ReadDouble();
            RocketId = msg.ReadInt32();
            PartId = msg.ReadInt32();
            EngineOn = msg.ReadBoolean();
        }
    }

    public class Packet_UpdatePart_WheelModule : Packet
    {
        public double WorldTime { get; set; } = double.NaN;
        public int RocketId { get; set; } = -1;
        public int PartId { get; set; } = -1;
        public bool WheelOn { get; set; }

        public override PacketType Type => PacketType.UpdatePart_WheelModule;
        public override void Serialize(NetOutgoingMessage msg)
        {
            msg.Write(WorldTime);
            msg.Write(RocketId);
            msg.Write(PartId);
            msg.Write(WheelOn);
        }
        public override void Deserialize(NetIncomingMessage msg)
        {
            WorldTime = msg.ReadDouble();
            RocketId = msg.ReadInt32();
            PartId = msg.ReadInt32();
            WheelOn = msg.ReadBoolean();
        }
    }

    public class Packet_UpdatePart_BoosterModule : Packet
    {
        public double WorldTime { get; set; } = double.NaN;
        public int RocketId { get; set; } = -1;
        public int PartId { get; set; } = -1;
        public bool Primed { get; set; }
        public float Throttle { get; set; }
        public float FuelPercent { get; set; }

        public override PacketType Type => PacketType.UpdatePart_BoosterModule;
        public override void Serialize(NetOutgoingMessage msg)
        {
            msg.Write(WorldTime);
            msg.Write(RocketId);
            msg.Write(PartId);
            msg.Write(Primed);
            msg.Write(Throttle);
            msg.Write(FuelPercent);
        }
        public override void Deserialize(NetIncomingMessage msg)
        {
            WorldTime = msg.ReadDouble();
            RocketId = msg.ReadInt32();
            PartId = msg.ReadInt32();
            Primed = msg.ReadBoolean();
            Throttle = msg.ReadFloat();
            FuelPercent = msg.ReadFloat();
        }
    }

    public class Packet_UpdatePart_ParachuteModule : Packet
    {
        public double WorldTime { get; set; } = double.NaN;
        public int RocketId { get; set; } = -1;
        public int PartId { get; set; } = -1;
        public float State { get; set; }
        public float TargetState { get; set; }

        public override PacketType Type => PacketType.UpdatePart_ParachuteModule;
        public override void Serialize(NetOutgoingMessage msg)
        {
            msg.Write(WorldTime);
            msg.Write(RocketId);
            msg.Write(PartId);
            msg.Write(State);
            msg.Write(TargetState);
        }
        public override void Deserialize(NetIncomingMessage msg)
        {
            WorldTime = msg.ReadDouble();
            RocketId = msg.ReadInt32();
            PartId = msg.ReadInt32();
            State = msg.ReadFloat();
            TargetState = msg.ReadFloat();
        }
    }

    public class Packet_UpdatePart_MoveModule : Packet
    {
        public double WorldTime { get; set; } = double.NaN;
        public int RocketId { get; set; } = -1;
        public int PartId { get; set; } = -1;
        public float Time { get; set; }
        public float TargetTime { get; set; }

        public override PacketType Type => PacketType.UpdatePart_MoveModule;
        public override void Serialize(NetOutgoingMessage msg)
        {
            msg.Write(WorldTime);
            msg.Write(RocketId);
            msg.Write(PartId);
            msg.Write(Time);
            msg.Write(TargetTime);
        }
        public override void Deserialize(NetIncomingMessage msg)
        {
            WorldTime = msg.ReadDouble();
            RocketId = msg.ReadInt32();
            PartId = msg.ReadInt32();
            Time = msg.ReadFloat();
            TargetTime = msg.ReadFloat();
        }
    }

    public class Packet_UpdatePart_ResourceModule : Packet
    {
        public double WorldTime { get; set; } = double.NaN;
        public int RocketId { get; set; } = -1;
        public double ResourcePercent { get; set; }
        public HashSet<int> PartIds { get; set; }

        public override PacketType Type => PacketType.UpdatePart_ResourceModule;
        public override void Serialize(NetOutgoingMessage msg)
        {
            msg.Write(WorldTime);
            msg.Write(RocketId);
            msg.Write(ResourcePercent);
            msg.WriteCollection(PartIds, msg.Write);
        }
        public override void Deserialize(NetIncomingMessage msg)
        {
            WorldTime = msg.ReadDouble();
            RocketId = msg.ReadInt32();
            ResourcePercent = msg.ReadDouble();
            PartIds = msg.ReadCollection(count => new HashSet<int>(count), msg.ReadInt32);
        }
    }

    public class Packet_UpdateCheatStatus : Packet
    {
        public bool InfiniteFuel { get; set; }
        public bool NoAtmosphericDrag { get; set; }
        public bool UnbreakableParts { get; set; }
        public bool NoGravity { get; set; }
        public bool NoHeatDamage { get; set; }
        public bool NoBurnMarks { get; set; }
        public bool InfiniteBuildArea { get; set; }
        public bool PartClipping { get; set; }

        public override PacketType Type => PacketType.UpdateCheatStatus;
        public override void Serialize(NetOutgoingMessage msg)
        {
            msg.Write(InfiniteFuel);
            msg.Write(NoAtmosphericDrag);
            msg.Write(UnbreakableParts);
            msg.Write(NoGravity);
            msg.Write(NoHeatDamage);
            msg.Write(NoBurnMarks);
            msg.Write(InfiniteBuildArea);
            msg.Write(PartClipping);
        }
        public override void Deserialize(NetIncomingMessage msg)
        {
            InfiniteFuel = msg.ReadBoolean();
            NoAtmosphericDrag = msg.ReadBoolean();
            UnbreakableParts = msg.ReadBoolean();
            NoGravity = msg.ReadBoolean();
            NoHeatDamage = msg.ReadBoolean();
            NoBurnMarks = msg.ReadBoolean();
            InfiniteBuildArea = msg.ReadBoolean();
            PartClipping = msg.ReadBoolean();
        }
    }

    // * Time Warp Packets

    public class Packet_TimeWarpRequest : Packet
    {
        public float TimeScale { get; set; }
        public string RequesterName { get; set; }
        public bool PhysicsWarp { get; set; }

        public override PacketType Type => PacketType.TimeWarpRequest;
        public override void Serialize(NetOutgoingMessage msg)
        {
            msg.Write(TimeScale);
            msg.Write(RequesterName);
            msg.Write(PhysicsWarp);
        }
        public override void Deserialize(NetIncomingMessage msg)
        {
            TimeScale = msg.ReadFloat();
            RequesterName = msg.ReadString();
            PhysicsWarp = msg.ReadBoolean();
        }
    }

    public class Packet_TimeWarpVote : Packet
    {
        public float TimeScale { get; set; }
        public string RequesterName { get; set; }
        public int VoteId { get; set; }
        public bool PhysicsWarp { get; set; }

        public override PacketType Type => PacketType.TimeWarpVote;
        public override void Serialize(NetOutgoingMessage msg)
        {
            msg.Write(TimeScale);
            msg.Write(RequesterName);
            msg.Write(VoteId);
            msg.Write(PhysicsWarp);
        }
        public override void Deserialize(NetIncomingMessage msg)
        {
            TimeScale = msg.ReadFloat();
            RequesterName = msg.ReadString();
            VoteId = msg.ReadInt32();
            PhysicsWarp = msg.ReadBoolean();
        }
    }

    public class Packet_TimeWarpVoteResponse : Packet
    {
        public int VoteId { get; set; }
        public bool Agreed { get; set; }

        public override PacketType Type => PacketType.TimeWarpVoteResponse;
        public override void Serialize(NetOutgoingMessage msg)
        {
            msg.Write(VoteId);
            msg.Write(Agreed);
        }
        public override void Deserialize(NetIncomingMessage msg)
        {
            VoteId = msg.ReadInt32();
            Agreed = msg.ReadBoolean();
        }
    }

    public class Packet_TimeWarpResult : Packet
    {
        public int VoteId { get; set; }
        public bool Approved { get; set; }
        public float TimeScale { get; set; }
        public bool PhysicsWarp { get; set; }
        public string StopperName { get; set; } = "";
        public string RejecterName { get; set; } = "";

        public override PacketType Type => PacketType.TimeWarpResult;
        public override void Serialize(NetOutgoingMessage msg)
        {
            msg.Write(VoteId);
            msg.Write(Approved);
            msg.Write(TimeScale);
            msg.Write(PhysicsWarp);
            msg.Write(StopperName);
            msg.Write(RejecterName);
        }
        public override void Deserialize(NetIncomingMessage msg)
        {
            VoteId = msg.ReadInt32();
            Approved = msg.ReadBoolean();
            TimeScale = msg.ReadFloat();
            PhysicsWarp = msg.ReadBoolean();
            StopperName = msg.ReadString();
            RejecterName = msg.ReadString();
        }
    }
}
