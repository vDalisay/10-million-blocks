from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]


def read(path):
    return (ROOT / path).read_text(encoding="utf-8")


def write(path, text):
    (ROOT / path).write_text(text, encoding="utf-8")


def replace_once(text, old, new, label):
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected one match, found {count}")
    return text.replace(old, new, 1)


def sub_once(text, pattern, repl, label, flags=0):
    text2, count = re.subn(pattern, repl, text, count=1, flags=flags)
    if count != 1:
        raise RuntimeError(f"{label}: expected one regex match, found {count}")
    return text2


# -----------------------------------------------------------------------------
# Resource collection: outlined real block drops + gravitational cursor suction.
# -----------------------------------------------------------------------------
path = "src/Collection/ResourceCollectionField.cs"
s = read(path)

s = replace_once(s,
'''    public bool Automated { get; set; }\n}\n''',
'''    public bool Automated { get; set; }\n    public long BlocksRemoved { get; set; } = 1L;\n}\n\npublic readonly record struct ResourcePickupCollected(\n    string BlockId,\n    long Amount,\n    long BlocksRemoved,\n    bool Automated,\n    Vector2 ScreenPosition);\n''', "pickup snapshot/event")

s = replace_once(s,
'''    private const float PickupScale = 0.30f;\n    private const float SpawnDuration = 0.48f;\n''',
'''    private const float PickupScale = 0.30f;\n    private const float OutlineScale = 1.085f;\n    private const float SpawnDuration = 0.48f;\n    private const float CursorTouchPixels = 15.0f;\n''', "pickup constants")

s = replace_once(s,
'''        public long Amount;\n        public bool Automated;\n        public RenderBucketKey RenderKey;\n''',
'''        public long Amount;\n        public long BlocksRemoved = 1L;\n        public bool Automated;\n        public bool Sucking;\n        public Vector3 Velocity;\n        public RenderBucketKey RenderKey;\n''', "pickup suction state")

s = replace_once(s,
'''        public MultiMeshInstance3D Node = null!;\n        public MultiMesh MultiMesh = null!;\n        public float BobPhase;\n''',
'''        public MultiMeshInstance3D Node = null!;\n        public MultiMesh MultiMesh = null!;\n        public MultiMeshInstance3D OutlineNode = null!;\n        public MultiMesh OutlineMultiMesh = null!;\n        public float BobPhase;\n''', "outline bucket fields")

s = replace_once(s,
'''    private BlockAssetRegistry _assets = null!;\n    private float _spacing;\n    private int _nextId = 1;\n    private long _pendingAmount;\n    private double _collectionBudget;\n    private double _visualTime;\n''',
'''    private BlockAssetRegistry _assets = null!;\n    private StandardMaterial3D _outlineMaterial = null!;\n    private float _spacing;\n    private int _nextId = 1;\n    private long _pendingAmount;\n    private double _visualTime;\n''', "collection fields")

s = replace_once(s,
'''    private readonly List<int> _sweepIds = new();\n    private readonly List<int> _activeSpawnIds = new();\n''',
'''    private readonly List<int> _sweepIds = new();\n    private readonly List<int> _activeSpawnIds = new();\n    private readonly List<int> _suctionIds = new();\n''', "suction list")

s = replace_once(s,
'''    public event Action? PendingChanged;\n    public int PendingCount => _pickups.Count;\n''',
'''    public event Action? PendingChanged;\n    public event Action<ResourcePickupCollected>? PickupCollected;\n    public int PendingCount => _pickups.Count;\n''', "collection event")

s = replace_once(s,
'''        _assets = assets ?? throw new ArgumentNullException(nameof(assets));\n        _spacing = Math.Max(0.01f, world.Profile.BlockSpacing);\n        BuildHint();\n''',
'''        _assets = assets ?? throw new ArgumentNullException(nameof(assets));\n        _spacing = Math.Max(0.01f, world.Profile.BlockSpacing);\n        _outlineMaterial = BuildOutlineMaterial();\n        BuildHint();\n''', "outline initialization")

