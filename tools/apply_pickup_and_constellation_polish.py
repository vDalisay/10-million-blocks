#!/usr/bin/env python3
from __future__ import annotations

import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def replace_once(path: str, old: str, new: str) -> None:
    target = ROOT / path
    text = target.read_text(encoding="utf-8")
    if old not in text:
        raise RuntimeError(f"anchor not found in {path}: {old[:140]!r}")
    target.write_text(text.replace(old, new, 1), encoding="utf-8")


def replace_regex(path: str, pattern: str, replacement: str) -> None:
    target = ROOT / path
    text = target.read_text(encoding="utf-8")
    updated, count = re.subn(pattern, replacement, text, count=1, flags=re.S)
    if count != 1:
        raise RuntimeError(f"regex patch count for {path}: {count} ({pattern[:90]!r})")
    target.write_text(updated, encoding="utf-8")


RESOURCE_COLLECTION = r'''using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using TenMillionBlocks.Content;
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
/// Deferred ordinary-resource collection rendered as miniature versions of the actual mined blocks.
///
/// There is still no Node/physics body per pickup. Pickups are authoritative data records. Rendering is
/// grouped by 8x8x8 spatial cell AND block visual so every MultiMesh can reuse the real block mesh and
/// material while culling remains coarse and bounded. Newly mined pickups receive a short CPU-side burst
/// transform only for their first half second; after that, one small transform per occupied render bucket
/// supplies the idle hover/bob. This keeps the persistent cost proportional to buckets, not pickup count.
/// </summary>
public partial class ResourceCollectionField : Node3D
{
    private const int BucketVoxelSize = 8;
    private const int BucketCapacity = BucketVoxelSize * BucketVoxelSize * BucketVoxelSize;
    private const float PickupScale = 0.30f;
    private const float SpawnDuration = 0.48f;

    private readonly record struct RenderBucketKey(Vector3I Cell, string BlockId);

    private sealed class Pickup
    {
        public int Id;
        public Vector3I Voxel;
        public string BlockId = string.Empty;
        public long Amount;
        public bool Automated;
        public RenderBucketKey RenderKey;
        public int RenderSlot;
        public Vector3 OriginPosition;
        public Vector3 FinalPosition;
        public Basis Basis = Basis.Identity;
        public double SpawnTime;
    }

    private sealed class RenderBucket
    {
        public RenderBucketKey Key;
        public MultiMeshInstance3D Node = null!;
        public MultiMesh MultiMesh = null!;
        public float BobPhase;
        public List<int> PickupIds { get; } = new(BucketCapacity);
    }

    private VirtualWorld _world = null!;
    private MiningService _mining = null!;
    private SkillTreeService _skills = null!;
    private OrbitCameraController _camera = null!;
    private ManualMiningController _manual = null!;
    private BlockAssetRegistry _assets = null!;
    private float _spacing;
    private int _nextId = 1;
    private long _pendingAmount;
    private double _collectionBudget;
    private double _visualTime;

    private readonly Dictionary<int, Pickup> _pickups = new();
    private readonly Dictionary<RenderBucketKey, RenderBucket> _buckets = new();
    private readonly Dictionary<Vector3I, List<RenderBucket>> _bucketsByCell = new();
    private readonly HashSet<Vector3I> _visitedCells = new();
    private readonly List<int> _hoverCandidates = new();
    private readonly List<int> _sweepIds = new();
    private readonly List<int> _activeSpawnIds = new();

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
        ManualMiningController manual,
        BlockAssetRegistry assets)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _mining = mining ?? throw new ArgumentNullException(nameof(mining));
        _skills = skills ?? throw new ArgumentNullException(nameof(skills));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _manual = manual ?? throw new ArgumentNullException(nameof(manual));
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _spacing = Math.Max(0.01f, world.Profile.BlockSpacing);
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
        _visualTime += Math.Max(0.0, delta);
        UpdateVisualMotion();

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
        // interaction ray, while allowing a margin for pickups that jumped into the newly opened cell.
        if (VoxelRaycaster.TryRaycast(_world, camera, mouse, maxDistance, out Vector3I hitVoxel, out _))
        {
            Vector3 hitPosition = (Vector3)hitVoxel * _spacing;
            maxDistance = Math.Min(maxDistance, rayOrigin.DistanceTo(hitPosition) + _spacing * 1.7f);
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
                notify: false,
                animateSpawn: false);
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

        if (!AddPickup(result.Voxel, result.BlockId, result.Reward, automated, notify: true, animateSpawn: true))
        {
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

    private bool AddPickup(
        Vector3I voxel,
        string blockId,
        long amount,
        bool automated,
        bool notify,
        bool animateSpawn)
    {
        Vector3I cell = BucketForVoxel(voxel);
        RenderBucket bucket = GetOrCreateBucket(cell, blockId);
        if (bucket.PickupIds.Count >= BucketCapacity)
        {
            GD.PushWarning($"Resource pickup bucket {cell}/{blockId} exceeded {BucketCapacity} entries.");
            return false;
        }

        int id = _nextId++;
        int slot = bucket.PickupIds.Count;
        Vector2 scatter = PickupScatter(voxel, id);
        float angle = MathF.Atan2(scatter.Y, scatter.X) * 0.35f;
        Vector3 blockCenter = (Vector3)voxel * _spacing;
        Vector3 finalPosition = blockCenter
            + new Vector3(scatter.X, _spacing * 0.27f, scatter.Y);

        var pickup = new Pickup
        {
            Id = id,
            Voxel = voxel,
            BlockId = blockId,
            Amount = amount,
            Automated = automated,
            RenderKey = bucket.Key,
            RenderSlot = slot,
            OriginPosition = blockCenter + Vector3.Up * (_spacing * 0.06f),
            FinalPosition = finalPosition,
            Basis = new Basis(Vector3.Up, angle).Scaled(Vector3.One * PickupScale),
            SpawnTime = animateSpawn ? _visualTime : _visualTime - SpawnDuration,
        };
        _pickups.Add(id, pickup);
        bucket.PickupIds.Add(id);
        _pendingAmount = checked(_pendingAmount + amount);
        WriteVisual(bucket, slot, pickup, animateSpawn ? pickup.OriginPosition : pickup.FinalPosition);
        bucket.MultiMesh.VisibleInstanceCount = bucket.PickupIds.Count;
        if (animateSpawn && GraphicsSettingsRuntime.Current?.ReducedMotionEnabled != true)
            _activeSpawnIds.Add(id);
        if (notify) NotifyPendingChanged(); else UpdateHint();
        return true;
    }

    private void RemovePickup(int id, bool notify)
    {
        if (!_pickups.Remove(id, out Pickup? pickup)) return;
        _pendingAmount = Math.Max(0L, _pendingAmount - pickup.Amount);

        if (!_buckets.TryGetValue(pickup.RenderKey, out RenderBucket? bucket))
        {
            if (notify) NotifyPendingChanged(); else UpdateHint();
            return;
        }

        int lastSlot = bucket.PickupIds.Count - 1;
        int slot = pickup.RenderSlot;
        if (slot < 0 || slot > lastSlot)
            throw new InvalidOperationException($"Pickup {id} has invalid render slot {slot} in bucket {pickup.RenderKey}.");

        if (slot != lastSlot)
        {
            int movedId = bucket.PickupIds[lastSlot];
            bucket.PickupIds[slot] = movedId;
            Pickup moved = _pickups[movedId];
            moved.RenderSlot = slot;
            WriteVisual(bucket, slot, moved, CurrentVisualPosition(moved));
        }
        bucket.PickupIds.RemoveAt(lastSlot);
        bucket.MultiMesh.VisibleInstanceCount = bucket.PickupIds.Count;

        if (bucket.PickupIds.Count == 0)
            RemoveBucket(bucket);

        if (notify) NotifyPendingChanged(); else UpdateHint();
    }

    private void GatherHoverCandidates(Vector3 origin, Vector3 direction, float maxDistance, float radius)
    {
        _hoverCandidates.Clear();
        _visitedCells.Clear();
        float radiusSquared = radius * radius;
        float step = Math.Max(_spacing * 1.5f, _spacing * BucketVoxelSize * 0.40f);

        for (float distance = 0.0f; distance <= maxDistance; distance += step)
        {
            Vector3I centerKey = BucketForWorldPoint(origin + direction * distance);
            for (int z = -1; z <= 1; z++)
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                Vector3I cell = centerKey + new Vector3I(x, y, z);
                if (!_visitedCells.Add(cell) || !_bucketsByCell.TryGetValue(cell, out List<RenderBucket>? buckets))
                    continue;

                foreach (RenderBucket bucket in buckets)
                foreach (int id in bucket.PickupIds)
                {
                    if (!_pickups.TryGetValue(id, out Pickup? pickup)) continue;
                    Vector3 position = pickup.FinalPosition + bucket.Node.Position;
                    float along = (position - origin).Dot(direction);
                    if (along < 0.0f || along > maxDistance) continue;
                    Vector3 closest = origin + direction * along;
                    if (closest.DistanceSquaredTo(position) <= radiusSquared) _hoverCandidates.Add(id);
                }
            }
        }
    }

    private RenderBucket GetOrCreateBucket(Vector3I cell, string blockId)
    {
        var key = new RenderBucketKey(cell, blockId);
        if (_buckets.TryGetValue(key, out RenderBucket? existing)) return existing;

        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = _assets.GetMesh(blockId),
            InstanceCount = BucketCapacity,
            VisibleInstanceCount = 0,
        };

        var node = new MultiMeshInstance3D
        {
            Name = $"PickupBucket_{blockId}_{cell.X}_{cell.Y}_{cell.Z}",
            Multimesh = multiMesh,
            MaterialOverride = _assets.GetMaterialOverride(blockId),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(node);

        var bucket = new RenderBucket
        {
            Key = key,
            Node = node,
            MultiMesh = multiMesh,
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
    {
        _buckets.Remove(bucket.Key);
        if (_bucketsByCell.TryGetValue(bucket.Key.Cell, out List<RenderBucket>? cellBuckets))
        {
            cellBuckets.Remove(bucket);
            if (cellBuckets.Count == 0) _bucketsByCell.Remove(bucket.Key.Cell);
        }
        bucket.Node.QueueFree();
    }

    private void UpdateVisualMotion()
    {
        bool reducedMotion = GraphicsSettingsRuntime.Current?.ReducedMotionEnabled == true;
        foreach (RenderBucket bucket in _buckets.Values)
        {
            float bob = reducedMotion
                ? 0.0f
                : MathF.Sin((float)(_visualTime * 1.85) + bucket.BobPhase) * _spacing * 0.035f;
            bucket.Node.Position = Vector3.Up * bob;
        }

        if (_activeSpawnIds.Count == 0) return;
        for (int index = _activeSpawnIds.Count - 1; index >= 0; index--)
        {
            int id = _activeSpawnIds[index];
            if (!_pickups.TryGetValue(id, out Pickup? pickup)
                || !_buckets.TryGetValue(pickup.RenderKey, out RenderBucket? bucket))
            {
                _activeSpawnIds.RemoveAt(index);
                continue;
            }

            if (reducedMotion)
            {
                WriteVisual(bucket, pickup.RenderSlot, pickup, pickup.FinalPosition);
                _activeSpawnIds.RemoveAt(index);
                continue;
            }

            float t = Mathf.Clamp((float)((_visualTime - pickup.SpawnTime) / SpawnDuration), 0.0f, 1.0f);
            if (t >= 1.0f)
            {
                WriteVisual(bucket, pickup.RenderSlot, pickup, pickup.FinalPosition);
                _activeSpawnIds.RemoveAt(index);
                continue;
            }

            float eased = 1.0f - MathF.Pow(1.0f - t, 3.0f);
            Vector3 position = pickup.OriginPosition.Lerp(pickup.FinalPosition, eased);
            position.Y += MathF.Sin(t * Mathf.Pi) * _spacing * 0.46f;
            WriteVisual(bucket, pickup.RenderSlot, pickup, position);
        }
    }

    private Vector3 CurrentVisualPosition(Pickup pickup)
    {
        float t = Mathf.Clamp((float)((_visualTime - pickup.SpawnTime) / SpawnDuration), 0.0f, 1.0f);
        if (t >= 1.0f || GraphicsSettingsRuntime.Current?.ReducedMotionEnabled == true) return pickup.FinalPosition;
        float eased = 1.0f - MathF.Pow(1.0f - t, 3.0f);
        Vector3 position = pickup.OriginPosition.Lerp(pickup.FinalPosition, eased);
        position.Y += MathF.Sin(t * Mathf.Pi) * _spacing * 0.46f;
        return position;
    }

    private void WriteVisual(RenderBucket bucket, int slot, Pickup pickup, Vector3 position)
        => bucket.MultiMesh.SetInstanceTransform(slot, new Transform3D(pickup.Basis, position));

    private Vector2 PickupScatter(Vector3I voxel, int id)
    {
        uint hash = Hash((uint)voxel.X, (uint)voxel.Y, (uint)voxel.Z, (uint)id);
        float angle = ((hash & 0xffffu) / 65535.0f) * Mathf.Tau;
        float radius01 = ((hash >> 16) & 0xffffu) / 65535.0f;
        float radius = _spacing * Mathf.Lerp(0.10f, 0.25f, radius01);
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
    }

    private static float BucketPhase(Vector3I cell, string blockId)
    {
        uint text = 2166136261u;
        foreach (char c in blockId) text = (text ^ c) * 16777619u;
        uint hash = Hash((uint)cell.X, (uint)cell.Y, (uint)cell.Z, text);
        return ((hash & 0xffffu) / 65535.0f) * Mathf.Tau;
    }

    private static uint Hash(uint x, uint y, uint z, uint salt)
    {
        unchecked
        {
            uint value = x * 0x9e3779b9u ^ y * 0x85ebca6bu ^ z * 0xc2b2ae35u ^ salt * 0x27d4eb2du;
            value ^= value >> 16;
            value *= 0x7feb352du;
            value ^= value >> 15;
            value *= 0x846ca68bu;
            return value ^ (value >> 16);
        }
    }

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
            Modulate = new Color(0.82f, 0.88f, 0.92f, 0.92f),
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
            : $"UNCLAIMED  {_pendingAmount:N0}  ·  HOVER MINI BLOCKS TO COLLECT";
    }

    private void NotifyPendingChanged()
    {
        UpdateHint();
        PendingChanged?.Invoke();
    }
}
'''

