#!/usr/bin/env python3
from __future__ import annotations

import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def replace_once(relative: str, old: str, new: str) -> None:
    path = ROOT / relative
    text = path.read_text(encoding="utf-8")
    if old not in text:
        raise RuntimeError(f"Patch anchor not found in {relative}: {old[:120]!r}")
    path.write_text(text.replace(old, new, 1), encoding="utf-8")


def write(relative: str, content: str) -> None:
    path = ROOT / relative
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")


RESOURCE_COLLECTION = r'''using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using TenMillionBlocks.Mining;
using TenMillionBlocks.Presentation;
using TenMillionBlocks.Skills;
using TenMillionBlocks.World;
using TenMillionBlocks.World.Interaction;

namespace TenMillionBlocks.Collection;

public sealed class ResourcePickupSnapshot
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public string BlockId { get; set; } = string.Empty;
    public long Amount { get; set; }
    public bool Automated { get; set; }
}

/// <summary>
/// Authoritative deferred ordinary-resource collection with instanced presentation.
///
/// Manual and live automation mining still calculate rewards in MiningService, but this observer is
/// deliberately subscribed before GameRoot and presentation observers. Unless the matching auto-collect
/// skill is owned, it immediately removes the just-awarded ordinary currency and stores it in a pickup.
/// Hovering the pickup credits the same amount back. Special resources keep their existing direct path.
///
/// Pickups are data, not Nodes/physics bodies. Rendering is split into 8x8x8 voxel buckets; each occupied
/// bucket owns one fixed-capacity MultiMesh (512 possible voxels) so thousands of pickups stay a small
/// number of draw submissions and Godot can coarse-cull buckets independently. Hover lookup walks only
/// buckets touched by the cursor ray instead of scanning every pending pickup every frame.
/// </summary>
public partial class ResourceCollectionField : Node3D
{
    private const int BucketVoxelSize = 8;
    private const int BucketCapacity = BucketVoxelSize * BucketVoxelSize * BucketVoxelSize;

    private sealed class Pickup
    {
        public int Id;
        public Vector3I Voxel;
        public string BlockId = string.Empty;
        public long Amount;
        public bool Automated;
        public Vector3I BucketKey;
        public int RenderSlot;
    }

    private sealed class RenderBucket
    {
        public Vector3I Key;
        public MultiMeshInstance3D Node = null!;
        public MultiMesh MultiMesh = null!;
        public List<int> PickupIds { get; } = new(BucketCapacity);
    }

    private VirtualWorld _world = null!;
    private MiningService _mining = null!;
    private SkillTreeService _skills = null!;
    private OrbitCameraController _camera = null!;
    private ManualMiningController _manual = null!;
    private Mesh _pickupMesh = null!;
    private float _spacing;
    private int _nextId = 1;
    private long _pendingAmount;
    private double _collectionBudget;

    private readonly Dictionary<int, Pickup> _pickups = new();
    private readonly Dictionary<Vector3I, RenderBucket> _buckets = new();
    private readonly HashSet<Vector3I> _visitedBuckets = new();
    private readonly List<int> _hoverCandidates = new();
    private readonly List<int> _sweepIds = new();

    private CanvasLayer? _hintLayer;
    private Label? _hint;

    public event Action? PendingChanged;
    public int PendingCount => _pickups.Count;
    public long PendingAmount => _pendingAmount;

    public void Initialize(
        VirtualWorld world,
        MiningService mining,
        SkillTreeService skills,
        OrbitCameraController camera,
        ManualMiningController manual)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _mining = mining ?? throw new ArgumentNullException(nameof(mining));
        _skills = skills ?? throw new ArgumentNullException(nameof(skills));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _manual = manual ?? throw new ArgumentNullException(nameof(manual));
        _spacing = Math.Max(0.01f, world.Profile.BlockSpacing);
        _pickupMesh = BuildPickupMesh(_spacing * 0.24f);
        BuildHint();

        // GameRoot intentionally attaches its BlockMined observer after this one. That guarantees a
        // final mined block cannot open completion before its deferred reward pickup exists.
        _mining.BlockMined += OnBlockMined;
        _skills.Changed += OnSkillsChanged;
    }

    public override void _ExitTree()
    {
        if (_mining is not null) _mining.BlockMined -= OnBlockMined;
        if (_skills is not null) _skills.Changed -= OnSkillsChanged;
    }

    public override void _Process(double delta)
    {
        if (_pickups.Count == 0)
        {
            _collectionBudget = 0.0;
            return;
        }

        if (!_manual.InputEnabled || _manual.PlacementMode || _camera.IsManipulating)
        {
            _collectionBudget = 0.0;
            return;
        }

        Camera3D camera = _camera.Camera;
        Vector2 mouse = GetViewport().GetMousePosition();
        Vector3 rayOrigin = camera.ProjectRayOrigin(mouse);
        Vector3 rayDirection = camera.ProjectRayNormal(mouse).Normalized();
        float maxDistance = _world.GetWorldBounds().Size.Length() * 2.5f;

        // Do not magnetically collect through the solid world. The first current voxel hit limits the
        // interaction ray, while allowing a small margin for pickups floating in the newly opened cell.
        if (VoxelRaycaster.TryRaycast(_world, camera, mouse, maxDistance, out Vector3I hitVoxel, out _))
        {
            Vector3 hitPosition = (Vector3)hitVoxel * _spacing;
            maxDistance = Math.Min(maxDistance, rayOrigin.DistanceTo(hitPosition) + _spacing * 1.35f);
        }

        float radius = (float)Math.Max(0.05, _skills.Derived.CollectionRadiusBlocks) * _spacing;
        GatherHoverCandidates(rayOrigin, rayDirection, maxDistance, radius);
        if (_hoverCandidates.Count == 0)
        {
            _collectionBudget = 0.0;
            return;
        }

        double rate = Math.Max(0.5, _skills.Derived.CollectionRatePerSecond);
        _collectionBudget = Math.Min(rate * 0.5, _collectionBudget + Math.Max(0.0, delta) * rate);
        int take = Math.Min(_hoverCandidates.Count, (int)_collectionBudget);
        if (take <= 0) return;

        long collected = 0L;
        for (int index = 0; index < take; index++)
        {
            int id = _hoverCandidates[index];
            if (!_pickups.TryGetValue(id, out Pickup? pickup)) continue;
            collected = checked(collected + pickup.Amount);
            RemovePickup(id, notify: false);
        }

        _collectionBudget -= take;
        if (collected > 0)
        {
            // One currency notification per rendered collection batch, even when a larger collection
            // radius catches several instanced pickups at once.
            _mining.GrantCurrency(collected);
            NotifyPendingChanged();
        }
    }

    public List<ResourcePickupSnapshot> CreateSnapshot()
        => _pickups.Values
            .OrderBy(pickup => pickup.Voxel.X)
            .ThenBy(pickup => pickup.Voxel.Y)
            .ThenBy(pickup => pickup.Voxel.Z)
            .Select(pickup => new ResourcePickupSnapshot
            {
                X = pickup.Voxel.X,
                Y = pickup.Voxel.Y,
                Z = pickup.Voxel.Z,
                BlockId = pickup.BlockId,
                Amount = pickup.Amount,
                Automated = pickup.Automated,
            })
            .ToList();

    public void RestoreSnapshot(IEnumerable<ResourcePickupSnapshot>? snapshot)
    {
        if (snapshot is null) return;
        foreach (ResourcePickupSnapshot item in snapshot)
        {
            if (item.Amount <= 0 || string.IsNullOrWhiteSpace(item.BlockId)) continue;
            AddPickup(
                new Vector3I(item.X, item.Y, item.Z),
                item.BlockId,
                item.Amount,
                item.Automated,
                notify: false);
        }
        UpdateHint();
    }

    private void OnBlockMined(MiningResult result)
    {
        if (!result.Success || !result.Removed || result.Reward <= 0) return;
        bool automated = result.Source == MiningSource.Automated;
        bool manual = result.Source == MiningSource.Manual;
        if (!manual && !automated) return;

        bool autoCollect = manual
            ? _skills.Derived.ManualAutoCollectUnlocked
            : _skills.Derived.AutomationAutoCollectUnlocked;
        if (autoCollect) return;

        // MiningService has just credited this exact reward. Move it from the bank into the world pickup
        // before later BlockMined observers execute. The reward calculation therefore stays centralized.
        if (!_mining.TrySpend(result.Reward))
        {
            GD.PushWarning($"Could not defer {result.Reward} resources for pickup at {result.Voxel}; leaving reward banked.");
            return;
        }

        if (!AddPickup(result.Voxel, result.BlockId, result.Reward, automated, notify: true))
        {
            // Defensive fallback: never destroy player value if malformed content somehow overfills a
            // bucket beyond the one-pickup-per-voxel invariant.
            _mining.GrantCurrency(result.Reward);
        }
    }

    private void OnSkillsChanged()
    {
        bool collectManual = _skills.Derived.ManualAutoCollectUnlocked;
        bool collectAutomation = _skills.Derived.AutomationAutoCollectUnlocked;
        if (!collectManual && !collectAutomation) return;

        _sweepIds.Clear();
        foreach ((int id, Pickup pickup) in _pickups)
        {
            if ((!pickup.Automated && collectManual) || (pickup.Automated && collectAutomation))
                _sweepIds.Add(id);
        }
        if (_sweepIds.Count == 0) return;

        long collected = 0L;
        foreach (int id in _sweepIds)
        {
            if (!_pickups.TryGetValue(id, out Pickup? pickup)) continue;
            collected = checked(collected + pickup.Amount);
            RemovePickup(id, notify: false);
        }
        if (collected > 0) _mining.GrantCurrency(collected);
        NotifyPendingChanged();
    }

    private bool AddPickup(Vector3I voxel, string blockId, long amount, bool automated, bool notify)
    {
        Vector3I key = BucketForVoxel(voxel);
        RenderBucket bucket = GetOrCreateBucket(key);
        if (bucket.PickupIds.Count >= BucketCapacity)
        {
            GD.PushWarning($"Resource pickup bucket {key} exceeded {BucketCapacity} entries.");
            return false;
        }

        int id = _nextId++;
        int slot = bucket.PickupIds.Count;
        var pickup = new Pickup
        {
            Id = id,
            Voxel = voxel,
            BlockId = blockId,
            Amount = amount,
            Automated = automated,
            BucketKey = key,
            RenderSlot = slot,
        };
        _pickups.Add(id, pickup);
        bucket.PickupIds.Add(id);
        _pendingAmount = checked(_pendingAmount + amount);
        WriteVisual(bucket, slot, pickup);
        bucket.MultiMesh.VisibleInstanceCount = bucket.PickupIds.Count;
        if (notify) NotifyPendingChanged(); else UpdateHint();
        return true;
    }

    private void RemovePickup(int id, bool notify)
    {
        if (!_pickups.Remove(id, out Pickup? pickup)) return;
        _pendingAmount = Math.Max(0L, _pendingAmount - pickup.Amount);

        if (!_buckets.TryGetValue(pickup.BucketKey, out RenderBucket? bucket))
        {
            if (notify) NotifyPendingChanged(); else UpdateHint();
            return;
        }

        int lastSlot = bucket.PickupIds.Count - 1;
        int slot = pickup.RenderSlot;
        if (slot < 0 || slot > lastSlot)
        {
            throw new InvalidOperationException($"Pickup {id} has invalid render slot {slot} in bucket {pickup.BucketKey}.");
        }

        if (slot != lastSlot)
        {
            int movedId = bucket.PickupIds[lastSlot];
            bucket.PickupIds[slot] = movedId;
            Pickup moved = _pickups[movedId];
            moved.RenderSlot = slot;
            WriteVisual(bucket, slot, moved);
        }
        bucket.PickupIds.RemoveAt(lastSlot);
        bucket.MultiMesh.VisibleInstanceCount = bucket.PickupIds.Count;

        if (bucket.PickupIds.Count == 0)
        {
            _buckets.Remove(bucket.Key);
            bucket.Node.QueueFree();
        }

        if (notify) NotifyPendingChanged(); else UpdateHint();
    }

    private void GatherHoverCandidates(Vector3 origin, Vector3 direction, float maxDistance, float radius)
    {
        _hoverCandidates.Clear();
        _visitedBuckets.Clear();
        float radiusSquared = radius * radius;
        float step = Math.Max(_spacing * 1.5f, _spacing * BucketVoxelSize * 0.40f);

        for (float distance = 0.0f; distance <= maxDistance; distance += step)
        {
            Vector3I centerKey = BucketForWorldPoint(origin + direction * distance);
            for (int z = -1; z <= 1; z++)
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                Vector3I key = centerKey + new Vector3I(x, y, z);
                if (!_visitedBuckets.Add(key) || !_buckets.TryGetValue(key, out RenderBucket? bucket)) continue;

                foreach (int id in bucket.PickupIds)
                {
                    if (!_pickups.TryGetValue(id, out Pickup? pickup)) continue;
                    Vector3 position = PickupWorldPosition(pickup.Voxel);
                    float along = (position - origin).Dot(direction);
                    if (along < 0.0f || along > maxDistance) continue;
                    Vector3 closest = origin + direction * along;
                    if (closest.DistanceSquaredTo(position) <= radiusSquared) _hoverCandidates.Add(id);
                }
            }
        }
    }

    private RenderBucket GetOrCreateBucket(Vector3I key)
    {
        if (_buckets.TryGetValue(key, out RenderBucket? existing)) return existing;

        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = _pickupMesh,
        };
        multiMesh.InstanceCount = BucketCapacity;
        multiMesh.VisibleInstanceCount = 0;

        var node = new MultiMeshInstance3D
        {
            Name = $"PickupBucket_{key.X}_{key.Y}_{key.Z}",
            Multimesh = multiMesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(node);

        var bucket = new RenderBucket { Key = key, Node = node, MultiMesh = multiMesh };
        _buckets.Add(key, bucket);
        return bucket;
    }

    private void WriteVisual(RenderBucket bucket, int slot, Pickup pickup)
    {
        Vector3 position = PickupWorldPosition(pickup.Voxel);
        bucket.MultiMesh.SetInstanceTransform(slot, new Transform3D(Basis.Identity, position));
        bucket.MultiMesh.SetInstanceColor(slot, PickupColor(pickup.BlockId));
    }

    private Vector3 PickupWorldPosition(Vector3I voxel)
        => (Vector3)voxel * _spacing + Vector3.Up * (_spacing * 0.28f);

    private Vector3I BucketForVoxel(Vector3I voxel)
        => new(
            FloorDiv(voxel.X, BucketVoxelSize),
            FloorDiv(voxel.Y, BucketVoxelSize),
            FloorDiv(voxel.Z, BucketVoxelSize));

    private Vector3I BucketForWorldPoint(Vector3 point)
    {
        Vector3I approximateVoxel = new(
            Mathf.RoundToInt(point.X / _spacing),
            Mathf.RoundToInt(point.Y / _spacing),
            Mathf.RoundToInt(point.Z / _spacing));
        return BucketForVoxel(approximateVoxel);
    }

    private static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        int remainder = value % divisor;
        return remainder < 0 ? quotient - 1 : quotient;
    }

    private static Mesh BuildPickupMesh(float size)
    {
        var material = new StandardMaterial3D
        {
            AlbedoColor = Colors.White,
            Roughness = 0.78f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
        };
        return new BoxMesh
        {
            Size = Vector3.One * Math.Max(0.02f, size),
            Material = material,
        };
    }

    private static Color PickupColor(string blockId)
        => blockId switch
        {
            "grass" or "dirt_grass" => new Color("#68b85a"),
            "dirt" => new Color("#9a6848"),
            "sand" => new Color("#e6c877"),
            "stone" => new Color("#9aa2aa"),
            "stone_dark" => new Color("#626a78"),
            "copper" => new Color("#c9794f"),
            "silver" => new Color("#c7d0da"),
            "gold" => new Color("#f0c84d"),
            "gem_red" => new Color("#ff5d67"),
            "gem_blue" => new Color("#55a8ff"),
            "gem_green" => new Color("#62dc8c"),
            "water" or "water_shallow" or "water_deep" => new Color("#55a8d9"),
            _ => new Color("#f0d66b"),
        };

    private void BuildHint()
    {
        _hintLayer = new CanvasLayer { Name = "ResourceCollectionHint", Layer = 23 };
        AddChild(_hintLayer);
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _hintLayer.AddChild(root);

        _hint = new Label
        {
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            AnchorTop = 1.0f,
            AnchorBottom = 1.0f,
            OffsetLeft = -280.0f,
            OffsetTop = -62.0f,
            OffsetRight = 280.0f,
            OffsetBottom = -34.0f,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
            Modulate = new Color(0.94f, 0.89f, 0.62f, 0.95f),
        };
        _hint.AddThemeFontSizeOverride("font_size", 12);
        _hint.AddThemeConstantOverride("outline_size", 4);
        _hint.AddThemeColorOverride("font_outline_color", new Color(0.02f, 0.025f, 0.04f, 0.92f));
        root.AddChild(_hint);
    }

    private void UpdateHint()
    {
        if (_hint is null) return;
        _hint.Visible = _pickups.Count > 0;
        _hint.Text = _pickups.Count == 0
            ? string.Empty
            : $"UNCLAIMED  {_pendingAmount:N0}  ·  HOVER FLOATING RESOURCE CUBES TO COLLECT";
    }

    private void NotifyPendingChanged()
    {
        UpdateHint();
        PendingChanged?.Invoke();
    }
}
'''

