using System;
using Godot;
using TenMillionBlocks.Content;

namespace TenMillionBlocks.World.Generation;

public readonly record struct BlockSample(bool Present, string BlockId, bool Mineable)
{
    public static readonly BlockSample Empty = new(false, string.Empty, false);
}

public readonly record struct FeatureSample(string BlockId, Vector3I AnchorVoxel, Vector3I OutwardNormal);

public sealed class ProceduralWorldSource
{
    private readonly WorldProfile _profile;

    public ProceduralWorldSource(WorldProfile profile)
    {
        _profile = profile;
    }

    public WorldProfile Profile => _profile;

    public BlockSample SampleVoxel(Vector3I coordinate)
    {
        int maxCoordinate = _profile.MaxCoordinate;
        if (Math.Abs(coordinate.X) > maxCoordinate || Math.Abs(coordinate.Y) > maxCoordinate || Math.Abs(coordinate.Z) > maxCoordinate)
        {
            return BlockSample.Empty;
        }

        float chebyshevRadius = MaxAbs(coordinate);
        float surfaceRadius = SampleSurfaceRadius(coordinate);
        float depth = surfaceRadius - chebyshevRadius;
        if (depth < -0.001f)
        {
            return BlockSample.Empty;
        }

        Vector3 surfacePoint = ToCubeSurfacePoint(coordinate);
        float macro = DeterministicNoise.Fractal3D(
            surfacePoint.X * _profile.MacroFrequency,
            surfacePoint.Y * _profile.MacroFrequency,
            surfacePoint.Z * _profile.MacroFrequency,
            _profile.Seed + 101,
            4);
        float moisture = DeterministicNoise.Fractal3D(
            surfacePoint.X * 1.7f,
            surfacePoint.Y * 1.7f,
            surfacePoint.Z * 1.7f,
            _profile.Seed + 401,
            3);
        float basin = DeterministicNoise.Fractal3D(
            surfacePoint.X * 2.15f,
            surfacePoint.Y * 2.15f,
            surfacePoint.Z * 2.15f,
            _profile.Seed + 809,
            3);

        if (depth <= 0.78f)
        {
            float wetness = moisture * 0.62f - basin * 0.55f - macro * 0.12f;
            if (wetness > _profile.WaterThreshold)
            {
                return new BlockSample(true, _profile.WaterBlock, true);
            }

            if (wetness > _profile.WaterThreshold - 0.13f)
            {
                return new BlockSample(true, _profile.SandBlock, true);
            }

            float cliff = DeterministicNoise.Fractal3D(
                surfacePoint.X * 5.8f,
                surfacePoint.Y * 5.8f,
                surfacePoint.Z * 5.8f,
                _profile.Seed + 1601,
                2);
            if (MathF.Abs(cliff) > 0.67f && macro > 0.08f)
            {
                return new BlockSample(true, cliff > 0.0f ? _profile.StoneBlock : _profile.DarkStoneBlock, true);
            }

            float edgeVariation = DeterministicNoise.Hash01(coordinate.X, coordinate.Y, coordinate.Z, _profile.Seed + 3011);
            return new BlockSample(true, edgeVariation > 0.74f ? _profile.SurfaceEdgeBlock : _profile.SurfaceBlock, true);
        }

        if (depth <= 2.85f)
        {
            float soilVariation = DeterministicNoise.Hash01(coordinate.X, coordinate.Y, coordinate.Z, _profile.Seed + 4049);
            return new BlockSample(true, soilVariation > 0.80f ? _profile.SurfaceEdgeBlock : _profile.SoilBlock, true);
        }

        float oreNoise = DeterministicNoise.Fractal3D(
            coordinate.X * 0.19f,
            coordinate.Y * 0.19f,
            coordinate.Z * 0.19f,
            _profile.Seed + 7001,
            3);

        if (oreNoise > 0.77f)
        {
            return new BlockSample(true, _profile.GoldBlock, true);
        }

        if (oreNoise > 0.67f)
        {
            return new BlockSample(true, _profile.SilverBlock, true);
        }

        if (oreNoise > 0.56f)
        {
            return new BlockSample(true, _profile.CopperBlock, true);
        }

        float stoneMix = DeterministicNoise.Fractal3D(
            coordinate.X * 0.11f,
            coordinate.Y * 0.11f,
            coordinate.Z * 0.11f,
            _profile.Seed + 9901,
            2);
        return new BlockSample(true, stoneMix < -0.12f ? _profile.DarkStoneBlock : _profile.StoneBlock, true);
    }