(ROOT / "src/Collection/ResourceCollectionField.cs").write_text(RESOURCE_COLLECTION, encoding="utf-8")

replace_once(
    "src/App/GameRoot.cs",
    "_resourceCollection.Initialize(_world, _mining, _skills, _camera, _manualMining);",
    "_resourceCollection.Initialize(_world, _mining, _skills, _camera, _manualMining, _assets);")

# Double the authored collector-reach upgrades, leaving the small base field intact so the progression
# still has a visible before/after. The upgraded values are now 1.0 and 1.6 block widths.
skill_path = ROOT / "data/skills/skill_tree.json"
skill_doc = json.loads(skill_path.read_text(encoding="utf-8"))
for node in skill_doc["nodes"]:
    if node["id"] == "collection_reach_1":
        node["description"] = "Double the initial hover pickup field to a full 1.0 block width so nearby miniature drops are easy to sweep up."
        node["effects"][0]["value"] = 1.0
    elif node["id"] == "collection_reach_2":
        node["description"] = "Expand the hover pickup field again, from 1.0 to 1.6 block widths for broad cluster collection."
        node["effects"][0]["value"] = 1.6
skill_doc["content_version"] = max(19, int(skill_doc.get("content_version", 0)) + 1)
skill_path.write_text(json.dumps(skill_doc, indent=2) + "\n", encoding="utf-8")