new_process = r'''    public override void _Process(double delta)
    {
        _visualTime += Math.Max(0.0, delta);
        UpdateVisualMotion();

        if (_pickups.Count == 0) return;
        if (!_manual.InputEnabled || _manual.PlacementMode || _camera.IsManipulating) return;

        Camera3D camera = _camera.Camera;
        Vector2 mouse = GetViewport().GetMousePosition();
        Vector3 rayOrigin = camera.ProjectRayOrigin(mouse);
        Vector3 rayDirection = camera.ProjectRayNormal(mouse).Normalized();
        float maxDistance = _world.GetWorldBounds().Size.Length() * 2.5f;

        // A pickup must first be visible/reachable from the cursor ray. Once it enters the collector
        // field it becomes captured by the cursor and keeps following it until contact, instead of
        // disappearing at the edge of the radius.
        if (VoxelRaycaster.TryRaycast(_world, camera, mouse, maxDistance, out Vector3I hitVoxel, out _))
        {
            Vector3 hitPosition = (Vector3)hitVoxel * _spacing;
            maxDistance = Math.Min(maxDistance, rayOrigin.DistanceTo(hitPosition) + _spacing * 1.7f);
        }

        float radius = (float)Math.Max(0.05, _skills.Derived.CollectionRadiusBlocks) * _spacing;
        GatherHoverCandidates(rayOrigin, rayDirection, maxDistance, radius);

        bool reducedMotion = GraphicsSettingsRuntime.Current?.ReducedMotionEnabled == true;
        if (reducedMotion)
        {
            if (_hoverCandidates.Count == 0) return;
            _mining.BeginCurrencyNotificationBatch();
            try
            {
                foreach (int id in _hoverCandidates.ToArray())
                    CollectPickup(id, mouse, notify: false);
            }
            finally
            {
                _mining.EndCurrencyNotificationBatch();
            }
            NotifyPendingChanged();
            return;
        }

        foreach (int id in _hoverCandidates)
        {
            if (!_pickups.TryGetValue(id, out Pickup? pickup) || pickup.Sucking) continue;
            pickup.Sucking = true;
            pickup.Velocity = Vector3.Zero;
            _suctionIds.Add(id);
        }

        AdvanceSuction((float)Math.Max(0.0, delta), camera, mouse, rayOrigin, rayDirection, maxDistance, radius);
    }
'''
s = sub_once(s, r'    public override void _Process\(double delta\)\n    \{.*?\n    \}\n\n    public List<ResourcePickupSnapshot>', new_process + '\n    public List<ResourcePickupSnapshot>', "collection process", re.S)

s = replace_once(s,
'''                Amount = pickup.Amount,\n                Automated = pickup.Automated,\n''',
'''                Amount = pickup.Amount,\n                Automated = pickup.Automated,\n                BlocksRemoved = pickup.BlocksRemoved,\n''', "snapshot blocks removed")

s = replace_once(s,
'''                item.Amount,\n                item.Automated,\n                notify: false,\n                animateSpawn: false);\n''',
'''                item.Amount,\n                item.Automated,\n                Math.Max(1L, item.BlocksRemoved),\n                notify: false,\n                animateSpawn: false);\n''', "restore pickup signature")

s = replace_once(s,
'''        bool autoCollect = manual\n            ? _skills.Derived.ManualAutoCollectUnlocked\n            : _skills.Derived.AutomationAutoCollectUnlocked;\n        if (autoCollect) return;\n''',
'''        bool autoCollect = manual\n            ? _skills.Derived.ManualAutoCollectUnlocked\n            : _skills.Derived.AutomationAutoCollectUnlocked;\n        if (autoCollect)\n        {\n            PickupCollected?.Invoke(new ResourcePickupCollected(\n                result.BlockId,\n                result.Reward,\n                Math.Max(1L, result.BlocksRemoved),\n                automated,\n                ProjectCollectionSource(result.Voxel, manual)));\n            return;\n        }\n''', "auto collect event")

s = replace_once(s,
'''        if (!AddPickup(result.Voxel, result.BlockId, result.Reward, automated, notify: true, animateSpawn: true))\n''',
'''        if (!AddPickup(result.Voxel, result.BlockId, result.Reward, automated, Math.Max(1L, result.BlocksRemoved), notify: true, animateSpawn: true))\n''', "mined pickup signature")

new_skills_changed = r'''    private void OnSkillsChanged()
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

        _mining.BeginCurrencyNotificationBatch();
        try
        {
            foreach (int id in _sweepIds)
            {
                if (!_pickups.TryGetValue(id, out Pickup? pickup)) continue;
                Vector2 source = ProjectCollectionSource(CurrentVisualPosition(pickup), pickup.Automated);
                CollectPickup(id, source, notify: false);
            }
        }
        finally
        {
            _mining.EndCurrencyNotificationBatch();
        }
        NotifyPendingChanged();
    }
'''
s = sub_once(s, r'    private void OnSkillsChanged\(\)\n    \{.*?\n    \}\n\n    private bool AddPickup', new_skills_changed + '\n    private bool AddPickup', "auto collect existing pickups", re.S)

s = replace_once(s,
'''        long amount,\n        bool automated,\n        bool notify,\n''',
'''        long amount,\n        bool automated,\n        long blocksRemoved,\n        bool notify,\n''', "AddPickup block count parameter")

s = replace_once(s,
'''            Amount = amount,\n            Automated = automated,\n            RenderKey = bucket.Key,\n''',
'''            Amount = amount,\n            BlocksRemoved = Math.Max(1L, blocksRemoved),\n            Automated = automated,\n            RenderKey = bucket.Key,\n''', "pickup blocks removed")

s = replace_once(s,
'''        bucket.MultiMesh.VisibleInstanceCount = bucket.PickupIds.Count;\n        if (animateSpawn && GraphicsSettingsRuntime.Current?.ReducedMotionEnabled != true)\n''',
'''        bucket.MultiMesh.VisibleInstanceCount = bucket.PickupIds.Count;\n        bucket.OutlineMultiMesh.VisibleInstanceCount = bucket.PickupIds.Count;\n        if (animateSpawn && GraphicsSettingsRuntime.Current?.ReducedMotionEnabled != true)\n''', "outline add visible count")

