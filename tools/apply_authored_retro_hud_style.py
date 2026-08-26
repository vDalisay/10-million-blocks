#!/usr/bin/env python3
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def write(path: str, text: str) -> None:
    (ROOT / path).write_text(text, encoding="utf-8")


def replace_once(path: str, old: str, new: str) -> None:
    text = read(path)
    if old not in text:
        raise RuntimeError(f"anchor not found in {path}: {old[:140]!r}")
    write(path, text.replace(old, new, 1))


# ---------------------------------------------------------------------------
# Main gameplay rails: retain layout, replace generic bordered-card treatment
# with sparse console chrome, stronger instrument copy and segmented progress.
# ---------------------------------------------------------------------------
path = "src/UI/MiningHud.RetroOverlay.cs"
replace_once(path, "    private ProgressBar? _retroProgress;", "    private RetroSegmentBar? _retroProgress;")
replace_once(path, '        _automationToggle.Text = "AUTOMATION  [A]";', '        _automationToggle.Text = "// AUTO BUS   [A]";')
replace_once(
    path,
    '        _retroAutomationRail.AddThemeStyleboxOverride("panel", RetroHudPanel(new Color("#5fd8cf"), 0.62f));\n        root.AddChild(_retroAutomationRail);',
    '        _retroAutomationRail.AddThemeStyleboxOverride("panel", RetroHudChrome.Glass(new Color("#5fd8cf"), 0.76f));\n        root.AddChild(_retroAutomationRail);\n        RetroHudChrome.Attach(_retroAutomationRail, new Color("#5fd8cf"), scanlines: true);')
replace_once(
    path,
    '        _retroAutomationRate = new Label\n        {\n            Text = "AUTO  0 /s",\n            MouseFilter = Control.MouseFilterEnum.Ignore,\n        };',
    '        _retroAutomationRate = new Label\n        {\n            Text = "BUS RATE  0.00 blk/s",\n            MouseFilter = Control.MouseFilterEnum.Ignore,\n        };')
replace_once(
    path,
    '        _retroBottomStrip.AddThemeStyleboxOverride("panel", RetroHudPanel(new Color("#55788a"), 0.58f));\n        root.AddChild(_retroBottomStrip);',
    '        _retroBottomStrip.AddThemeStyleboxOverride("panel", RetroHudChrome.Glass(new Color("#55788a"), 0.72f));\n        root.AddChild(_retroBottomStrip);\n        RetroHudChrome.Attach(_retroBottomStrip, new Color("#55788a"), dense: true, scanlines: false);')
replace_once(
    path,
    '            Text = _world.Profile.AutomationAvailable\n                ? "K  UPGRADES    A  AUTOMATION    H  DETAILS"\n                : _world.Profile.SkillTreeAvailable ? "K  UPGRADES    H  DETAILS" : "LMB  MINE",',
    '            Text = _world.Profile.AutomationAvailable\n                ? "[K] GRID    [A] AUTO    [H] DIAG"\n                : _world.Profile.SkillTreeAvailable ? "[K] GRID    [H] DIAG" : "[LMB] MINE",')
replace_once(
    path,
    '''        _retroProgress = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 100,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, 3),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _retroProgress.AddThemeStyleboxOverride("background", FlatBar(new Color("#122330")));
        _retroProgress.AddThemeStyleboxOverride("fill", FlatBar(new Color("#5fd8cf")));
        bottomColumn.AddChild(_retroProgress);''',
    '''        _retroProgress = new RetroSegmentBar
        {
            MinValue = 0,
            MaxValue = 100,
            SegmentCount = 64,
            Accent = new Color("#5fd8cf"),
            CustomMinimumSize = new Vector2(0, 4),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        bottomColumn.AddChild(_retroProgress);''')
replace_once(
    path,
    '        ApplyRetroButton(button, accent);\n        string captured = minerId;',
    '        ApplyRetroButton(button, accent);\n        RetroHudChrome.Attach(button, accent, dense: true, scanlines: false);\n        string captured = minerId;')