GAME_ROOT_COLLECTION = r'''using TenMillionBlocks.Collection;

namespace TenMillionBlocks.App;

public partial class GameRoot
{
    private ResourceCollectionField? _resourceCollection;

    private void OnPendingCollectionChanged()
    {
        MarkAutosaveDirty();
        if (_sessionPersists
            && _world is not null
            && _world.RemainingMineableBlocks == 0
            && (_resourceCollection?.PendingCount ?? 0) == 0
            && !_completionShown)
        {
            ShowCompletion(debugPreview: false);
        }
    }
}
'''

write("src/Collection/ResourceCollectionField.cs", RESOURCE_COLLECTION)
write("src/App/GameRoot.ResourceCollection.cs", GAME_ROOT_COLLECTION)

# Skill runtime stats/effects.
replace_once(
    "src/Skills/SkillTreeService.cs",
    "    public int ManualPenetrationDepth { get; internal set; } = 1;\n\n    public double ResourceYieldMultiplier",
    "    public int ManualPenetrationDepth { get; internal set; } = 1;\n\n"
    "    public double CollectionRadiusBlocks { get; internal set; } = 0.32;\n"
    "    public double CollectionRatePerSecond { get; internal set; } = 8.0;\n"
    "    public bool ManualAutoCollectUnlocked { get; internal set; }\n"
    "    public bool AutomationAutoCollectUnlocked { get; internal set; }\n\n"
    "    public double ResourceYieldMultiplier")
