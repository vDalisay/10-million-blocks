using System;
using Godot;
using TenMillionBlocks.World.Generation;

namespace TenMillionBlocks.Automation;

public partial class MinerSimulationService
{
    private const long PlacementPreviewRotorId = long.MinValue;

    private Node3D? _attentionOutline;
    private long? _attentionHighlightedMinerId;
    private bool _attentionHighlightHovered;

    private ShaderMaterial? _ghostValidMaterial;
    private ShaderMaterial? _ghostInvalidMaterial;
    private ShaderMaterial? _attentionOutlineMaterial;

    public MinerInstance? HighlightedAttentionMiner
    {
        get
        {
            if (_attentionHighlightedMinerId is not long id) return null;
            return _miners.Find(candidate => candidate.InstanceId == id);
        }
    }

    public bool AttentionHighlightHovered => _attentionHighlightHovered;

    /// <summary>
    /// Shared placement policy used by normal placement, deferred buy-and-place and relocation.
    /// Keeping this separate from mutation lets the placement ghost accurately show green/red before
    /// the player commits anything or spends resources.
    /// </summary>
    public bool CanPlaceMiner(
        string definitionId,
        Vector3I surfaceVoxel,
        bool requireUnlocked = true,
        long? ignoreInstanceId = null)
    {
        if (!_catalog.Miners.TryGetValue(definitionId, out MinerDefinition? definition)) return false;
        if (requireUnlocked && !_skills.IsMinerUnlocked(definitionId)) return false;

        BlockSample placementSample = _world.SampleVoxel(surfaceVoxel);
        if (!placementSample.Present || !_world.IsExposed(surfaceVoxel)) return false;

        string patternId = EffectivePatternId(definition);
        if (!_patterns.Contains(patternId)) return false;

        foreach (MinerInstance other in _miners)
        {
            if (ignoreInstanceId == other.InstanceId) continue;
            MinerDefinition otherDefinition = _catalog.Get(other.DefinitionId);
            if (other.Origin == surfaceVoxel || MinerAnchorVoxel(other, otherDefinition) == surfaceVoxel)
            {
                return false;
            }
        }

        Vector3I outward = _world.Source.GetOutwardNormal(surfaceVoxel);
        if (IsShovel(definition)
            && (!IsShovelMaterial(placementSample) || HasBlockingShovelSurfaceFeature(surfaceVoxel, outward)))
        {
            return false;
        }
        if (IsAxe(definition) && !IsTreeAnchor(surfaceVoxel)) return false;
        return true;
    }