s = replace_once(s,
'''        bucket.PickupIds.RemoveAt(lastSlot);\n        bucket.MultiMesh.VisibleInstanceCount = bucket.PickupIds.Count;\n''',
'''        bucket.PickupIds.RemoveAt(lastSlot);\n        bucket.MultiMesh.VisibleInstanceCount = bucket.PickupIds.Count;\n        bucket.OutlineMultiMesh.VisibleInstanceCount = bucket.PickupIds.Count;\n''', "outline remove visible count")

s = replace_once(s,
'''                    if (!_pickups.TryGetValue(id, out Pickup? pickup)) continue;\n                    Vector3 position = pickup.FinalPosition + bucket.Node.Position;\n''',
'''                    if (!_pickups.TryGetValue(id, out Pickup? pickup) || pickup.Sucking) continue;\n                    Vector3 position = CurrentVisualPosition(pickup) + bucket.Node.Position;\n''', "hover current visual position")

new_bucket = r'''    private RenderBucket GetOrCreateBucket(Vector3I cell, string blockId)
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

        // Inverted-hull outline: the slightly enlarged black backfaces remain visible around the
        // miniature block while the real block mesh/material renders normally on top.
        var outlineNode = new MultiMeshInstance3D
        {
            Name = $"PickupOutline_{blockId}_{cell.X}_{cell.Y}_{cell.Z}",
            Multimesh = outlineMultiMesh,
            MaterialOverride = _outlineMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(outlineNode);

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
'''
s = sub_once(s, r'    private RenderBucket GetOrCreateBucket\(Vector3I cell, string blockId\)\n    \{.*?\n    \}\n\n    private void RemoveBucket', new_bucket + '\n    private void RemoveBucket', "outlined render bucket", re.S)

s = replace_once(s,
'''        bucket.Node.QueueFree();\n''',
'''        bucket.Node.QueueFree();\n        bucket.OutlineNode.QueueFree();\n''', "free outline node")

s = replace_once(s,
'''            bucket.Node.Position = Vector3.Up * bob;\n''',
'''            bucket.Node.Position = Vector3.Up * bob;\n            bucket.OutlineNode.Position = bucket.Node.Position;\n''', "outline bob")

s = replace_once(s,
'''            if (!_pickups.TryGetValue(id, out Pickup? pickup)\n                || !_buckets.TryGetValue(pickup.RenderKey, out RenderBucket? bucket))\n''',
'''            if (!_pickups.TryGetValue(id, out Pickup? pickup)\n                || !_buckets.TryGetValue(pickup.RenderKey, out RenderBucket? bucket))\n''', "spawn lookup anchor")
# Add suction conflict guard immediately after the lookup failure block.
s = replace_once(s,
'''                _activeSpawnIds.RemoveAt(index);\n                continue;\n            }\n\n            if (reducedMotion)\n''',
'''                _activeSpawnIds.RemoveAt(index);\n                continue;\n            }\n\n            if (pickup.Sucking)\n            {\n                _activeSpawnIds.RemoveAt(index);\n                continue;\n            }\n\n            if (reducedMotion)\n''', "spawn suction guard")

s = replace_once(s,
'''    private Vector3 CurrentVisualPosition(Pickup pickup)\n    {\n        float t = Mathf.Clamp((float)((_visualTime - pickup.SpawnTime) / SpawnDuration), 0.0f, 1.0f);\n''',
'''    private Vector3 CurrentVisualPosition(Pickup pickup)\n    {\n        if (pickup.Sucking) return pickup.FinalPosition;\n        float t = Mathf.Clamp((float)((_visualTime - pickup.SpawnTime) / SpawnDuration), 0.0f, 1.0f);\n''', "current suction position")

