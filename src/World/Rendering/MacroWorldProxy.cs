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
    private readonly List<StandardMaterial3D> _materials = new();

    public int InstanceCount { get; private set; }
    public int Resolution { get; private set; }
    public double BuildMilliseconds { get; private set; }
    public float ContextOpacity { get; private set; } = 1.0f;

    public void Build(VirtualWorld world)
    {
        ulong started = Time.GetTicksUsec();
        Resolution = Math.Clamp(world.Profile.MacroResolution, 4, 64);
        int max = world.MaxCoordinate;
        float spacing = world.Profile.BlockSpacing;
        float logicalCell = (max * 2.0f + 1.0f) / Resolution;
        float worldCell = MathF.Max(spacing, logicalCell * spacing);

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

                Vector3I foundVoxel;
                string family;
                if (world.Source.TrySampleOutermostSurfaceVoxel(normal, tangentU, tangentV, out foundVoxel, out BlockSample found))
                {
                    family = Family(world, found.BlockId);
                }
                else
                {
                    // Every macro cell gets geometry. Near cube seams the dominant axis can flip to
                    // the neighbouring face; a conservative fallback prevents black holes while the
                    // neighbouring face overlaps it. This proxy is presentation-only.
                    int fallbackRadius = Math.Max(
                        Math.Max(1, (int)MathF.Round(world.Profile.BaseRadius)),
                        Math.Max(Math.Abs(tangentU), Math.Abs(tangentV)));
                    foundVoxel = FaceVoxel(face, fallbackRadius, tangentU, tangentV);
                    family = "grass";
                }

                Basis orientation = BasisForNormal(normal);

                // Slight tangential overlap plus a deep inward skirt makes the six macro faces read
                // as one continuous solid shell. The shell is inset below detailed blocks, which lets
                // it remain visible as translucent context during close inspection without replacing
                // the real block meshes in front of it.
                float thickness = MathF.Max(spacing * 2.0f, worldCell * 0.62f);
                float detailInset = spacing * 0.72f;
                Vector3 position = (Vector3)foundVoxel * spacing
                    - (Vector3)normal * (thickness * 0.5f + detailInset);
                Vector3 scale = new(worldCell * 1.025f, thickness, worldCell * 1.025f);
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

        ApplyOpacityToMaterials();
        BuildMilliseconds = (Time.GetTicksUsec() - started) / 1000.0;
    }

    public void SetContextOpacity(float opacity)
    {
        opacity = Mathf.Clamp(opacity, 0.08f, 1.0f);
        if (MathF.Abs(opacity - ContextOpacity) < 0.015f)
        {
            return;
        }

        ContextOpacity = opacity;
        ApplyOpacityToMaterials();
    }

    private void ApplyOpacityToMaterials()
    {
        bool translucent = ContextOpacity < 0.995f;
        foreach (StandardMaterial3D material in _materials)
        {
            Color color = material.AlbedoColor;
            color.A = ContextOpacity;
            material.AlbedoColor = color;
            material.Transparency = translucent
                ? BaseMaterial3D.TransparencyEnum.Alpha
                : BaseMaterial3D.TransparencyEnum.Disabled;
        }
    }

    private void AddBatch(string family, List<Transform3D> transforms)
    {
        Color color = FamilyColor(family);
        color.A = ContextOpacity;
        var material = new StandardMaterial3D
        {
            AlbedoColor = color,
            Roughness = 1.0f,
            Metallic = 0.0f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            Transparency = ContextOpacity < 0.995f
                ? BaseMaterial3D.TransparencyEnum.Alpha
                : BaseMaterial3D.TransparencyEnum.Disabled,
        };
        _materials.Add(material);

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
            0 => new Vector3I(radius, u, v),
            1 => new Vector3I(-radius, u, v),
            2 => new Vector3I(u, radius, v),
            3 => new Vector3I(u, -radius, v),
            4 => new Vector3I(u, v, radius),
            _ => new Vector3I(u, v, -radius),
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
