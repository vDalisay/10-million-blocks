#!/usr/bin/env python3
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def write(path: str, content: str) -> None:
    (ROOT / path).write_text(content, encoding="utf-8")


def replace_once(text: str, old: str, new: str, label: str) -> str:
    if old not in text:
        raise RuntimeError(f"anchor missing: {label}")
    return text.replace(old, new, 1)


# ---------------------------------------------------------------------------
# ResourceCollectionField: expo suction, momentum decay, and real pickup shader.
# ---------------------------------------------------------------------------
path = "src/Collection/ResourceCollectionField.cs"
text = read(path)

text = replace_once(
    text,
    "    private const float PickupScale = 0.30f;\n"
    "    private const float OutlineScale = 1.045f;\n"
    "    private const float CrtScale = 1.012f;\n"
    "    private const float SpawnDuration = 0.48f;\n"
    "    private const float CursorTouchPixels = 15.0f;",
    "    private const float PickupScale = 0.30f;\n"
    "    private const float OutlineScale = 1.035f;\n"
    "    private const float SpawnDuration = 0.48f;\n"
    "    private const float CursorTouchPixels = 15.0f;\n"
    "    private const float BaseSuctionDuration = 0.82f;\n"
    "    private const float ReleaseDamping = 16.0f;",
    "constants")

text = replace_once(
    text,
    "        public bool Sucking;\n"
    "        public Vector3 Velocity;\n"
    "        public RenderBucketKey RenderKey;",
    "        public bool Sucking;\n"
    "        public bool Coasting;\n"
    "        public Vector3 Velocity;\n"
    "        public Vector3 SuctionStartPosition;\n"
    "        public float SuctionProgress;\n"
    "        public RenderBucketKey RenderKey;",
    "pickup state")

text = replace_once(
    text,
    "        public MultiMeshInstance3D OutlineNode = null!;\n"
    "        public MultiMesh OutlineMultiMesh = null!;\n"
    "        public MultiMeshInstance3D CrtNode = null!;\n"
    "        public MultiMesh CrtMultiMesh = null!;\n"
    "        public float BobPhase;",
    "        public MultiMeshInstance3D OutlineNode = null!;\n"
    "        public MultiMesh OutlineMultiMesh = null!;\n"
    "        public float BobPhase;",
    "bucket CRT fields")

text = replace_once(
    text,
    "    private BlockAssetRegistry _assets = null!;\n"
    "    private StandardMaterial3D _outlineMaterial = null!;\n"
    "    private ShaderMaterial _crtMaterial = null!;",
    "    private BlockAssetRegistry _assets = null!;\n"
    "    private StandardMaterial3D _outlineMaterial = null!;",
    "materials fields")

text = replace_once(
    text,
    "    private readonly List<int> _activeSpawnIds = new();\n"
    "    private readonly List<int> _suctionIds = new();",
    "    private readonly List<int> _activeSpawnIds = new();\n"
    "    private readonly List<int> _suctionIds = new();\n"
    "    private readonly List<int> _coastingIds = new();\n"
    "    private readonly Dictionary<string, ShaderMaterial> _pickupMaterials = new(StringComparer.Ordinal);",
    "runtime lists")

text = replace_once(
    text,
    "        _spacing = Math.Max(0.01f, world.Profile.BlockSpacing);\n"
    "        _outlineMaterial = BuildOutlineMaterial();\n"
    "        _crtMaterial = BuildCrtMaterial();\n"
    "        BuildHint();",
    "        _spacing = Math.Max(0.01f, world.Profile.BlockSpacing);\n"
    "        _outlineMaterial = BuildOutlineMaterial();\n"
    "        BuildHint();",
    "init CRT material")

