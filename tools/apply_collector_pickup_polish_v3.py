from pathlib import Path

root = Path(__file__).resolve().parents[1]


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected exactly one match, found {count}")
    return text.replace(old, new, 1)


# Collector Reach II is a five-block-wide field, so the radial distance is 2.5 blocks.
p = root / "data/skills/skill_tree.json"
s = p.read_text(encoding="utf-8")
start = s.index('      "id": "collection_reach_2"')
end = s.index('      "id": "collection_rate_2"', start)
segment = s[start:end]
segment = replace_once(
    segment,
    '      "description": "Expand the collector field to a full 5 block widths from the cursor before the final instant-collection upgrade.",',
    '      "description": "Expand the collector field to a 5-block-wide diameter (2.5 blocks from the cursor center) before the final instant-collection upgrade.",',
    "collector reach description",
)
segment = replace_once(segment, '          "value": 5.0', '          "value": 2.5', "collector reach radius")
s = s[:start] + segment + s[end:]
p.write_text(s, encoding="utf-8")


# Resource pickup behavior + presentation.
p = root / "src/Collection/ResourceCollectionField.cs"
s = p.read_text(encoding="utf-8")
s = replace_once(
    s,
    '    private const float OutlineScale = 1.085f;\n',
    '    private const float OutlineScale = 1.045f;\n    private const float CrtScale = 1.012f;\n',
    "pickup outline scale",
)
s = replace_once(
    s,
    '        public MultiMeshInstance3D OutlineNode = null!;\n        public MultiMesh OutlineMultiMesh = null!;\n',
    '        public MultiMeshInstance3D OutlineNode = null!;\n        public MultiMesh OutlineMultiMesh = null!;\n        public MultiMeshInstance3D CrtNode = null!;\n        public MultiMesh CrtMultiMesh = null!;\n',
    "render bucket crt fields",
)
s = replace_once(
    s,
    '    private StandardMaterial3D _outlineMaterial = null!;\n',
    '    private StandardMaterial3D _outlineMaterial = null!;\n    private ShaderMaterial _crtMaterial = null!;\n',
    "crt material field",
)
s = replace_once(
    s,
    '        _outlineMaterial = BuildOutlineMaterial();\n        BuildHint();\n',
    '        _outlineMaterial = BuildOutlineMaterial();\n        _crtMaterial = BuildCrtMaterial();\n        BuildHint();\n',
    "crt material init",
)
s = replace_once(
    s,
    '        // A pickup must first be visible/reachable from the cursor ray. Once it enters the collector\n        // field it becomes captured by the cursor and keeps following it until contact, instead of\n        // disappearing at the edge of the radius.\n',
    '        // Collection is a live cursor field, not a permanent capture. A pickup accelerates toward\n        // the cursor only while it remains inside the current collector radius; moving the cursor away\n        // releases it immediately so reach upgrades have a clear, bounded footprint.\n',
    "collector behavior comment",
)
s = replace_once(
    s,
    '            if (item.Amount <= 0 || string.IsNullOrWhiteSpace(item.BlockId)) continue;\n',
    '            if (item.Amount < 0 || string.IsNullOrWhiteSpace(item.BlockId)) continue;\n',
    "restore zero-value pickups",
)
old_method = '''    private void OnBlockMined(MiningResult result)\n    {\n        if (!result.Success || !result.Removed || result.Reward <= 0) return;\n        bool automated = result.Source == MiningSource.Automated;\n        bool manual = result.Source == MiningSource.Manual;\n        if (!manual && !automated) return;\n\n        bool autoCollect = manual\n            ? _skills.Derived.ManualAutoCollectUnlocked\n            : _skills.Derived.AutomationAutoCollectUnlocked;\n        if (autoCollect)\n        {\n            PickupCollected?.Invoke(new ResourcePickupCollected(\n                result.BlockId,\n                result.Reward,\n                Math.Max(1L, result.BlocksRemoved),\n                automated,\n                ProjectCollectionSource(result.Voxel, manual)));\n            return;\n        }\n\n        // MiningService has just credited this exact reward. Move it from the bank into the world pickup\n        // before later BlockMined observers execute. The reward calculation therefore stays centralized.\n        if (!_mining.TrySpend(result.Reward))\n        {\n            GD.PushWarning($"Could not defer {result.Reward} resources for pickup at {result.Voxel}; leaving reward banked.");\n            return;\n        }\n\n        if (!AddPickup(result.Voxel, result.BlockId, result.Reward, automated, Math.Max(1L, result.BlocksRemoved), notify: true, animateSpawn: true))\n        {\n            _mining.GrantCurrency(result.Reward);\n        }\n    }\n'''
new_method = '''    private void OnBlockMined(MiningResult result)\n    {\n        if (!result.Success || !result.Removed) return;\n        bool automated = result.Source == MiningSource.Automated;\n        bool manual = result.Source == MiningSource.Manual;\n        if (!manual && !automated) return;\n\n        bool autoCollect = manual\n            ? _skills.Derived.ManualAutoCollectUnlocked\n            : _skills.Derived.AutomationAutoCollectUnlocked;\n        if (autoCollect)\n        {\n            Vector2 source = ProjectCollectionSource(result.Voxel, manual);\n            _manual.PulseCollectionCursor(source);\n            PickupCollected?.Invoke(new ResourcePickupCollected(\n                result.BlockId,\n                Math.Max(0L, result.Reward),\n                Math.Max(1L, result.BlocksRemoved),\n                automated,\n                source));\n            return;\n        }\n\n        // MiningService has just credited this exact reward. Positive rewards are moved from the bank\n        // into the world pickup before later BlockMined observers execute. Zero-value blocks (notably\n        // water) still materialize a pickup so collection feedback remains consistent without changing\n        // the economy.\n        if (result.Reward > 0 && !_mining.TrySpend(result.Reward))\n        {\n            GD.PushWarning($"Could not defer {result.Reward} resources for pickup at {result.Voxel}; leaving reward banked.");\n            return;\n        }\n\n        if (!AddPickup(result.Voxel, result.BlockId, Math.Max(0L, result.Reward), automated, Math.Max(1L, result.BlocksRemoved), notify: true, animateSpawn: true)\n            && result.Reward > 0)\n        {\n            _mining.GrantCurrency(result.Reward);\n        }\n    }\n'''
s = replace_once(s, old_method, new_method, "OnBlockMined")
s = replace_once(
    s,
    '        bucket.MultiMesh.VisibleInstanceCount = bucket.PickupIds.Count;\n        bucket.OutlineMultiMesh.VisibleInstanceCount = bucket.PickupIds.Count;\n        if (animateSpawn',
    '        bucket.MultiMesh.VisibleInstanceCount = bucket.PickupIds.Count;\n        bucket.OutlineMultiMesh.VisibleInstanceCount = bucket.PickupIds.Count;\n        bucket.CrtMultiMesh.VisibleInstanceCount = bucket.PickupIds.Count;\n        if (animateSpawn',
    "add pickup crt visibility",
)
s = replace_once(
    s,
    '        bucket.MultiMesh.VisibleInstanceCount = bucket.PickupIds.Count;\n        bucket.OutlineMultiMesh.VisibleInstanceCount = bucket.PickupIds.Count;\n\n        if (bucket.PickupIds.Count == 0)',
    '        bucket.MultiMesh.VisibleInstanceCount = bucket.PickupIds.Count;\n        bucket.OutlineMultiMesh.VisibleInstanceCount = bucket.PickupIds.Count;\n        bucket.CrtMultiMesh.VisibleInstanceCount = bucket.PickupIds.Count;\n\n        if (bucket.PickupIds.Count == 0)',
    "remove pickup crt visibility",
)
s = replace_once(
    s,
    '''        var multiMesh = new MultiMesh\n        {\n            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,\n            Mesh = mesh,\n            InstanceCount = BucketCapacity,\n            VisibleInstanceCount = 0,\n        };\n\n        // Inverted-hull outline''',
    '''        var multiMesh = new MultiMesh\n        {\n            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,\n            Mesh = mesh,\n            InstanceCount = BucketCapacity,\n            VisibleInstanceCount = 0,\n        };\n        var crtMultiMesh = new MultiMesh\n        {\n            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,\n            Mesh = mesh,\n            InstanceCount = BucketCapacity,\n            VisibleInstanceCount = 0,\n        };\n\n        // Inverted-hull outline''',
    "crt multimesh",
)
s = replace_once(
    s,
    '''        var node = new MultiMeshInstance3D\n        {\n            Name = $"PickupBucket_{blockId}_{cell.X}_{cell.Y}_{cell.Z}",\n            Multimesh = multiMesh,\n            MaterialOverride = _assets.GetMaterialOverride(blockId),\n            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,\n        };\n        AddChild(node);\n\n        var bucket = new RenderBucket''',
    '''        var node = new MultiMeshInstance3D\n        {\n            Name = $"PickupBucket_{blockId}_{cell.X}_{cell.Y}_{cell.Z}",\n            Multimesh = multiMesh,\n            MaterialOverride = _assets.GetMaterialOverride(blockId),\n            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,\n            Transparency = 0.20f,\n        };\n        AddChild(node);\n\n        // A very light screen-space scanline/RGB-mask shell gives pickups a CRT/readout quality while\n        // keeping the source block art visible underneath. It stays inside the thinner black outline.\n        var crtNode = new MultiMeshInstance3D\n        {\n            Name = $"PickupCrt_{blockId}_{cell.X}_{cell.Y}_{cell.Z}",\n            Multimesh = crtMultiMesh,\n            MaterialOverride = _crtMaterial,\n            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,\n        };\n        AddChild(crtNode);\n\n        var bucket = new RenderBucket''',
    "pickup node opacity and crt node",
)
s = replace_once(
    s,
    '''            OutlineNode = outlineNode,\n            OutlineMultiMesh = outlineMultiMesh,\n            BobPhase = BucketPhase(cell, blockId),''',
    '''            OutlineNode = outlineNode,\n            OutlineMultiMesh = outlineMultiMesh,\n            CrtNode = crtNode,\n            CrtMultiMesh = crtMultiMesh,\n            BobPhase = BucketPhase(cell, blockId),''',
    "bucket crt assignment",
)
s = replace_once(
    s,
    '        bucket.Node.QueueFree();\n        bucket.OutlineNode.QueueFree();\n',
    '        bucket.Node.QueueFree();\n        bucket.OutlineNode.QueueFree();\n        bucket.CrtNode.QueueFree();\n',
    "free crt bucket",
)
s = replace_once(
    s,
    '            bucket.Node.Position = Vector3.Up * bob;\n            bucket.OutlineNode.Position = bucket.Node.Position;\n',
    '            bucket.Node.Position = Vector3.Up * bob;\n            bucket.OutlineNode.Position = bucket.Node.Position;\n            bucket.CrtNode.Position = bucket.Node.Position;\n',
    "crt bob sync",
)
s = replace_once(
    s,
    '''        bucket.MultiMesh.SetInstanceTransform(slot, new Transform3D(pickup.Basis, position));\n        Basis outlineBasis = pickup.Basis.Scaled(Vector3.One * OutlineScale);\n        bucket.OutlineMultiMesh.SetInstanceTransform(slot, new Transform3D(outlineBasis, position));\n''',
    '''        bucket.MultiMesh.SetInstanceTransform(slot, new Transform3D(pickup.Basis, position));\n        Basis crtBasis = pickup.Basis.Scaled(Vector3.One * CrtScale);\n        bucket.CrtMultiMesh.SetInstanceTransform(slot, new Transform3D(crtBasis, position));\n        Basis outlineBasis = pickup.Basis.Scaled(Vector3.One * OutlineScale);\n        bucket.OutlineMultiMesh.SetInstanceTransform(slot, new Transform3D(outlineBasis, position));\n''',
    "crt transform",
)
old_suction = '''                Vector3 position = pickup.FinalPosition;\n                float along = Mathf.Clamp((position - rayOrigin).Dot(rayDirection), _spacing * 0.35f, maxDistance);\n                Vector3 target = rayOrigin + rayDirection * along;\n                Vector3 toTarget = target - position;\n                float distance = toTarget.Length();\n                float closeBoost = collectorRadius <= 0.001f\n                    ? 0.0f\n                    : 0.65f * (1.0f - Mathf.Clamp(distance / collectorRadius, 0.0f, 1.0f));\n\n                pickup.Velocity += toTarget * (spring * (1.0f + closeBoost) * delta);'''
new_suction = '''                Vector3 position = pickup.FinalPosition;\n                float rawAlong = (position - rayOrigin).Dot(rayDirection);\n                float rangeAlong = Mathf.Clamp(rawAlong, 0.0f, maxDistance);\n                Vector3 closestOnCursorRay = rayOrigin + rayDirection * rangeAlong;\n                float liveRange = position.DistanceTo(closestOnCursorRay);\n\n                // Capture is conditional every frame. Once the cursor ray moves farther away than the\n                // collector field, stop applying force and leave the miniature block where it reached.\n                if (rawAlong < 0.0f || rawAlong > maxDistance || liveRange > collectorRadius * 1.02f)\n                {\n                    pickup.Sucking = false;\n                    pickup.Velocity = Vector3.Zero;\n                    _suctionIds.RemoveAt(index);\n                    continue;\n                }\n\n                float along = Mathf.Clamp(rawAlong, _spacing * 0.35f, maxDistance);\n                Vector3 target = rayOrigin + rayDirection * along;\n                Vector3 toTarget = target - position;\n                float distance = toTarget.Length();\n                float closeBoost = collectorRadius <= 0.001f\n                    ? 0.0f\n                    : 0.65f * (1.0f - Mathf.Clamp(distance / collectorRadius, 0.0f, 1.0f));\n\n                pickup.Velocity += toTarget * (spring * (1.0f + closeBoost) * delta);'''
s = replace_once(s, old_suction, new_suction, "live suction range")
s = replace_once(
    s,
    '''        long amount = pickup.Amount;\n        RemovePickup(id, notify: false);\n        _mining.GrantCurrency(amount);\n        PickupCollected?.Invoke(collected);\n''',
    '''        long amount = pickup.Amount;\n        RemovePickup(id, notify: false);\n        if (amount > 0) _mining.GrantCurrency(amount);\n        _manual.PulseCollectionCursor(screenPosition);\n        PickupCollected?.Invoke(collected);\n''',
    "collection cursor pulse",
)
s = replace_once(
    s,
    '''    private static StandardMaterial3D BuildOutlineMaterial()\n        => new()\n        {\n            AlbedoColor = new Color(0.004f, 0.005f, 0.008f, 1.0f),\n            Roughness = 1.0f,\n            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,\n            CullMode = BaseMaterial3D.CullModeEnum.Front,\n        };\n\n''',
    '''    private static StandardMaterial3D BuildOutlineMaterial()\n        => new()\n        {\n            AlbedoColor = new Color(0.004f, 0.005f, 0.008f, 1.0f),\n            Roughness = 1.0f,\n            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,\n            CullMode = BaseMaterial3D.CullModeEnum.Front,\n        };\n\n    private static ShaderMaterial BuildCrtMaterial()\n    {\n        var shader = new Shader\n        {\n            Code = \"shader_type spatial;\\n\"\n                + \"render_mode unshaded, blend_mix, cull_back, depth_draw_never;\\n\\n\"\n                + \"void fragment() {\\n\"\n                + \"    float row = mod(floor(FRAGCOORD.y), 3.0);\\n\"\n                + \"    float scan = row < 1.0 ? 1.0 : 0.14;\\n\"\n                + \"    float column = mod(floor(FRAGCOORD.x), 3.0);\\n\"\n                + \"    vec3 mask = column < 1.0 ? vec3(1.0, 0.58, 0.58) : (column < 2.0 ? vec3(0.58, 1.0, 0.58) : vec3(0.58, 0.68, 1.0));\\n\"\n                + \"    vec3 tint = vec3(0.23, 0.62, 0.56) * mask;\\n\"\n                + \"    ALBEDO = tint;\\n\"\n                + \"    EMISSION = tint * 0.18;\\n\"\n                + \"    ALPHA = 0.035 + scan * 0.12;\\n\"\n                + \"}\\n\",\n        };\n        return new ShaderMaterial { Shader = shader };\n    }\n\n''',
    "crt material builder",
)
p.write_text(s, encoding="utf-8")