replace_once(
    "src/Skills/SkillTreeService.cs",
    "            case \"unlock_hover_mining\": stats.HoverMiningUnlocked = true; break;\n            case \"multiply_resource_yield\"",
    "            case \"unlock_hover_mining\": stats.HoverMiningUnlocked = true; break;\n"
    "            case \"set_collection_radius_blocks\": stats.CollectionRadiusBlocks = Math.Max(stats.CollectionRadiusBlocks, Math.Max(0.05, effect.Value)); break;\n"
    "            case \"multiply_collection_rate\": stats.CollectionRatePerSecond *= Math.Max(0.05, effect.Value); break;\n"
    "            case \"unlock_manual_auto_collect\": stats.ManualAutoCollectUnlocked = true; break;\n"
    "            case \"unlock_automation_auto_collect\": stats.AutomationAutoCollectUnlocked = true; break;\n"
    "            case \"multiply_resource_yield\"")

# Runtime content validation.
replace_once(
    "src/Skills/SkillTreeDefinition.cs",
    "        \"unlock_hover_mining\",\n        \"multiply_resource_yield\",",
    "        \"unlock_hover_mining\",\n"
    "        \"set_collection_radius_blocks\",\n"
    "        \"multiply_collection_rate\",\n"
    "        \"unlock_manual_auto_collect\",\n"
    "        \"unlock_automation_auto_collect\",\n"
    "        \"multiply_resource_yield\",")
