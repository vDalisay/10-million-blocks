using System;
using System.Collections.Generic;
using Godot;
using TenMillionBlocks.World.Generation;

namespace TenMillionBlocks.World.Rendering;

/// <summary>
/// Bounded whole-world representation for profiles whose detailed voxel surface is too large to
/// materialize. It samples a fixed grid on the six cube faces, so cost depends on MacroResolution,
/// never on logical world dimensions.
/// </summary>
public partial class MacroWorldProxy : Node3D
{
    private readonly record struct MacroInstance(string Family, Transform3D Transform);

    public int InstanceCount { get; private set; }
    public int Resolution { get; private set; }
    public double BuildMilliseconds { get; private set; }

    public void Build(VirtualWorld world)
    {
        ulong started = Time.GetTicksUsec();
        Resolution = Math.Clamp(world.Profile.MacroResolution, 4, 64);
        int max = world.MaxCoordinate;
        float spacing = world.Profile.BlockSpacing;
        float logicalCell = max * 2.0f / Resolution;
        float worldCell = MathF.Max(spacing, logicalCell * spacing);
        int searchDepth = Math.Max(8, (int)MathF.Ceiling(
            world.Profile.TerrainAmplitude + world.Profile.DetailAmplitude
            + MathF.Abs(world.Profile.SeaLevelOffset) + 8.0f));

        var batches = new Dictionary<string, List<Transform3D>>(StringComparer.Ordinal);
        for (int face = 0; face < 6; face++)
        {
            Vector3I normal = FaceNormal(face);
            for (int v = 0; v < Resolution; v++)
            for (int u = 0; u < Resolution; u++)
            {
                float fu = ((u + 0.5f) / Resolution) * 2.0f - 1.0f;
                float fv = ((v + 0.5f) / Resolution) * 2.0f - 1.0f;
                int tangentU = Math.Clamp((int)MathF.Round(fu * max), -max, max);
                int tangentV = Math.Clamp((int)MathF.Round(fv * max), -max, max);
                Vector3I outer = FaceVoxel(face, max, tangentU, tangentV);

                BlockSample found = BlockSample.Empty;
                Vector3I foundVoxel = outer;
                for (int depth = 0; depth <= searchDepth; depth++)
                {
                    Vector3I candidate = outer - normal * depth;
                    BlockSample sample = world.Source.SampleVoxel(candidate);
                    if (!sample.Present)
                    {
                        continue;
                    }

                    found = sample;
                    foundVoxel = candidate;
                    break;
                }

                if (!found.Present)
                {
                    continue;
                }

                string family = Family(world, found.BlockId);
                Basis orientation = BasisForNormal(normal);
                // The proxy is intentionally inset slightly so detailed streamed blocks can render
                // over it without z-fighting when both representations overlap.
                Vector3 position = (Vector3)foundVoxel * spacing - (Vector3)normal * worldCell * 0.07f;
                Vector3 scale = new(worldCell * 0.94f, worldCell * 0.16f, worldCell * 0.94f);
                Transform3D transform = new(
                    orientation * Basis.Identity.Scaled(scale),
                    position);

                if (!batches.TryGetValue(family, out List<Transform3D>? transforms))
                {
                    transforms = new List<Transform3D>();
                    batches.Add(family, transforms);
                }
                transforms.Add(transform);
            }
        }

        InstanceCount = 0;
        foreach ((string family, List<Transform3D> transforms) in batches)
        {
            AddBatch(family, transforms);
            InstanceCount += transforms.Count;
        }

        BuildMilliseconds = (Time.GetTicksUsec() - started) / 1000.0;
    }

    private void AddBatch(string family, List<Transform3D> transforms)
    {
        var material = new StandardMaterial3D
        {
            AlbedoColor = FamilyColor(family),
            Roughness = 0.94f,
            Metallic = 0.0f,
        };

        var mesh = new BoxMesh
        {
            Size = Vector3.One,
            Material = material,
        };

        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = mesh,
            InstanceCount = transforms.Count,
            VisibleInstanceCount = transforms.Count,
        };

        for (int index = 0; index < transforms.Count; index++)
        {
            multiMesh.SetInstanceTransform(index, transforms[index]);
        }

        AddChild(new MultiMeshInstance3D
        {
            Name = $"Macro_{family}",
            Multimesh = multiMesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    private static string Family(VirtualWorld world, string blockId)
    {
        if (blockId == world.Profile.ShallowWaterBlock) return "water_shallow";
        if (blockId == world.Profile.DeepWaterBlock) return "water_deep";
        if (blockId == world.Profile.WaterBlock) return "water";
        if (blockId == world.Profile.SandBlock) return "sand";
        if (blockId == world.Profile.SurfaceBlock || blockId == world.Profile.SurfaceEdgeBlock) return "grass";
        return "stone";
    }

    private static Color FamilyColor(string family)
        => family switch
        {
            "grass" => new Color(0.16f, 0.58f, 0.28f),
            "sand" => new Color(0.72f, 0.64f, 0.42f),
            "water_shallow" => new Color(0.20f, 0.62f, 0.82f),
            "water" => new Color(0.10f, 0.40f, 0.68f),
            "water_deep" => new Color(0.055f, 0.20f, 0.45f),
            _ => new Color(0.34f, 0.37f, 0.40f),
        };

    private static Vector3I FaceNormal(int face)
        => face switch
        {
            0 => Vector3I.Right,
            1 => Vector3I.Left,
            2 => Vector3I.Up,
            3 => Vector3I.Down,
            4 => Vector3I.Back,
            _ => Vector3I.Forward,
        };

    private static Vector3I FaceVoxel(int face, int radius, int u, int v)
        => face switch
        {
            0 => new Vector3I(radius, v, u),
            1 => new Vector3I(-radius, v, -u),
            2 => new Vector3I(u, radius, v),
            3 => new Vector3I(u, -radius, -v),
            4 => new Vector3I(u, v, radius),
            _ => new Vector3I(-u, v, -radius),
        };

    private static Basis BasisForNormal(Vector3I normal)
    {
        if (normal == Vector3I.Up) return Basis.Identity;
        if (normal == Vector3I.Down) return new Basis(Vector3.Right, Mathf.Pi);
        if (normal == Vector3I.Right) return new Basis(Vector3.Back, -Mathf.Pi * 0.5f);
        if (normal == Vector3I.Left) return new Basis(Vector3.Back, Mathf.Pi * 0.5f);
        if (normal == Vector3I.Back) return new Basis(Vector3.Right, Mathf.Pi * 0.5f);
        return new Basis(Vector3.Right, -Mathf.Pi * 0.5f);
    }
}