text = replace_once(
    text,
    "    public override void _Process(double delta)\n"
    "    {\n"
    "        _visualTime += Math.Max(0.0, delta);\n"
    "        UpdateVisualMotion();\n\n"
    "        if (_pickups.Count == 0) return;",
    "    public override void _Process(double delta)\n"
    "    {\n"
    "        float dt = Math.Max(0.0f, (float)delta);\n"
    "        _visualTime += dt;\n"
    "        UpdateVisualMotion();\n"
    "        AdvanceReleasedMomentum(dt);\n\n"
    "        if (_pickups.Count == 0) return;",
    "process prelude")

text = replace_once(
    text,
    "        foreach (int id in _hoverCandidates)\n"
    "        {\n"
    "            if (!_pickups.TryGetValue(id, out Pickup? pickup) || pickup.Sucking) continue;\n"
    "            pickup.Sucking = true;\n"
    "            pickup.Velocity = Vector3.Zero;\n"
    "            _suctionIds.Add(id);\n"
    "        }\n\n"
    "        AdvanceSuction((float)Math.Max(0.0, delta), camera, mouse, rayOrigin, rayDirection, maxDistance, radius);",
    "        foreach (int id in _hoverCandidates)\n"
    "        {\n"
    "            if (!_pickups.TryGetValue(id, out Pickup? pickup) || pickup.Sucking) continue;\n"
    "            Vector3 current = CurrentVisualPosition(pickup);\n"
    "            pickup.Sucking = true;\n"
    "            pickup.Coasting = false;\n"
    "            pickup.SuctionStartPosition = current;\n"
    "            pickup.FinalPosition = current;\n"
    "            pickup.SuctionProgress = 0.0f;\n"
    "            pickup.Velocity = Vector3.Zero;\n"
    "            _suctionIds.Add(id);\n"
    "        }\n\n"
    "        AdvanceSuction(dt, camera, mouse, rayOrigin, rayDirection, maxDistance, radius);",
    "capture setup")

text = text.replace("        bucket.CrtMultiMesh.VisibleInstanceCount = bucket.PickupIds.Count;\n", "")

# Replace complete bucket creation method up to RemoveBucket.
pattern = re.compile(r"    private RenderBucket GetOrCreateBucket\(Vector3I cell, string blockId\)\n    \{.*?\n    private void RemoveBucket\(RenderBucket bucket\)\n    \{", re.S)
replacement = r'''    private RenderBucket GetOrCreateBucket(Vector3I cell, string blockId)
    {
        var key = new RenderBucketKey(cell, blockId);
        if (_buckets.TryGetValue(key, out RenderBucket? existing)) return existing;

        Mesh mesh = _assets.GetMesh(blockId);
        var outlineMultiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = mesh,
            InstanceCount = BucketCapacity,
            VisibleInstanceCount = 0,
        };
        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = mesh,
            InstanceCount = BucketCapacity,
            VisibleInstanceCount = 0,
        };

        var outlineNode = new MultiMeshInstance3D
        {
            Name = $"PickupOutline_{blockId}_{cell.X}_{cell.Y}_{cell.Z}",
            Multimesh = outlineMultiMesh,
            MaterialOverride = _outlineMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(outlineNode);

        // The pickup material owns both the real 80% transparency and CRT modulation. The previous
        // separate shell left the actual block opaque, which made both requested effects nearly invisible.
        var node = new MultiMeshInstance3D
        {
            Name = $"PickupBucket_{blockId}_{cell.X}_{cell.Y}_{cell.Z}",
            Multimesh = multiMesh,
            MaterialOverride = GetPickupMaterial(blockId),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(node);

        var bucket = new RenderBucket
        {
            Key = key,
            Node = node,
            MultiMesh = multiMesh,
            OutlineNode = outlineNode,
            OutlineMultiMesh = outlineMultiMesh,
            BobPhase = BucketPhase(cell, blockId),
        };
        _buckets.Add(key, bucket);
        if (!_bucketsByCell.TryGetValue(cell, out List<RenderBucket>? cellBuckets))
        {
            cellBuckets = new List<RenderBucket>();
            _bucketsByCell.Add(cell, cellBuckets);
        }
        cellBuckets.Add(bucket);
        return bucket;
    }

    private void RemoveBucket(RenderBucket bucket)
    {'''