replace_once(
    "tools/validate_content.py",
    "    \"unlock_hover_mining\",\n    \"multiply_resource_yield\",",
    "    \"unlock_hover_mining\",\n"
    "    \"set_collection_radius_blocks\",\n"
    "    \"multiply_collection_rate\",\n"
    "    \"unlock_manual_auto_collect\",\n"
    "    \"unlock_automation_auto_collect\",\n"
    "    \"multiply_resource_yield\",")

# New collection nodes are authored in the normal data file so editor/runtime/validation all see the
# same graph. Keep the chain left of the manual branch to preserve the responsive constellation layout.
skill_path = ROOT / "data/skills/skill_tree.json"
skill_doc = json.loads(skill_path.read_text(encoding="utf-8"))
existing_ids = {node["id"] for node in skill_doc["nodes"]}
collection_nodes = [
    {
        "id": "collection_reach_1",
        "display_name": "Collector Reach I",
        "description": "Increase the hover pickup field from 0.32 to 0.50 block widths so floating resources are easier to catch without precise pixel-perfect aiming.",
        "grid_x": -2,
        "grid_y": 0,
        "category": "manual",
        "purchase_mode": "once",
        "prerequisites": [],
        "cost": 5,
        "max_rank": 1,
        "effects": [{"type": "set_collection_radius_blocks", "value": 0.50}],
    },
    {
        "id": "collection_rate_1",
        "display_name": "Collector Pull I",
        "description": "Pull hovered resource pickups 75% faster. Dense clusters are collected more quickly while the cursor passes over them.",
        "grid_x": -2,
        "grid_y": 2,
        "category": "manual",
        "purchase_mode": "once",
        "prerequisites": [{"node_id": "collection_reach_1", "required_rank": 1}],
        "hide_until_prerequisites_met": True,
        "cost": 12,
        "max_rank": 1,
        "effects": [{"type": "multiply_collection_rate", "value": 1.75}],
    },
    {
        "id": "collection_reach_2",
        "display_name": "Collector Reach II",
        "description": "Expand the hover pickup field again, from 0.50 to 0.80 block widths.",
        "grid_x": -2,
        "grid_y": 4,
        "category": "manual",
        "purchase_mode": "once",
        "prerequisites": [{"node_id": "collection_rate_1", "required_rank": 1}],
        "hide_until_prerequisites_met": True,
        "cost": 45,
        "max_rank": 1,
        "effects": [{"type": "set_collection_radius_blocks", "value": 0.80}],
    },
    {
        "id": "collection_rate_2",
        "display_name": "Collector Pull II",
        "description": "Double collection throughput again. Hovering a resource cluster now vacuums many pickups per second.",
        "grid_x": -2,
        "grid_y": 6,
        "category": "manual",
        "purchase_mode": "once",
        "prerequisites": [
            {"node_id": "collection_reach_2", "required_rank": 1},
            {"node_id": "hover_mining_unlock", "required_rank": 1},
        ],
        "hide_until_prerequisites_met": True,
        "cost": 120,
        "max_rank": 1,
        "effects": [{"type": "multiply_collection_rate", "value": 2.0}],
    },
    {
        "id": "collection_auto_manual",
        "display_name": "Personal Auto-Collect",
        "description": "Manual and Hover Mining rewards bank themselves immediately. Existing manual pickups are collected when this upgrade is purchased.",
        "grid_x": -2,
        "grid_y": 8,
        "category": "manual",
        "purchase_mode": "once",
        "prerequisites": [
            {"node_id": "collection_rate_2", "required_rank": 1},
            {"node_id": "manual_hover_speed_3", "required_rank": 1},
        ],
        "hide_until_prerequisites_met": True,
        "cost": 900,
        "max_rank": 1,
        "effects": [{"type": "unlock_manual_auto_collect"}],
    },
    {
        "id": "collection_auto_automation",
        "display_name": "Automation Logistics",
        "description": "Live automation rewards bank themselves immediately instead of leaving pickups behind. Existing automation pickups are collected on purchase.",
        "grid_x": 9,
        "grid_y": 5,
        "category": "automation",
        "purchase_mode": "once",
        "prerequisites": [
            {"node_id": "automation_unlock", "required_rank": 1},
            {"node_id": "resource_sensors", "required_rank": 1},
        ],
        "hide_until_prerequisites_met": True,
        "cost": 2200,
        "max_rank": 1,
        "effects": [{"type": "unlock_automation_auto_collect"}],
    },
]
if not any(node["id"] in existing_ids for node in collection_nodes):
    skill_doc["nodes"] = collection_nodes + skill_doc["nodes"]
    skill_doc["content_version"] = max(18, int(skill_doc.get("content_version", 0)) + 1)
