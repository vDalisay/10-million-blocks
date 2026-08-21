using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using TenMillionBlocks.Content;
using TenMillionBlocks.Save;

namespace TenMillionBlocks.UI;

/// <summary>
/// Lightweight playable world browser. Revisit resumes each world's persistent run; Replay requests
/// the immutable recorded run and is exposed separately so the two concepts cannot be confused.
/// </summary>
public partial class WorldSelectView : CanvasLayer
{
    private WorldCatalog _catalog = null!;
    private GameSaveData _save = null!;
    private string _currentWorldId = string.Empty;

    private Control _overlay = null!;
    private VBoxContainer _list = null!;
    private Button _toggle = null!;

    public event Action<string>? RevisitRequested;
    public event Action<string>? ReplayRequested;
    public event Action<bool>? OpenChanged;

    public bool IsOpen => _overlay is not null && _overlay.Visible;

    public void Initialize(WorldCatalog catalog, GameSaveData save, string currentWorldId)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _save = save ?? throw new ArgumentNullException(nameof(save));
        _currentWorldId = currentWorldId ?? string.Empty;
    }

    public override void _Ready()
    {
        Layer = 34;
        BuildUi();
        Refresh();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;
        if (key.Keycode == Key.Escape && IsOpen)
        {
            SetOpen(false);
            GetViewport().SetInputAsHandled();
        }
    }

    public void Refresh(string? currentWorldId = null)
    {
        if (!string.IsNullOrWhiteSpace(currentWorldId)) _currentWorldId = currentWorldId;
        if (_list is null) return;

        foreach (Node child in _list.GetChildren()) child.QueueFree();

        IEnumerable<WorldProfile> unlocked = _save.UnlockedWorldIds
            .Where(id => _catalog.Worlds.ContainsKey(id))
            .Select(id => _catalog.Get(id))
            .OrderBy(profile => Math.Max(profile.LogicalWidth, Math.Max(profile.LogicalHeight, profile.LogicalDepth)))
            .ThenBy(profile => profile.Id, StringComparer.Ordinal);

        foreach (WorldProfile profile in unlocked)
        {
            _list.AddChild(BuildWorldCard(profile));
        }
    }

    private void BuildUi()
    {
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        _toggle = new Button
        {
            Text = "WORLDS",
            AnchorLeft = 1.0f,
            AnchorTop = 1.0f,
            AnchorRight = 1.0f,
            AnchorBottom = 1.0f,
            OffsetLeft = -132.0f,
            OffsetTop = -58.0f,
            OffsetRight = -16.0f,
            OffsetBottom = -16.0f,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _toggle.Pressed += () => SetOpen(!IsOpen);
        root.AddChild(_toggle);

        _overlay = new Control
        {
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(_overlay);

        var backdrop = new ColorRect
        {
            Color = new Color(0.002f, 0.006f, 0.015f, 0.90f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _overlay.AddChild(backdrop);

        var panel = new PanelContainer
        {
            AnchorLeft = 0.5f,
            AnchorTop = 0.5f,
            AnchorRight = 0.5f,
            AnchorBottom = 0.5f,
            OffsetLeft = -360.0f,
            OffsetTop = -300.0f,
            OffsetRight = 360.0f,
            OffsetBottom = 300.0f,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _overlay.AddChild(panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 18);
        margin.AddThemeConstantOverride("margin_top", 16);
        margin.AddThemeConstantOverride("margin_right", 18);
        margin.AddThemeConstantOverride("margin_bottom", 16);
        panel.AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        margin.AddChild(column);

        var header = new HBoxContainer();
        var title = new Label
        {
            Text = "WORLDS",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        title.AddThemeFontSizeOverride("font_size", 25);
        header.AddChild(title);
        var close = new Button { Text = "Close", CustomMinimumSize = new Vector2(76, 36) };
        close.Pressed += () => SetOpen(false);
        header.AddChild(close);
        column.AddChild(header);

        column.AddChild(new Label
        {
            Text = "Revisit resumes the saved world. Replay is read-only and never changes that save.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        column.AddChild(scroll);

        _list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 9);
        scroll.AddChild(_list);
    }

    private Control BuildWorldCard(WorldProfile profile)
    {
        var card = new PanelContainer
        {
            CustomMinimumSize = new Vector2(0, 118),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_top", 9);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_bottom", 9);
        card.AddChild(margin);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        margin.AddChild(row);

        var information = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        information.AddThemeConstantOverride("separation", 2);
        row.AddChild(information);

        bool isCurrent = profile.Id == _currentWorldId;
        bool completed = _save.CompletedWorldIds.Contains(profile.Id);
        long mined = SavedMinedCount(profile.Id);
        long target = SavedTarget(profile);
        bool targetKnown = target > 0L;
        double progress = completed
            ? 1.0
            : targetKnown
                ? Math.Clamp(mined / (double)target, 0.0, 1.0)
                : 0.0;
        string progressText = completed
            ? "CLEARED"
            : targetKnown
                ? $"{progress:P0} cleared"
                : $"{mined:N0} blocks mined";
        string dimensions = $"{profile.LogicalWidth} x {profile.LogicalHeight} x {profile.LogicalDepth}";

        var name = new Label
        {
            Text = profile.DisplayName + (isCurrent ? "  · CURRENT" : string.Empty),
        };
        name.AddThemeFontSizeOverride("font_size", 18);
        information.AddChild(name);
        information.AddChild(new Label
        {
            Text = $"{dimensions}  ·  {progressText}",
        });
        information.AddChild(new Label
        {
            Text = LastPlayedText(profile.Id),
            Modulate = new Color(0.78f, 0.82f, 0.88f),
        });
        information.AddChild(new ProgressBar
        {
            MinValue = 0,
            MaxValue = 1,
            Value = progress,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, 7),
        });

        var actions = new VBoxContainer { CustomMinimumSize = new Vector2(132, 0) };
        actions.AddThemeConstantOverride("separation", 5);
        row.AddChild(actions);

        var revisit = new Button
        {
            Text = isCurrent ? "CONTINUE" : "REVISIT",
            CustomMinimumSize = new Vector2(126, 36),
        };
        string worldId = profile.Id;
        revisit.Pressed += () =>
        {
            SetOpen(false);
            if (isCurrent) return;
            WorldLoadingScreen.RunTransition(
                this,
                $"LOADING {profile.DisplayName}",
                () => RevisitRequested?.Invoke(worldId));
        };
        actions.AddChild(revisit);

        bool replayAvailable = completed && ReplayExists(profile.Id);
        var replay = new Button
        {
            Text = "REPLAY",
            Disabled = !replayAvailable,
            TooltipText = replayAvailable ? "Watch the recorded clear without changing the saved world." : "Replay becomes available after a recorded clear.",
            CustomMinimumSize = new Vector2(126, 32),
        };
        replay.Pressed += () =>
        {
            SetOpen(false);
            WorldLoadingScreen.RunTransition(
                this,
                $"LOADING {profile.DisplayName} REPLAY",
                () => ReplayRequested?.Invoke(worldId));
        };
        actions.AddChild(replay);

        return card;
    }

    private long SavedMinedCount(string worldId)
    {
        if (!_save.Worlds.TryGetValue(worldId, out WorldSaveData? saved)) return 0L;
        long sparse = saved.MinedChunks.Sum(chunk => (long)(chunk.MinedLocalIndices?.Count ?? 0));
        long exhausted = saved.ExhaustedRegions.Sum(region => Math.Max(0L, region.MinedCount));
        return checked(sparse + exhausted);
    }

    private long SavedTarget(WorldProfile profile)
    {
        if (_save.Worlds.TryGetValue(profile.Id, out WorldSaveData? saved)
            && saved.InitialMineableBlocks > 0)
        {
            return saved.InitialMineableBlocks;
        }
        return Math.Max(0L, profile.TargetMineableBlocks);
    }

    private string LastPlayedText(string worldId)
    {
        if (!_save.Worlds.TryGetValue(worldId, out WorldSaveData? saved)
            || saved.LastPlayedUnixSeconds <= 0)
        {
            return "Last played: not yet";
        }

        DateTimeOffset timestamp = DateTimeOffset.FromUnixTimeSeconds(saved.LastPlayedUnixSeconds).ToLocalTime();
        return $"Last played: {timestamp:dd MMM yyyy HH:mm}";
    }

    private bool ReplayExists(string worldId)
    {
        if (!_save.Worlds.TryGetValue(worldId, out WorldSaveData? saved)
            || string.IsNullOrWhiteSpace(saved.ReplayFile))
        {
            return false;
        }
        return System.IO.File.Exists(ProjectSettings.GlobalizePath(saved.ReplayFile));
    }

    private void SetOpen(bool open)
    {
        if (_overlay.Visible == open) return;
        _overlay.Visible = open;
        _toggle.Visible = !open;
        OpenChanged?.Invoke(open);
    }
}