text, count = pattern.subn(replacement, text, count=1)
if count != 1:
    raise RuntimeError("could not replace GetOrCreateBucket")

text = text.replace("        bucket.CrtNode.QueueFree();\n", "")
text = text.replace("            bucket.CrtNode.Position = bucket.Node.Position;\n", "")

text = replace_once(
    text,
    "    private void WriteVisual(RenderBucket bucket, int slot, Pickup pickup, Vector3 position)\n"
    "    {\n"
    "        bucket.MultiMesh.SetInstanceTransform(slot, new Transform3D(pickup.Basis, position));\n"
    "        Basis crtBasis = pickup.Basis.Scaled(Vector3.One * CrtScale);\n"
    "        bucket.CrtMultiMesh.SetInstanceTransform(slot, new Transform3D(crtBasis, position));\n"
    "        Basis outlineBasis = pickup.Basis.Scaled(Vector3.One * OutlineScale);\n"
    "        bucket.OutlineMultiMesh.SetInstanceTransform(slot, new Transform3D(outlineBasis, position));\n"
    "    }",
    "    private void WriteVisual(RenderBucket bucket, int slot, Pickup pickup, Vector3 position)\n"
    "    {\n"
    "        bucket.MultiMesh.SetInstanceTransform(slot, new Transform3D(pickup.Basis, position));\n"
    "        Basis outlineBasis = pickup.Basis.Scaled(Vector3.One * OutlineScale);\n"
    "        bucket.OutlineMultiMesh.SetInstanceTransform(slot, new Transform3D(outlineBasis, position));\n"
    "    }",
    "WriteVisual")

