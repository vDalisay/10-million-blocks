using System;
using System.Collections.Generic;
using Godot;
using TenMillionBlocks.Content;

namespace TenMillionBlocks.World.Generation;

public readonly record struct BlockSample(bool Present, string BlockId, bool Mineable)
{
    public static readonly BlockSample Empty = new(false, string.Empty, false);
}

public readonly record struct FeatureSample(string BlockId, Vector3I AnchorVoxel, Vector3I OutwardNormal);

/// <summary>
/// Deterministic cube-world generator. Procedural terrain follows a Minecraft-like column contract:
/// each of the six cube faces owns stable 2D height columns and accepted terrain is filled inward.
/// Water is a coherent face-column system with explicit shoreline and depth rules rather than an
/// independent per-voxel noise material.
/// </summary>
public sealed class ProceduralWorldSource
{
    private readonly record struct TerrainContext(
        float GroundRadius,
        float WaterRadius,
        bool HasWater,
        float WaterDepth,
        float Continentalness,
        float Erosion,
        float Humidity,
        float Temperature,
        float Cliffiness,
        float ShoreFactor,
        float ForestField);

    private readonly record struct RawTerrain(
        float GroundRadius,
        float Hydrology,
        float LakeSignal,
        float Humidity,
        float Temperature,
        float Erosion,
        float Cliffiness,
        float ForestField,
        float EdgeFade,
        bool OceanCandidate,
        bool LakeCandidate,
        float WaterStrength);

    private readonly record struct ColumnKey(int Axis, int Sign, int U, int V);

    private readonly record struct SurfaceCandidate(
        Vector3I Normal,
        int U,
        int V,
        float Radial,
        TerrainContext Terrain,
        bool IsSolid,
        bool IsWater,
        float SurfaceDistance);

    private static readonly Vector3I[] FaceNormals =
    [
        Vector3I.Right,
        Vector3I.Left,
        Vector3I.Up,
        Vector3I.Down,
        Vector3I.Back,
        Vector3I.Forward,
    ];

    private readonly WorldProfile _profile;
    private readonly WorldOverrideSet? _overrides;
    private readonly Dictionary<ColumnKey, TerrainContext> _columnCache = new();

    public ProceduralWorldSource(WorldProfile profile)
    {
        _profile = profile;
        _overrides = WorldOverrideSet.Load(profile);
    }

    public WorldProfile Profile => _profile;

    public BlockSample SampleVoxel(Vector3I coordinate)
    {
        if (_overrides is not null && _overrides.TryGet(coordinate, out BlockSample authored))
        {
            return authored;
        }

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
        if (Math.Abs(coordinate.X) > maxCoordinate
            || Math.Abs(coordinate.Y) > maxCoordinate
            || Math.Abs(coordinate.Z) > maxCoordinate)
        {
            return BlockSample.Empty;
        }

        return SampleProceduralVoxel(coordinate);
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
        if (!IsCardinal(outwardNormal)) return false;

        if (_profile.UsesSingleBlockGenerator || _profile.UsesSolidCubeGenerator)
        {
            int authoredRadial = AuthoredBoxSurfaceRadius(outwardNormal);
            voxel = FaceVoxel(outwardNormal, authoredRadial, tangentU, tangentV);
            sample = SampleVoxel(voxel);
            return sample.Present;
        }

        TerrainContext terrain = SampleTerrain(outwardNormal, tangentU, tangentV);
        float outerRadius = terrain.HasWater
            ? MathF.Max(terrain.GroundRadius, terrain.WaterRadius)
            : terrain.GroundRadius;
        int radial = Math.Max(0, Mathf.FloorToInt(outerRadius + 0.001f));

        if (Math.Abs(tangentU) > outerRadius + 0.001f || Math.Abs(tangentV) > outerRadius + 0.001f)
        {
            return false;
        }

        for (int inward = 0; inward <= 4 && radial - inward >= 0; inward++)
        {
            Vector3I candidate = FaceVoxel(outwardNormal, radial - inward, tangentU, tangentV);
            BlockSample candidateSample = SampleVoxel(candidate);
            if (!candidateSample.Present) continue;
            voxel = candidate;
            sample = candidateSample;
            return true;
        }

        return false;
    }

