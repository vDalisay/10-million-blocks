#!/usr/bin/env python3
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def write(path: str, text: str) -> None:
    p = ROOT / path
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(text, encoding="utf-8")


def replace_once(path: str, old: str, new: str) -> None:
    text = read(path)
    if old not in text:
        raise RuntimeError(f"anchor not found in {path}: {old[:120]!r}")
    write(path, text.replace(old, new, 1))


def replace_method(path: str, signature: str, next_signature: str, replacement: str) -> None:
    text = read(path)
    start = text.find(signature)
    if start < 0:
        raise RuntimeError(f"method signature not found in {path}: {signature}")
    end = text.find(next_signature, start)
    if end < 0:
        raise RuntimeError(f"next method signature not found in {path}: {next_signature}")
    write(path, text[:start] + replacement.rstrip() + "\n\n" + text[end:])


# ---------------------------------------------------------------------------
# IncrementalFeedbackView: move the primary mined counter to the upper-left
# and build a vertical resource ledger on the right. Presentation flights now
# terminate at the relevant resource bucket rather than a generic top-center bar.
# ---------------------------------------------------------------------------
path = "src/UI/IncrementalFeedbackView.cs"
text = read(path)
text = text.replace("    private HBoxContainer _counterBar = null!;\n    private HBoxContainer _specialRow = null!;",
                    "    private Control _counterBar = null!;\n    private VBoxContainer _resourceRail = null!;\n    private VBoxContainer _specialRow = null!;")
write(path, text)

replace_method(
    path,
    "    private void BuildUi()",
    "    private CounterChip BuildCounterChip",
    r'''    private void BuildUi()
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
    }''')

replace_method(
    path,
    "    private CounterChip BuildCounterChip",
    "    private CounterChip EnsureSpecialChip",
    r'''    private CounterChip BuildCounterChip(string caption, string value, float width)
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
    }''')

replace_method(
    path,
    "    private CounterChip EnsureSpecialChip",
    "    private static VBoxContainer? FindFirstVBox",
    r'''    private CounterChip EnsureSpecialChip(string resourceId)
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
    }''')

# Counter formatting: one dominant number, contextual progress in the caption.
replace_once(
    path,
    '''        _blocksChip.Value.Text =
            $"{IncrementalNumberFormatter.Format(_mining.TotalMined)} / {IncrementalNumberFormatter.Format(_world.InitialMineableBlocks)}";
        _resourcesChip.Value.Text = IncrementalNumberFormatter.Format(_mining.Currency);''',
    '''        double percent = _world.InitialMineableBlocks <= 0
            ? 100.0
            : Math.Clamp(_mining.TotalMined * 100.0 / _world.InitialMineableBlocks, 0.0, 100.0);
        _blocksChip.Caption.Text = $"BLOCKS MINED  //  {percent:0.0}% OF {IncrementalNumberFormatter.Format(_world.InitialMineableBlocks)}";
        _blocksChip.Value.Text = IncrementalNumberFormatter.Format(_mining.TotalMined);
        _resourcesChip.Value.Text = IncrementalNumberFormatter.Format(_mining.Currency);''')

# Ordinary collected value goes to the resource bucket; zero-value blocks (water) still fly to the
# mined counter. The mined counter pulses either way, so the count remains visually acknowledged.
replace_once(
    path,
    '''        QueuePickup(
            collected.BlockId,
            _blocksChip.Root,
            Math.Max(1L, collected.BlocksRemoved),
            Math.Max(0L, collected.Amount),
            collected.ScreenPosition,
            hasSource: true,
            special: false);''',
    '''        Control destination = collected.Amount > 0 ? _resourcesChip.Root : _blocksChip.Root;
        QueuePickup(
            collected.BlockId,
            destination,
            Math.Max(1L, collected.BlocksRemoved),
            Math.Max(0L, collected.Amount),
            collected.ScreenPosition,
            hasSource: true,
            special: false);''')

replace_once(
    path,
    '''                QueuePickup(
                    result.BlockId,
                    _blocksChip.Root,
                    result.BlocksRemoved,
                    result.Reward,
                    source,
                    hasSource,
                    special: false);''',
    '''                Control destination = result.Reward > 0 ? _resourcesChip.Root : _blocksChip.Root;
                QueuePickup(
                    result.BlockId,
                    destination,
                    result.BlocksRemoved,
                    result.Reward,
                    source,
                    hasSource,
                    special: false);''')

# ---------------------------------------------------------------------------
# MiningHud: retire the large bottom status box and add an always-visible automation activity rail +
# one-line world progress strip. The existing Automation drawer remains the interaction/shop surface.
# ---------------------------------------------------------------------------
path = "src/UI/MiningHud.cs"
replace_once(path, "using System.Collections.Generic;\n", "using System.Collections.Generic;\nusing System.Linq;\n")
replace_once(
    path,
    '''        _panel = new PanelContainer
        {
            AnchorTop = 1.0f,
            AnchorBottom = 1.0f,
            OffsetLeft = 16.0f,
            OffsetTop = -94.0f,
            OffsetRight = 690.0f,
            OffsetBottom = -16.0f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };''',
    '''        _panel = new PanelContainer
        {
            AnchorTop = 1.0f,
            AnchorBottom = 1.0f,
            OffsetLeft = 16.0f,
            OffsetTop = -220.0f,
            OffsetRight = 690.0f,
            OffsetBottom = -54.0f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };''')

