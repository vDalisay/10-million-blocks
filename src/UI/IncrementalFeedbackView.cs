using System;
using System.Collections.Generic;
using Godot;
using TenMillionBlocks.Collection;
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
    private ResourceCollectionField _collection = null!;

    private Control _root = null!;
    private Control _counterBar = null!;
    private VBoxContainer _resourceRail = null!;
    private VBoxContainer _specialRow = null!;
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
        BlockAssetRegistry assets,
        ResourceCollectionField collection)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _worldView = worldView ?? throw new ArgumentNullException(nameof(worldView));
        _mining = mining ?? throw new ArgumentNullException(nameof(mining));
        _specialResources = specialResources ?? throw new ArgumentNullException(nameof(specialResources));
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _collection = collection ?? throw new ArgumentNullException(nameof(collection));
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

        // The mined total is intentionally isolated in the upper-left. Incremental games make the
        // primary number the strongest piece of hierarchy; the world itself stays visually central.
        _counterBar = new Control
        {
            AnchorLeft = 0.0f,
            AnchorTop = 0.0f,
            AnchorRight = 0.0f,
            AnchorBottom = 0.0f,
            OffsetLeft = 14.0f,
            OffsetTop = 14.0f,
            OffsetRight = 242.0f,
            OffsetBottom = 96.0f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _root.AddChild(_counterBar);

        _blocksChip = BuildCounterChip("BLOCKS MINED", "0", 228.0f);
        _blocksChip.Root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _counterBar.AddChild(_blocksChip.Root);

        // Resource buckets live on the opposite side, echoing the reference idlers without inventing
        // additional currencies. Ordinary resources are one bucket; the three existing gem inventories
        // are persistent individual buckets and remain visible at zero so the player can read the system.
        _resourceRail = new VBoxContainer
        {
            AnchorLeft = 1.0f,
            AnchorTop = 0.0f,
            AnchorRight = 1.0f,
            AnchorBottom = 0.0f,
            OffsetLeft = -176.0f,
            OffsetTop = 72.0f,
            OffsetRight = -14.0f,
            OffsetBottom = 370.0f,
            Alignment = BoxContainer.AlignmentMode.Begin,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _resourceRail.AddThemeConstantOverride("separation", 6);
        _root.AddChild(_resourceRail);

        var resourceHeader = new Label
        {
            Text = "RESOURCE LEDGER",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        resourceHeader.AddThemeFontSizeOverride("font_size", 10);
        resourceHeader.AddThemeColorOverride("font_color", new Color("#6d8796"));
        _resourceRail.AddChild(resourceHeader);

        _resourcesChip = BuildCounterChip("RESOURCES", "0", 162.0f);
        _resourceRail.AddChild(_resourcesChip.Root);

        _specialRow = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Begin,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _specialRow.AddThemeConstantOverride("separation", 6);
        _resourceRail.AddChild(_specialRow);

        EnsureSpecialChip("gem_red");
        EnsureSpecialChip("gem_blue");
        EnsureSpecialChip("gem_green");
        foreach ((string resourceId, _) in _specialResources.Balances)
        {
            EnsureSpecialChip(resourceId);
        }
    }

    private CounterChip BuildCounterChip(string caption, string value, float width)
    {
        bool primary = string.Equals(caption, "BLOCKS MINED", StringComparison.Ordinal);
        Color accent = primary ? new Color("#71ded0") : new Color("#e7b45c");
        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(width, primary ? 82.0f : 58.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        panel.AddThemeStyleboxOverride("panel", RetroPanel(accent, primary ? 0.78f : 0.72f));

        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", primary ? 14 : 10);
        margin.AddThemeConstantOverride("margin_right", primary ? 14 : 10);
        margin.AddThemeConstantOverride("margin_top", primary ? 8 : 6);
        margin.AddThemeConstantOverride("margin_bottom", primary ? 8 : 6);
        panel.AddChild(margin);

        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", primary ? 1 : 0);
        margin.AddChild(column);

        var captionLabel = new Label
        {
            Text = caption,
            HorizontalAlignment = HorizontalAlignment.Left,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        captionLabel.AddThemeFontSizeOverride("font_size", primary ? 11 : 10);
        captionLabel.AddThemeColorOverride("font_color", new Color(accent, 0.82f));
        column.AddChild(captionLabel);

        var valueLabel = new Label
        {
            Text = value,
            HorizontalAlignment = HorizontalAlignment.Left,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        valueLabel.AddThemeFontSizeOverride("font_size", primary ? 31 : 20);
        valueLabel.AddThemeColorOverride("font_color", primary ? new Color("#effffd") : new Color("#fff4d5"));
        valueLabel.AddThemeConstantOverride("outline_size", 3);
        valueLabel.AddThemeColorOverride("font_outline_color", new Color(0.0f, 0.04f, 0.06f, 0.9f));
        column.AddChild(valueLabel);

        return new CounterChip { Root = panel, Caption = captionLabel, Value = valueLabel };
    }

    private static StyleBoxFlat RetroPanel(Color accent, float opacity)
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.008f, 0.022f, 0.036f, opacity),
            BorderColor = new Color(accent, 0.58f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 2,
            CornerRadiusTopRight = 2,
            CornerRadiusBottomLeft = 2,
            CornerRadiusBottomRight = 2,
            ShadowColor = new Color(0, 0, 0, 0.30f),
            ShadowSize = 5,
            ShadowOffset = new Vector2(0, 2),
        };
        return style;
    }

    private CounterChip EnsureSpecialChip(string resourceId)
    {
        if (_specialChips.TryGetValue(resourceId, out CounterChip? existing)) return existing;

        BlockDefinition definition = _mining.GetBlockDefinition(resourceId);
        Color accent = resourceId switch
        {
            "gem_red" => new Color("#f06a61"),
            "gem_blue" => new Color("#55b8ec"),
            "gem_green" => new Color("#54d79a"),
            _ => new Color("#9eb8c5"),
        };

        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(162.0f, 54.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        panel.AddThemeStyleboxOverride("panel", RetroPanel(accent, 0.68f));

        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", 7);
        margin.AddThemeConstantOverride("margin_right", 9);
        margin.AddThemeConstantOverride("margin_top", 5);
        margin.AddThemeConstantOverride("margin_bottom", 5);
        panel.AddChild(margin);

        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 7);
        margin.AddChild(row);

        var icon = new TextureRect
        {
            Texture = GetPreviewTexture(resourceId),
            CustomMinimumSize = new Vector2(34, 34),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(1, 1, 1, 0.92f),
        };
        row.AddChild(icon);

        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        column.AddThemeConstantOverride("separation", -2);
        row.AddChild(column);

        var caption = new Label
        {
            Text = definition.DisplayName.ToUpperInvariant(),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        caption.AddThemeFontSizeOverride("font_size", 9);
        caption.AddThemeColorOverride("font_color", new Color(accent, 0.86f));
        column.AddChild(caption);

        var value = new Label
        {
            Text = "0",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        value.AddThemeFontSizeOverride("font_size", 19);
        value.AddThemeColorOverride("font_color", new Color("#edf7f7"));
        column.AddChild(value);

        var chip = new CounterChip { Root = panel, Caption = caption, Value = value };
        _specialRow.AddChild(panel);
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
        _collection.PickupCollected += OnPickupCollected;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        _mining.BlockMined -= OnBlockMined;
        _mining.BulkMined -= OnBulkMined;
        _mining.CurrencyChanged -= OnCurrencyChanged;
        _specialResources.Changed -= OnSpecialResourcesChanged;
        _collection.PickupCollected -= OnPickupCollected;
        _subscribed = false;
    }

    private void OnBlockMined(MiningResult result)
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
                Control destination = result.Reward > 0 ? _resourcesChip.Root : _blocksChip.Root;
                QueuePickup(
                    result.BlockId,
                    destination,
                    result.BlocksRemoved,
                    result.Reward,
                    source,
                    hasSource,
                    special: false);
            }
        }

        // The inventory itself remains authoritative/direct, but presentation now waits for the same
        // physical collection beat as ordinary resources. Direct/world-event sources still celebrate now.
        if (special && !deferredCollectionSource)
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
        else if (special)
        {
            _ = EnsureSpecialChip(result.BlockId);
        }
    }

    private void OnPickupCollected(ResourcePickupCollected collected)
    {
        _counterRefreshPending = true;
        Pulse(_blocksChip.Root, strong: collected.BlocksRemoved > 1);
        if (collected.Amount > 0) Pulse(_resourcesChip.Root, strong: collected.Amount > 1);
        Control destination = collected.Amount > 0 ? _resourcesChip.Root : _blocksChip.Root;
        QueuePickup(
            collected.BlockId,
            destination,
            Math.Max(1L, collected.BlocksRemoved),
            Math.Max(0L, collected.Amount),
            collected.ScreenPosition,
            hasSource: true,
            special: false);

        BlockDefinition definition = _mining.GetBlockDefinition(collected.BlockId);
        if (definition.Tags.Contains("gem", StringComparer.Ordinal))
        {
            CounterChip specialChip = EnsureSpecialChip(collected.BlockId);
            Pulse(specialChip.Root, strong: true);
            QueuePickup(
                collected.BlockId,
                specialChip.Root,
                1L,
                0L,
                collected.ScreenPosition,
                hasSource: true,
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
        double percent = _world.InitialMineableBlocks <= 0
            ? 100.0
            : Math.Clamp(_mining.TotalMined * 100.0 / _world.InitialMineableBlocks, 0.0, 100.0);
        _blocksChip.Caption.Text = $"BLOCKS MINED  //  {percent:0.0}% OF {IncrementalNumberFormatter.Format(_world.InitialMineableBlocks)}";
        _blocksChip.Value.Text = IncrementalNumberFormatter.Format(_mining.TotalMined);
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