# The hover cursor widget also owns the short collection-pop feedback so it works with Hover Mining off.
p = root / "src/Mining/HoverMiningCursorIndicator.cs"
p.write_text(r'''using System;
using Godot;
using TenMillionBlocks.Presentation;

namespace TenMillionBlocks.Mining;

/// <summary>
/// Screen-space cursor feedback. The hover-mining ring follows mining cadence, while collection emits a
/// short independent pop at the pointer even when Hover Mining itself is disabled.
/// </summary>
public partial class HoverMiningCursorIndicator : Control
{
    private const float Tau = MathF.PI * 2.0f;

    private float _radius = 18.0f;
    private float _progress;
    private float _pulse;
    private float _collectionPulse;
    private bool _active;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        FocusMode = FocusModeEnum.None;
        Size = Vector2.One;
        Visible = false;
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        float dt = Math.Max(0.0f, (float)delta);
        bool redraw = false;

        if (_pulse > 0.0f)
        {
            _pulse = MathF.Max(0.0f, _pulse - dt * 3.6f);
            redraw = true;
        }
        if (_collectionPulse > 0.0f)
        {
            _collectionPulse = MathF.Max(0.0f, _collectionPulse - dt * 5.2f);
            redraw = true;
        }

        Visible = _active || _collectionPulse > 0.0f;
        if (redraw) QueueRedraw();
    }

    public override void _Draw()
    {
        if (_active)
        {
            var baseColor = new Color(1.0f, 0.94f, 0.42f, 0.52f);
            var progressColor = new Color(1.0f, 0.97f, 0.62f, 0.92f);
            DrawArc(Vector2.Zero, _radius, 0.0f, Tau, 64, baseColor, 2.0f, true);

            if (_progress > 0.001f)
            {
                float start = -MathF.PI * 0.5f;
                DrawArc(
                    Vector2.Zero,
                    _radius,
                    start,
                    start + Tau * Mathf.Clamp(_progress, 0.0f, 1.0f),
                    64,
                    progressColor,
                    3.0f,
                    true);
            }

            if (_pulse > 0.0f)
            {
                float age = 1.0f - _pulse;
                float pulseRadius = _radius * (1.0f + age * 0.48f);
                var pulseColor = new Color(1.0f, 0.96f, 0.54f, 0.78f * _pulse);
                DrawArc(Vector2.Zero, pulseRadius, 0.0f, Tau, 64, pulseColor, 3.0f, true);
            }
        }

        if (_collectionPulse > 0.0f)
        {
            float age = 1.0f - _collectionPulse;
            float bump = MathF.Sin(age * MathF.PI);
            float radius = 7.5f * (1.0f + bump * 0.42f) + age * 2.0f;
            float alpha = 0.72f * _collectionPulse;
            var color = new Color(0.76f, 1.0f, 0.93f, alpha);
            DrawArc(Vector2.Zero, radius, 0.0f, Tau, 28, color, 2.2f, true);

            float tickInner = radius + 2.0f;
            float tickOuter = tickInner + 3.5f * (0.65f + bump * 0.35f);
            for (int i = 0; i < 4; i++)
            {
                float angle = i * MathF.PI * 0.5f;
                Vector2 direction = new(MathF.Cos(angle), MathF.Sin(angle));
                DrawLine(direction * tickInner, direction * tickOuter, color, 1.6f, true);
            }
        }
    }

    public void SetState(
        bool active,
        Vector2 screenPosition,
        ManualMiningFootprintKind footprint,
        float progress)
    {
        _active = active;
        Position = screenPosition;
        if (!active)
        {
            _progress = 0.0f;
            _pulse = 0.0f;
            Visible = _collectionPulse > 0.0f;
            QueueRedraw();
            return;
        }

        Visible = true;
        _radius = RadiusFor(footprint);
        _progress = Mathf.Clamp(progress, 0.0f, 1.0f);
        QueueRedraw();
    }

    public void Pulse()
    {
        if (!_active) return;
        _pulse = 1.0f;
        QueueRedraw();
    }

    public void PulseCollection(Vector2 screenPosition)
    {
        if (GraphicsSettingsRuntime.Current?.ReducedMotionEnabled == true) return;
        Position = screenPosition;
        _collectionPulse = 1.0f;
        Visible = true;
        QueueRedraw();
    }

    private static float RadiusFor(ManualMiningFootprintKind footprint)
        => footprint switch
        {
            ManualMiningFootprintKind.Plus3 => 28.0f,
            ManualMiningFootprintKind.Square3 => 34.0f,
            ManualMiningFootprintKind.Square10 => 72.0f,
            _ => 18.0f,
        };
}
''', encoding="utf-8")