    public float SampleSurfaceRadius(Vector3I coordinate)
    {
        if (_profile.UsesSingleBlockGenerator) return 0.0f;
        if (_profile.UsesSolidCubeGenerator) return MaxAbs(coordinate);
        Vector3I normal = GetDominantNormal(coordinate);
        GetFaceTangents(coordinate, normal, out int u, out int v, out _);
        return SampleTerrain(normal, u, v).GroundRadius;
    }

    public Vector3I GetOutwardNormal(Vector3I coordinate)
    {
        if (_profile.UsesSingleBlockGenerator || _profile.UsesSolidCubeGenerator)
        {
            return GetDominantNormal(coordinate);
        }

        if (TryFindControllingCandidate(coordinate, preferSolid: true, out SurfaceCandidate candidate))
        {
            return candidate.Normal;
        }

        return GetDominantNormal(coordinate);
    }

    public bool TrySampleTree(Vector3I surfaceVoxel, out FeatureSample feature)
    {
        feature = default;

        if (_overrides is not null && _overrides.TryGetFeature(surfaceVoxel, out FeatureSample authoredFeature))
        {
            BlockSample support = SampleVoxel(surfaceVoxel);
            if (!support.Present || SampleVoxel(surfaceVoxel + authoredFeature.OutwardNormal).Present) return false;
            feature = authoredFeature;
            return string.Equals(feature.BlockId, "tree", StringComparison.Ordinal);
        }

        if (_profile.UsesSingleBlockGenerator || _profile.UsesSolidCubeGenerator) return false;

        BlockSample sample = SampleVoxel(surfaceVoxel);
        if (!sample.Present
            || (sample.BlockId != _profile.SurfaceBlock && sample.BlockId != _profile.SurfaceEdgeBlock))
        {
            return false;
        }

        if (!TryFindControllingCandidate(surfaceVoxel, preferSolid: true, out SurfaceCandidate controlling)
            || !controlling.IsSolid)
        {
            return false;
        }

        Vector3I normal = controlling.Normal;
        if (SampleVoxel(surfaceVoxel + normal).Present) return false;

        TerrainContext terrain = controlling.Terrain;
        if (terrain.HasWater || terrain.ShoreFactor > 0.22f || terrain.Cliffiness > 0.52f) return false;

        float temperate = 1.0f - MathF.Min(1.0f, MathF.Abs(terrain.Temperature) * 0.82f);
        float suitability = terrain.ForestField * 0.48f + terrain.Humidity * 0.40f + temperate * 0.22f;
        if (suitability < _profile.ForestThreshold) return false;

        float densityBoost = 0.55f + Smooth01((suitability - _profile.ForestThreshold) / 0.75f) * 1.35f;
        float chance = MathF.Min(0.42f, _profile.TreeDensity * densityBoost);
        float hash = DeterministicNoise.Hash01(surfaceVoxel.X, surfaceVoxel.Y, surfaceVoxel.Z, _profile.Seed + 22003);
        if (hash >= chance) return false;

        feature = new FeatureSample("tree", surfaceVoxel, normal);
        return true;
    }

    private BlockSample SampleProceduralVoxel(Vector3I coordinate)
    {
        SurfaceCandidate? bestSolid = null;
        SurfaceCandidate? bestWater = null;

        foreach (Vector3I normal in FaceNormals)
        {
            if (!TryBuildCandidate(coordinate, normal, out SurfaceCandidate candidate)) continue;

            if (candidate.IsSolid
                && (bestSolid is null || candidate.SurfaceDistance < bestSolid.Value.SurfaceDistance))
            {
                bestSolid = candidate;
            }
            else if (candidate.IsWater
                && (bestWater is null || candidate.SurfaceDistance < bestWater.Value.SurfaceDistance))
            {
                bestWater = candidate;
            }
        }

        if (bestSolid is SurfaceCandidate solid)
        {
            float depth = MathF.Max(0.0f, solid.Terrain.GroundRadius - solid.Radial);
            return ClassifySolid(coordinate, solid.Normal, solid.U, solid.V, solid.Terrain, depth);
        }

        if (bestWater is SurfaceCandidate water)
        {
            return ClassifyWater(water.Normal, water.U, water.V, water.Terrain);
        }

        return BlockSample.Empty;
    }