else:
    missing = [node["id"] for node in collection_nodes if node["id"] not in existing_ids]
    if missing:
        raise RuntimeError(f"Collection skill graph is partially present; refusing mixed patch: {missing}")
skill_path.write_text(json.dumps(skill_doc, indent=2) + "\n", encoding="utf-8")

# Make collection stat nodes circular and auto-collect nodes milestone-shaped, and reuse the resource
# sensor icon family instead of the generic fallback icon.
replace_once(
    "src/UI/SkillTreeIncrementalTheme.cs",
    "                \"unlock_orb_breaker\" or\n                \"add_orb_breaker_count\"))",
    "                \"unlock_orb_breaker\" or\n"
    "                \"add_orb_breaker_count\" or\n"
    "                \"unlock_manual_auto_collect\" or\n"
    "                \"unlock_automation_auto_collect\"))")
replace_once(
    "src/UI/SkillTreeIncrementalTheme.cs",
    "                \"set_manual_footprint\" or\n                \"multiply_resource_yield\" or",
    "                \"set_manual_footprint\" or\n"
    "                \"set_collection_radius_blocks\" or\n"
    "                \"multiply_collection_rate\" or\n"
    "                \"multiply_resource_yield\" or")
replace_once(
    "src/UI/SkillTreeIncrementalTheme.cs",
    "        [\"manual_aftershock\"] = 23,\n        [\"automation_unlock\"] = 4,",
    "        [\"manual_aftershock\"] = 23,\n"
    "        [\"collection_reach_1\"] = 9,\n"
    "        [\"collection_rate_1\"] = 9,\n"
    "        [\"collection_reach_2\"] = 9,\n"
    "        [\"collection_rate_2\"] = 9,\n"
    "        [\"collection_auto_manual\"] = 9,\n"
    "        [\"collection_auto_automation\"] = 9,\n"
    "        [\"automation_unlock\"] = 4,")