    /// <summary>
    /// Relocates an existing stopped automation without creating or charging for a second unit.
    /// Relocation starts a fresh local route while preserving the instance id used by saves/HUD.
    /// </summary>
    public bool TryMoveStoppedMiner(MinerInstance miner, Vector3I surfaceVoxel)
    {
        if (!_miners.Contains(miner) || !miner.Exhausted) return false;
        if (!CanPlaceMiner(miner.DefinitionId, surfaceVoxel, requireUnlocked: false, ignoreInstanceId: miner.InstanceId))
        {
            return false;
        }

        MinerDefinition definition = _catalog.Get(miner.DefinitionId);
        Vector3I outward = _world.Source.GetOutwardNormal(surfaceVoxel);

        miner.Origin = surfaceVoxel;
        miner.Direction = -outward;
        miner.LastMinedVoxel = surfaceVoxel;
        miner.CandidateIndex = 0;
        // Shovel/axe first-step logic historically keys from BlocksMined. A moved unit needs to treat
        // its new anchor as a fresh route, so reset this per-route counter as well.
        miner.BlocksMined = 0;
        miner.WorkAccumulator = 0.0;
        ResumeMiner(miner, grantImmediateWork: false);
        _lastDebrisAtMs.Remove(miner.InstanceId);

        if (_visuals.TryGetValue(miner.InstanceId, out Node3D? root))
        {
            float spacing = _world.Profile.BlockSpacing;
            root.Transform = new Transform3D(
                BasisForNormal(outward),
                MinerPosition(miner, definition, outward, spacing));
            UpdateVisual(miner);
        }

        if (_attentionHighlightedMinerId == miner.InstanceId)
        {
            SetAttentionHighlight(null);
        }

        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Builds the exact same automation model used by the live unit, then replaces its materials with
    /// a translucent placement shader. The controller reuses this for purchase, ordinary placement and
    /// moving a stopped automation.
    /// </summary>
    public Node3D CreatePlacementGhost(string definitionId)
    {
        MinerDefinition definition = _catalog.Get(definitionId);
        float spacing = _world.Profile.BlockSpacing;
        var root = new Node3D
        {
            Name = $"PlacementGhost_{definitionId}",
            Visible = false,
        };
        AddChild(root);

        if (IsShovel(definition)) BuildShovelVisual(root, spacing);
        else if (IsPickaxe(definition)) BuildPickaxeVisual(root, spacing);
        else if (IsAxe(definition)) BuildAxeVisual(root, spacing);
        else
        {
            BuildDrillVisual(root, PlacementPreviewRotorId, spacing);
            _rotors.Remove(PlacementPreviewRotorId);
        }

        ApplyMaterialOverride(root, GhostMaterial(valid: false));
        return root;
    }

    public void UpdatePlacementGhost(Node3D ghost, string definitionId, Vector3I surfaceVoxel, bool valid)
    {
        MinerDefinition definition = _catalog.Get(definitionId);
        Vector3I outward = _world.Source.GetOutwardNormal(surfaceVoxel);
        float spacing = _world.Profile.BlockSpacing;
        var preview = new MinerInstance
        {
            DefinitionId = definitionId,
            Origin = surfaceVoxel,
            Direction = -outward,
            LastMinedVoxel = surfaceVoxel,
        };

        ghost.Transform = new Transform3D(
            BasisForNormal(outward),
            MinerPosition(preview, definition, outward, spacing));
        float footprint = DrillFootprint(definition);
        ghost.Scale = IsShovel(definition) || IsAxe(definition) || IsPickaxe(definition)
            ? Vector3.One
            : new Vector3(footprint, 1.0f, footprint);
        ApplyMaterialOverride(ghost, GhostMaterial(valid));
        ghost.Visible = true;
    }

    public void HidePlacementGhost(Node3D? ghost)
    {
        if (ghost is not null) ghost.Visible = false;
    }

    public void DestroyPlacementGhost(Node3D? ghost)
    {
        if (ghost is not null && GodotObject.IsInstanceValid(ghost)) ghost.QueueFree();
    }

    public void SetMinerHiddenForMove(MinerInstance miner, bool hidden)
    {
        if (!_visuals.TryGetValue(miner.InstanceId, out Node3D? root)) return;
        if (hidden)
        {
            root.Visible = false;
        }
        else
        {
            RefreshVisualVisibility(miner);
        }
    }

    /// <summary>
    /// Selects one stopped automation for the attention workflow. The overlay is rendered with depth
    /// testing disabled, so a stopped machine can still be located through surface blocks or tunnel
    /// walls after the camera focuses its area. Only an inverted-hull outline is drawn; the source
    /// model is never replaced by a solid orange x-ray fill.
    /// </summary>
    public void SetAttentionHighlight(MinerInstance? miner)
    {
        ClearAttentionOverlay();
        _attentionHighlightedMinerId = null;
        _attentionHighlightHovered = false;

        if (miner is null || !_miners.Contains(miner) || !miner.Exhausted) return;
        if (!_visuals.TryGetValue(miner.InstanceId, out Node3D? source)) return;

        _attentionHighlightedMinerId = miner.InstanceId;
        _attentionOutline = BuildGeometryOverlay(
            source,
            $"AutomationOutline_{miner.InstanceId}",
            AttentionOutlineMaterial());
        RefreshAttentionOverlayTransform();
        SetAttentionHoverState(false);
    }

    /// <summary>
    /// Screen-space hit testing deliberately ignores world occlusion. The x-ray outline shows where
    /// the stopped machine is; this test lets the player interact with that same silhouette through
    /// blocks. Hovering strengthens the outline instead of filling the model.
    /// </summary>
    public bool UpdateAttentionHover(Vector2 mousePosition, Camera3D camera)
    {
        MinerInstance? miner = HighlightedAttentionMiner;
        if (miner is null || !miner.Exhausted)
        {
            SetAttentionHighlight(null);
            return false;
        }

        RefreshAttentionOverlayTransform();
        if (!_visuals.TryGetValue(miner.InstanceId, out Node3D? root) || camera.IsPositionBehind(root.GlobalPosition))
        {
            SetAttentionHoverState(false);
            return false;
        }

        Vector2 screen = camera.UnprojectPosition(root.GlobalPosition);
        MinerDefinition definition = _catalog.Get(miner.DefinitionId);
        float radius = 38.0f + DrillFootprint(definition) * 12.0f;
        bool hovered = screen.DistanceTo(mousePosition) <= radius;
        SetAttentionHoverState(hovered);
        return hovered;
    }

    private Node3D BuildGeometryOverlay(Node3D source, string name, Material material)
    {
        var root = new Node3D { Name = name };
        AddChild(root);
        CloneGeometryRecursive(source, root, Transform3D.Identity, material);
        return root;
    }

    private static void CloneGeometryRecursive(Node source, Node3D destination, Transform3D accumulated, Material material)
    {
        foreach (Node child in source.GetChildren())
        {
            if (child is not Node3D child3D) continue;
            Transform3D transform = accumulated * child3D.Transform;

            if (child is MeshInstance3D meshInstance && meshInstance.Mesh is not null)
            {
                destination.AddChild(new MeshInstance3D
                {
                    Mesh = meshInstance.Mesh,
                    Transform = transform,
                    MaterialOverride = material,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                });
            }

            CloneGeometryRecursive(child, destination, transform, material);
        }
    }

    private void RefreshAttentionOverlayTransform()
    {
        MinerInstance? miner = HighlightedAttentionMiner;
        if (miner is null || !_visuals.TryGetValue(miner.InstanceId, out Node3D? source)) return;
        if (_attentionOutline is not null) _attentionOutline.Transform = source.Transform;
    }

    private void SetAttentionHoverState(bool hovered)
    {
        _attentionHighlightHovered = hovered;
        if (_attentionOutlineMaterial is null) return;

        _attentionOutlineMaterial.SetShaderParameter(
            "outline_color",
            hovered
                ? new Color(1.0f, 0.58f, 0.04f, 1.0f)
                : new Color(1.0f, 0.72f, 0.18f, 0.94f));
        _attentionOutlineMaterial.SetShaderParameter("outline_width", hovered ? 6.0f : 4.25f);
    }

    private void ClearAttentionOverlay()
    {
        if (_attentionOutline is not null && GodotObject.IsInstanceValid(_attentionOutline)) _attentionOutline.QueueFree();
        _attentionOutline = null;
    }

    private Material GhostMaterial(bool valid)
    {
        if (valid)
        {
            return _ghostValidMaterial ??= CreateGhostTintShaderMaterial(new Color(0.18f, 1.0f, 0.30f, 0.52f));
        }
        return _ghostInvalidMaterial ??= CreateGhostTintShaderMaterial(new Color(1.0f, 0.18f, 0.16f, 0.55f));
    }

    private Material AttentionOutlineMaterial()
        => _attentionOutlineMaterial ??= CreatePixelStableXrayOutlineMaterial();

    private static ShaderMaterial CreateGhostTintShaderMaterial(Color color)
    {
        var shader = new Shader
        {
            Code = @"shader_type spatial;
render_mode unshaded, cull_back, blend_mix, shadows_disabled;
uniform vec4 tint : source_color = vec4(1.0);
void fragment() {
    ALBEDO = tint.rgb;
    ALPHA = tint.a;
}",
        };
        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("tint", color);
        return material;
    }

    /// <summary>
    /// Pixel-stable inverted-hull silhouette. This follows the common Godot outline technique of
    /// rendering only expanded back faces (cull_front) and offsets the hull in clip space so the
    /// border remains readable from different zoom levels. Depth testing is disabled specifically for
    /// the stopped-automation locator, allowing only the outline to remain visible through terrain.
    /// </summary>
    private static ShaderMaterial CreatePixelStableXrayOutlineMaterial()
    {
        var shader = new Shader
        {
            Code = @"shader_type spatial;
render_mode unshaded, cull_front, depth_test_disabled, blend_mix, shadows_disabled;
uniform vec4 outline_color : source_color = vec4(1.0, 0.72, 0.18, 0.94);
uniform float outline_width = 4.25;
void vertex() {
    vec4 clip_position = PROJECTION_MATRIX * (MODELVIEW_MATRIX * vec4(VERTEX, 1.0));
    vec3 clip_normal = mat3(PROJECTION_MATRIX) * (mat3(MODELVIEW_MATRIX) * NORMAL);
    vec2 normal_xy = clip_normal.xy;
    if (length(normal_xy) > 0.0001) {
        vec2 offset = normalize(normal_xy) / VIEWPORT_SIZE * clip_position.w * outline_width * 2.0;
        clip_position.xy += offset;
    }
    POSITION = clip_position;
}
void fragment() {
    ALBEDO = outline_color.rgb;
    ALPHA = outline_color.a;
}",
        };
        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("outline_color", new Color(1.0f, 0.72f, 0.18f, 0.94f));
        material.SetShaderParameter("outline_width", 4.25f);
        return material;
    }

    private static void ApplyMaterialOverride(Node root, Material material)
    {
        if (root is GeometryInstance3D geometry)
        {
            geometry.MaterialOverride = material;
        }

        foreach (Node child in root.GetChildren())
        {
            ApplyMaterialOverride(child, material);
        }
    }
}