    private bool TryBuildCandidate(Vector3I coordinate, Vector3I normal, out SurfaceCandidate candidate)
    {
        candidate = default;
        GetFaceTangents(coordinate, normal, out int u, out int v, out float radial);
        if (radial < -0.001f) return false;

        TerrainContext terrain = SampleTerrain(normal, u, v);
        float outer = terrain.HasWater ? MathF.Max(terrain.GroundRadius, terrain.WaterRadius) : terrain.GroundRadius;
        if (Math.Abs(u) > outer + 0.001f || Math.Abs(v) > outer + 0.001f) return false;

        bool solid = radial <= terrain.GroundRadius + 0.001f;
        bool water = !solid && terrain.HasWater && radial <= terrain.WaterRadius + 0.001f;
        if (!solid && !water) return false;

        float surface = solid ? terrain.GroundRadius : terrain.WaterRadius;
        candidate = new SurfaceCandidate(
            normal, u, v, radial, terrain, solid, water, MathF.Max(0.0f, surface - radial));
        return true;
    }

    private bool TryFindControllingCandidate(Vector3I coordinate, bool preferSolid, out SurfaceCandidate best)
    {
        best = default;
        bool found = false;
        bool foundPreferred = false;
        float bestDistance = float.MaxValue;

        foreach (Vector3I normal in FaceNormals)
        {
            if (!TryBuildCandidate(coordinate, normal, out SurfaceCandidate candidate)) continue;
            bool preferred = preferSolid ? candidate.IsSolid : candidate.IsWater;
            if (foundPreferred && !preferred) continue;
            if (preferred && !foundPreferred)
            {
                foundPreferred = true;
                bestDistance = float.MaxValue;
            }
            if (candidate.SurfaceDistance >= bestDistance) continue;
            best = candidate;
            bestDistance = candidate.SurfaceDistance;
            found = true;
        }

        return found;
    }

    private bool WouldResolveToWater(Vector3I coordinate)
    {
        if (_overrides is not null && _overrides.TryGet(coordinate, out BlockSample authored))
        {
            return authored.Present && IsWaterBlockId(authored.BlockId);
        }

        int maxCoordinate = _profile.MaxCoordinate;
        if (Math.Abs(coordinate.X) > maxCoordinate
            || Math.Abs(coordinate.Y) > maxCoordinate
            || Math.Abs(coordinate.Z) > maxCoordinate)
        {
            return false;
        }

        bool foundSolid = false;
        bool foundWater = false;
        foreach (Vector3I normal in FaceNormals)
        {
            if (!TryBuildCandidate(coordinate, normal, out SurfaceCandidate candidate)) continue;
            if (candidate.IsSolid) foundSolid = true;
            else if (candidate.IsWater) foundWater = true;
            if (foundSolid) return false;
        }
        return foundWater;
    }

    private bool HasAdjacentResolvedWater(Vector3I coordinate)
    {
        foreach (Vector3I direction in FaceNormals)
        {
            if (WouldResolveToWater(coordinate + direction)) return true;
        }
        return false;
    }

    private bool IsWaterBlockId(string blockId)
        => blockId == _profile.WaterBlock
            || blockId == _profile.ShallowWaterBlock
            || blockId == _profile.DeepWaterBlock;

    private BlockSample ClassifyWater(Vector3I normal, int u, int v, TerrainContext terrain)
    {
        if (terrain.WaterDepth <= 1.60f)
        {
            return new BlockSample(true, _profile.ShallowWaterBlock, true);
        }

        // Dark/deep water is reserved for the interior of a coherent body. A deep basin cell next to
        // any dry/shallow cardinal column is still rendered as normal water so the shoreline never
        // gets a dark rim merely because the floor drops quickly at that point.
        if (terrain.WaterDepth >= 2.85f && IsDeepWaterInterior(normal, u, v))
        {
            return new BlockSample(true, _profile.DeepWaterBlock, true);
        }

        return new BlockSample(true, _profile.WaterBlock, true);
    }