# Expose collection cursor feedback to the collector without coupling it to the UI implementation.
p = root / "src/Mining/ManualMiningController.Accessors.cs"
s = p.read_text(encoding="utf-8")
s = replace_once(s, 'using TenMillionBlocks.Content;\n', 'using Godot;\nusing TenMillionBlocks.Content;\n', "accessor godot using")
s = replace_once(
    s,
    '    public WorldProfile WorldProfile => _world.Profile;\n',
    '    public WorldProfile WorldProfile => _world.Profile;\n    public void PulseCollectionCursor(Vector2 screenPosition) => _hoverIndicator?.PulseCollection(screenPosition);\n',
    "collection pulse accessor",
)
p.write_text(s, encoding="utf-8")


# Constellation icons: preserve per-glyph optical centering, then move the whole glyph field up/left and
# enlarge the star plates without changing connection centers or node hit boxes.
p = root / "src/UI/SkillTreeSpaceVisuals.cs"
s = p.read_text(encoding="utf-8")
s = replace_once(
    s,
    '''        const float iconSize = 42.0f;\n        Vector2 opticalOffset = SkillTreeIconAtlas.OpticalOffsetForSkill(node.Id, iconSize);\n        _icon.Position = new Vector2((70.0f - iconSize) * 0.5f, (70.0f - iconSize) * 0.5f) + opticalOffset;\n''',
    '''        const float iconSize = 42.0f;\n        Vector2 opticalOffset = SkillTreeIconAtlas.OpticalOffsetForSkill(node.Id, iconSize);\n        Vector2 globalCenterCorrection = new(-4.0f, -4.0f);\n        _icon.Position = new Vector2((70.0f - iconSize) * 0.5f, (70.0f - iconSize) * 0.5f)\n            + opticalOffset\n            + globalCenterCorrection;\n''',
    "icon global centering",
)
s = replace_once(
    s,
    '''        _starPlate = new SkillNodeStarPlate\n        {\n            Position = Vector2.Zero,\n            Size = new Vector2(70, 70),\n            MouseFilter = MouseFilterEnum.Ignore,\n        };''',
    '''        _starPlate = new SkillNodeStarPlate\n        {\n            Position = new Vector2(-6, -6),\n            Size = new Vector2(82, 82),\n            MouseFilter = MouseFilterEnum.Ignore,\n        };''',
    "larger star plate",
)
s = replace_once(
    s,
    '''        _spaceAura = new SkillNodeSpaceAura\n        {\n            Position = new Vector2(-4, -4),\n            Size = new Vector2(78, 78),\n            PivotOffset = new Vector2(39, 39),''',
    '''        _spaceAura = new SkillNodeSpaceAura\n        {\n            Position = new Vector2(-10, -10),\n            Size = new Vector2(90, 90),\n            PivotOffset = new Vector2(45, 45),''',
    "larger star aura",
)
p.write_text(s, encoding="utf-8")

print("Applied collector live-range, water pickups, CRT pickup presentation, cursor pop, and constellation centering polish.")
