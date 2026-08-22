using System;
using System.Collections.Generic;
using Godot;
using TenMillionBlocks.Content;
using TenMillionBlocks.Economy;
using TenMillionBlocks.Mining;
using TenMillionBlocks.Presentation;
using TenMillionBlocks.World;
using TenMillionBlocks.World.Rendering;

namespace TenMillionBlocks.UI;

/// <summary>
/// Incremental-game presentation layer. Authoritative state is already complete before this view is
/// notified; every animation here is disposable presentation and may be aggregated/dropped without
/// affecting mining, currency, special resources, save data or replay.
/// </summary>
public partial class IncrementalFeedbackView : CanvasLayer
{
    private const int MaxActiveFlights = 48;
    private const int MaxFlightSpawnsPerFrame = 8;
    private const double AggregationWindowSeconds = 0.11;
    private const float FlightDurationSeconds = 0.58f;

    private readonly record struct PendingKey(string VisualId, bool Special);

    private sealed class CounterChip
    {
        public PanelContainer Root { get; init; } = null!;
        public Label Caption { get; init; } = null!;
        public Label Value { get; init; } = null!;
    }

    private sealed class PendingBucket
    {
        public string VisualId { get; init; } = string.Empty;
        public Control Target { get; init; } = null!;
        public long Count;
        public long Reward;
        public Vector2 Source;
        public bool HasSource;
        public double Age;
        public bool Special;
    }

    private sealed class PickupFlight
    {
        public Control Root { get; init; } = null!;
        public TextureRect Icon { get; init; } = null!;
        public Label Amount { get; init; } = null!;
        public Control Target { get; set; } = null!;
        public Vector2 Start;
        public float Age;
        public float Duration;
    }

    private sealed class PulseAnimation
    {
        public Control Target { get; set; } = null!;
        public float Age;
        public float Duration;
        public float StartScale;
    }

    private VirtualWorld _world = null!;
    private WorldView _worldView = null!;
    private MiningService _mining = null!;
    private SpecialResourceInventory _specialResources = null!;
    private BlockAssetRegistry _assets = null!;

    private Control _root = null!;
    private HBoxContainer _counterBar = null!;
    private HBoxContainer _specialRow = null!;
    private CounterChip _blocksChip = null!;
    private CounterChip _resourcesChip = null!;

    private readonly Dictionary<string, CounterChip> _specialChips = new(StringComparer.Ordinal);
    private readonly Dictionary<PendingKey, PendingBucket> _pending = new();
    private readonly List<PendingKey> _readyBuckets = new();
    private readonly List<PickupFlight> _activeFlights = new();
    private readonly Stack<PickupFlight> _flightPool = new();
    private readonly List<PulseAnimation> _activePulses = new();
    private readonly Stack<PulseAnimation> _pulsePool = new();
    private readonly Dictionary<string, Texture2D> _previewTextures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SubViewport> _previewViewports = new(StringComparer.Ordinal);

    private bool _subscribed;
    private bool _counterRefreshPending;

    public long SpawnedFeedbackCount { get; private set; }
    public long AggregatedFeedbackCount { get; private set; }
    public long DroppedFeedbackCount { get; private set; }
    public int ActiveFeedbackCount => _activeFlights.Count;
    public int PooledFeedbackCount => _flightPool.Count;

