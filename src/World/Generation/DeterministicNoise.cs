using System;

namespace TenMillionBlocks.World.Generation;

public static class DeterministicNoise
{
    public static float Fractal3D(float x, float y, float z, int seed, int octaves = 4, float lacunarity = 2.0f, float gain = 0.5f)
    {
        float amplitude = 1.0f;
        float frequency = 1.0f;
        float total = 0.0f;
        float normalization = 0.0f;

        for (int octave = 0; octave < octaves; octave++)
        {
            total += Value3D(x * frequency, y * frequency, z * frequency, seed + octave * 1013) * amplitude;
            normalization += amplitude;
            amplitude *= gain;
            frequency *= lacunarity;
        }

        return normalization > 0.0f ? total / normalization : 0.0f;
    }

    public static float Value3D(float x, float y, float z, int seed)
    {
        int x0 = (int)MathF.Floor(x);
        int y0 = (int)MathF.Floor(y);
        int z0 = (int)MathF.Floor(z);
        int x1 = x0 + 1;
        int y1 = y0 + 1;
        int z1 = z0 + 1;

        float tx = Smooth(x - x0);
        float ty = Smooth(y - y0);
        float tz = Smooth(z - z0);

        float c000 = HashSigned(x0, y0, z0, seed);
        float c100 = HashSigned(x1, y0, z0, seed);
        float c010 = HashSigned(x0, y1, z0, seed);
        float c110 = HashSigned(x1, y1, z0, seed);
        float c001 = HashSigned(x0, y0, z1, seed);
        float c101 = HashSigned(x1, y0, z1, seed);
        float c011 = HashSigned(x0, y1, z1, seed);
        float c111 = HashSigned(x1, y1, z1, seed);

        float x00 = Lerp(c000, c100, tx);
        float x10 = Lerp(c010, c110, tx);
        float x01 = Lerp(c001, c101, tx);
        float x11 = Lerp(c011, c111, tx);
        float y0v = Lerp(x00, x10, ty);
        float y1v = Lerp(x01, x11, ty);
        return Lerp(y0v, y1v, tz);
    }

    public static float Hash01(int x, int y, int z, int seed)
    {
        unchecked
        {
            uint h = 2166136261u;
            h = (h ^ (uint)x) * 16777619u;
            h = (h ^ (uint)y) * 16777619u;
            h = (h ^ (uint)z) * 16777619u;
            h = (h ^ (uint)seed) * 16777619u;
            h ^= h >> 13;
            h *= 0x5bd1e995u;
            h ^= h >> 15;
            return (h & 0x00ffffffu) / 16777215.0f;
        }
    }

    private static float HashSigned(int x, int y, int z, int seed) => Hash01(x, y, z, seed) * 2.0f - 1.0f;

    private static float Smooth(float value) => value * value * (3.0f - 2.0f * value);
    private static float Lerp(float from, float to, float weight) => from + (to - from) * weight;
}
