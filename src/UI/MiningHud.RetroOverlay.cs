using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using TenMillionBlocks.Automation;
using TenMillionBlocks.Skills;

namespace TenMillionBlocks.UI;

public partial class MiningHud
{
    private sealed class RetroAutomationEntry
    {
        public string MinerId { get; init; } = string.Empty;
        public string SkillId { get; init; } = string.Empty;
        public string Code { get; init; } = string.Empty;
        public Color Accent { get; init; }
        public Button Button { get; init; } = null!;
        public Label CodeLabel { get; init; } = null!;
        public Label CountLabel { get; init; } = null!;
        public Label StatusLabel { get; init; } = null!;
    }

    private PanelContainer? _retroAutomationRail;
    private VBoxContainer? _retroAutomationList;
    private Label? _retroAutomationRate;
    private PanelContainer? _retroBottomStrip;
    private ProgressBar? _retroProgress;
    private Label? _retroWorldLine;
    private Label? _retroControls;
    private Label? _retroEvent;
    private readonly Dictionary<string, RetroAutomationEntry> _retroAutomationEntries = new(StringComparer.Ordinal);
    private double _retroEventRemaining;

    private void BuildRetroHud(Control root)
    {
        // Automation is the persistent left rail. The drawer is still the buy/place/detail surface.
        _automationToggle.AnchorLeft = 0.0f;
        _automationToggle.AnchorRight = 0.0f;
        _automationToggle.AnchorTop = 0.0f;
        _automationToggle.AnchorBottom = 0.0f;
        _automationToggle.OffsetLeft = 14.0f;
        _automationToggle.OffsetTop = 116.0f;
        _automationToggle.OffsetRight = 202.0f;
        _automationToggle.OffsetBottom = 148.0f;
        _automationToggle.Text = "AUTOMATION  [A]";
        _automationToggle.AddThemeFontSizeOverride("font_size", 11);
        ApplyRetroButton(_automationToggle, new Color("#5fd8cf"));

        _retroAutomationRail = new PanelContainer
        {
            AnchorLeft = 0.0f,
            AnchorTop = 0.0f,
            AnchorRight = 0.0f,
            AnchorBottom = 0.0f,
            OffsetLeft = 14.0f,
            OffsetTop = 154.0f,
            OffsetRight = 202.0f,
            OffsetBottom = 432.0f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = _world.Profile.AutomationAvailable,
        };
        _retroAutomationRail.AddThemeStyleboxOverride("panel", RetroHudPanel(new Color("#5fd8cf"), 0.62f));
        root.AddChild(_retroAutomationRail);

        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", 7);
        margin.AddThemeConstantOverride("margin_right", 7);
        margin.AddThemeConstantOverride("margin_top", 7);
        margin.AddThemeConstantOverride("margin_bottom", 7);
        _retroAutomationRail.AddChild(margin);

        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 5);
        margin.AddChild(column);

        _retroAutomationList = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _retroAutomationList.AddThemeConstantOverride("separation", 4);
        column.AddChild(_retroAutomationList);

        AddRetroAutomationEntry("line_miner", "automation_unlock", "DRL", new Color("#66c9e8"));
        AddRetroAutomationEntry("shovel_miner", "shovel_unlock", "SHV", new Color("#e3b35d"));
        AddRetroAutomationEntry("pickaxe_miner", "pickaxe_unlock", "RBK", new Color("#a89ce8"));
        AddRetroAutomationEntry("axe_miner", "axe_unlock", "CUT", new Color("#66d99a"));