s = replace_once(s,
'''    private void WriteVisual(RenderBucket bucket, int slot, Pickup pickup, Vector3 position)\n        => bucket.MultiMesh.SetInstanceTransform(slot, new Transform3D(pickup.Basis, position));\n\n    private Vector2 PickupScatter''',
'''    private void WriteVisual(RenderBucket bucket, int slot, Pickup pickup, Vector3 position)\n    {\n        bucket.MultiMesh.SetInstanceTransform(slot, new Transform3D(pickup.Basis, position));\n        Basis outlineBasis = pickup.Basis.Scaled(Vector3.One * OutlineScale);\n        bucket.OutlineMultiMesh.SetInstanceTransform(slot, new Transform3D(outlineBasis, position));\n    }\n\n    private void AdvanceSuction(\n        float delta,\n        Camera3D camera,\n        Vector2 mouse,\n        Vector3 rayOrigin,\n        Vector3 rayDirection,\n        float maxDistance,\n        float collectorRadius)\n    {\n        if (_suctionIds.Count == 0 || delta <= 0.0f) return;\n\n        float rate = (float)Math.Clamp(_skills.Derived.CollectionRatePerSecond, 0.5, 160.0);\n        float spring = 8.0f + rate * 0.85f;\n        float maxSpeed = _spacing * (2.2f + rate * 0.16f);\n        bool collectedAny = false;\n\n        _mining.BeginCurrencyNotificationBatch();\n        try\n        {\n            for (int index = _suctionIds.Count - 1; index >= 0; index--)\n            {\n                int id = _suctionIds[index];\n                if (!_pickups.TryGetValue(id, out Pickup? pickup)\n                    || !_buckets.TryGetValue(pickup.RenderKey, out RenderBucket? bucket))\n                {\n                    _suctionIds.RemoveAt(index);\n                    continue;\n                }\n\n                Vector3 position = pickup.FinalPosition;\n                float along = Mathf.Clamp((position - rayOrigin).Dot(rayDirection), _spacing * 0.35f, maxDistance);\n                Vector3 target = rayOrigin + rayDirection * along;\n                Vector3 toTarget = target - position;\n                float distance = toTarget.Length();\n                float closeBoost = collectorRadius <= 0.001f\n                    ? 0.0f\n                    : 0.65f * (1.0f - Mathf.Clamp(distance / collectorRadius, 0.0f, 1.0f));\n\n                pickup.Velocity += toTarget * (spring * (1.0f + closeBoost) * delta);\n                pickup.Velocity *= MathF.Pow(0.16f, delta);\n                float speed = pickup.Velocity.Length();\n                if (speed > maxSpeed) pickup.Velocity = pickup.Velocity / speed * maxSpeed;\n\n                pickup.FinalPosition = position + pickup.Velocity * delta;\n                WriteVisual(bucket, pickup.RenderSlot, pickup, pickup.FinalPosition);\n\n                Vector2 screen = camera.UnprojectPosition(pickup.FinalPosition + bucket.Node.Position);\n                if (screen.DistanceTo(mouse) > CursorTouchPixels\n                    && pickup.FinalPosition.DistanceTo(target) > _spacing * 0.035f) continue;\n\n                CollectPickup(id, mouse, notify: false);\n                _suctionIds.RemoveAt(index);\n                collectedAny = true;\n            }\n        }\n        finally\n        {\n            _mining.EndCurrencyNotificationBatch();\n        }\n\n        if (collectedAny) NotifyPendingChanged();\n    }\n\n    private void CollectPickup(int id, Vector2 screenPosition, bool notify)\n    {\n        if (!_pickups.TryGetValue(id, out Pickup? pickup)) return;\n        var collected = new ResourcePickupCollected(\n            pickup.BlockId,\n            pickup.Amount,\n            Math.Max(1L, pickup.BlocksRemoved),\n            pickup.Automated,\n            screenPosition);\n        long amount = pickup.Amount;\n        RemovePickup(id, notify: false);\n        _mining.GrantCurrency(amount);\n        PickupCollected?.Invoke(collected);\n        if (notify) NotifyPendingChanged();\n    }\n\n    private Vector2 ProjectCollectionSource(Vector3I voxel, bool manual)\n        => manual\n            ? GetViewport().GetMousePosition()\n            : ProjectCollectionSource((Vector3)voxel * _spacing, automated: true);\n\n    private Vector2 ProjectCollectionSource(Vector3 worldPosition, bool automated)\n    {\n        Camera3D camera = _camera.Camera;\n        if (!camera.IsPositionBehind(worldPosition)) return camera.UnprojectPosition(worldPosition);\n        return GetViewport().GetMousePosition();\n    }\n\n    private static StandardMaterial3D BuildOutlineMaterial()\n        => new()\n        {\n            AlbedoColor = new Color(0.004f, 0.005f, 0.008f, 1.0f),\n            Roughness = 1.0f,\n            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,\n            CullMode = BaseMaterial3D.CullModeEnum.Front,\n        };\n\n    private Vector2 PickupScatter''', "suction/outline helpers")

write(path, s)


# -----------------------------------------------------------------------------
# Incremental HUD feedback: BLOCKS MINED flight only when a pickup is collected.
# -----------------------------------------------------------------------------
path = "src/UI/IncrementalFeedbackView.cs"
s = read(path)
s = replace_once(s, "using TenMillionBlocks.Content;\n", "using TenMillionBlocks.Collection;\nusing TenMillionBlocks.Content;\n", "feedback collection using")
s = replace_once(s,
'''    private BlockAssetRegistry _assets = null!;\n''',
'''    private BlockAssetRegistry _assets = null!;\n    private ResourceCollectionField _collection = null!;\n''', "feedback collection field")
s = replace_once(s,
'''        MiningService mining,\n        SpecialResourceInventory specialResources,\n        BlockAssetRegistry assets)\n''',
'''        MiningService mining,\n        SpecialResourceInventory specialResources,\n        BlockAssetRegistry assets,\n        ResourceCollectionField collection)\n''', "feedback initialize signature")
s = replace_once(s,
'''        _specialResources = specialResources ?? throw new ArgumentNullException(nameof(specialResources));\n        _assets = assets ?? throw new ArgumentNullException(nameof(assets));\n''',
'''        _specialResources = specialResources ?? throw new ArgumentNullException(nameof(specialResources));\n        _assets = assets ?? throw new ArgumentNullException(nameof(assets));\n        _collection = collection ?? throw new ArgumentNullException(nameof(collection));\n''', "feedback collection assignment")
s = replace_once(s,
'''        _mining.CurrencyChanged += OnCurrencyChanged;\n        _specialResources.Changed += OnSpecialResourcesChanged;\n''',
'''        _mining.CurrencyChanged += OnCurrencyChanged;\n        _specialResources.Changed += OnSpecialResourcesChanged;\n        _collection.PickupCollected += OnPickupCollected;\n''', "feedback subscribe collection")
s = replace_once(s,
'''        _mining.CurrencyChanged -= OnCurrencyChanged;\n        _specialResources.Changed -= OnSpecialResourcesChanged;\n''',
'''        _mining.CurrencyChanged -= OnCurrencyChanged;\n        _specialResources.Changed -= OnSpecialResourcesChanged;\n        _collection.PickupCollected -= OnPickupCollected;\n''', "feedback unsubscribe collection")