# Thin the atlas strokes so the symbols read as technical instrumentation rather than chunky stickers.
replace_once(
    "assets/ui/skill_icons.svg",
    ".i{fill:none;stroke:#ffffff;stroke-width:3.2;stroke-linecap:round;stroke-linejoin:round}\n      .f{fill:#ffffff}\n      .t{fill:none;stroke:#ffffff;stroke-width:2.2;stroke-linecap:round;stroke-linejoin:round}",
    ".i{fill:none;stroke:#ffffff;stroke-width:2.55;stroke-linecap:square;stroke-linejoin:round}\n      .f{fill:#ffffff}\n      .t{fill:none;stroke:#ffffff;stroke-width:1.65;stroke-linecap:square;stroke-linejoin:round}")

# Per-cell optical centering. TextureRect already centers the 64x64 atlas cell; these offsets correct the
# deliberately asymmetric glyph drawings inside those cells (shovel, radar, cloud, scout, etc.).
replace_once(
    "src/UI/SkillTreeIncrementalTheme.cs",
    "    public static Texture2D? ForSkill(string skillId)\n",
    '''    private static readonly Dictionary<int, Vector2> OpticalOffsets = new()\n    {\n        [0] = new Vector2(-2.0f, 2.0f),\n        [2] = new Vector2(-3.0f, 4.0f),\n        [6] = new Vector2(0.5f, 1.0f),\n        [7] = new Vector2(1.0f, 0.0f),\n        [9] = new Vector2(-2.5f, 2.0f),\n        [10] = new Vector2(1.0f, -2.0f),\n        [11] = new Vector2(0.0f, -2.0f),\n        [12] = new Vector2(-2.0f, 0.0f),\n        [13] = new Vector2(4.0f, 0.0f),\n        [14] = new Vector2(1.0f, 0.0f),\n        [16] = new Vector2(-2.0f, 0.0f),\n        [17] = new Vector2(-2.0f, 0.0f),\n        [19] = new Vector2(0.0f, -3.0f),\n        [20] = new Vector2(2.0f, 2.0f),\n        [21] = new Vector2(2.0f, 0.0f),\n        [22] = new Vector2(-2.5f, 0.0f),\n    };\n\n    public static Vector2 OpticalOffsetForSkill(string skillId)\n    {\n        int index = Indices.GetValueOrDefault(skillId, 4);\n        return OpticalOffsets.GetValueOrDefault(index, Vector2.Zero);\n    }\n\n    public static Texture2D? ForSkill(string skillId)\n''')