replace_once(path, '            entry.CountLabel.Text = $"×{totalCount}";', '            entry.CountLabel.Text = $"x{totalCount:00}";')
replace_once(path, '                entry.StatusLabel.Text = "LOCKED";', '                entry.StatusLabel.Text = "-- LOCKED";')
replace_once(path, '                entry.StatusLabel.Text = $"RUN {running}  //  STOP {attention}";', '                entry.StatusLabel.Text = $"FAULT {attention:00} / RUN {running:00}";')
replace_once(path, '                entry.StatusLabel.Text = completed > 0 ? $"RUN {running}  //  DONE {completed}" : $"RUNNING {running}";', '                entry.StatusLabel.Text = completed > 0 ? $"LIVE {running:00} / DONE {completed:00}" : $"LIVE {running:00}";')
replace_once(path, '                entry.StatusLabel.Text = $"DONE {completed}";', '                entry.StatusLabel.Text = $"IDLE / DONE {completed:00}";')
replace_once(path, '                entry.StatusLabel.Text = "READY";', '                entry.StatusLabel.Text = "-- READY";')
replace_once(
    path,
    '        _retroWorldLine.Text = $"{_world.Profile.DisplayName.ToUpperInvariant()}  //  {_mining.Remaining:N0} LEFT  //  {percent:0.0}%";',
    '        _retroWorldLine.Text = $"SECTOR::{_world.Profile.DisplayName.ToUpperInvariant()}   REM {_mining.Remaining:N0}   CLR {percent:0.0}%";')
replace_once(
    path,
    '            _retroAutomationRate.Text = $"AUTO OUTPUT   {_miners.BlocksPerSecond:0.##} BLOCKS/s";',
    '            _retroAutomationRate.Text = $"BUS RATE   {_miners.BlocksPerSecond:0.##} blk/s";')
replace_once(
    path,
    '''    private static void ApplyRetroButton(Button button, Color accent)
    {
        button.AddThemeStyleboxOverride("normal", RetroHudPanel(accent, 0.68f));
        button.AddThemeStyleboxOverride("hover", RetroHudPanel(accent.Lightened(0.10f), 0.86f));
        button.AddThemeStyleboxOverride("pressed", RetroHudPanel(accent.Darkened(0.08f), 0.94f));
        button.AddThemeColorOverride("font_color", new Color("#dcebec"));
        button.AddThemeColorOverride("font_hover_color", Colors.White);
        button.AddThemeColorOverride("font_pressed_color", Colors.White);
    }''',
    '''    private static void ApplyRetroButton(Button button, Color accent)
    {
        RetroHudChrome.SkinButton(button, accent);
    }''')
# Header button gets its own bracket overlay after it is styled.
replace_once(
    path,
    '        ApplyRetroButton(_automationToggle, new Color("#5fd8cf"));',
    '        ApplyRetroButton(_automationToggle, new Color("#5fd8cf"));\n        RetroHudChrome.Attach(_automationToggle, new Color("#5fd8cf"), dense: true, scanlines: false);')

# ---------------------------------------------------------------------------
# Mined counter + resource ledger: make every module a readout/instrument instead
# of a card. Resource codes are deliberately terse and repeated across the HUD.
# ---------------------------------------------------------------------------
path = "src/UI/IncrementalFeedbackView.cs"
replace_once(path, '            Text = "RESOURCE LEDGER",', '            Text = "// STORAGE BUS  03",')
replace_once(
    path,
    '        panel.AddThemeStyleboxOverride("panel", RetroPanel(accent, primary ? 0.78f : 0.72f));',
    '        panel.AddThemeStyleboxOverride("panel", RetroHudChrome.Glass(accent, primary ? 0.90f : 0.82f));\n        RetroHudChrome.Attach(panel, accent, dense: !primary, scanlines: primary);')
replace_once(path, '        valueLabel.AddThemeConstantOverride("outline_size", 3);', '        valueLabel.AddThemeConstantOverride("outline_size", 1);')
replace_once(
    path,
    '        panel.AddThemeStyleboxOverride("panel", RetroPanel(accent, 0.68f));',
    '        panel.AddThemeStyleboxOverride("panel", RetroHudChrome.Glass(accent, 0.82f));\n        RetroHudChrome.Attach(panel, accent, dense: true, scanlines: false);')
replace_once(
    path,
    '''        var caption = new Label
        {
            Text = definition.DisplayName.ToUpperInvariant(),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };''',
    '''        string code = resourceId switch
        {
            "gem_red" => "CRG",
            "gem_blue" => "AZG",
            "gem_green" => "VDG",
            _ => "RSC",
        };
        var caption = new Label
        {
            Text = $"{code} // {definition.DisplayName.ToUpperInvariant()}",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };''')
replace_once(
    path,
    '        _blocksChip.Caption.Text = $"BLOCKS MINED  //  {percent:0.0}% OF {IncrementalNumberFormatter.Format(_world.InitialMineableBlocks)}";',
    '        _blocksChip.Caption.Text = $"MINE CORE  //  {percent:0.0}% OF {IncrementalNumberFormatter.Format(_world.InitialMineableBlocks)}";')