new_on_block = r'''    private void OnBlockMined(MiningResult result)
    {
        if (!result.Success || !result.Removed) return;

        BlockDefinition definition = _mining.GetBlockDefinition(result.BlockId);
        bool special = definition.Tags.Contains("gem", StringComparer.Ordinal);
        bool deferredCollectionSource = result.Source is MiningSource.Manual or MiningSource.Automated;
        Vector2 source = default;
        bool hasSource = result.Source != MiningSource.Offline && TryProjectSource(result.Voxel, out source);

        // Manual and live-automation ordinary feedback is now emitted by ResourceCollectionField only
        // when the world pickup actually reaches the cursor. Direct/offline sources have no deferred
        // pickup, so they retain their existing immediate/aggregated feedback path.
        if (!deferredCollectionSource)
        {
            _counterRefreshPending = true;
            Pulse(_blocksChip.Root, strong: false);
            if (result.Reward > 0) Pulse(_resourcesChip.Root, strong: false);

            if (result.Source == MiningSource.Offline)
            {
                AggregatedFeedbackCount++;
            }
            else
            {
                QueuePickup(
                    result.BlockId,
                    _blocksChip.Root,
                    result.BlocksRemoved,
                    result.Reward,
                    source,
                    hasSource,
                    special: false);
            }
        }

        // Special-resource inventory remains authoritative/direct, so its own chip still celebrates at
        // discovery time. The ordinary BLOCKS MINED flight for the same gem waits for collection.
        if (special)
        {
            CounterChip specialChip = EnsureSpecialChip(result.BlockId);
            Pulse(specialChip.Root, strong: true);
            QueuePickup(
                result.BlockId,
                specialChip.Root,
                1L,
                0L,
                source,
                hasSource,
                special: true);
        }
    }

    private void OnPickupCollected(ResourcePickupCollected collected)
    {
        _counterRefreshPending = true;
        Pulse(_blocksChip.Root, strong: collected.BlocksRemoved > 1);
        if (collected.Amount > 0) Pulse(_resourcesChip.Root, strong: collected.Amount > 1);
        QueuePickup(
            collected.BlockId,
            _blocksChip.Root,
            Math.Max(1L, collected.BlocksRemoved),
            Math.Max(0L, collected.Amount),
            collected.ScreenPosition,
            hasSource: true,
            special: false);
    }
'''
s = sub_once(s, r'    private void OnBlockMined\(MiningResult result\)\n    \{.*?\n    \}\n\n    private void OnBulkMined', new_on_block + '\n    private void OnBulkMined', "feedback block/collection routing", re.S)
write(path, s)


# GameRoot passes the authoritative collection field into the feedback presenter.
path = "src/App/GameRoot.cs"
s = read(path)
s = replace_once(s,
'''        incrementalFeedback.Initialize(_world, _worldView, _mining, _specialResources, _assets);\n''',
'''        incrementalFeedback.Initialize(_world, _worldView, _mining, _specialResources, _assets, _resourceCollection);\n''', "feedback GameRoot wiring")
write(path, s)