replace_once(
    "src/UI/SkillTreeIncrementalTheme.cs",
    "        MouseEntered += () => { Hovered?.Invoke(this); AnimateScale(1.11f, 0.08f); };\n        MouseExited += () => AnimateScale(1.0f, 0.10f);\n        ButtonDown += () => AnimateScale(0.92f, 0.045f);\n        ButtonUp += () => AnimateScale(IsHovered() ? 1.11f : 1.0f, 0.09f);",
    "        MouseEntered += () => { Hovered?.Invoke(this); AnimateScale(1.045f, 0.09f); };\n        MouseExited += () => AnimateScale(1.0f, 0.11f);\n        ButtonDown += () => AnimateScale(0.97f, 0.05f);\n        ButtonUp += () => AnimateScale(IsHovered() ? 1.045f : 1.0f, 0.10f);")
replace_once(
    "src/UI/SkillTreeIncrementalTheme.cs",
    "            float amplitude = _recommended ? 0.030f : 0.014f;\n            float frequency = _recommended ? 3.8f : 3.1f;",
    "            float amplitude = _recommended ? 0.011f : 0.005f;\n            float frequency = _recommended ? 2.2f : 1.8f;")
replace_once(
    "src/UI/SkillTreeIncrementalTheme.cs",
    "        tween.TweenProperty(this, \"scale\", Vector2.One * 1.27f, 0.10f);\n        tween.TweenProperty(this, \"scale\", Vector2.One * 0.96f, 0.08f);",
    "        tween.TweenProperty(this, \"scale\", Vector2.One * 1.10f, 0.11f);\n        tween.TweenProperty(this, \"scale\", Vector2.One * 0.985f, 0.08f);")