# Persist deferred pickups without a schema bump: this is an additive field and older v3 saves simply
# deserialize the missing list as empty after normalization.
replace_once(
    "src/Save/SaveService.cs",
    "using TenMillionBlocks.Automation;\nusing TenMillionBlocks.Content;",
    "using TenMillionBlocks.Automation;\nusing TenMillionBlocks.Collection;\nusing TenMillionBlocks.Content;")
replace_once(
    "src/Save/SaveService.cs",
    "    public List<MinerSnapshot> Miners { get; set; } = new();\n}",
    "    public List<MinerSnapshot> Miners { get; set; } = new();\n"
    "    public List<ResourcePickupSnapshot> PendingPickups { get; set; } = new();\n}")
replace_once(
    "src/Save/SaveService.cs",
    "            world.Miners ??= new List<MinerSnapshot>();\n            if (world.Completed)",
    "            world.Miners ??= new List<MinerSnapshot>();\n"
    "            world.PendingPickups ??= new List<ResourcePickupSnapshot>();\n"
    "            if (world.Completed)")

# GameRoot lifecycle: resource field subscribes first, persists pickups, and final-world completion waits
# until the player has collected the last deferred reward.
replace_once(
    "src/App/GameRoot.cs",
    "        _mining.BlockMined += OnBlockMined;\n        _mining.BulkMined += OnBulkMined;",
    "        // BlockMined is attached after ResourceCollectionField so deferred rewards exist before\n"
    "        // completion/stat observers see the removal.\n"
    "        _mining.BulkMined += OnBulkMined;")