# Replace suction method through CollectPickup.
pattern = re.compile(r"    private void AdvanceSuction\(\n.*?\n    private void CollectPickup\(int id, Vector2 screenPosition, bool notify\)\n    \{", re.S)
replacement = r'''    private void AdvanceSuction(
        float delta,
        Camera3D camera,
        Vector2 mouse,
        Vector3 rayOrigin,
        Vector3 rayDirection,
        float maxDistance,
        float collectorRadius)
    {
        if (_suctionIds.Count == 0 || delta <= 0.0f) return;

        float rate = (float)Math.Clamp(_skills.Derived.CollectionRatePerSecond, 0.5, 160.0);
        float duration = Mathf.Clamp(BaseSuctionDuration * MathF.Sqrt(8.0f / rate), 0.30f, BaseSuctionDuration);
        bool collectedAny = false;

        _mining.BeginCurrencyNotificationBatch();
        try
        {
            for (int index = _suctionIds.Count - 1; index >= 0; index--)
            {
                int id = _suctionIds[index];
                if (!_pickups.TryGetValue(id, out Pickup? pickup)
                    || !_buckets.TryGetValue(pickup.RenderKey, out RenderBucket? bucket))
                {
                    _suctionIds.RemoveAt(index);
                    continue;
                }

                Vector3 position = pickup.FinalPosition;
                float rawAlong = (position - rayOrigin).Dot(rayDirection);
                float rangeAlong = Mathf.Clamp(rawAlong, 0.0f, maxDistance);
                Vector3 closestOnCursorRay = rayOrigin + rayDirection * rangeAlong;
                float liveRange = position.DistanceTo(closestOnCursorRay);

                // Leaving the field no longer kills velocity in one frame. The pickup is released with
                // the velocity it had at that instant, then AdvanceReleasedMomentum damps it rapidly.
                if (rawAlong < 0.0f || rawAlong > maxDistance || liveRange > collectorRadius * 1.02f)
                {
                    pickup.Sucking = false;
                    pickup.Coasting = true;
                    pickup.SuctionProgress = 0.0f;
                    _suctionIds.RemoveAt(index);
                    _coastingIds.Add(id);
                    continue;
                }

                float along = Mathf.Clamp(rawAlong, _spacing * 0.35f, maxDistance);
                Vector3 target = rayOrigin + rayDirection * along;

                pickup.SuctionProgress = Math.Min(1.0f, pickup.SuctionProgress + delta / duration);
                float eased = EaseInOutExpo(pickup.SuctionProgress);
                Vector3 next = pickup.SuctionStartPosition.Lerp(target, eased);
                pickup.Velocity = (next - position) / Math.Max(0.0001f, delta);
                pickup.FinalPosition = next;
                WriteVisual(bucket, pickup.RenderSlot, pickup, next);

                Vector2 screen = camera.UnprojectPosition(next + bucket.Node.Position);
                if (screen.DistanceTo(mouse) > CursorTouchPixels && pickup.SuctionProgress < 1.0f) continue;

                CollectPickup(id, mouse, notify: false);
                _suctionIds.RemoveAt(index);
                collectedAny = true;
            }
        }
        finally
        {
            _mining.EndCurrencyNotificationBatch();
        }

        if (collectedAny) NotifyPendingChanged();
    }

    private void AdvanceReleasedMomentum(float delta)
    {
        if (_coastingIds.Count == 0 || delta <= 0.0f) return;

        float damping = MathF.Exp(-ReleaseDamping * delta);
        float stopSpeed = _spacing * 0.045f;
        for (int index = _coastingIds.Count - 1; index >= 0; index--)
        {
            int id = _coastingIds[index];
            if (!_pickups.TryGetValue(id, out Pickup? pickup)
                || !_buckets.TryGetValue(pickup.RenderKey, out RenderBucket? bucket)
                || pickup.Sucking
                || !pickup.Coasting)
            {
                _coastingIds.RemoveAt(index);
                continue;
            }

            pickup.Velocity *= damping;
            pickup.FinalPosition += pickup.Velocity * delta;
            WriteVisual(bucket, pickup.RenderSlot, pickup, pickup.FinalPosition);

            if (pickup.Velocity.Length() > stopSpeed) continue;
            pickup.Velocity = Vector3.Zero;
            pickup.Coasting = false;
            _coastingIds.RemoveAt(index);
        }
    }

    private static float EaseInOutExpo(float value)
    {
        float x = Mathf.Clamp(value, 0.0f, 1.0f);
        if (x <= 0.0f) return 0.0f;
        if (x >= 1.0f) return 1.0f;
        return x < 0.5f
            ? MathF.Pow(2.0f, 20.0f * x - 10.0f) * 0.5f
            : (2.0f - MathF.Pow(2.0f, -20.0f * x + 10.0f)) * 0.5f;
    }

    private void CollectPickup(int id, Vector2 screenPosition, bool notify)
    {'''
text, count = pattern.subn(replacement, text, count=1)
if count != 1:
    raise RuntimeError("could not replace AdvanceSuction")