# Space palette: less saturated, more aerospace/instrument-panel than arcade neon.
replace_once("src/UI/SkillTreeSpaceVisuals.cs", '    public static readonly Color Panel = new("#101a2f");\n    public static readonly Color PanelRaised = new("#15233d");', '    public static readonly Color Panel = new("#0b1424");\n    public static readonly Color PanelRaised = new("#111d30");')
replace_once(
    "src/UI/SkillTreeSpaceVisuals.cs",
    '''        ["manual"] = new Color("#52dcc8"),\n        ["automation"] = new Color("#55a9ff"),\n        ["drill"] = new Color("#7e83ff"),\n        ["patterns"] = new Color("#c978ff"),\n        ["resources"] = new Color("#f3c75e"),\n        ["tools"] = new Color("#ff8872"),\n        ["events"] = new Color("#a979ff"),\n        ["forest"] = new Color("#76d58a"),\n        ["shovel"] = new Color("#ffad62"),\n        ["finale"] = new Color("#ff78b7"),''',
    '''        ["manual"] = new Color("#68c7b9"),\n        ["automation"] = new Color("#6d9fc8"),\n        ["drill"] = new Color("#858fc0"),\n        ["patterns"] = new Color("#9d86b9"),\n        ["resources"] = new Color("#c3a563"),\n        ["tools"] = new Color("#bd8274"),\n        ["events"] = new Color("#9a82b7"),\n        ["forest"] = new Color("#7dad87"),\n        ["shovel"] = new Color("#bd8a5d"),\n        ["finale"] = new Color("#b7809d"),''')