# -----------------------------------------------------------------------------
# Skill icons: exact optical centering and explicit hover-exit signal.
# -----------------------------------------------------------------------------
path = "src/UI/SkillTreeIncrementalTheme.cs"
s = read(path)
new_offsets = r'''    private static readonly Dictionary<int, Vector2> OpticalOffsets = new()
    {
        // Measured from the rendered alpha bounds of every 64x64 atlas cell, not hand-tuned guesses.
        [0] = new Vector2(0.5f, -1.0f),
        [1] = new Vector2(0.5f, 0.5f),
        [2] = new Vector2(-4.0f, 6.5f),
        [3] = new Vector2(1.0f, 0.5f),
        [4] = new Vector2(0.5f, 0.5f),
        [5] = new Vector2(1.5f, 0.5f),
        [6] = new Vector2(1.0f, 2.0f),
        [7] = new Vector2(1.5f, 0.5f),
        [8] = new Vector2(0.5f, 0.5f),
        [9] = new Vector2(-2.5f, 3.5f),
        [10] = new Vector2(2.0f, -1.5f),
        [11] = new Vector2(-1.0f, -3.0f),
        [12] = new Vector2(-2.5f, 1.0f),
        [13] = new Vector2(4.5f, 1.0f),
        [14] = new Vector2(1.0f, 0.5f),
        [15] = new Vector2(0.5f, 0.5f),
        [16] = new Vector2(1.0f, 2.5f),
        [17] = new Vector2(-1.5f, 1.0f),
        [18] = new Vector2(0.5f, 0.5f),
        [19] = new Vector2(1.5f, -5.0f),
        [20] = new Vector2(4.5f, 3.0f),
        [21] = new Vector2(4.0f, 0.5f),
        [22] = new Vector2(-2.5f, 0.5f),
        [23] = new Vector2(0.5f, 0.5f),
    };
'''
s = sub_once(s, r'    private static readonly Dictionary<int, Vector2> OpticalOffsets = new\(\)\n    \{.*?\n    \};', new_offsets.rstrip(), "exact icon optical offsets", re.S)
s = replace_once(s,
'''    public event Action<IncrementalSkillNodeButton>? Hovered;\n''',
'''    public event Action<IncrementalSkillNodeButton>? Hovered;\n    public event Action<IncrementalSkillNodeButton>? HoverEnded;\n''', "hover ended event")
s = replace_once(s,
'''        MouseExited += () => AnimateScale(1.0f, 0.11f);\n''',
'''        MouseExited += () => { HoverEnded?.Invoke(this); AnimateScale(1.0f, 0.11f); };\n''', "hover ended emit")
write(path, s)


# -----------------------------------------------------------------------------
# Space visual pass: much darker sky + four-pronged star node containers.
# -----------------------------------------------------------------------------
path = "src/UI/SkillTreeSpaceVisuals.cs"
s = read(path)
s = s.replace('public static readonly Color Backdrop = new("#070d1c");', 'public static readonly Color Backdrop = new("#01030a");')
s = s.replace('public static readonly Color BackdropDeep = new("#030712");', 'public static readonly Color BackdropDeep = new("#000106");')
s = s.replace('new Color(0.20f, 0.28f, 0.64f, 0.055f)', 'new Color(0.20f, 0.28f, 0.64f, 0.022f)')
s = s.replace('new Color(0.50f, 0.19f, 0.62f, 0.045f)', 'new Color(0.50f, 0.19f, 0.62f, 0.018f)')
s = s.replace('new Color(0.08f, 0.54f, 0.58f, 0.035f)', 'new Color(0.08f, 0.54f, 0.58f, 0.014f)')
s = s.replace('Color orbit = new(0.30f, 0.47f, 0.72f, 0.08f);', 'Color orbit = new(0.30f, 0.47f, 0.72f, 0.045f);')