# Replace obsolete CRT shell builder with per-block pickup material.
pattern = re.compile(r"    private static ShaderMaterial BuildCrtMaterial\(\)\n    \{.*?\n    \}\n\n    private Vector2 PickupScatter", re.S)
replacement = r'''    private ShaderMaterial GetPickupMaterial(string blockId)
    {
        if (_pickupMaterials.TryGetValue(blockId, out ShaderMaterial? cached)) return cached;

        StandardMaterial3D? source = _assets.GetMaterialOverride(blockId) as StandardMaterial3D;
        Texture2D? albedoTexture = source?.AlbedoTexture;
        Color albedoColor = source?.AlbedoColor ?? Colors.White;

        var shader = new Shader
        {
            Code = "shader_type spatial;\n"
                + "render_mode blend_mix, depth_prepass_alpha, cull_back;\n\n"
                + "uniform sampler2D albedo_texture : source_color, filter_linear_mipmap_anisotropic, repeat_enable;\n"
                + "uniform bool has_albedo_texture = false;\n"
                + "uniform vec4 albedo_color : source_color = vec4(1.0);\n"
                + "uniform float opacity = 0.80;\n"
                + "uniform float crt_strength = 0.42;\n\n"
                + "void fragment() {\n"
                + "    vec4 texel = has_albedo_texture ? texture(albedo_texture, UV) : vec4(1.0);\n"
                + "    vec3 base = texel.rgb * albedo_color.rgb;\n"
                + "    float scan = mod(floor(FRAGCOORD.y), 2.0) < 1.0 ? 0.62 : 1.0;\n"
                + "    float column = mod(floor(FRAGCOORD.x), 3.0);\n"
                + "    vec3 mask = column < 1.0 ? vec3(1.0, 0.82, 0.82) : (column < 2.0 ? vec3(0.82, 1.0, 0.82) : vec3(0.82, 0.86, 1.0));\n"
                + "    float flicker = 0.985 + 0.015 * sin(TIME * 18.0 + FRAGCOORD.y * 0.13);\n"
                + "    vec3 crt = base * scan * mask * flicker;\n"
                + "    ALBEDO = mix(base, crt, crt_strength);\n"
                + "    ROUGHNESS = 1.0;\n"
                + "    SPECULAR = 0.0;\n"
                + "    EMISSION = ALBEDO * (0.035 * crt_strength);\n"
                + "    ALPHA = clamp(opacity * texel.a * albedo_color.a, 0.0, 1.0);\n"
                + "}\n",
        };
        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("has_albedo_texture", albedoTexture is not null);
        material.SetShaderParameter("albedo_color", albedoColor);
        material.SetShaderParameter("opacity", 0.80f);
        material.SetShaderParameter("crt_strength", 0.42f);
        if (albedoTexture is not null) material.SetShaderParameter("albedo_texture", albedoTexture);
        _pickupMaterials.Add(blockId, material);
        return material;
    }

    private Vector2 PickupScatter'''
text, count = pattern.subn(replacement, text, count=1)
if count != 1:
    raise RuntimeError("could not replace BuildCrtMaterial")

# Stale comments from the permanent-capture version.
text = text.replace(
    "        // Collection is a live cursor field, not a permanent capture. A pickup accelerates toward\n"
    "        // the cursor only while it remains inside the current collector radius; moving the cursor away\n"
    "        // releases it immediately so reach upgrades have a clear, bounded footprint.\n",
    "        // Collection is a live cursor field. Entering range starts an easeInOutExpo pull; leaving\n"
    "        // range releases the pickup into a short damped coast instead of hard-stopping it.\n")

write(path, text)


