using UnityEngine;
using SFS.World;
using SFS.Parts;
using SFS.Parts.Modules;
using MultiplayerSFS.Common;
using System.Collections.Generic;

namespace MultiplayerSFS.Mod
{
    public class Interpolator : MonoBehaviour
    {
        public static double TimeDelay => System.Math.Max(0.05, 2 * LocalManager.updateRocketsPeriod / 1000.0);
        public static double DelayedWorldTime => ClientManager.world.WorldTime - TimeDelay;
        public const int MaxBuffer = 10;
        public const int MinBufferForInterpolation = 2;

        public LocalRocket rocket;
        public Packet_UpdateRocketPrimary currentUpdate;
        public bool isNewlyCreated = false;
        /// <summary>
        /// Buffer of update packets used for interpolating position, velocity, etc.
        /// </summary>
        public List<Packet_UpdateRocketPrimary> updateBuffer = new List<Packet_UpdateRocketPrimary>();
        /// <summary>
        /// Buffer of packets which is used to update local rocket at the correct time, taking into account `TimeDelay`.
        /// </summary>
        public global::System.Collections.Generic.List<(double, Packet)> packetBuffer = new global::System.Collections.Generic.List<(double, Packet)>();
        
        public enum InterpolationMode
        {
            Hermite,    
            Linear,     
            Spherical   
        }
        public InterpolationMode interpolationMode = InterpolationMode.Hermite;

        public static void AddPacketToQueue(Packet packet, int rocketId, double worldTime)
        {
            if (LocalManager.syncedRockets.TryGetValue(rocketId, out LocalRocket rocket) && rocket.interpolator is Interpolator interpolator)
            {
                if (interpolator.rocket == null)
                {
                    interpolator.rocket = rocket;
                    interpolator.currentUpdate = rocket.rocket.ToUpdatePacketPrimary(rocketId);
                }

                if (packet is Packet_UpdateRocketPrimary updatePacket)
                {
                    if (interpolator.updateBuffer.Count < MaxBuffer)
                    {
                        interpolator.updateBuffer.Add(updatePacket);
                    }
                }
                else
                {
                    interpolator.packetBuffer.Add((worldTime, packet));
                }
            }
        }

        void Update()
        {
            if (currentUpdate == null)
            {
                // * This interpolator hasn't recieved any packets yet.
                return;
            }

            if (LocalManager.updateAuthority.Contains(currentUpdate.RocketId))
            {
                rocket.rocket.rb2d.bodyType = RigidbodyType2D.Dynamic;
                rocket.rocket.rb2d.interpolation = RigidbodyInterpolation2D.None;
                RunAllPackets();
                currentUpdate = rocket.rocket.ToUpdatePacketPrimary(currentUpdate.RocketId);
                return;
            }
            else
            {
                rocket.rocket.rb2d.bodyType = RigidbodyType2D.Kinematic;
                rocket.rocket.rb2d.interpolation = RigidbodyInterpolation2D.None;
            }

            if (isNewlyCreated)
            {
                if (updateBuffer.Count > 0)
                {
                    isNewlyCreated = false;
                }
                return;
            }

            if (updateBuffer.Count < MinBufferForInterpolation)
            {
                if (updateBuffer.Count > 0)
                {
                    PredictState(currentUpdate);
                }
            }
            else
            {
                while (updateBuffer.Count > 0)
                {
                    Packet_UpdateRocketPrimary prev = currentUpdate;
                    Packet_UpdateRocketPrimary next = updateBuffer[0];

                    if (DelayedWorldTime > next.WorldTime)
                    {
                        // * The current packet is now "out of date".
                        currentUpdate = updateBuffer[0];
                        updateBuffer.RemoveAt(0);
                        continue;
                    }
                    
                    // * The current packet is now "up to date" and the location of the rocket can be set via interpolation.
                    InterpolatePackets(prev, next);
                    break;
                }
            }

            // * Run and remove any packets that have passed their world time.
            packetBuffer.RemoveAll
            (
                ((double time, Packet packet) tuple) =>
                {
                    if (double.IsNaN(tuple.time))
                    {
                        Debug.LogError($"Interpolator Error: WorldTime of `{tuple.packet.Type}` packet has not been set!");
                        return true;
                    }
                    if (tuple.time >= DelayedWorldTime)
                    {
                        RunPacket(tuple.packet);
                        return true;
                    }
                    return false;
                }
            );
        }