star_classes = r'''internal static class SkillNodeStarGeometry
{
    public static Vector2[] Points(Vector2 size, float inset = 2.0f)
    {
        Vector2 c = size * 0.5f;
        float outerX = MathF.Max(2.0f, c.X - inset);
        float outerY = MathF.Max(2.0f, c.Y - inset);
        float shoulderX = outerX * 0.39f;
        float shoulderY = outerY * 0.39f;
        return
        [
            c + new Vector2(0, -outerY),
            c + new Vector2(shoulderX, -shoulderY),
            c + new Vector2(outerX, 0),
            c + new Vector2(shoulderX, shoulderY),
            c + new Vector2(0, outerY),
            c + new Vector2(-shoulderX, shoulderY),
            c + new Vector2(-outerX, 0),
            c + new Vector2(-shoulderX, -shoulderY),
        ];
    }

    public static void DrawOutline(Control canvas, Vector2[] points, Color color, float width)
    {
        for (int i = 0; i < points.Length; i++)
            canvas.DrawLine(points[i], points[(i + 1) % points.Length], color, width, true);
    }
}

internal partial class SkillNodeStarPlate : Control
{
    private Color _fill = new(0.02f, 0.035f, 0.055f, 0.98f);
    private Color _border = new(0.32f, 0.46f, 0.58f, 0.8f);
    private float _borderWidth = 1.0f;
    private bool _hovered;

    public void SetState(Color fill, Color border, float borderWidth)
    {
        _fill = fill;
        _border = border;
        _borderWidth = borderWidth;
        QueueRedraw();
    }

    public void SetHovered(bool hovered)
    {
        if (_hovered == hovered) return;
        _hovered = hovered;
        QueueRedraw();
    }

    public override void _Draw()
    {
        Vector2[] points = SkillNodeStarGeometry.Points(Size, 3.0f);
        Color fill = _hovered ? _fill.Lightened(0.045f) : _fill;
        Color border = _hovered ? _border.Lightened(0.10f) : _border;
        DrawColoredPolygon(points, fill);
        SkillNodeStarGeometry.DrawOutline(this, points, border, _borderWidth + (_hovered ? 0.7f : 0.0f));
    }
}

internal partial class SkillNodeSpaceAura : Control
{
    public Color RingColor { get; set; } = Colors.White;

    public override void _Draw()
    {
        Vector2[] points = SkillNodeStarGeometry.Points(Size, 2.0f);
        Color outer = RingColor;
        outer.A *= 0.16f;
        Color inner = RingColor;
        inner.A *= 0.30f;
        SkillNodeStarGeometry.DrawOutline(this, points, outer, 3.2f);
        SkillNodeStarGeometry.DrawOutline(this, points, inner, 0.9f);
    }
}
'''
s = sub_once(s, r'internal partial class SkillNodeSpaceAura : Control\n\{.*?\n\}\n\npublic partial class IncrementalSkillNodeButton', star_classes + '\npublic partial class IncrementalSkillNodeButton', "star plate classes", re.S)
s = replace_once(s,
'''    private SkillNodeSpaceAura? _spaceAura;\n''',
'''    private SkillNodeStarPlate? _starPlate;\n    private SkillNodeSpaceAura? _spaceAura;\n''', "star plate field")
s = replace_once(s,
'''        const float iconSize = 44.0f;\n''',
'''        const float iconSize = 42.0f;\n''', "smaller centered icon")
s = replace_once(s,
'''        _lockGlyph.Position = new Vector2(20, 20);\n        _lockGlyph.GlyphColor = SkillTreeSpacePalette.TextMuted;\n        _lockGlyph.QueueRedraw();\n\n        _spaceAura = new SkillNodeSpaceAura\n''',
'''        _lockGlyph.Position = new Vector2(20, 20);\n        _lockGlyph.GlyphColor = SkillTreeSpacePalette.TextMuted;\n        _lockGlyph.QueueRedraw();\n\n        _starPlate = new SkillNodeStarPlate\n        {\n            Position = Vector2.Zero,\n            Size = new Vector2(70, 70),\n            MouseFilter = MouseFilterEnum.Ignore,\n        };\n        AddChild(_starPlate);\n        MoveChild(_starPlate, 0);\n\n        _spaceAura = new SkillNodeSpaceAura\n''', "install star plate")
s = replace_once(s,
'''            Position = new Vector2(-3, -3),\n            Size = new Vector2(76, 76),\n            PivotOffset = new Vector2(38, 38),\n''',
'''            Position = new Vector2(-4, -4),\n            Size = new Vector2(78, 78),\n            PivotOffset = new Vector2(39, 39),\n''', "star aura size")

# Replace the rectangular/circular StyleBox state with the custom star plate.
s = sub_once(s,
r'''        Color fill;\n        Color border;\n        Color icon;\n        int radius = .*?\n        int borderWidth = 1;\n\n        if \(maxed\).*?\n        AddThemeStyleboxOverride\("disabled", SkillTreeSpacePalette.Box\(fill, border, radius, borderWidth\)\);''',
r'''        Color fill;
        Color border;
        Color icon;
        int borderWidth = 1;

        if (maxed)
        {
            fill = new Color(0.016f, 0.030f, 0.050f, 0.99f);
            border = _categoryColor.Lightened(0.06f);
            icon = new Color(0.86f, 0.91f, 0.95f);
        }
        else if (!requirementsMet)
        {
            fill = new Color(0.010f, 0.017f, 0.030f, 0.98f);
            border = SkillTreeSpacePalette.Locked.Darkened(0.14f);
            icon = SkillTreeSpacePalette.TextFaint;
        }
        else
        {
            fill = affordable
                ? new Color(0.018f, 0.034f, 0.055f, 0.99f)
                : new Color(0.013f, 0.024f, 0.040f, 0.98f);
            border = affordable ? _categoryColor.Darkened(0.08f) : _categoryColor.Darkened(0.48f);
            icon = affordable ? new Color(0.88f, 0.93f, 0.97f) : SkillTreeSpacePalette.TextMuted;
        }

        if (_recommended && !maxed && requirementsMet)
        {
            border = _categoryColor.Lightened(0.13f);
            borderWidth = 2;
        }

        Color transparent = new(0, 0, 0, 0);
        AddThemeStyleboxOverride("normal", SkillTreeSpacePalette.Box(transparent, transparent, 0, 0));
        AddThemeStyleboxOverride("hover", SkillTreeSpacePalette.Box(transparent, transparent, 0, 0));
        AddThemeStyleboxOverride("pressed", SkillTreeSpacePalette.Box(transparent, transparent, 0, 0));
        AddThemeStyleboxOverride("disabled", SkillTreeSpacePalette.Box(transparent, transparent, 0, 0));
        _starPlate?.SetState(fill, border, borderWidth);''', "star state styling", re.S)

s = replace_once(s,
'''        if (_spaceAura is null || _icon is null) return;\n        float auraAlpha = hovered ? 0.26f : (_purchased ? 0.12f : _affordable && _requirementsMet ? 0.09f : 0.025f);\n''',
'''        if (_spaceAura is null || _icon is null) return;\n        _starPlate?.SetHovered(hovered);\n        float auraAlpha = hovered ? 0.22f : (_purchased ? 0.10f : _affordable && _requirementsMet ? 0.075f : 0.018f);\n''', "star hover")
write(path, s)


