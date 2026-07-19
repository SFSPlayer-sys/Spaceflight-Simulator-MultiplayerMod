using System;
using System.Collections.Generic;
using System.IO.Compression;
using Lidgren.Network;

namespace MultiplayerSFS.ServerCommon
{
    public interface INetData
    {
        void Serialize(NetOutgoingMessage msg);
        void Deserialize(NetIncomingMessage msg);
    }

    public class NetLocation : INetData
    {
        public Double2 position;
        public Double2 velocity;
        public string address;

        public NetLocation() { }
        public NetLocation(Double2 pos, Double2 vel, string planetName)
        {
            position = pos;
            velocity = vel;
            address = planetName;
        }

        public void Serialize(NetOutgoingMessage msg)
        {
            msg.WriteCompressedDouble2(position);
            msg.WriteCompressedDouble2(velocity);
            msg.WriteCompressedString(address);
        }
        public void Deserialize(NetIncomingMessage msg)
        {
            position = msg.ReadCompressedDouble2();
            velocity = msg.ReadCompressedDouble2();
            address = msg.ReadCompressedString();
        }
    }

    public static class NetDataExtensions
    {
        public static void Write(this NetOutgoingMessage msg, INetData data)
        {
            data.Serialize(msg);
        }
        public static D Read<D>(this NetIncomingMessage msg) where D : INetData, new()
        {
            D data = new D();
            data.Deserialize(msg);
            return data;
        }

        public static void WriteCollection<T>(this NetOutgoingMessage msg, ICollection<T> collection, Action<T> writeFunc)
        {
            msg.WriteCompressedInt(collection.Count);
            foreach (T item in collection)
            {
                writeFunc(item);
            }
        }
        public static C ReadCollection<C, T>(this NetIncomingMessage msg, Func<int, C> initFunc, Func<T> readFunc) where C : ICollection<T>
        {
            int count = msg.ReadCompressedInt();
            C collection = initFunc(count);
            for (int i = 0; i < count; i++)
            {
                collection.Add(readFunc());
            }
            return collection;
        }
    }

    public static class ExistingTypeExtensions
    {
        public static void WriteCompressedDouble2(this NetOutgoingMessage msg, Double2 double2)
        {
            msg.WriteCompressedDouble(double2.x);
            msg.WriteCompressedDouble(double2.y);
        }
        public static Double2 ReadCompressedDouble2(this NetIncomingMessage msg)
        {
            return new Double2
            (
                msg.ReadCompressedDouble(),
                msg.ReadCompressedDouble()
            );
        }

        public static void WriteCompressedVector2(this NetOutgoingMessage msg, Vector2 vector2)
        {
            msg.WriteCompressedFloat(vector2.x);
            msg.WriteCompressedFloat(vector2.y);
        }
        public static Vector2 ReadCompressedVector2(this NetIncomingMessage msg)
        {
            return new Vector2
            (
                msg.ReadCompressedFloat(),
                msg.ReadCompressedFloat()
            );
        }

        public static void WriteCompressedColor(this NetOutgoingMessage msg, Color color)
        {
            msg.WriteCompressedFloat(color.r);
            msg.WriteCompressedFloat(color.g);
            msg.WriteCompressedFloat(color.b);
        }

        public static Color ReadCompressedColor(this NetIncomingMessage msg)
        {
            return new Color(msg.ReadCompressedFloat(), msg.ReadCompressedFloat(), msg.ReadCompressedFloat());
        }

        public static void WriteCompressedOrientation(this NetOutgoingMessage msg, Orientation orientation)
        {
            msg.WriteCompressedFloat(orientation.x);
            msg.WriteCompressedFloat(orientation.y);
            msg.WriteCompressedFloat(orientation.z);
        }
        public static Orientation ReadCompressedOrientation(this NetIncomingMessage msg)
        {
            return new Orientation
            (
                msg.ReadCompressedFloat(),
                msg.ReadCompressedFloat(),
                msg.ReadCompressedFloat()
            );
        }

