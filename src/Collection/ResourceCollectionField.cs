using System;
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