    public void Initialize(
        VirtualWorld world,
        WorldView worldView,
        MiningService mining,
        SpecialResourceInventory specialResources,
        BlockAssetRegistry assets)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _worldView = worldView ?? throw new ArgumentNullException(nameof(worldView));
        _mining = mining ?? throw new ArgumentNullException(nameof(mining));
        _specialResources = specialResources ?? throw new ArgumentNullException(nameof(specialResources));
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
    }

    public override void _Ready()
    {
        Layer = 24;
        BuildUi();
        RefreshCounters();
        Subscribe();
    }

    public override void _ExitTree()
    {
        Unsubscribe();
    }

    public override void _Process(double delta)
    {
        // Mining can emit BlockMined + CurrencyChanged many times in one frame. The authoritative values
        // are already current, so rebuild the displayed strings once at the next presentation tick rather
        // than once per event. This eliminates a large amount of formatting/layout garbage under dense
        // automation without changing gameplay accounting.
        if (_counterRefreshPending)
        {
            RefreshCounters();
        }

        if (GraphicsSettingsRuntime.Current?.ReducedMotionEnabled == true)
        {
            StopAnimatedFeedback();
            return;
        }

        FlushReadyBuckets(delta);
        AdvanceFlights((float)delta);
        AdvancePulses((float)delta);
    }

    private void BuildUi()
    {
        _root = new Control
        {
            Name = "IncrementalFeedbackRoot",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        _counterBar = new HBoxContainer
        {
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            OffsetLeft = -310.0f,
            OffsetTop = 14.0f,
            OffsetRight = 310.0f,
            OffsetBottom = 66.0f,
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _counterBar.AddThemeConstantOverride("separation", 8);
        _root.AddChild(_counterBar);

        _blocksChip = BuildCounterChip("BLOCKS MINED", "0", 178.0f);
        _resourcesChip = BuildCounterChip("RESOURCES", "0", 154.0f);
        _counterBar.AddChild(_blocksChip.Root);
        _counterBar.AddChild(_resourcesChip.Root);

        _specialRow = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _specialRow.AddThemeConstantOverride("separation", 6);
        _counterBar.AddChild(_specialRow);

        foreach ((string resourceId, long amount) in _specialResources.Balances)
        {
            if (amount > 0) EnsureSpecialChip(resourceId);
        }
    }

    private CounterChip BuildCounterChip(string caption, string value, float width)
    {
        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(width, 50.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.AddThemeConstantOverride("margin_top", 5);
        margin.AddThemeConstantOverride("margin_bottom", 5);
        panel.AddChild(margin);

        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 0);
        margin.AddChild(column);

        var captionLabel = new Label
        {
            Text = caption,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        captionLabel.AddThemeFontSizeOverride("font_size", 11);
        column.AddChild(captionLabel);

        var valueLabel = new Label
        {
            Text = value,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        valueLabel.AddThemeFontSizeOverride("font_size", 20);
        column.AddChild(valueLabel);

        return new CounterChip { Root = panel, Caption = captionLabel, Value = valueLabel };
    }

    private CounterChip EnsureSpecialChip(string resourceId)
    {
        if (_specialChips.TryGetValue(resourceId, out CounterChip? existing)) return existing;

        BlockDefinition definition = _mining.GetBlockDefinition(resourceId);
        CounterChip chip = BuildCounterChip(definition.DisplayName.ToUpperInvariant(), "0", 136.0f);

        var row = new HBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        row.AddThemeConstantOverride("separation", 2);

        var icon = new TextureRect
        {
            Texture = GetPreviewTexture(resourceId),
            CustomMinimumSize = new Vector2(26, 26),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        row.AddChild(icon);

        VBoxContainer? column = FindFirstVBox(chip.Root);
        column?.AddChild(row);
        if (column is not null)
        {
            column.MoveChild(row, column.GetChildCount() - 1);
        }

        _specialRow.AddChild(chip.Root);
        _specialChips.Add(resourceId, chip);
        return chip;
    }

    private static VBoxContainer? FindFirstVBox(Node node)
    {
        if (node is VBoxContainer vbox) return vbox;
        foreach (Node child in node.GetChildren())
        {
            VBoxContainer? found = FindFirstVBox(child);
            if (found is not null) return found;
        }
        return null;
    }

    private void Subscribe()
    {
        if (_subscribed) return;
        _mining.BlockMined += OnBlockMined;
        _mining.BulkMined += OnBulkMined;
        _mining.CurrencyChanged += OnCurrencyChanged;
        _specialResources.Changed += OnSpecialResourcesChanged;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        _mining.BlockMined -= OnBlockMined;
        _mining.BulkMined -= OnBulkMined;
        _mining.CurrencyChanged -= OnCurrencyChanged;
        _specialResources.Changed -= OnSpecialResourcesChanged;
        _subscribed = false;
    }

    private void OnBlockMined(MiningResult result)
    {
        if (!result.Success || !result.Removed) return;

        _counterRefreshPending = true;
        Pulse(_blocksChip.Root, strong: false);
        if (result.Reward > 0) Pulse(_resourcesChip.Root, strong: false);

        BlockDefinition definition = _mining.GetBlockDefinition(result.BlockId);
        bool special = definition.Tags.Contains("gem", StringComparer.Ordinal);
        Vector2 source = default;
        bool hasSource = result.Source != MiningSource.Offline && TryProjectSource(result.Voxel, out source);

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

    private void OnBulkMined(BulkMiningResult result)
    {
        if (!result.Success) return;
        _counterRefreshPending = true;
        Pulse(_blocksChip.Root, strong: true);
        if (result.Reward > 0) Pulse(_resourcesChip.Root, strong: true);
        AggregatedFeedbackCount = checked(AggregatedFeedbackCount + Math.Max(1L, result.BlocksMined));
    }

    private void OnCurrencyChanged(long _)
    {
        _counterRefreshPending = true;
    }

    private void OnSpecialResourcesChanged()
    {
        _counterRefreshPending = true;
    }

    private void RefreshCounters()
    {
        _counterRefreshPending = false;
        if (_blocksChip is null || _resourcesChip is null) return;
        _blocksChip.Value.Text =
            $"{IncrementalNumberFormatter.Format(_mining.TotalMined)} / {IncrementalNumberFormatter.Format(_world.InitialMineableBlocks)}";
        _resourcesChip.Value.Text = IncrementalNumberFormatter.Format(_mining.Currency);
        RefreshSpecialCounters();
    }

    private void RefreshSpecialCounters()
    {
        if (_specialRow is null) return;

        foreach ((string resourceId, long amount) in _specialResources.Balances)
        {
            if (amount <= 0) continue;
            CounterChip chip = EnsureSpecialChip(resourceId);
            chip.Value.Text = IncrementalNumberFormatter.Format(amount);
        }

        foreach ((string resourceId, CounterChip chip) in _specialChips)
        {
            chip.Value.Text = IncrementalNumberFormatter.Format(_specialResources.Get(resourceId));
        }
    }

    private void QueuePickup(
        string visualId,
        Control target,
        long count,
        long reward,
        Vector2 source,
        bool hasSource,
        bool special)
    {
        if (GraphicsSettingsRuntime.Current?.ReducedMotionEnabled == true)
        {
            return;
        }

        var key = new PendingKey(visualId, special);
        if (_pending.TryGetValue(key, out PendingBucket? bucket))
        {
            bucket.Count = checked(bucket.Count + Math.Max(1L, count));
            bucket.Reward = checked(bucket.Reward + Math.Max(0L, reward));
            if (hasSource)
            {
                bucket.Source = source;
                bucket.HasSource = true;
            }
            AggregatedFeedbackCount++;
            return;
        }

        _pending.Add(key, new PendingBucket
        {
            VisualId = visualId,
            Target = target,
            Count = Math.Max(1L, count),
            Reward = Math.Max(0L, reward),
            Source = source,
            HasSource = hasSource,
            Age = 0.0,
            Special = special,
        });
    }

    private void FlushReadyBuckets(double delta)
    {
        if (_pending.Count == 0) return;

        _readyBuckets.Clear();
        foreach ((PendingKey key, PendingBucket bucket) in _pending)
        {
            bucket.Age += delta;
            if (bucket.Age >= AggregationWindowSeconds) _readyBuckets.Add(key);
        }

        int spawned = 0;
        foreach (PendingKey key in _readyBuckets)
        {
            if (!_pending.Remove(key, out PendingBucket? bucket)) continue;

            if (!bucket.HasSource || spawned >= MaxFlightSpawnsPerFrame)
            {
                DroppedFeedbackCount++;
                Pulse(bucket.Target, bucket.Special || bucket.Count > 1);
                continue;
            }

            SpawnFlight(bucket);
            spawned++;
        }
    }

    private void SpawnFlight(PendingBucket bucket)
    {
        if (_activeFlights.Count >= MaxActiveFlights)
        {
            DroppedFeedbackCount++;
            Pulse(bucket.Target, bucket.Special || bucket.Count > 1);
            return;
        }

        PickupFlight flight = _flightPool.Count > 0 ? _flightPool.Pop() : CreateFlight();
        flight.Root.Visible = true;
        flight.Root.Modulate = Colors.White;
        flight.Root.Scale = Vector2.One;
        flight.Icon.Texture = GetPreviewTexture(bucket.VisualId);
        flight.Amount.Text = bucket.Special
            ? bucket.Count > 1 ? $"+{bucket.Count:N0}" : "+1"
            : bucket.Count > 1
                ? $"x{bucket.Count:N0}  +{bucket.Reward:N0}"
                : bucket.Reward > 0 ? $"+1  +{bucket.Reward:N0}" : "+1";
        flight.Target = bucket.Target;
        flight.Start = bucket.Source;
        flight.Age = 0.0f;
        flight.Duration = bucket.Special ? 0.72f : FlightDurationSeconds;
        flight.Root.Position = bucket.Source - new Vector2(26.0f, 26.0f);
        _activeFlights.Add(flight);
        SpawnedFeedbackCount++;
    }

    private PickupFlight CreateFlight()
    {
        var root = new HBoxContainer
        {
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(104, 52),
        };
        root.AddThemeConstantOverride("separation", 2);
        _root.AddChild(root);

        var icon = new TextureRect
        {
            CustomMinimumSize = new Vector2(50, 50),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        root.AddChild(icon);

        var amount = new Label
        {
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        amount.AddThemeFontSizeOverride("font_size", 16);
        root.AddChild(amount);

        return new PickupFlight { Root = root, Icon = icon, Amount = amount };
    }

    private void AdvanceFlights(float delta)
    {
        for (int i = _activeFlights.Count - 1; i >= 0; i--)
        {
            PickupFlight flight = _activeFlights[i];
            flight.Age += Math.Max(0.0f, delta);
            float t = Math.Clamp(flight.Age / Math.Max(0.001f, flight.Duration), 0.0f, 1.0f);
            float eased = 1.0f - MathF.Pow(1.0f - t, 3.0f);
            Vector2 destination = TargetCenter(flight.Target);
            Vector2 straight = flight.Start.Lerp(destination, eased);
            float arc = MathF.Sin(t * MathF.PI) * MathF.Min(90.0f, flight.Start.DistanceTo(destination) * 0.16f);
            flight.Root.Position = straight + new Vector2(0.0f, -arc) - new Vector2(26.0f, 26.0f);
            flight.Root.Modulate = new Color(1, 1, 1, Math.Clamp((1.0f - t) * 1.7f, 0.0f, 1.0f));
            flight.Root.Scale = Vector2.One * Mathf.Lerp(1.0f, 0.72f, t);

            if (t < 1.0f) continue;

            Pulse(flight.Target, strong: true);
            flight.Root.Visible = false;
            _activeFlights.RemoveAt(i);
            _flightPool.Push(flight);
        }
    }

    private bool TryProjectSource(Vector3I voxel, out Vector2 screen)
    {
        screen = default;
        Camera3D? camera = GetViewport().GetCamera3D();
        if (camera is null) return false;

        Vector3 worldPosition = _worldView.VoxelToWorld(voxel);
        if (camera.IsPositionBehind(worldPosition)) return false;

        Vector2 projected = camera.UnprojectPosition(worldPosition);
        Rect2 viewportRect = GetViewport().GetVisibleRect().Grow(24.0f);
        if (!viewportRect.HasPoint(projected)) return false;

        screen = projected;
        return true;
    }

    private static Vector2 TargetCenter(Control target)
        => target.GlobalPosition + target.Size * 0.5f;

    private void Pulse(Control target, bool strong)
    {
        if (target is null || !IsInstanceValid(target)) return;
        if (GraphicsSettingsRuntime.Current?.ReducedMotionEnabled == true)
        {
            target.Scale = Vector2.One;
            return;
        }

        float startScale = strong ? 1.12f : 1.055f;
        float duration = strong ? 0.24f : 0.16f;
        target.PivotOffset = target.Size * 0.5f;
        target.Scale = Vector2.One * startScale;

        foreach (PulseAnimation pulse in _activePulses)
        {
            if (!ReferenceEquals(pulse.Target, target)) continue;
            pulse.Age = 0.0f;
            pulse.Duration = duration;
            pulse.StartScale = startScale;
            return;
        }

        PulseAnimation animation = _pulsePool.Count > 0 ? _pulsePool.Pop() : new PulseAnimation();
        animation.Target = target;
        animation.Age = 0.0f;
        animation.Duration = duration;
        animation.StartScale = startScale;
        _activePulses.Add(animation);
    }

    private void AdvancePulses(float delta)
    {
        float dt = Math.Max(0.0f, delta);
        for (int i = _activePulses.Count - 1; i >= 0; i--)
        {
            PulseAnimation pulse = _activePulses[i];
            if (!IsInstanceValid(pulse.Target))
            {
                _activePulses.RemoveAt(i);
                _pulsePool.Push(pulse);
                continue;
            }

            pulse.Age += dt;
            float t = Math.Clamp(pulse.Age / Math.Max(0.001f, pulse.Duration), 0.0f, 1.0f);
            float eased = 1.0f - MathF.Pow(1.0f - t, 3.0f);
            pulse.Target.Scale = Vector2.One * Mathf.Lerp(pulse.StartScale, 1.0f, eased);
            if (t < 1.0f) continue;

            pulse.Target.Scale = Vector2.One;
            _activePulses.RemoveAt(i);
            _pulsePool.Push(pulse);
        }
    }

    private void StopAnimatedFeedback()
    {
        _pending.Clear();
        _readyBuckets.Clear();

        for (int i = _activeFlights.Count - 1; i >= 0; i--)
        {
            PickupFlight flight = _activeFlights[i];
            flight.Root.Visible = false;
            flight.Root.Modulate = Colors.White;
            flight.Root.Scale = Vector2.One;
            _flightPool.Push(flight);
        }
        _activeFlights.Clear();

        for (int i = _activePulses.Count - 1; i >= 0; i--)
        {
            PulseAnimation pulse = _activePulses[i];
            if (IsInstanceValid(pulse.Target)) pulse.Target.Scale = Vector2.One;
            _pulsePool.Push(pulse);
        }
        _activePulses.Clear();
    }

    private Texture2D GetPreviewTexture(string blockId)
    {
        if (_previewTextures.TryGetValue(blockId, out Texture2D? cached)) return cached;

        BlockDefinition definition = _assets.GetDefinition(blockId);
        var viewport = new SubViewport
        {
            Name = $"PickupPreview_{blockId}",
            Size = new Vector2I(72, 72),
            TransparentBg = true,
            OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
        };
        AddChild(viewport);

        var modelRoot = new Node3D();
        viewport.AddChild(modelRoot);
        var mesh = new MeshInstance3D
        {
            Mesh = _assets.GetMesh(blockId),
            MaterialOverride = _assets.GetMaterialOverride(blockId),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        float scale = definition.Tags.Contains("tree", StringComparer.Ordinal) ? 0.30f : 0.76f;
        mesh.Scale = Vector3.One * scale;
        modelRoot.AddChild(mesh);
        modelRoot.RotationDegrees = new Vector3(-18.0f, 34.0f, 0.0f);

        viewport.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-45.0f, -38.0f, 0.0f),
            LightEnergy = 1.25f,
            ShadowEnabled = false,
        });
        viewport.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(20.0f, 145.0f, 0.0f),
            LightEnergy = 0.45f,
            ShadowEnabled = false,
        });

        var camera = new Camera3D
        {
            Position = definition.Tags.Contains("tree", StringComparer.Ordinal)
                ? new Vector3(5.0f, 3.4f, 5.0f)
                : new Vector3(2.5f, 2.0f, 2.5f),
            Current = true,
            Fov = 35.0f,
        };
        viewport.AddChild(camera);
        camera.LookAt(Vector3.Zero, Vector3.Up);

        Texture2D texture = viewport.GetTexture();
        _previewViewports.Add(blockId, viewport);
        _previewTextures.Add(blockId, texture);
        return texture;
    }
}