replace_once(
    "src/App/GameRoot.cs",
    "        _manualMining.Initialize(_world, _camera, _worldView, _mining, _skills);\n        if (savedWorld is not null) _manualMining.RestoreHoverMiningEnabled(savedWorld.HoverMiningEnabled);\n        _sessionRoot.AddChild(_manualMining);\n\n        _miners = new MinerSimulationService",
    "        _manualMining.Initialize(_world, _camera, _worldView, _mining, _skills);\n"
    "        if (savedWorld is not null) _manualMining.RestoreHoverMiningEnabled(savedWorld.HoverMiningEnabled);\n"
    "        _sessionRoot.AddChild(_manualMining);\n\n"
    "        _resourceCollection = new ResourceCollectionField { Name = \"ResourceCollectionField\" };\n"
    "        _resourceCollection.Initialize(_world, _mining, _skills, _camera, _manualMining);\n"
    "        if (savedWorld is not null) _resourceCollection.RestoreSnapshot(savedWorld.PendingPickups);\n"
    "        _resourceCollection.PendingChanged += OnPendingCollectionChanged;\n"
    "        _sessionRoot.AddChild(_resourceCollection);\n"
    "        _mining.BlockMined += OnBlockMined;\n\n"
    "        _miners = new MinerSimulationService")