    private bool IsDeepWaterInterior(Vector3I normal, int u, int v)
    {
        TerrainContext a = SampleTerrain(normal, u + 1, v);
        TerrainContext b = SampleTerrain(normal, u - 1, v);
        TerrainContext c = SampleTerrain(normal, u, v + 1);
        TerrainContext d = SampleTerrain(normal, u, v - 1);
        return a.HasWater && b.HasWater && c.HasWater && d.HasWater
            && a.WaterDepth >= 1.60f
            && b.WaterDepth >= 1.60f
            && c.WaterDepth >= 1.60f
            && d.WaterDepth >= 1.60f;
    }

    private BlockSample ClassifySolid(
        Vector3I coordinate,
        Vector3I normal,
        int u,
        int v,
        TerrainContext terrain,
        float depth)
    {
        if (depth <= 0.78f)
        {
            // Shoreline classification is based on the final resolved six-neighbour topology, not
            // only this face's raw hydrology field. That matters at cube seams where a visible water
            // cell can be controlled by one face while its touching dry cell is controlled by another.
            // Any surface solid directly touching resolved water becomes beach/sand.
            if (terrain.HasWater || terrain.ShoreFactor > 0.38f || HasAdjacentResolvedWater(coordinate))
            {
                return new BlockSample(true, _profile.SandBlock, true);
            }

            if (terrain.Cliffiness > 0.68f)
            {
                return new BlockSample(
                    true,
                    terrain.Cliffiness > 0.86f ? _profile.DarkStoneBlock : _profile.StoneBlock,
                    true);
            }

            if (IsCubeOuterSeam(coordinate))
            {
                return new BlockSample(true, _profile.SurfaceBlock, true);
            }

            return new BlockSample(
                true,
                IsNaturalLedge(normal, u, v, terrain) ? _profile.SurfaceEdgeBlock : _profile.SurfaceBlock,
                true);
        }

        if (depth <= 2.85f)
        {
            if (terrain.HasWater && depth < 1.85f) return new BlockSample(true, _profile.SandBlock, true);
            return new BlockSample(true, _profile.SoilBlock, true);
        }

        float oreNoise = DeterministicNoise.Fractal3D(
            coordinate.X * 0.19f,
            coordinate.Y * 0.19f,
            coordinate.Z * 0.19f,
            _profile.Seed + 7001,
            3);
        if (oreNoise > 0.77f) return new BlockSample(true, _profile.GoldBlock, true);
        if (oreNoise > 0.67f) return new BlockSample(true, _profile.SilverBlock, true);
        if (oreNoise > 0.56f) return new BlockSample(true, _profile.CopperBlock, true);

        float stoneMix = DeterministicNoise.Fractal3D(
            coordinate.X * 0.11f,
            coordinate.Y * 0.11f,
            coordinate.Z * 0.11f,
            _profile.Seed + 9901,
            2);
        return new BlockSample(true, stoneMix < -0.12f ? _profile.DarkStoneBlock : _profile.StoneBlock, true);
    }

