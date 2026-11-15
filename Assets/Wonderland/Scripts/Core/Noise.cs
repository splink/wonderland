using UnityEngine;

namespace Wonderland
{
    /// <summary>Deterministic hash and value noise for seeded variation (assets, terrain colour, tree placement).</summary>
    public static class Noise
    {
        public static float Hash(int x, int y = 0, int z = 0)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)z * 2147483647u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        public static float Value2(float x, float y)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            float a = Mathf.Lerp(Hash(ix, iy), Hash(ix + 1, iy), fx);
            float b = Mathf.Lerp(Hash(ix, iy + 1), Hash(ix + 1, iy + 1), fx);
            return Mathf.Lerp(a, b, fy);
        }

        public static float Value3(Vector3 p)
        {
            int ix = Mathf.FloorToInt(p.x), iy = Mathf.FloorToInt(p.y), iz = Mathf.FloorToInt(p.z);
            float fx = p.x - ix, fy = p.y - iy, fz = p.z - iz;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy); fz = fz * fz * (3f - 2f * fz);
            float Layer(int z)
            {
                float a = Mathf.Lerp(Hash(ix, iy, z), Hash(ix + 1, iy, z), fx);
                float b = Mathf.Lerp(Hash(ix, iy + 1, z), Hash(ix + 1, iy + 1, z), fx);
                return Mathf.Lerp(a, b, fy);
            }
            return Mathf.Lerp(Layer(iz), Layer(iz + 1), fz);
        }

        public static float Fbm2(float x, float y, int octaves = 3)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Value2(x, y) * amp; norm += amp;
                x = x * 2.03f + 17.1f; y = y * 2.03f + 9.7f; amp *= 0.5f;
            }
            return sum / norm;
        }
    }

    /// <summary>Small seeded RNG (xorshift) so generated assets are identical on every device and run.</summary>
    public struct Rng
    {
        private uint state;
        public Rng(int seed) { state = (uint)seed * 2654435761u + 0x9E3779B9u; if (state == 0) state = 1; Next(); }
        public uint Next() { state ^= state << 13; state ^= state >> 17; state ^= state << 5; return state; }
        public float Value => (Next() & 0xFFFFFF) / 16777216f;
        public float Range(float min, float max) => min + (max - min) * Value;
        public int Range(int min, int maxExclusive) => min + (int)(Next() % (uint)(maxExclusive - min));
        public bool Chance(float p) => Value < p;
        public Vector2 InsideUnitCircle()
        {
            float a = Value * Mathf.PI * 2f, r = Mathf.Sqrt(Value);
            return new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
        }
    }

    /// <summary>The art-directed colour palette (sRGB). One palette keeps every generated asset in the same storybook style.</summary>
    public static class Pal
    {
        public static Color Hex(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }

        public static Color Jitter(Color c, ref Rng rng, float amount)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            h = Mathf.Repeat(h + rng.Range(-amount, amount) * 0.08f, 1f);
            s = Mathf.Clamp01(s + rng.Range(-amount, amount) * 0.3f);
            v = Mathf.Clamp01(v + rng.Range(-amount, amount) * 0.35f);
            var o = Color.HSVToRGB(h, s, v); o.a = c.a; return o;
        }

        public static Color Shade(Color c, float f) => new(c.r * f, c.g * f, c.b * f, c.a);
        public static Color Glow(Color c) => new(c.r, c.g, c.b, 0f);

        // Greens
        public static readonly Color LeafDeep = Hex("#2F6B2A");
        public static readonly Color LeafMid = Hex("#4A9133");
        public static readonly Color LeafLight = Hex("#79B044");
        public static readonly Color LeafSun = Hex("#A3C755");
        public static readonly Color Conifer = Hex("#1F5A3C");
        public static readonly Color ConiferLight = Hex("#3E8A55");
        public static readonly Color Cypress = Hex("#29553A");
        public static readonly Color Blossom = Hex("#F29BB8");
        public static readonly Color BlossomLight = Hex("#FFD0DE");
        public static readonly Color Autumn = Hex("#E59A3A");
        public static readonly Color Bark = Hex("#6B4630");
        public static readonly Color BarkDark = Hex("#4A3022");
        public static readonly Color Birch = Hex("#E9E2D2");

        // Buildings
        public static readonly Color Plaster = Hex("#F3E3C3");
        public static readonly Color PlasterWarm = Hex("#F0D2A2");
        public static readonly Color PlasterPink = Hex("#F2C9B8");
        public static readonly Color Timber = Hex("#5A3826");
        public static readonly Color RoofTerracotta = Hex("#C4573A");
        public static readonly Color RoofSlate = Hex("#56627A");
        public static readonly Color Thatch = Hex("#D9A94E");
        public static readonly Color Stone = Hex("#A79C8C");
        public static readonly Color StoneDark = Hex("#7E7466");
        public static readonly Color Brick = Hex("#A8503A");
        public static readonly Color WindowGlow = Hex("#FFD27A");
        public static readonly Color FrameWhite = Hex("#F7F1E6");
        public static readonly Color DoorGreen = Hex("#3F7A4A");
        public static readonly Color DoorRed = Hex("#A33A2E");
        public static readonly Color DoorBlue = Hex("#3C6A9A");
        public static readonly Color ShutterBlue = Hex("#5B8DB8");
        public static readonly Color ShutterGreen = Hex("#5E9A63");

        // Flowers
        public static readonly Color FlowerPink = Hex("#F07FA8");
        public static readonly Color FlowerYellow = Hex("#FFD34D");
        public static readonly Color FlowerWhite = Hex("#FFF8EC");
        public static readonly Color FlowerLilac = Hex("#B08AE0");
        public static readonly Color FlowerRed = Hex("#E2483D");

        // Railway
        public static readonly Color Ballast = Hex("#9A8F80");
        public static readonly Color BallastDark = Hex("#6F665B");
        public static readonly Color Sleeper = Hex("#6E4A33");
        public static readonly Color RailTop = Hex("#D8DADC");
        public static readonly Color RailSide = Hex("#7A5A48");

        // Train
        public static readonly Color TrainRed = Hex("#C3312A");
        public static readonly Color TrainRedDark = Hex("#8E1F1C");
        public static readonly Color TrainBlack = Hex("#2A2A30");
        public static readonly Color Brass = Hex("#E2B04A");
        public static readonly Color CoachCream = Hex("#F2E2BE");
        public static readonly Color CoachMaroon = Hex("#7E2A2F");
        public static readonly Color CoachRoof = Hex("#7D7F86");
    }
}
