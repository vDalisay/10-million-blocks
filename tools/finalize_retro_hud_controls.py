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
        raise RuntimeError(f"anchor not found in {path}: {old[:160]!r}")
    write(path, text.replace(old, new, 1))


# Hover mining lives outside MiningHud, so explicitly bring it into the same authored console skin.
path = "src/Mining/ManualMiningController.cs"
replace_once(
    path,
    "using TenMillionBlocks.Skills;",
    "using TenMillionBlocks.Skills;\nusing TenMillionBlocks.UI;")
replace_once(
    path,
    '''        _hoverToggle.Pressed += () => SetHoverMiningEnabled(!HoverMiningEnabled);
        root.AddChild(_hoverToggle);''',
    '''        _hoverToggle.Pressed += () => SetHoverMiningEnabled(!HoverMiningEnabled);
        root.AddChild(_hoverToggle);
        RetroHudChrome.SkinButton(_hoverToggle, new Color("#63d8cb"));
        RetroHudChrome.Attach(_hoverToggle, new Color("#63d8cb"), dense: true, scanlines: true);''')
replace_once(
    path,
    '''        _hoverToggle.Visible = unlocked;
        _hoverToggle.Text = $"HOVER MINING: {(HoverMiningEnabled ? "ON" : "OFF")}";
        _hoverToggle.TooltipText = unlocked''',
    '''        _hoverToggle.Visible = unlocked;
        _hoverToggle.Text = HoverMiningEnabled ? "HVR// ACTIVE   [CLICK: DISARM]" : "HVR// STANDBY  [CLICK: ARM]";
        _hoverToggle.AddThemeColorOverride(
            "font_color",
            HoverMiningEnabled ? new Color("#dffcf6") : new Color("#78939a"));
        _hoverToggle.TooltipText = unlocked''')

# The full automation drawer should feel like the same hardware console, not a conventional modal.
path = "src/UI/MiningHud.cs"
replace_once(
    path,
    '''        _automationDrawer.AddThemeStyleboxOverride("panel", RetroHudPanel(new Color("#5fd8cf"), 0.94f));
        root.AddChild(_automationDrawer);''',
    '''        _automationDrawer.AddThemeStyleboxOverride("panel", RetroHudChrome.Glass(new Color("#5fd8cf"), 0.96f));
        root.AddChild(_automationDrawer);
        RetroHudChrome.Attach(_automationDrawer, new Color("#5fd8cf"), scanlines: true);''')
replace_once(
    path,
    '''        var title = new Label { Text = "AUTOMATION", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        title.AddThemeFontSizeOverride("font_size", 22);''',
    '''        var title = new Label { Text = "AUTO// DEPLOYMENT BUS", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        title.AddThemeFontSizeOverride("font_size", 15);
        title.AddThemeColorOverride("font_color", new Color("#8ee9dc"));''')
replace_once(
    path,
    '''        var close = new Button { Text = "CLOSE", CustomMinimumSize = new Vector2(64.0f, 32.0f) };
        ApplyRetroButton(close, new Color("#5fd8cf"));''',
    '''        var close = new Button { Text = "X // BACK", CustomMinimumSize = new Vector2(82.0f, 32.0f) };
        ApplyRetroButton(close, new Color("#5fd8cf"));
        RetroHudChrome.Attach(close, new Color("#5fd8cf"), dense: true, scanlines: false);''')
replace_once(
    path,
    '''        AddAutomationEntry(list, "line_miner", "automation_unlock", "DRILL",
            "Straight-line miner. Unlock the class in the skill tree, then buy each physical Drill for its fixed unit price in the current world.");
        AddAutomationEntry(list, "shovel_miner", "shovel_unlock", "POWERED SHOVEL",
            "Surface crawler for soft terrain. Every physical Shovel is bought for the same fixed unit price and belongs to this world.");
        AddAutomationEntry(list, "pickaxe_miner", "pickaxe_unlock", "ROCK BREAKER",
            "Stone and ore miner. Permanent capability unlock; fixed-price physical units per world.");
        AddAutomationEntry(list, "axe_miner", "axe_unlock", "FOREST CUTTER",
            "Tree-clearing surface tool. Permanent capability unlock; fixed-price physical units per world.");''',
    '''        AddAutomationEntry(list, "line_miner", "automation_unlock", "DRL // DRILL",
            "Straight-line miner. Unlock the class in the skill tree, then buy each physical Drill for its fixed unit price in the current world.");
        AddAutomationEntry(list, "shovel_miner", "shovel_unlock", "SHV // POWERED SHOVEL",
            "Surface crawler for soft terrain. Every physical Shovel is bought for the same fixed unit price and belongs to this world.");
        AddAutomationEntry(list, "pickaxe_miner", "pickaxe_unlock", "RBK // ROCK BREAKER",
            "Stone and ore miner. Permanent capability unlock; fixed-price physical units per world.");
        AddAutomationEntry(list, "axe_miner", "axe_unlock", "CUT // FOREST CUTTER",
            "Tree-clearing surface tool. Permanent capability unlock; fixed-price physical units per world.");''')
replace_once(
    path,
    '''        card.AddThemeStyleboxOverride("panel", RetroHudPanel(new Color("#55788a"), 0.72f));
        list.AddChild(card);''',
    '''        card.AddThemeStyleboxOverride("panel", RetroHudChrome.Glass(new Color("#55788a"), 0.82f));
        list.AddChild(card);
        RetroHudChrome.Attach(card, new Color("#55788a"), dense: true, scanlines: false);''')
replace_once(
    path,
    '''        var name = new Label { Text = displayName };
        name.AddThemeFontSizeOverride("font_size", 18);''',
    '''        var name = new Label { Text = displayName };
        name.AddThemeFontSizeOverride("font_size", 14);
        name.AddThemeColorOverride("font_color", new Color("#b9e5e1"));''')
replace_once(
    path,
    '''        var action = new Button { CustomMinimumSize = new Vector2(0.0f, 34.0f) };
        ApplyRetroButton(action, new Color("#5fd8cf"));''',
    '''        var action = new Button { CustomMinimumSize = new Vector2(0.0f, 34.0f) };
        ApplyRetroButton(action, new Color("#5fd8cf"));
        RetroHudChrome.Attach(action, new Color("#5fd8cf"), dense: true, scanlines: false);''')
replace_once(
    path,
    '''        _automationOpen = open;
        _manual.InputEnabled = !open;
        _automationToggle.Text = open ? "CLOSE AUTOMATION" : "AUTOMATION [A]";''',
    '''        _automationOpen = open;
        _manual.InputEnabled = !open;
        _automationToggle.Text = open ? "// AUTO BUS   CLOSE [A]" : "// AUTO BUS   [A]";''')

# Clean up the temporary locator now that the escaped control has been identified.
locator = ROOT / ".github/workflows/locate_hover_ui.yml"
if locator.exists():
    locator.unlink()

print("final retro HUD controls styled")