        void PredictState(Packet_UpdateRocketPrimary lastPacket)
        {
            double dt = DelayedWorldTime - lastPacket.WorldTime;
            if (dt <= 0) return;

            Location loc = lastPacket.Location.ToVanillaLocation();
            loc.position += lastPacket.Location.velocity * dt;
            loc.velocity = lastPacket.Location.velocity;

            float rot = lastPacket.Rotation + lastPacket.AngularVelocity * (float)dt;
            float angVel = lastPacket.AngularVelocity;

            SetState(loc, rot, angVel);
        }

        void InterpolatePackets(Packet_UpdateRocketPrimary prev, Packet_UpdateRocketPrimary next)
        {
            if (prev.Location.address != next.Location.address)
            {
                // * If the rocket has changed planet, skip interpolation to avoid incorrect positioning.
                SetState(next.Location.ToVanillaLocation(), next.Rotation, next.AngularVelocity);
                return;
            }

            double dt = DelayedWorldTime - prev.WorldTime;
            double t = dt / (next.WorldTime - prev.WorldTime);
            t = System.Math.Clamp(t, 0, 1); // 确保t在[0,1]范围内

            Location loc = prev.Location.ToVanillaLocation();
            float rot, angVel;

            switch (interpolationMode)
            {
                case InterpolationMode.Hermite:
                    // * Hermite样条插值
                    // ? https://gafferongames.com/post/snapshot_interpolation/
                    HermiteInterpolation(prev, next, t, out loc, out rot, out angVel);
                    break;
                    
                case InterpolationMode.Linear:
                    // * 线性插值
                    LinearInterpolation(prev, next, t, out loc, out rot, out angVel);
                    break;
                    
                case InterpolationMode.Spherical:
                    // * 球面插值（用于在星球表面移动）
                    SphericalInterpolation(prev, next, t, out loc, out rot, out angVel);
                    break;
                    
                default:
                    HermiteInterpolation(prev, next, t, out loc, out rot, out angVel);
                    break;
            }

            SetState(loc, rot, angVel);
        }

        // Hermite样条插值
        void HermiteInterpolation(Packet_UpdateRocketPrimary prev, Packet_UpdateRocketPrimary next, double t, out Location loc, out float rot, out float angVel)
        {
            double t2 = t * t;
            double t3 = t2 * t;

            Double2 p0 = prev.Location.position;
            Double2 v0 = prev.Location.velocity;
            Double2 p1 = next.Location.position;
            Double2 v1 = next.Location.velocity;

            double interval = next.WorldTime - prev.WorldTime;

            double h00 = (2 * t3) - (3 * t2) + 1;
            double h10 = t3 - (2 * t2) + t;
            double h01 = (-2 * t3) + (3 * t2);
            double h11 = t3 - t2;

            loc = prev.Location.ToVanillaLocation();
            loc.position = (h00 * p0) + (h10 * v0 * interval) + (h01 * p1) + (h11 * v1 * interval);
            loc.velocity = Double2.Lerp(v0, v1, t);

            rot = Mathf.LerpAngle(prev.Rotation, next.Rotation, (float) t);
            angVel = Mathf.Lerp(prev.AngularVelocity, next.AngularVelocity, (float) t);
        }

        // 线性插值
        void LinearInterpolation(Packet_UpdateRocketPrimary prev, Packet_UpdateRocketPrimary next, double t, out Location loc, out float rot, out float angVel)
        {
            loc = prev.Location.ToVanillaLocation();
            loc.position = Double2.Lerp(prev.Location.position, next.Location.position, t);
            loc.velocity = Double2.Lerp(prev.Location.velocity, next.Location.velocity, t);
            
            rot = Mathf.LerpAngle(prev.Rotation, next.Rotation, (float) t);
            angVel = Mathf.Lerp(prev.AngularVelocity, next.AngularVelocity, (float) t);
        }