    private TerrainContext SampleTerrain(Vector3I normal, int u, int v)
    {
        ColumnKey key = MakeColumnKey(normal, u, v);
        if (_columnCache.TryGetValue(key, out TerrainContext cached)) return cached;

        RawTerrain center = SampleRawTerrain(normal, u, v);
        RawTerrain n1 = SampleRawTerrain(normal, u + 1, v);
        RawTerrain n2 = SampleRawTerrain(normal, u - 1, v);
        RawTerrain n3 = SampleRawTerrain(normal, u, v + 1);
        RawTerrain n4 = SampleRawTerrain(normal, u, v - 1);

        float median = Median5(
            center.GroundRadius,
            n1.GroundRadius,
            n2.GroundRadius,
            n3.GroundRadius,
            n4.GroundRadius);
        float clampedCenter = Math.Clamp(center.GroundRadius, median - 1.0f, median + 1.0f);
        float groundRadius = Quantize(clampedCenter * 0.68f + median * 0.32f, _profile.PlateauStep);

        int waterVotes = 0;
        if (IsWaterCandidate(center)) waterVotes++;
        if (IsWaterCandidate(n1)) waterVotes++;
        if (IsWaterCandidate(n2)) waterVotes++;
        if (IsWaterCandidate(n3)) waterVotes++;
        if (IsWaterCandidate(n4)) waterVotes++;

        bool centerWater = IsWaterCandidate(center);
        bool hasWater = centerWater && waterVotes >= 3;
        float waterRadius = Quantize(
            _profile.BaseRadius + _profile.SeaLevelOffset,
            MathF.Max(0.5f, _profile.PlateauStep));
        float waterDepth = 0.0f;

        if (hasWater)
        {
            float neighborhoodStrength = (
                center.WaterStrength + n1.WaterStrength + n2.WaterStrength + n3.WaterStrength + n4.WaterStrength) / 5.0f;
            float strength = Math.Clamp(center.WaterStrength * 0.72f + neighborhoodStrength * 0.28f, 0.0f, 1.0f);
            float maximumExtraDepth = center.OceanCandidate ? 3.5f : 2.5f;
            float bowl = strength * strength * (3.0f - 2.0f * strength);
            float desiredFloor = waterRadius - 1.0f - maximumExtraDepth * bowl;
            groundRadius = MathF.Min(groundRadius, Quantize(desiredFloor, _profile.PlateauStep));
            waterDepth = MathF.Max(0.0f, waterRadius - groundRadius);
        }

        float thresholdDistance = center.OceanCandidate
            ? MathF.Abs(center.Hydrology - _profile.OceanThreshold)
            : MathF.Abs(center.LakeSignal - _profile.WaterThreshold);
        float shoreFactor = hasWater
            ? 1.0f
            : 1.0f - Smooth01(thresholdDistance / MathF.Max(0.001f, _profile.ShoreBand));
        if (!hasWater && (centerWater || waterVotes > 0)) shoreFactor = MathF.Max(shoreFactor, 0.78f);

        TerrainContext result = new(
            groundRadius,
            waterRadius,
            hasWater,
            waterDepth,
            center.Hydrology,
            center.Erosion,
            center.Humidity,
            center.Temperature,
            center.Cliffiness,
            shoreFactor,
            center.ForestField);
        _columnCache[key] = result;
        return result;
    }

    private RawTerrain SampleRawTerrain(Vector3I normal, int u, int v)
    {
        Vector3 point = ToFacePoint(normal, u, v);

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

        float edgeFade = SurfaceEdgeFade(u, v);
        float relief = broadRelief + mountainRelief + valleyRelief + localDetail + plateauBias;
        float rawGroundRadius = _profile.BaseRadius + relief * edgeFade;

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
        float lakeSignal = basin - continentalness * 0.38f + humidity * 0.10f;
        float waterRadius = _profile.BaseRadius + _profile.SeaLevelOffset;
        bool lowEnoughForWater = rawGroundRadius <= waterRadius + 0.75f;
        bool oceanCandidate = edgeFade > 0.42f && lowEnoughForWater && hydrology < _profile.OceanThreshold;
        bool lakeCandidate = edgeFade > 0.42f
            && lowEnoughForWater
            && !oceanCandidate
            && lakeSignal > _profile.WaterThreshold;
        float waterStrength = oceanCandidate
            ? Smooth01((_profile.OceanThreshold - hydrology) / 0.30f)
            : lakeCandidate
                ? Smooth01((lakeSignal - _profile.WaterThreshold) / 0.26f)
                : 0.0f;

        float cliffiness = MathF.Min(
            1.0f,
            mountainMask * (0.48f + ridge * 0.52f)
            + MathF.Abs(detail) * (1.0f - erosion) * 0.34f);

        return new RawTerrain(
            rawGroundRadius,
            hydrology,
            lakeSignal,
            humidity,
            temperature,
            erosion,
            cliffiness,
            forestField,
            edgeFade,
            oceanCandidate,
            lakeCandidate,
            waterStrength);
    }

    private bool IsNaturalLedge(Vector3I normal, int u, int v, TerrainContext terrain)
    {
        const float ledgeDrop = 0.85f;
        return SampleTerrain(normal, u + 1, v).GroundRadius < terrain.GroundRadius - ledgeDrop
            || SampleTerrain(normal, u - 1, v).GroundRadius < terrain.GroundRadius - ledgeDrop
            || SampleTerrain(normal, u, v + 1).GroundRadius < terrain.GroundRadius - ledgeDrop
            || SampleTerrain(normal, u, v - 1).GroundRadius < terrain.GroundRadius - ledgeDrop;
    }