replace_regex(
    "src/UI/SkillTreeSpaceVisuals.cs",
    r'''    public override void _Draw\(\)\n    \{\n        Vector2 center = Size \* 0\.5f;\n        float radius = MathF\.Min\(Size\.X, Size\.Y\) \* 0\.43f;\n        Color outer = RingColor;\n        outer\.A \*= 0\.42f;\n        Color inner = RingColor;\n        inner\.A \*= 0\.72f;\n        DrawArc\(center, radius, 0, Mathf\.Tau, 48, outer, 5\.0f, true\);\n        DrawArc\(center, MathF\.Max\(1\.0f, radius - 4\.0f\), 0, Mathf\.Tau, 48, inner, 1\.2f, true\);\n    \}''',
    '''    public override void _Draw()\n    {\n        Vector2 center = Size * 0.5f;\n        float radius = MathF.Min(Size.X, Size.Y) * 0.45f;\n        Color outer = RingColor;\n        outer.A *= 0.20f;\n        Color inner = RingColor;\n        inner.A *= 0.38f;\n        DrawArc(center, radius, 0, Mathf.Tau, 64, outer, 2.2f, true);\n        DrawArc(center, MathF.Max(1.0f, radius - 3.0f), 0, Mathf.Tau, 64, inner, 0.85f, true);\n\n        Color tick = RingColor;\n        tick.A *= 0.30f;\n        for (int i = 0; i < 4; i++)\n        {\n            float angle = i * Mathf.Pi * 0.5f;\n            Vector2 direction = new(MathF.Cos(angle), MathF.Sin(angle));\n            DrawLine(center + direction * (radius - 1.5f), center + direction * (radius + 2.5f), tick, 1.0f, true);\n        }\n    }''')

replace_once(
    "src/UI/SkillTreeSpaceVisuals.cs",
    '''        // Center the atlas cell itself rather than relying on each glyph's old hand-authored inset.\n        _icon.Position = new Vector2(11, 11);\n        _icon.Size = new Vector2(48, 48);\n        _icon.PivotOffset = new Vector2(24, 24);\n        _icon.Scale = Vector2.One;\n        _icon.Rotation = 0.0f;''',
    '''        // Center the atlas cell, then apply a small optical correction for asymmetric glyph art.\n        const float iconSize = 44.0f;\n        Vector2 opticalOffset = SkillTreeIconAtlas.OpticalOffsetForSkill(node.Id);\n        _icon.Position = new Vector2((70.0f - iconSize) * 0.5f, (70.0f - iconSize) * 0.5f) + opticalOffset;\n        _icon.Size = new Vector2(iconSize, iconSize);\n        _icon.PivotOffset = new Vector2(iconSize * 0.5f, iconSize * 0.5f);\n        _icon.Scale = Vector2.One;\n        _icon.Rotation = 0.0f;''')
replace_once(
    "src/UI/SkillTreeSpaceVisuals.cs",
    '''            Position = new Vector2(-5, -5),\n            Size = new Vector2(80, 80),\n            PivotOffset = new Vector2(40, 40),''',
    '''            Position = new Vector2(-3, -3),\n            Size = new Vector2(76, 76),\n            PivotOffset = new Vector2(38, 38),''')