replace_once(
    "src/App/GameRoot.cs",
    "        if (persistSession && _world.RemainingMineableBlocks == 0) ShowCompletion(debugPreview: false);",
    "        if (persistSession\n"
    "            && _world.RemainingMineableBlocks == 0\n"
    "            && (_resourceCollection?.PendingCount ?? 0) == 0)\n"
    "        {\n"
    "            ShowCompletion(debugPreview: false);\n"
    "        }")
replace_once(
    "src/App/GameRoot.cs",
    "        _worldEvents = null;\n        _replayPath = string.Empty;\n\n        if (_sessionRoot is null) return;",
    "        _worldEvents = null;\n"
    "        _replayPath = string.Empty;\n"
    "        if (_resourceCollection is not null) _resourceCollection.PendingChanged -= OnPendingCollectionChanged;\n\n"
    "        if (_sessionRoot is null) return;")
replace_once(
    "src/App/GameRoot.cs",
    "        _manualMining = null;\n        _miners = null;",
    "        _manualMining = null;\n"
    "        _resourceCollection = null;\n"
    "        _miners = null;")
replace_once(
    "src/App/GameRoot.cs",
    "        if (_sessionPersists && result.Remaining == 0 && !_completionShown) ShowCompletion(debugPreview: false);",
    "        if (_sessionPersists\n"
    "            && result.Remaining == 0\n"
    "            && (_resourceCollection?.PendingCount ?? 0) == 0\n"
    "            && !_completionShown)\n"
    "        {\n"
    "            ShowCompletion(debugPreview: false);\n"
    "        }")
replace_once(
    "src/App/GameRoot.cs",
    "            Miners = _miners.CreateSnapshot(),\n        };",
    "            Miners = _miners.CreateSnapshot(),\n"
    "            PendingPickups = _resourceCollection?.CreateSnapshot() ?? previous?.PendingPickups ?? new(),\n"
    "        };")

# The old presentation comment said currency was committed before any pickup model. Keep the feedback
# layer accurate now that ordinary manual/live-automation rewards can be deferred.
replace_once(
    "src/App/GameRoot.cs",
    "        // Incremental-game feedback is a pure observer: state/currency/replay have already been\n        // committed by MiningService before any pickup model, number or counter pulse is emitted.",
    "        // Incremental-game feedback remains presentation-only. ResourceCollectionField has already\n"
    "        // decided whether ordinary manual/live-automation rewards are banked or deferred pickups.")

# Add a concise implementation note to the status document if it is not already there.
status_path = ROOT / "docs/IMPLEMENTATION_STATUS.md"
status = status_path.read_text(encoding="utf-8")
marker = "## Hover resource collection pass"
if marker not in status:
    status += "\n\n" + marker + "\n\n"
    status += (
        "- Manual and live automation mining now leave ordinary-resource pickups in-world until hovered; "
        "special resources retain their direct authoritative path.\n"
        "- Pickup presentation is data-only and rendered through fixed-capacity 8x8x8 MultiMesh buckets; "
        "hover interaction queries only buckets along the cursor ray.\n"
        "- Collector Reach/Pull upgrades expand radius and throughput, Personal Auto-Collect removes manual "
        "pickup friction later, and Automation Logistics separately enables live-automation auto-collection.\n"
        "- Pending pickups persist in world saves and final completion waits until the last deferred reward is collected.\n"
    )
    status_path.write_text(status, encoding="utf-8")

print("Applied resource collection progression pass.")