        var separator = new ColorRect
        {
            Color = new Color(0.35f, 0.72f, 0.72f, 0.25f),
            CustomMinimumSize = new Vector2(0, 1),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        column.AddChild(separator);

        _retroAutomationRate = new Label
        {
            Text = "AUTO  0 /s",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _retroAutomationRate.AddThemeFontSizeOverride("font_size", 10);
        _retroAutomationRate.AddThemeColorOverride("font_color", new Color("#8fb5bd"));
        column.AddChild(_retroAutomationRate);

        // A thin bottom strip carries world progress and hotkeys without obscuring the cube.
        _retroBottomStrip = new PanelContainer
        {
            AnchorLeft = 0.0f,
            AnchorTop = 1.0f,
            AnchorRight = 1.0f,
            AnchorBottom = 1.0f,
            OffsetLeft = 14.0f,
            OffsetTop = -50.0f,
            OffsetRight = -14.0f,
            OffsetBottom = -12.0f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _retroBottomStrip.AddThemeStyleboxOverride("panel", RetroHudPanel(new Color("#55788a"), 0.58f));
        root.AddChild(_retroBottomStrip);

        var bottomMargin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        bottomMargin.AddThemeConstantOverride("margin_left", 10);
        bottomMargin.AddThemeConstantOverride("margin_right", 10);
        bottomMargin.AddThemeConstantOverride("margin_top", 5);
        bottomMargin.AddThemeConstantOverride("margin_bottom", 5);
        _retroBottomStrip.AddChild(bottomMargin);

        var bottomColumn = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        bottomColumn.AddThemeConstantOverride("separation", 2);
        bottomMargin.AddChild(bottomColumn);

        var bottomRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        bottomRow.AddThemeConstantOverride("separation", 16);
        bottomColumn.AddChild(bottomRow);

        _retroWorldLine = new Label
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _retroWorldLine.AddThemeFontSizeOverride("font_size", 11);
        _retroWorldLine.AddThemeColorOverride("font_color", new Color("#d7e7e7"));
        bottomRow.AddChild(_retroWorldLine);

        _retroEvent = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _retroEvent.AddThemeFontSizeOverride("font_size", 10);
        _retroEvent.AddThemeColorOverride("font_color", new Color("#e5bc6c"));
        bottomRow.AddChild(_retroEvent);

        _retroControls = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Text = _world.Profile.AutomationAvailable
                ? "K  UPGRADES    A  AUTOMATION    H  DETAILS"
                : _world.Profile.SkillTreeAvailable ? "K  UPGRADES    H  DETAILS" : "LMB  MINE",
        };
        _retroControls.AddThemeFontSizeOverride("font_size", 9);
        _retroControls.AddThemeColorOverride("font_color", new Color("#758d9b"));
        bottomRow.AddChild(_retroControls);

        _retroProgress = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 100,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, 3),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _retroProgress.AddThemeStyleboxOverride("background", FlatBar(new Color("#122330")));
        _retroProgress.AddThemeStyleboxOverride("fill", FlatBar(new Color("#5fd8cf")));
        bottomColumn.AddChild(_retroProgress);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_retroEventRemaining <= 0.0 || _retroEvent is null) return;
        _retroEventRemaining = Math.Max(0.0, _retroEventRemaining - delta);
        if (_retroEventRemaining <= 0.0) _retroEvent.Text = string.Empty;
    }

    private void AddRetroAutomationEntry(string minerId, string skillId, string code, Color accent)
    {
        if (_retroAutomationList is null) return;

        var button = new Button
        {
            CustomMinimumSize = new Vector2(0, 48),
            MouseFilter = Control.MouseFilterEnum.Stop,
            Text = string.Empty,
        };
        ApplyRetroButton(button, accent);
        string captured = minerId;
        button.Pressed += () => OpenAutomationMenu(captured);
        _retroAutomationList.AddChild(button);

        var row = new HBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorRight = 1.0f,
            AnchorBottom = 1.0f,
            OffsetLeft = 7,
            OffsetTop = 4,
            OffsetRight = -7,
            OffsetBottom = -4,
        };
        row.AddThemeConstantOverride("separation", 7);
        button.AddChild(row);

        var codeLabel = new Label
        {
            Text = code,
            CustomMinimumSize = new Vector2(34, 0),
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        codeLabel.AddThemeFontSizeOverride("font_size", 12);
        codeLabel.AddThemeColorOverride("font_color", accent);
        row.AddChild(codeLabel);

        var statusColumn = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        statusColumn.AddThemeConstantOverride("separation", -2);
        row.AddChild(statusColumn);

        var status = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        status.AddThemeFontSizeOverride("font_size", 9);
        status.AddThemeColorOverride("font_color", new Color("#9bb0b8"));
        statusColumn.AddChild(status);

        var count = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(42, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        count.AddThemeFontSizeOverride("font_size", 17);
        count.AddThemeColorOverride("font_color", new Color("#eef8f7"));
        row.AddChild(count);

        _retroAutomationEntries[minerId] = new RetroAutomationEntry
        {
            MinerId = minerId,
            SkillId = skillId,
            Code = code,
            Accent = accent,
            Button = button,
            CodeLabel = codeLabel,
            CountLabel = count,
            StatusLabel = status,
        };
    }

    private void RefreshRetroHud()
    {
        if (_retroWorldLine is null) return;

        long total = _mining.TotalMined + _mining.Remaining;
        double percent = total <= 0 ? 100.0 : _mining.TotalMined * 100.0 / total;
        _retroWorldLine.Text = $"{_world.Profile.DisplayName.ToUpperInvariant()}  //  {_mining.Remaining:N0} LEFT  //  {percent:0.0}%";
        if (_retroProgress is not null) _retroProgress.Value = percent;
        if (_retroAutomationRate is not null)
            _retroAutomationRate.Text = $"AUTO OUTPUT   {_miners.BlocksPerSecond:0.##} BLOCKS/s";

        foreach (RetroAutomationEntry entry in _retroAutomationEntries.Values)
        {
            SkillNodeDefinition skill = _skills.Catalog.Get(entry.SkillId);
            bool stageVisible = _world.Profile.IsSkillCategoryVisible(skill.Category);
            entry.Button.Visible = stageVisible;
            if (!stageVisible) continue;

            bool unlocked = _skills.IsMinerUnlocked(entry.MinerId);
            List<MinerInstance> instances = _miners.Miners.Where(miner => miner.DefinitionId == entry.MinerId).ToList();
            int totalCount = instances.Count;
            int running = instances.Count(miner => !miner.Exhausted);
            int attention = instances.Count(miner => miner.Exhausted && miner.StopReason is not MinerStopReason.RangeComplete);
            int completed = Math.Max(0, totalCount - running - attention);

            entry.CountLabel.Text = $"×{totalCount}";
            if (!unlocked)
            {
                entry.StatusLabel.Text = "LOCKED";
                entry.StatusLabel.AddThemeColorOverride("font_color", new Color("#586872"));
                entry.Button.Modulate = new Color(1, 1, 1, 0.52f);
                continue;
            }

            entry.Button.Modulate = Colors.White;
            if (attention > 0)
            {
                entry.StatusLabel.Text = $"RUN {running}  //  STOP {attention}";
                entry.StatusLabel.AddThemeColorOverride("font_color", new Color("#efb45f"));
            }
            else if (running > 0)
            {
                entry.StatusLabel.Text = completed > 0 ? $"RUN {running}  //  DONE {completed}" : $"RUNNING {running}";
                entry.StatusLabel.AddThemeColorOverride("font_color", new Color("#68d9b0"));
            }
            else if (completed > 0)
            {
                entry.StatusLabel.Text = $"DONE {completed}";
                entry.StatusLabel.AddThemeColorOverride("font_color", new Color("#7f9aaa"));
            }
            else
            {
                entry.StatusLabel.Text = "READY";
                entry.StatusLabel.AddThemeColorOverride("font_color", new Color("#8099a6"));
            }
        }
    }

    private void ShowRetroEvent(string message, double duration)
    {
        if (_retroEvent is null || string.IsNullOrWhiteSpace(message)) return;
        _retroEvent.Text = message.ToUpperInvariant();
        _retroEventRemaining = Math.Max(0.35, duration);
    }

    private static void ApplyRetroButton(Button button, Color accent)
    {
        button.AddThemeStyleboxOverride("normal", RetroHudPanel(accent, 0.68f));
        button.AddThemeStyleboxOverride("hover", RetroHudPanel(accent.Lightened(0.10f), 0.86f));
        button.AddThemeStyleboxOverride("pressed", RetroHudPanel(accent.Darkened(0.08f), 0.94f));
        button.AddThemeColorOverride("font_color", new Color("#dcebec"));
        button.AddThemeColorOverride("font_hover_color", Colors.White);
        button.AddThemeColorOverride("font_pressed_color", Colors.White);
    }

    private static StyleBoxFlat RetroHudPanel(Color accent, float opacity)
        => new()
        {
            BgColor = new Color(0.006f, 0.018f, 0.030f, opacity),
            BorderColor = new Color(accent, 0.52f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 1,
            CornerRadiusTopRight = 1,
            CornerRadiusBottomLeft = 1,
            CornerRadiusBottomRight = 1,
        };

    private static StyleBoxFlat FlatBar(Color color)
        => new()
        {
            BgColor = color,
            CornerRadiusTopLeft = 0,
            CornerRadiusTopRight = 0,
            CornerRadiusBottomLeft = 0,
            CornerRadiusBottomRight = 0,
        };
}