replace_regex(
    "src/UI/SkillTreeSpaceVisuals.cs",
    r'''    public void ApplySpaceState\(int rank, int maxRank, bool maxed, bool requirementsMet, bool affordable\)\n    \{.*?\n    \}\n\n    public void PlaySpacePurchaseBurst''',
    '''    public void ApplySpaceState(int rank, int maxRank, bool maxed, bool requirementsMet, bool affordable)\n    {\n        Color fill;\n        Color border;\n        Color icon;\n        int radius = _visualKind == SkillNodeVisualKind.Stat ? 28 : (_visualKind == SkillNodeVisualKind.Milestone ? 9 : 7);\n        int borderWidth = 1;\n\n        if (maxed)\n        {\n            fill = new Color(0.045f, 0.085f, 0.13f, 0.98f);\n            border = _categoryColor.Lightened(0.10f);\n            icon = new Color(0.86f, 0.91f, 0.95f);\n        }\n        else if (!requirementsMet)\n        {\n            fill = new Color(0.040f, 0.065f, 0.10f, 0.96f);\n            border = SkillTreeSpacePalette.Locked.Darkened(0.08f);\n            icon = SkillTreeSpacePalette.TextFaint;\n        }\n        else\n        {\n            fill = affordable\n                ? new Color(0.050f, 0.090f, 0.14f, 0.97f)\n                : new Color(0.050f, 0.075f, 0.115f, 0.96f);\n            border = affordable ? _categoryColor : _categoryColor.Darkened(0.38f);\n            icon = affordable ? new Color(0.88f, 0.93f, 0.97f) : SkillTreeSpacePalette.TextMuted;\n        }\n\n        if (_recommended && !maxed && requirementsMet)\n        {\n            border = _categoryColor.Lightened(0.17f);\n            borderWidth = 2;\n        }\n\n        AddThemeStyleboxOverride("normal", SkillTreeSpacePalette.Box(fill, border, radius, borderWidth));\n        AddThemeStyleboxOverride("hover", SkillTreeSpacePalette.Box(fill.Lightened(0.035f), border.Lightened(0.08f), radius, borderWidth + 1));\n        AddThemeStyleboxOverride("pressed", SkillTreeSpacePalette.Box(fill.Darkened(0.035f), border, radius, borderWidth + 1));\n        AddThemeStyleboxOverride("disabled", SkillTreeSpacePalette.Box(fill, border, radius, borderWidth));\n\n        _icon.SelfModulate = icon;\n        _rankBadge.AddThemeColorOverride("font_color", maxed ? SkillTreeSpacePalette.Text : SkillTreeSpacePalette.TextMuted);\n        _lockGlyph.GlyphColor = SkillTreeSpacePalette.TextMuted;\n        _lockGlyph.QueueRedraw();\n        if (_spaceAura is not null)\n        {\n            _spaceAura.RingColor = maxed ? _categoryColor.Lightened(0.12f) : _categoryColor;\n            _spaceAura.QueueRedraw();\n            if (!IsHovered())\n                _spaceAura.Modulate = new Color(1, 1, 1, maxed ? 0.12f : affordable && requirementsMet ? 0.09f : 0.025f);\n        }\n    }\n\n    public void PlaySpacePurchaseBurst''')

replace_regex(
    "src/UI/SkillTreeSpaceVisuals.cs",
    r'''    public void PlaySpacePurchaseBurst\(\)\n    \{.*?\n    \}\n\n    private void SetSpaceHover''',
    '''    public void PlaySpacePurchaseBurst()\n    {\n        if (_spaceAura is null) return;\n        if (GraphicsSettingsRuntime.Current?.ReducedMotionEnabled == true)\n        {\n            _spaceAura.Modulate = new Color(1, 1, 1, 0.15f);\n            return;\n        }\n\n        _spacePurchaseTween?.Kill();\n        _spaceHoverTween?.Kill();\n        _spaceAura.Scale = Vector2.One;\n        _spaceAura.Modulate = new Color(1, 1, 1, 0.55f);\n        _icon.Rotation = 0.0f;\n        _icon.Scale = Vector2.One;\n\n        _spacePurchaseTween = CreateTween();\n        _spacePurchaseTween.SetParallel(true);\n        _spacePurchaseTween.SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);\n        _spacePurchaseTween.TweenProperty(_spaceAura, "scale", Vector2.One * 1.32f, 0.30f);\n        _spacePurchaseTween.TweenProperty(_spaceAura, "modulate:a", 0.0f, 0.34f);\n        _spacePurchaseTween.TweenProperty(_icon, "scale", Vector2.One * 1.075f, 0.12f);\n        _spacePurchaseTween.Chain().TweenProperty(_icon, "scale", Vector2.One, 0.15f);\n        _spacePurchaseTween.TweenCallback(Callable.From(() => SetSpaceHover(IsHovered())));\n    }\n\n    private void SetSpaceHover''')