# Build the compact persistent rail after the existing automation button exists, then repurpose that
# button as the rail header action rather than keeping a large top-right button.
replace_once(
    path,
    '''        _automationToggle.Pressed += ToggleAutomationMenu;
        _automationToggle.Visible = _world.Profile.AutomationAvailable;
        root.AddChild(_automationToggle);

        _placementHint = new Label''',
    '''        _automationToggle.Pressed += ToggleAutomationMenu;
        _automationToggle.Visible = _world.Profile.AutomationAvailable;
        root.AddChild(_automationToggle);
        BuildRetroHud(root);

        _placementHint = new Label''')

# H details still exists, but the legacy panel is only shown on demand.
replace_once(
    path,
    '''        _detailsVisible = !_detailsVisible;
        _details.Visible = _detailsVisible;
        _panel.OffsetTop = _detailsVisible ? -222.0f : -94.0f;
        if (_detailsVisible) RefreshDetails();''',
    '''        _detailsVisible = !_detailsVisible;
        _details.Visible = _detailsVisible;
        _panel.Visible = _detailsVisible;
        if (_detailsVisible) RefreshDetails();''')

replace_once(
    path,
    '''        // The expensive four-card prerequisite/cost refresh is irrelevant while its drawer is hidden.
        // Opening the drawer refreshes it immediately, and while open it tracks the same coalesced tick.
        if (_automationOpen) RefreshAutomationMenu();''',
    '''        RefreshRetroHud();

        // The expensive four-card prerequisite/cost refresh is irrelevant while its drawer is hidden.
        // Opening the drawer refreshes it immediately, and while open it tracks the same coalesced tick.
        if (_automationOpen) RefreshAutomationMenu();''')

# Mirror transient feedback into the new one-line ticker.
replace_once(
    path,
    '''        _feedback.Text = message;
        _feedback.Visible = true;
        _feedbackTime = duration;''',
    '''        _feedback.Text = message;
        _feedback.Visible = true;
        _feedbackTime = duration;
        ShowRetroEvent(message, duration);''')

retro_partial = r'''using System;
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
'''
write("src/UI/MiningHud.RetroOverlay.cs", retro_partial)

# ---------------------------------------------------------------------------
# Automation attention: shrink the old large floating warning into a compact left-side action beneath
# the activity rail. It remains clickable/focusable but no longer blocks a large portion of the cube.
# ---------------------------------------------------------------------------
path = "src/UI/AutomationAttentionView.cs"
replace_once(
    path,
    '''        _panel = new PanelContainer
        {
            AnchorLeft = 1.0f,
            AnchorRight = 1.0f,
            OffsetLeft = -420.0f,
            OffsetTop = 106.0f,
            OffsetRight = -16.0f,
            OffsetBottom = 188.0f,
            Visible = false,
        };''',
    '''        _panel = new PanelContainer
        {
            AnchorLeft = 0.0f,
            AnchorRight = 0.0f,
            OffsetLeft = 14.0f,
            OffsetTop = 440.0f,
            OffsetRight = 202.0f,
            OffsetBottom = 492.0f,
            Visible = false,
        };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.022f, 0.016f, 0.008f, 0.78f),
            BorderColor = new Color(0.92f, 0.58f, 0.26f, 0.72f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 1,
            CornerRadiusTopRight = 1,
            CornerRadiusBottomLeft = 1,
            CornerRadiusBottomRight = 1,
        });''')
replace_once(
    path,
    '''        _button = new Button
        {
            Text = "Automation needs attention",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };''',
    '''        _button = new Button
        {
            Text = "ATTENTION",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _button.AddThemeFontSizeOverride("font_size", 9);
        _button.AddThemeColorOverride("font_color", new Color("#f3c17a"));
        _button.AddThemeColorOverride("font_hover_color", new Color("#ffe0aa"));''')
replace_once(
    path,
    '''        string detail = selected is null
            ? "automation stopped"
            : $"{selected.DefinitionId}: {_miners.DescribeStop(selected)}";
        _button.Text = count == 1
            ? $"AUTOMATION STOPPED\n{detail}  ·  Click to focus/select"
            : $"{count} AUTOMATIONS NEED ATTENTION\n{detail}  ·  Click to cycle/focus";''',
    '''        string code = selected?.DefinitionId switch
        {
            "line_miner" => "DRL",
            "shovel_miner" => "SHV",
            "pickaxe_miner" => "RBK",
            "axe_miner" => "CUT",
            _ => "AUTO",
        };
        _button.Text = count == 1
            ? $"ATTENTION  //  {code} STOPPED  //  CLICK TO FOCUS"
            : $"ATTENTION  {count}  //  {code} + OTHERS  //  CLICK TO CYCLE";''')

# Add a status note to implementation docs.
status_path = ROOT / "docs/IMPLEMENTATION_STATUS.md"
status = status_path.read_text(encoding="utf-8")
marker = "## Retro-futuristic gameplay HUD pass"
if marker not in status:
    status += "\n\n" + marker + "\n\n"
    status += (
        "- Replaced the top-center counter cluster and large bottom status box with a stronger incremental-game hierarchy: primary mined total upper-left, automation activity rail left, resource ledger right and one thin world-progress strip along the bottom.\n"
        "- The left automation rail shows each staged automation class, world-local unit count and running/stopped/done state while keeping the full buy/place drawer behind `[A]`.\n"
        "- Ordinary collected block feedback now flies to the ordinary resource bucket when it has resource value; zero-value water still flies to the mined counter, and special gems retain their own colored resource buckets.\n"
        "- The three gem buckets remain visible at zero so future special resources read as part of the persistent economy rather than appearing as surprise top-bar panels.\n"
        "- The large automation-attention overlay is reduced to a compact left-side focus/cycle control beneath the automation rail.\n"
    )
    status_path.write_text(status, encoding="utf-8")

print("Applied retro-futuristic HUD overhaul.")