# ---------------------------------------------------------------------------
# Hardware custom cursor pop. No software circle/halo proxy.
# ---------------------------------------------------------------------------
cursor_path = "src/Mining/HoverMiningCursorIndicator.cs"
cursor = r'''using System;
using Godot;
using TenMillionBlocks.Presentation;

namespace TenMillionBlocks.Mining;

/// <summary>
/// Screen-space hover-mining cadence plus a short hardware-cursor size pop when resources are collected.
/// The collection feedback uses Input.SetCustomMouseCursor so the cursor itself changes size rather than
/// drawing a separate ring near it. The normal system cursor is restored immediately after the beat.
/// </summary>
public partial class HoverMiningCursorIndicator : Control
{
    private const float Tau = MathF.PI * 2.0f;
    private const float CursorPopDuration = 0.18f;

    private float _radius = 18.0f;
    private float _progress;
    private float _pulse;
    private float _cursorPopRemaining;
    private int _cursorStage;
    private bool _active;
    private ImageTexture? _cursorNormal;
    private ImageTexture? _cursorMedium;
    private ImageTexture? _cursorLarge;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        FocusMode = FocusModeEnum.None;
        Size = Vector2.One;
        Visible = false;
        _cursorNormal = BuildCursorTexture(24);
        _cursorMedium = BuildCursorTexture(28);
        _cursorLarge = BuildCursorTexture(32);
        SetProcess(true);
    }

    public override void _ExitTree()
    {
        if (_cursorStage != 0)
        {
            Input.SetCustomMouseCursor(null, Input.CursorShape.Arrow);
            _cursorStage = 0;
        }
    }

    public override void _Process(double delta)
    {
        float dt = Math.Max(0.0f, (float)delta);
        if (_pulse > 0.0f)
        {
            _pulse = MathF.Max(0.0f, _pulse - dt * 3.6f);
            QueueRedraw();
        }

        if (_cursorPopRemaining > 0.0f)
        {
            _cursorPopRemaining = MathF.Max(0.0f, _cursorPopRemaining - dt);
            UpdateHardwareCursorStage();
        }
        else if (_cursorStage != 0)
        {
            Input.SetCustomMouseCursor(null, Input.CursorShape.Arrow);
            _cursorStage = 0;
        }
    }

    public override void _Draw()
    {
        if (!_active) return;

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

    public void SetState(
        bool active,
        Vector2 screenPosition,
        ManualMiningFootprintKind footprint,
        float progress)
    {
        _active = active;
        Visible = active;
        Position = screenPosition;
        if (!active)
        {
            _progress = 0.0f;
            _pulse = 0.0f;
            QueueRedraw();
            return;
        }

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
        _cursorPopRemaining = CursorPopDuration;
        SetHardwareCursorStage(3);
    }

    private void UpdateHardwareCursorStage()
    {
        int stage = _cursorPopRemaining switch
        {
            > 0.115f => 3,
            > 0.055f => 2,
            > 0.0f => 1,
            _ => 0,
        };
        SetHardwareCursorStage(stage);
    }

    private void SetHardwareCursorStage(int stage)
    {
        if (_cursorStage == stage) return;
        _cursorStage = stage;
        Resource? texture = stage switch
        {
            3 => _cursorLarge,
            2 => _cursorMedium,
            1 => _cursorNormal,
            _ => null,
        };
        Input.SetCustomMouseCursor(texture, Input.CursorShape.Arrow, Vector2.Zero);
    }

    private static ImageTexture BuildCursorTexture(int size)
    {
        Image image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        image.Fill(new Color(0, 0, 0, 0));

        Vector2[] polygon =
        [
            new(1.0f, 1.0f),
            new(1.0f, size * 0.73f),
            new(size * 0.23f, size * 0.57f),
            new(size * 0.40f, size * 0.91f),
            new(size * 0.53f, size * 0.84f),
            new(size * 0.36f, size * 0.53f),
            new(size * 0.67f, size * 0.49f),
        ];

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            Vector2 point = new(x + 0.5f, y + 0.5f);
            if (!PointInPolygon(point, polygon)) continue;

            bool edge = false;
            for (int oy = -1; oy <= 1 && !edge; oy++)
            for (int ox = -1; ox <= 1; ox++)
            {
                if (ox == 0 && oy == 0) continue;
                if (!PointInPolygon(point + new Vector2(ox, oy), polygon))
                {
                    edge = true;
                    break;
                }
            }
            image.SetPixel(x, y, edge ? new Color(0.02f, 0.03f, 0.035f, 1.0f) : Colors.White);
        }

        return ImageTexture.CreateFromImage(image);
    }

    private static bool PointInPolygon(Vector2 point, Vector2[] polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[j];
            bool crosses = (a.Y > point.Y) != (b.Y > point.Y)
                && point.X < (b.X - a.X) * (point.Y - a.Y) / Math.Max(0.0001f, b.Y - a.Y) + a.X;
            if (crosses) inside = !inside;
        }
        return inside;
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
'''
write(cursor_path, cursor)

print("Applied collector motion v4.")