# -----------------------------------------------------------------------------
# Skill detail card: animated appear/disappear wiggle.
# -----------------------------------------------------------------------------
path = "src/UI/SkillTreeView.cs"
s = read(path)
s = replace_once(s,
'''    private Tween? _transition;\n''',
'''    private Tween? _transition;\n    private Tween? _detailTween;\n''', "detail tween field")
s = replace_once(s,
'''            button.Hovered += _ => ShowDetails(node);\n''',
'''            button.Hovered += _ => ShowDetails(node);\n            button.HoverEnded += _ => HideDetailsAnimated();\n''', "detail hover exit")
s = replace_once(s,
'''        _detailPanel.Visible = true;\n        PositionDetailCard(node);\n    }\n\n    private void PositionDetailCard''',
'''        PositionDetailCard(node);\n        ShowDetailsAnimated();\n    }\n\n    private void ShowDetailsAnimated()\n    {\n        _detailTween?.Kill();\n        _detailPanel.PivotOffset = _detailPanel.Size * 0.5f;\n        _detailPanel.Visible = true;\n\n        if (GraphicsSettingsRuntime.Current?.ReducedMotionEnabled == true)\n        {\n            _detailPanel.Modulate = Colors.White;\n            _detailPanel.Scale = Vector2.One;\n            _detailPanel.Rotation = 0.0f;\n            return;\n        }\n\n        _detailPanel.Modulate = new Color(1, 1, 1, 0);\n        _detailPanel.Scale = Vector2.One * 0.94f;\n        _detailPanel.Rotation = -0.030f;\n        _detailTween = CreateTween();\n        _detailTween.SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);\n        _detailTween.SetParallel(true);\n        _detailTween.TweenProperty(_detailPanel, "modulate:a", 1.0f, 0.13f);\n        _detailTween.TweenProperty(_detailPanel, "scale", Vector2.One * 1.015f, 0.15f);\n        _detailTween.TweenProperty(_detailPanel, "rotation", 0.014f, 0.11f);\n        _detailTween.Chain().SetParallel(true);\n        _detailTween.TweenProperty(_detailPanel, "scale", Vector2.One, 0.10f);\n        _detailTween.TweenProperty(_detailPanel, "rotation", 0.0f, 0.12f);\n    }\n\n    private void HideDetailsAnimated()\n    {\n        if (!_detailPanel.Visible) return;\n        _detailTween?.Kill();\n\n        if (GraphicsSettingsRuntime.Current?.ReducedMotionEnabled == true)\n        {\n            _detailPanel.Visible = false;\n            _detailPanel.Modulate = Colors.White;\n            _detailPanel.Scale = Vector2.One;\n            _detailPanel.Rotation = 0.0f;\n            return;\n        }\n\n        _detailTween = CreateTween().SetParallel(true);\n        _detailTween.SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Quad);\n        _detailTween.TweenProperty(_detailPanel, "modulate:a", 0.0f, 0.11f);\n        _detailTween.TweenProperty(_detailPanel, "scale", Vector2.One * 0.95f, 0.12f);\n        _detailTween.TweenProperty(_detailPanel, "rotation", -0.025f, 0.12f);\n        _detailTween.TweenCallback(Callable.From(() =>\n        {\n            _detailPanel.Visible = false;\n            _detailPanel.Modulate = Colors.White;\n            _detailPanel.Scale = Vector2.One;\n            _detailPanel.Rotation = 0.0f;\n        })).SetDelay(0.12f);\n    }\n\n    private void PositionDetailCard''', "detail wiggle methods")
s = replace_once(s,
'''        if (_detailPanel.Visible)\n            _detailPanel.Visible = false;\n''',
'''        if (_detailPanel.Visible)\n            HideDetailsAnimated();\n''', "pan detail hide animation")
write(path, s)


# -----------------------------------------------------------------------------
# Collector progression: five-block reach immediately before final auto-collect.
# -----------------------------------------------------------------------------
path = "data/skills/skill_tree.json"
s = read(path)
s = replace_once(s, '"content_version": 19', '"content_version": 20', "skill content version")
s = replace_once(s,
'''"description": "Expand the hover pickup field again, from 1.0 to 1.6 block widths for broad cluster collection."''',
'''"description": "Expand the collector field to a full 5 block widths from the cursor before the final instant-collection upgrade."''', "reach II description")
s = replace_once(s,
'''"type": "set_collection_radius_blocks",\n          "value": 1.6''',
'''"type": "set_collection_radius_blocks",\n          "value": 5.0''', "reach II five blocks")
s = replace_once(s,
'''"description": "Manual and Hover Mining rewards bank themselves immediately. Existing manual pickups are collected when this upgrade is purchased."''',
'''"description": "Final collector upgrade: manual and Hover Mining rewards are collected instantly when mined. Existing manual pickups are swept up immediately on purchase."''', "final collector description")
write(path, s)

print("Applied collection gravity, collection-timed feedback, five-block reach, star-node constellation visuals, exact icon centering, and detail-card wiggle animations.")
