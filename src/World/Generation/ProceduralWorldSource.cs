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
    private readonly record struct TerrainContext(
        float GroundRadius,
        float WaterRadius,
        bool HasWater,
        float Continentalness,
        float Erosion,
        float Humidity,
        float Temperature,
        float Cliffiness,
        float ShoreFactor,
        float ForestField);

    private readonly WorldProfile _profile;

    public ProceduralWorldSource(WorldProfile profile)
    {
        _profile = profile;
    }

    public WorldProfile Profile => _profile;

    public BlockSample SampleVoxel(Vector3I coordinate)
    {
        if (_profile.UsesSingleBlockGenerator)
        {
            return coordinate == Vector3I.Zero
                ? new BlockSample(true, _profile.SurfaceBlock, true)
                : BlockSample.Empty;
        }

        if (_profile.UsesSolidCubeGenerator)
        {
            return IsInsideAuthoredBox(coordinate)
                ? new BlockSample(true, _profile.SurfaceBlock, true)
                : BlockSample.Empty;
        }

        int maxCoordinate = _profile.MaxCoordinate;
        if (Math.Abs(coordinate.X) > maxCoordinate || Math.Abs(coordinate.Y) > maxCoordinate || Math.Abs(coordinate.Z) > maxCoordinate)
        {
            return BlockSample.Empty;
        }

        TerrainContext terrain = SampleTerrain(coordinate);
        return SampleVoxelFromTerrain(coordinate, terrain);
    }

    public bool TrySampleOutermostSurfaceVoxel(
        Vector3I outwardNormal,
        int tangentU,
        int tangentV,
        out Vector3I voxel,
        out BlockSample sample)
    {
        voxel = default;
        sample = BlockSample.Empty;
        if (!IsCardinal(outwardNormal))
        {
            return false;
        }

        if (_profile.UsesSingleBlockGenerator || _profile.UsesSolidCubeGenerator)
        {
            int authoredRadial = AuthoredBoxSurfaceRadius(outwardNormal);
            voxel = FaceVoxel(outwardNormal, authoredRadial, tangentU, tangentV);
            sample = SampleVoxel(voxel);
            return sample.Present;
        }

        int referenceRadius = Math.Max(1, (int)MathF.Round(_profile.BaseRadius));
        referenceRadius = Math.Max(referenceRadius, Math.Max(Math.Abs(tangentU), Math.Abs(tangentV)));
        Vector3I reference = FaceVoxel(outwardNormal, referenceRadius, tangentU, tangentV);
        TerrainContext terrain = SampleTerrain(reference);
        float outerRadius = terrain.HasWater
            ? MathF.Max(terrain.GroundRadius, terrain.WaterRadius)
            : terrain.GroundRadius;
        int radial = Math.Max(0, Mathf.FloorToInt(outerRadius + 0.001f));

        if (Math.Abs(tangentU) > radial || Math.Abs(tangentV) > radial)
        {
            return false;
        }

        voxel = FaceVoxel(outwardNormal, radial, tangentU, tangentV);
        sample = SampleVoxelFromTerrain(voxel, terrain);
        if (sample.Present)
        {
            return true;
        }

        for (int inward = 1; inward <= 2; inward++)
        {
            int candidateRadial = radial - inward;
            if (candidateRadial < 0) break;
            Vector3I candidate = FaceVoxel(outwardNormal, candidateRadial, tangentU, tangentV);
            BlockSample candidateSample = SampleVoxelFromTerrain(candidate, terrain);
            if (!candidateSample.Present) continue;
            voxel = candidate;
            sample = candidateSample;
            return true;
        }

        return false;
    }

    private BlockSample SampleVoxelFromTerrain(Vector3I coordinate, TerrainContext terrain)
    {
        float radius = MaxAbs(coordinate);
        float outerRadius = terrain.HasWater
            ? MathF.Max(terrain.GroundRadius, terrain.WaterRadius)
            : terrain.GroundRadius;

        if (radius > outerRadius + 0.001f)
        {
            return BlockSample.Empty;
        }

        if (terrain.HasWater && radius > terrain.GroundRadius + 0.001f && radius <= terrain.WaterRadius + 0.001f)
        {
            float totalDepth = MathF.Max(0.0f, terrain.WaterRadius - terrain.GroundRadius);

            if (totalDepth <= 1.60f)
            {
                return new BlockSample(true, _profile.ShallowWaterBlock, true);
            }

            if (totalDepth > 2.60f)
            {
                return new BlockSample(true, _profile.DeepWaterBlock, true);
            }

            return new BlockSample(true, _profile.WaterBlock, true);
        }

        float depth = terrain.GroundRadius - radius;
        if (depth < -0.001f)
        {
            return BlockSample.Empty;
        }

        if (depth <= 0.78f)
        {
            if (terrain.HasWater || terrain.ShoreFactor > 0.43f)
            {
                return new BlockSample(true, _profile.SandBlock, true);
            }

            if (terrain.Cliffiness > 0.62f)
            {
                return new BlockSample(true,
                    terrain.Cliffiness > 0.82f ? _profile.DarkStoneBlock : _profile.StoneBlock,
                    true);
            }

            float edgeVariation = DeterministicNoise.Hash01(coordinate.X, coordinate.Y, coordinate.Z, _profile.Seed + 3011);
            return new BlockSample(true, edgeVariation > 0.80f ? _profile.SurfaceEdgeBlock : _profile.SurfaceBlock, true);
        }

        if (depth <= 2.85f)
        {
            if (terrain.HasWater && depth < 1.85f)
            {
                return new BlockSample(true, _profile.SandBlock, true);
            }

            return new BlockSample(true, _profile.SoilBlock, true);
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
        if (_profile.UsesSingleBlockGenerator) return 0.0f;
        if (_profile.UsesSolidCubeGenerator) return MaxAbs(coordinate);
        return SampleTerrain(coordinate).GroundRadius;
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
        if (_profile.UsesSingleBlockGenerator || _profile.UsesSolidCubeGenerator)
        {
            return false;
        }

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

        TerrainContext terrain = SampleTerrain(surfaceVoxel);
        if (terrain.HasWater || terrain.ShoreFactor > 0.22f || terrain.Cliffiness > 0.52f)
        {
            return false;
        }

        float temperate = 1.0f - MathF.Min(1.0f, MathF.Abs(terrain.Temperature) * 0.82f);
        float suitability = terrain.ForestField * 0.48f + terrain.Humidity * 0.40f + temperate * 0.22f;
        if (suitability < _profile.ForestThreshold)
        {
            return false;
        }

        float densityBoost = 0.55f + Smooth01((suitability - _profile.ForestThreshold) / 0.75f) * 1.35f;
        float chance = MathF.Min(0.42f, _profile.TreeDensity * densityBoost);
        float hash = DeterministicNoise.Hash01(surfaceVoxel.X, surfaceVoxel.Y, surfaceVoxel.Z, _profile.Seed + 22003);
        if (hash >= chance)
        {
            return false;
        }

        feature = new FeatureSample("tree", surfaceVoxel, normal);
        return true;
    }

    private TerrainContext SampleTerrain(Vector3I coordinate)
    {
        Vector3 point = ToCubeSurfacePoint(coordinate);

        float continentalness = DeterministicNoise.Fractal3D(
            point.X * _profile.ClimateFrequency,
            point.Y * _profile.ClimateFrequency,
            point.Z * _profile.ClimateFrequency,
            _profile.Seed + 101,
            4);

        float erosionSigned = DeterministicNoise.Fractal3D(
            point.X * _profile.ErosionFrequency,
            point.Y * _profile.ErosionFrequency,
            point.Z * _profile.ErosionFrequency,
            _profile.Seed + 503,
            4);
        float erosion = (erosionSigned + 1.0f) * 0.5f;

        float ridgeSigned = DeterministicNoise.Fractal3D(
            point.X * _profile.RidgeFrequency,
            point.Y * _profile.RidgeFrequency,
            point.Z * _profile.RidgeFrequency,
            _profile.Seed + 907,
            3);
        float ridge = 1.0f - MathF.Abs(ridgeSigned);

        float weirdness = DeterministicNoise.Fractal3D(
            point.X * _profile.MacroFrequency * 1.55f,
            point.Y * _profile.MacroFrequency * 1.55f,
            point.Z * _profile.MacroFrequency * 1.55f,
            _profile.Seed + 1301,
            3);

        float detail = DeterministicNoise.Fractal3D(
            point.X * _profile.DetailFrequency,
            point.Y * _profile.DetailFrequency,
            point.Z * _profile.DetailFrequency,
            _profile.Seed + 13007,
            3);

        float mountainMask = Smooth01((continentalness - 0.02f) / 0.68f)
            * Smooth01((0.76f - erosion) / 0.58f);
        float broadRelief = continentalness * _profile.TerrainAmplitude * 0.52f;
        float mountainRelief = ridge * mountainMask * _profile.TerrainAmplitude * 0.92f;
        float valleyRelief = -Smooth01((-continentalness - 0.08f) / 0.55f) * _profile.TerrainAmplitude * 0.38f;
        float detailStrength = 0.18f + (1.0f - erosion) * 0.82f;
        float localDetail = detail * _profile.DetailAmplitude * detailStrength;
        float plateauBias = weirdness * _profile.TerrainAmplitude * 0.12f;

        float rawGroundRadius = _profile.BaseRadius
            + broadRelief
            + mountainRelief
            + valleyRelief
            + localDetail
            + plateauBias;
        float groundRadius = Quantize(rawGroundRadius, _profile.PlateauStep);

        float humidity = DeterministicNoise.Fractal3D(
            point.X * _profile.ClimateFrequency * 1.42f,
            point.Y * _profile.ClimateFrequency * 1.42f,
            point.Z * _profile.ClimateFrequency * 1.42f,
            _profile.Seed + 3001,
            4);
        float temperature = DeterministicNoise.Fractal3D(
            point.X * _profile.ClimateFrequency * 1.17f,
            point.Y * _profile.ClimateFrequency * 1.17f,
            point.Z * _profile.ClimateFrequency * 1.17f,
            _profile.Seed + 3701,
            3);
        float basin = DeterministicNoise.Fractal3D(
            point.X * _profile.ClimateFrequency * 1.72f,
            point.Y * _profile.ClimateFrequency * 1.72f,
            point.Z * _profile.ClimateFrequency * 1.72f,
            _profile.Seed + 4201,
            4);
        float forestField = DeterministicNoise.Fractal3D(
            point.X * _profile.ClimateFrequency * 2.05f,
            point.Y * _profile.ClimateFrequency * 2.05f,
            point.Z * _profile.ClimateFrequency * 2.05f,
            _profile.Seed + 5101,
            3);

        float hydrology = continentalness * 0.72f + basin * 0.28f;
        bool ocean = hydrology < _profile.OceanThreshold;
        float lakeSignal = basin - continentalness * 0.38f + humidity * 0.10f;
        bool inlandLake = !ocean && lakeSignal > _profile.WaterThreshold;
        bool hasWater = ocean || inlandLake;
        float waterRadius = _profile.BaseRadius + _profile.SeaLevelOffset;

        float waterStrength = ocean
            ? Smooth01((_profile.OceanThreshold - hydrology) / 0.30f)
            : Smooth01((lakeSignal - _profile.WaterThreshold) / 0.26f);
        if (hasWater)
        {
            float minimumDepth = ocean ? 1.60f : 1.40f;
            float maximumExtraDepth = ocean ? 3.60f : 2.60f;
            float bowl = waterStrength * waterStrength * (3.0f - 2.0f * waterStrength);
            float desiredFloor = waterRadius - minimumDepth - maximumExtraDepth * bowl;
            groundRadius = MathF.Min(groundRadius, Quantize(desiredFloor, _profile.PlateauStep));
        }

        float shoreDistance = ocean
            ? MathF.Abs(hydrology - _profile.OceanThreshold)
            : MathF.Abs(lakeSignal - _profile.WaterThreshold);
        float shoreFactor = hasWater
            ? 1.0f
            : 1.0f - Smooth01(shoreDistance / MathF.Max(0.001f, _profile.ShoreBand));

        float cliffiness = MathF.Min(1.0f,
            mountainMask * (0.48f + ridge * 0.52f)
            + MathF.Abs(detail) * (1.0f - erosion) * 0.34f);

        return new TerrainContext(
            groundRadius,
            waterRadius,
            hasWater,
            continentalness,
            erosion,
            humidity,
            temperature,
            cliffiness,
            shoreFactor,
            forestField);
    }

    private bool IsInsideAuthoredBox(Vector3I coordinate)
        => IsInsideAxis(coordinate.X, _profile.LogicalWidth)
            && IsInsideAxis(coordinate.Y, _profile.LogicalHeight)
            && IsInsideAxis(coordinate.Z, _profile.LogicalDepth);

    private static bool IsInsideAxis(int coordinate, int size)
    {
        int minimum = -(size / 2);
        int maximumExclusive = minimum + size;
        return coordinate >= minimum && coordinate < maximumExclusive;
    }

    private int AuthoredBoxSurfaceRadius(Vector3I normal)
    {
        int size = Math.Abs(normal.X) == 1
            ? _profile.LogicalWidth
            : Math.Abs(normal.Y) == 1
                ? _profile.LogicalHeight
                : _profile.LogicalDepth;
        int minimum = -(size / 2);
        return normal.X < 0 || normal.Y < 0 || normal.Z < 0
            ? -minimum
            : minimum + size - 1;
    }

    private static Vector3I FaceVoxel(Vector3I normal, int radial, int u, int v)
    {
        if (normal == Vector3I.Right) return new Vector3I(radial, u, v);
        if (normal == Vector3I.Left) return new Vector3I(-radial, u, v);
        if (normal == Vector3I.Up) return new Vector3I(u, radial, v);
        if (normal == Vector3I.Down) return new Vector3I(u, -radial, v);
        if (normal == Vector3I.Back) return new Vector3I(u, v, radial);
        return new Vector3I(u, v, -radial);
    }

    private static bool IsCardinal(Vector3I normal)
        => Math.Abs(normal.X) + Math.Abs(normal.Y) + Math.Abs(normal.Z) == 1;

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

    private static float Quantize(float value, float step)
        => MathF.Round(value / step) * step;

    private static float Smooth01(float value)
    {
        float t = Math.Clamp(value, 0.0f, 1.0f);
        return t * t * (3.0f - 2.0f * t);
    }
}