replace_regex(
    "src/UI/SkillTreeSpaceVisuals.cs",
    r'''    private void SetSpaceHover\(bool hovered\)\n    \{.*?\n    \}\n\}''',
    '''    private void SetSpaceHover(bool hovered)\n    {\n        if (_spaceAura is null || _icon is null) return;\n        float auraAlpha = hovered ? 0.26f : (_purchased ? 0.12f : _affordable && _requirementsMet ? 0.09f : 0.025f);\n        Vector2 iconScale = hovered ? Vector2.One * 1.045f : Vector2.One;\n\n        if (GraphicsSettingsRuntime.Current?.ReducedMotionEnabled == true)\n        {\n            _spaceAura.Modulate = new Color(1, 1, 1, auraAlpha);\n            _icon.Scale = iconScale;\n            _icon.Rotation = 0.0f;\n            return;\n        }\n\n        _spaceHoverTween?.Kill();\n        _spaceHoverTween = CreateTween().SetParallel(true);\n        _spaceHoverTween.SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);\n        _spaceHoverTween.TweenProperty(_spaceAura, "modulate:a", auraAlpha, 0.13f);\n        _spaceHoverTween.TweenProperty(_spaceAura, "scale", hovered ? Vector2.One * 1.025f : Vector2.One, 0.15f);\n        _spaceHoverTween.TweenProperty(_icon, "scale", iconScale, 0.13f);\n        _spaceHoverTween.TweenProperty(_icon, "rotation", 0.0f, 0.10f);\n    }\n}''')

# The graph itself now uses thinner technical traces and much smaller flow markers; the detail panel is
# also less rounded/arcade-like.
replace_once(
    "src/UI/SkillTreeView.cs",
    '            new Color(0.055f, 0.095f, 0.17f, 0.97f), new Color("#344b70"), 12, 2));',
    '            new Color(0.035f, 0.065f, 0.115f, 0.97f), new Color("#31445f"), 7, 1));')
replace_once(
    "src/UI/SkillTreeView.cs",
    '''                float lineWidth = requirementMet ? 3.2f : 2.4f;''',
    '''                float lineWidth = requirementMet ? 1.85f : 1.25f;''')
replace_once(
    "src/UI/SkillTreeView.cs",
    '''                    glow.A = requirementMet ? 0.18f : 0.10f;\n                    DrawLine(points[i], points[i + 1], glow, lineWidth + 6.0f, true);''',
    '''                    glow.A = requirementMet ? 0.09f : 0.045f;\n                    DrawLine(points[i], points[i + 1], glow, lineWidth + 3.0f, true);''')
replace_once(
    "src/UI/SkillTreeView.cs",
    '''        float t = (float)((_flowTime * 0.34 + offset) % 1.0);\n        Vector2 position = from.Lerp(to, t);\n        Color glow = color;\n        glow.A = 0.24f;\n        DrawCircle(position, 7.0f, glow);\n        DrawCircle(position, 2.7f, color.Lightened(0.30f));''',
    '''        float t = (float)((_flowTime * 0.22 + offset) % 1.0);\n        Vector2 position = from.Lerp(to, t);\n        Color glow = color;\n        glow.A = 0.13f;\n        DrawCircle(position, 3.8f, glow);\n        DrawCircle(position, 1.45f, color.Lightened(0.22f));''')

# Keep the implementation note truthful for the new material-preserving, animated bucket split.
status_path = ROOT / "docs/IMPLEMENTATION_STATUS.md"
status = status_path.read_text(encoding="utf-8")
status = status.replace(
    "Pickup presentation is data-only and rendered through fixed-capacity 8x8x8 MultiMesh buckets; hover interaction queries only buckets along the cursor ray.",
    "Pickup presentation is data-only and rendered through fixed-capacity 8x8x8 spatial buckets split by real block visual; each MultiMesh reuses the mined block mesh/material, new drops pop outward before settling into a bucket-level hover bob, and hover interaction queries only cells along the cursor ray.")
status_path.write_text(status, encoding="utf-8")

print("Applied pickup material/motion and constellation polish pass.")