        public static void WriteCompressedBurnSave(this NetOutgoingMessage msg, BurnMark.BurnSave burnSave)
        {
            msg.Write(burnSave == null);
            if (burnSave == null)
                return;
                
            msg.WriteCompressedFloat(burnSave.angle);
            msg.WriteCompressedFloat(burnSave.intensity);
            msg.WriteCompressedFloat(burnSave.x);
            msg.WriteCompressedString(burnSave.top);
            msg.WriteCompressedString(burnSave.bottom);
        }
        public static BurnMark.BurnSave ReadCompressedBurnSave(this NetIncomingMessage msg)
        {
            if (msg.ReadBoolean())
                return null;
            
            return new BurnMark.BurnSave()
            {
                angle = msg.ReadCompressedFloat(),
                intensity = msg.ReadCompressedFloat(),
                x = msg.ReadCompressedFloat(),
                top = msg.ReadCompressedString(),
                bottom = msg.ReadCompressedString(),
            };
        }

        public static void WriteCompressedInt(this NetOutgoingMessage msg, int value)
        {
            msg.Write(value);
        }
        public static int ReadCompressedInt(this NetIncomingMessage msg)
        {
            return msg.ReadInt32();
        }

        public static void WriteCompressedFloat(this NetOutgoingMessage msg, float value)
        {
            msg.Write(value);
        }
        public static float ReadCompressedFloat(this NetIncomingMessage msg)
        {
            return msg.ReadFloat();
        }

        public static void WriteCompressedDouble(this NetOutgoingMessage msg, double value)
        {
            msg.Write(value);
        }
        public static double ReadCompressedDouble(this NetIncomingMessage msg)
        {
            return msg.ReadDouble();
        }

        public static void WriteCompressedString(this NetOutgoingMessage msg, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                msg.Write(false);
                return;
            }
            msg.Write(true);
            msg.Write(value);
        }
        public static string ReadCompressedString(this NetIncomingMessage msg)
        {
            if (!msg.ReadBoolean())
                return string.Empty;
            return msg.ReadString();
        }
    }

    // Basic types that don't depend on Unity or SFS
    public struct Double2
    {
        public double x;
        public double y;

        public Double2(double x, double y)
        {
            this.x = x;
            this.y = y;
        }

        public double magnitude
        {
            get { return Math.Sqrt(x * x + y * y); }
        }

        public static Double2 operator -(Double2 a, Double2 b)
        {
            return new Double2(a.x - b.x, a.y - b.y);
        }
    }

    public struct Vector2
    {
        public float x;
        public float y;

        public Vector2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        public static Vector2 zero
        {
            get { return new Vector2(0, 0); }
        }
    }

    public struct Color
    {
        public float r;
        public float g;
        public float b;
        public float a;

        public Color(float r, float g, float b)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = 1.0f;
        }

        public Color(float r, float g, float b, float a)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        public static Color HSVToRGB(float h, float s, float v)
        {
            float r, g, b;
            
            if (s == 0)
            {
                r = g = b = v;
            }
            else
            {
                int i = (int)Math.Floor(h * 6);
                float f = h * 6 - i;
                float p = v * (1 - s);
                float q = v * (1 - s * f);
                float t = v * (1 - s * (1 - f));

                switch (i % 6)
                {
                    case 0: r = v; g = t; b = p; break;
                    case 1: r = q; g = v; b = p; break;
                    case 2: r = p; g = v; b = t; break;
                    case 3: r = p; g = q; b = v; break;
                    case 4: r = t; g = p; b = v; break;
                    case 5: r = v; g = p; b = q; break;
                    default: r = g = b = 0; break;
                }
            }

            return new Color(r, g, b);
        }
    }

    public struct Orientation
    {
        public float x;
        public float y;
        public float z;

        public Orientation(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }
    }

    public class BurnMark
    {
        public class BurnSave
        {
            public float angle;
            public float intensity;
            public float x;
            public string top;
            public string bottom;
        }
    }
}