    public float SampleSurfaceRadius(Vector3I coordinate)
    {
        Vector3 point = ToCubeSurfacePoint(coordinate);
        float macro = DeterministicNoise.Fractal3D(
            point.X * _profile.MacroFrequency,
            point.Y * _profile.MacroFrequency,
            point.Z * _profile.MacroFrequency,
            _profile.Seed,
            4);
        float detail = DeterministicNoise.Fractal3D(
            point.X * _profile.DetailFrequency,
            point.Y * _profile.DetailFrequency,
            point.Z * _profile.DetailFrequency,
            _profile.Seed + 13007,
            3);

        float radius = _profile.BaseRadius
            + macro * _profile.TerrainAmplitude
            + detail * _profile.DetailAmplitude;

        // Quantization creates readable stepped terraces instead of a noisy fuzzy shell.
        return MathF.Round(radius * 2.0f) * 0.5f;
    }

    public Vector3I GetOutwardNormal(Vector3I coordinate)
    {
        int ax = Math.Abs(coordinate.X);
        int ay = Math.Abs(coordinate.Y);
        int az = Math.Abs(coordinate.Z);

        if (ax >= ay && ax >= az)
        {
            return coordinate.X >= 0 ? Vector3I.Right : Vector3I.Left;
        }

        if (ay >= ax && ay >= az)
        {
            return coordinate.Y >= 0 ? Vector3I.Up : Vector3I.Down;
        }

        return coordinate.Z >= 0 ? Vector3I.Back : Vector3I.Forward;
    }

    public bool TrySampleTree(Vector3I surfaceVoxel, out FeatureSample feature)
    {
        feature = default;
        BlockSample sample = SampleVoxel(surfaceVoxel);
        if (!sample.Present || (sample.BlockId != _profile.SurfaceBlock && sample.BlockId != _profile.SurfaceEdgeBlock))
        {
            return false;
        }

        Vector3I normal = GetOutwardNormal(surfaceVoxel);
        if (SampleVoxel(surfaceVoxel + normal).Present)
        {
            return false;
        }

        float chance = DeterministicNoise.Hash01(surfaceVoxel.X, surfaceVoxel.Y, surfaceVoxel.Z, _profile.Seed + 22003);
        if (chance >= _profile.TreeDensity)
        {
            return false;
        }

        // A second low-frequency mask produces groves and open fields instead of uniform peppering.
        Vector3 point = ToCubeSurfacePoint(surfaceVoxel);
        float grove = DeterministicNoise.Fractal3D(point.X * 2.5f, point.Y * 2.5f, point.Z * 2.5f, _profile.Seed + 25013, 3);
        if (grove < -0.20f)
        {
            return false;
        }

        feature = new FeatureSample("tree", surfaceVoxel, normal);
        return true;
    }

    private static float MaxAbs(Vector3I coordinate)
        => Math.Max(Math.Abs(coordinate.X), Math.Max(Math.Abs(coordinate.Y), Math.Abs(coordinate.Z)));

    private static Vector3 ToCubeSurfacePoint(Vector3I coordinate)
    {
        float max = MaxAbs(coordinate);
        if (max < 0.001f)
        {
            return Vector3.Zero;
        }

        return new Vector3(coordinate.X / max, coordinate.Y / max, coordinate.Z / max);
    }
}