    private static bool IsWaterCandidate(RawTerrain terrain)
        => terrain.OceanCandidate || terrain.LakeCandidate;

    private float SurfaceEdgeFade(int u, int v)
    {
        float distanceToSeam = _profile.BaseRadius - Math.Max(Math.Abs(u), Math.Abs(v));
        if (distanceToSeam <= -0.001f) return 0.0f;
        return Smooth01((distanceToSeam + 0.20f) / 1.70f);
    }

    private Vector3 ToFacePoint(Vector3I normal, int u, int v)
    {
        float radius = MathF.Max(1.0f, _profile.BaseRadius);
        float a = u / radius;
        float b = v / radius;
        if (normal == Vector3I.Right) return new Vector3(1.0f, a, b);
        if (normal == Vector3I.Left) return new Vector3(-1.0f, a, b);
        if (normal == Vector3I.Up) return new Vector3(a, 1.0f, b);
        if (normal == Vector3I.Down) return new Vector3(a, -1.0f, b);
        if (normal == Vector3I.Back) return new Vector3(a, b, 1.0f);
        return new Vector3(a, b, -1.0f);
    }

    private static ColumnKey MakeColumnKey(Vector3I normal, int u, int v)
    {
        if (normal.X != 0) return new ColumnKey(0, Math.Sign(normal.X), u, v);
        if (normal.Y != 0) return new ColumnKey(1, Math.Sign(normal.Y), u, v);
        return new ColumnKey(2, Math.Sign(normal.Z), u, v);
    }

    private static void GetFaceTangents(
        Vector3I coordinate,
        Vector3I normal,
        out int u,
        out int v,
        out float radial)
    {
        if (normal.X != 0)
        {
            u = coordinate.Y;
            v = coordinate.Z;
            radial = coordinate.X * normal.X;
            return;
        }
        if (normal.Y != 0)
        {
            u = coordinate.X;
            v = coordinate.Z;
            radial = coordinate.Y * normal.Y;
            return;
        }
        u = coordinate.X;
        v = coordinate.Y;
        radial = coordinate.Z * normal.Z;
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
        return normal.X < 0 || normal.Y < 0 || normal.Z < 0 ? -minimum : minimum + size - 1;
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

    private static Vector3I GetDominantNormal(Vector3I coordinate)
    {
        int ax = Math.Abs(coordinate.X);
        int ay = Math.Abs(coordinate.Y);
        int az = Math.Abs(coordinate.Z);
        if (ax >= ay && ax >= az) return coordinate.X >= 0 ? Vector3I.Right : Vector3I.Left;
        if (ay >= ax && ay >= az) return coordinate.Y >= 0 ? Vector3I.Up : Vector3I.Down;
        return coordinate.Z >= 0 ? Vector3I.Back : Vector3I.Forward;
    }

    private static bool IsCubeOuterSeam(Vector3I coordinate)
    {
        int ax = Math.Abs(coordinate.X);
        int ay = Math.Abs(coordinate.Y);
        int az = Math.Abs(coordinate.Z);
        int max = Math.Max(ax, Math.Max(ay, az));
        int ties = (ax == max ? 1 : 0) + (ay == max ? 1 : 0) + (az == max ? 1 : 0);
        return max > 0 && ties >= 2;
    }

    private static bool IsCardinal(Vector3I normal)
        => Math.Abs(normal.X) + Math.Abs(normal.Y) + Math.Abs(normal.Z) == 1;

    private static float MaxAbs(Vector3I coordinate)
        => Math.Max(Math.Abs(coordinate.X), Math.Max(Math.Abs(coordinate.Y), Math.Abs(coordinate.Z)));

    private static float Quantize(float value, float step)
        => MathF.Round(value / MathF.Max(0.001f, step)) * MathF.Max(0.001f, step);

    private static float Median5(float a, float b, float c, float d, float e)
    {
        Span<float> values = stackalloc float[5] { a, b, c, d, e };
        values.Sort();
        return values[2];
    }

    private static float Smooth01(float value)
    {
        float t = Math.Clamp(value, 0.0f, 1.0f);
        return t * t * (3.0f - 2.0f * t);
    }
}