        // 球面插值（用于在星球表面移动）
        void SphericalInterpolation(Packet_UpdateRocketPrimary prev, Packet_UpdateRocketPrimary next, double t, out Location loc, out float rot, out float angVel)
        {
            loc = prev.Location.ToVanillaLocation();
            
            double smoothT = t * t * (3 - 2 * t);
            
            loc.position = Double2.Lerp(prev.Location.position, next.Location.position, smoothT);
            loc.velocity = Double2.Lerp(prev.Location.velocity, next.Location.velocity, smoothT);
            
            rot = Mathf.LerpAngle(prev.Rotation, next.Rotation, (float) smoothT);
            angVel = Mathf.Lerp(prev.AngularVelocity, next.AngularVelocity, (float) smoothT);
        }

        void SetState(Location loc, float rot, float angVel)
        {
            rocket.rocket.rb2d.transform.eulerAngles = new Vector3(0, 0, rot);
            rocket.rocket.rb2d.angularVelocity = angVel;

            if (rocket.rocket.physics.PhysicsMode)
            {
                (rocket.rocket as I_Physics).LocalPosition = WorldView.ToLocalPosition(loc.position);
                (rocket.rocket as I_Physics).LocalVelocity = WorldView.ToLocalVelocity(loc.velocity);
            }
            else
            {
                rocket.rocket.physics.SetLocationAndState(loc, false);
            }
        }

        void RunAllPackets()
        {
            foreach ((double _, Packet packet) in packetBuffer)
            {
                RunPacket(packet);
            }
            packetBuffer.Clear();

            Packet_UpdateRocketPrimary updatePacket = null;
            while (updateBuffer.Count > 0)
            {
                updatePacket = updateBuffer[0];
                updateBuffer.RemoveAt(0);
            }
            if (updatePacket != null)
                SetState(updatePacket.Location.ToVanillaLocation(), updatePacket.Rotation, updatePacket.AngularVelocity);
        }

        /// <summary>
        /// Returns true if the packet ran successfully and should be removed from the `packetBuffer`.
        /// </summary>
        void RunPacket(Packet packet)
        {
            switch (packet.Type)
            {
                case PacketType.UpdateRocketSecondary:
                    OnPacket_UpdateRocketSecondary(packet as Packet_UpdateRocketSecondary);
                    break;
                case PacketType.DestroyPart:
                    OnPacket_DestroyPart(packet as Packet_DestroyPart);
                    break;
                case PacketType.UpdateStaging:
                    OnPacket_UpdateStaging(packet as Packet_UpdateStaging);
                    break;
                case PacketType.UpdatePart_EngineModule:
                    OnPacket_UpdatePart_EngineModule(packet as Packet_UpdatePart_EngineModule);
                    break;
                case PacketType.UpdatePart_WheelModule:
                    OnPacket_UpdatePart_WheelModule(packet as Packet_UpdatePart_WheelModule);
                    break;
                case PacketType.UpdatePart_BoosterModule:
                    OnPacket_UpdatePart_BoosterModule(packet as Packet_UpdatePart_BoosterModule);
                    break;
                case PacketType.UpdatePart_ParachuteModule:
                    OnPacket_UpdatePart_ParachuteModule(packet as Packet_UpdatePart_ParachuteModule);
                    break;
                case PacketType.UpdatePart_MoveModule:
                    OnPacket_UpdatePart_MoveModule(packet as Packet_UpdatePart_MoveModule);
                    break;
                case PacketType.UpdatePart_ResourceModule:
                    OnPacket_UpdatePart_ResourceModule(packet as Packet_UpdatePart_ResourceModule);
                    break;
                default:
                    Debug.LogError($"Invalid packet type used in interpolator: {packet.Type}");
                    break;
            }
        }

        void OnPacket_UpdateRocketSecondary(Packet_UpdateRocketSecondary packet)
        {
            Arrowkeys arrowkeys = rocket.rocket.arrowkeys;
            arrowkeys.turnAxis.Value = packet.Input_Turn;
            arrowkeys.rawArrowkeysAxis.Value = packet.Input_Raw;
            arrowkeys.horizontalAxis.Value = packet.Input_Horizontal;
            arrowkeys.verticalAxis.Value = packet.Input_Vertical;
            arrowkeys.rcs.Value = packet.RCS;
            rocket.rocket.throttle.throttlePercent.Value = packet.ThrottlePercent;
            rocket.rocket.throttle.throttleOn.Value = packet.ThrottleOn;

        }

