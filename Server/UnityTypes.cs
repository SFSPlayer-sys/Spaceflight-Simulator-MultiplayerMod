using System;

namespace UnityEngine
{
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

    public struct Vector2
    {
        public float x;
        public float y;

        public Vector2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }
    }
}

namespace SFS.World
{
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
    }

    public class Difficulty
    {
        public enum DifficultyType
        {
            Easy,
            Normal,
            Hard
        }
    }
}

namespace SFS.Parts.Modules
{
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