# ---------------------------------------------------------------------------
# Automation fault module follows the same authored chrome.
# ---------------------------------------------------------------------------
path = "src/UI/AutomationAttentionView.cs"
start = '''        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
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
        });
        AddChild(_panel);'''
replace_once(
    path,
    start,
    '''        _panel.AddThemeStyleboxOverride("panel", RetroHudChrome.Glass(new Color("#e5a34d"), 0.86f));
        AddChild(_panel);
        RetroHudChrome.Attach(_panel, new Color("#e5a34d"), dense: true, scanlines: false);''')
replace_once(path, '            Text = "ATTENTION",', '            Text = "!! AUTO FAULT",')
replace_once(
    path,
    '        _button.AddThemeColorOverride("font_hover_color", new Color("#ffe0aa"));',
    '        _button.AddThemeColorOverride("font_hover_color", new Color("#ffe0aa"));\n        RetroHudChrome.SkinButton(_button, new Color("#e5a34d"));')

# ---------------------------------------------------------------------------
# Debug A/B harness is visible in development screenshots, so it must not remain
# a stock Godot rectangle/button cluster while evaluating the shipping HUD.
# ---------------------------------------------------------------------------
path = "src/Presentation/ReferenceVisualHarness.cs"
replace_once(
    path,
    '        canvas.AddChild(panel);',
    '        panel.AddThemeStyleboxOverride("panel", RetroHudChrome.Glass(new Color("#697d91"), 0.88f));\n        canvas.AddChild(panel);\n        RetroHudChrome.Attach(panel, new Color("#697d91"), dense: true, scanlines: true);')
replace_once(path, '            Text = $"Camera: Medium · Look: {VisualLookProfiles.Shipping}",', '            Text = $"VIS// CAM Medium  LOOK {VisualLookProfiles.Shipping}",')
replace_once(path, '        var recenter = new Button { Text = "Center [F]" };', '        var recenter = new Button { Text = "CTR [F]" };\n        RetroHudChrome.SkinButton(recenter, new Color("#70879a"));')
replace_once(path, '            Text = "A/B:",', '            Text = "LOOK//",')
replace_once(
    path,
    '''        var capture = new Button
        {
            Text = "Capture [F6]",
            TooltipText = "Save a PNG under user://reference_captures with world/version/camera/look metadata.",
        };''',
    '''        var capture = new Button
        {
            Text = "CAP [F6]",
            TooltipText = "Save a PNG under user://reference_captures with world/version/camera/look metadata.",
        };
        RetroHudChrome.SkinButton(capture, new Color("#8c789e"));''')
replace_once(
    path,
    '            _status.Text = $"Camera: {_camera.ActivePresetName} · Look: {_visualPreset}";',
    '            _status.Text = $"VIS// CAM {_camera.ActivePresetName}  LOOK {_visualPreset}";')
replace_once(
    path,
    '''        var button = new Button { Text = text };
        button.Pressed += () => _camera.ApplyPreset(preset);''',
    '''        var button = new Button { Text = text };
        RetroHudChrome.SkinButton(button, new Color("#70879a"));
        button.Pressed += () => _camera.ApplyPreset(preset);''')
replace_once(
    path,
    '''        var button = new Button { Text = text };
        button.Pressed += () => ApplyVisualPreset(preset);''',
    '''        var button = new Button { Text = text };
        RetroHudChrome.SkinButton(button, new Color("#8c789e"));
        button.Pressed += () => ApplyVisualPreset(preset);''')

# ---------------------------------------------------------------------------
# Update design note to explicitly reject the generic-card failure mode.
# ---------------------------------------------------------------------------
path = "docs/RETRO_FUTURISTIC_HUD.md"
text = read(path)
needle = "## Visual language\n"
addition = """## Anti-generic rule\n\nA dark rectangle with a one-pixel neon outline is not, by itself, the visual identity. Shipping gameplay HUD modules use broken corner brackets, registration ticks, scan-lines, asymmetric accent rails and segmented progress. Avoid full four-sided borders on every module, rounded SaaS-style cards, large soft shadows, and decorative gradients. The UI should read as a compact mining instrument panel assembled from hardware modules.\n\n"""
if addition not in text:
    if needle not in text:
        raise RuntimeError("visual language heading missing")
    text = text.replace(needle, addition + needle, 1)
    write(path, text)

print("authored retro HUD styling applied")