        void OnPacket_DestroyPart(Packet_DestroyPart packet)
        {
            if (rocket.parts.TryGetValue(packet.PartId, out Part localPart) && localPart != null)
            {
                LocalManager.TrueDestructionReason = packet.Reason;
                localPart.DestroyPart(packet.CreateExplosion, true, LocalManager.CustomDestructionReason);
            }

        }

        void OnPacket_UpdateStaging(Packet_UpdateStaging packet)
        {
            rocket.rocket.staging.ClearStages(false);
            foreach (StageState stage in packet.Stages)
            {
                global::System.Collections.Generic.List<Part> stageParts = new global::System.Collections.Generic.List<Part>();
                foreach (int id in stage.partIDs)
                {
                    stageParts.Add(rocket.parts[id]);
                }
                rocket.rocket.staging.InsertStage(new Stage(stage.stageID, stageParts), false);
            }

        }

        void OnPacket_UpdatePart_EngineModule(Packet_UpdatePart_EngineModule packet)
        {
            if (rocket.parts.TryGetValue(packet.PartId, out Part part))
            {
                EngineModule[] modules = part.GetModules<EngineModule>();
                if (modules.Length > 1)
                {
                    Debug.LogWarning($"OnPacket_UpdatePart_EngineModule: Found multiple engine modules on part \"{part.Name}\".");
                }
                modules[0].engineOn.Value = packet.EngineOn;
            }

        }

        void OnPacket_UpdatePart_WheelModule(Packet_UpdatePart_WheelModule packet)
        {
            if (rocket.parts.TryGetValue(packet.PartId, out Part part))
            {
                WheelModule[] modules = part.GetModules<WheelModule>();
                if (modules.Length > 1)
                {
                    Debug.LogWarning($"OnPacket_UpdatePart_WheelModule: Found multiple wheel modules on part \"{part.Name}\".");
                }
                modules[0].on.Value = packet.WheelOn;
            }

        }

        void OnPacket_UpdatePart_BoosterModule(Packet_UpdatePart_BoosterModule packet)
        {
            if (rocket.parts.TryGetValue(packet.PartId, out Part part))
            {
                BoosterModule[] modules = part.GetModules<BoosterModule>();
                if (modules.Length > 1)
                {
                    Debug.LogWarning($"OnPacket_UpdatePart_BoosterModule: Found multiple booster modules on part \"{part.Name}\".");
                }
                modules[0].boosterPrimed.Value = packet.Primed;
                modules[0].throttle_Out.Value = packet.Throttle;
                modules[0].fuelPercent.Value = packet.FuelPercent;
            }

        }

        void OnPacket_UpdatePart_ParachuteModule(Packet_UpdatePart_ParachuteModule packet)
        {
            if (rocket.parts.TryGetValue(packet.PartId, out Part part))
            {
                ParachuteModule[] modules = part.GetModules<ParachuteModule>();
                if (modules.Length > 1)
                {
                    Debug.LogWarning($"OnPacket_UpdatePart_ParachuteModule: Found multiple parachute modules on part \"{part.Name}\".");
                }
                modules[0].state.Value = packet.State;
                modules[0].targetState.Value = packet.TargetState;
            }

        }

        void OnPacket_UpdatePart_MoveModule(Packet_UpdatePart_MoveModule packet)
        {
            if (rocket.parts.TryGetValue(packet.PartId, out Part part))
            {
                MoveModule[] modules = part.GetModules<MoveModule>();
                if (modules.Length > 1)
                {
                    Debug.LogWarning($"OnPacket_UpdatePart_MoveModule: Found multiple move modules on part \"{part.Name}\".");
                }
                modules[0].time.Value = packet.Time;
                modules[0].targetTime.Value = packet.TargetTime;
            }

        }

        void OnPacket_UpdatePart_ResourceModule(Packet_UpdatePart_ResourceModule packet)
        {
            foreach (int partId in packet.PartIds)
            {
                if (rocket.parts.TryGetValue(partId, out Part part))
                {
                    ResourceModule[] modules = part.GetModules<ResourceModule>();
                    if (modules.Length > 1)
                    {
                        Debug.LogWarning($"OnPacket_UpdatePart_ResourceModule: Found multiple resource modules on part \"{part.Name}\".");
                    }
                    modules[0].resourcePercent.Value = packet.ResourcePercent;
                }
            }

        }
    }
}