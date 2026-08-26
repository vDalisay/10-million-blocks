#!/usr/bin/env python3
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def patch(path: str, old: str, new: str) -> None:
    file = ROOT / path
    text = file.read_text(encoding="utf-8")
    if old not in text:
        raise RuntimeError(f"anchor not found in {path}: {old[:140]!r}")
    file.write_text(text.replace(old, new, 1), encoding="utf-8")


# Full automation drawer follows the new left activity rail rather than covering the right resource ledger.
patch(
    "src/UI/MiningHud.cs",
    '''        _automationDrawer = new PanelContainer
        {
            AnchorLeft = 1.0f,
            AnchorRight = 1.0f,
            AnchorTop = 0.0f,
            AnchorBottom = 1.0f,
            OffsetLeft = -AutomationDrawerWidth,
            OffsetTop = 72.0f,
            OffsetRight = -16.0f,
            OffsetBottom = -16.0f,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        root.AddChild(_automationDrawer);''',
    '''        _automationDrawer = new PanelContainer
        {
            AnchorLeft = 0.0f,
            AnchorRight = 0.0f,
            AnchorTop = 0.0f,
            AnchorBottom = 1.0f,
            OffsetLeft = -AutomationDrawerWidth,
            OffsetTop = 116.0f,
            OffsetRight = 0.0f,
            OffsetBottom = -58.0f,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _automationDrawer.AddThemeStyleboxOverride("panel", RetroHudPanel(new Color("#5fd8cf"), 0.94f));
        root.AddChild(_automationDrawer);''')

patch(
    "src/UI/MiningHud.cs",
    '''        var close = new Button { Text = "Close", CustomMinimumSize = new Vector2(64.0f, 32.0f) };
        close.Pressed += CloseAutomationMenu;
        header.AddChild(close);''',
    '''        var close = new Button { Text = "CLOSE", CustomMinimumSize = new Vector2(64.0f, 32.0f) };
        ApplyRetroButton(close, new Color("#5fd8cf"));
        close.Pressed += CloseAutomationMenu;
        header.AddChild(close);''')

patch(
    "src/UI/MiningHud.cs",
    '''        var card = new PanelContainer
        {
            CustomMinimumSize = new Vector2(0.0f, 132.0f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        list.AddChild(card);''',
    '''        var card = new PanelContainer
        {
            CustomMinimumSize = new Vector2(0.0f, 124.0f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        card.AddThemeStyleboxOverride("panel", RetroHudPanel(new Color("#55788a"), 0.72f));
        list.AddChild(card);''')

patch(
    "src/UI/MiningHud.cs",
    '''        var action = new Button { CustomMinimumSize = new Vector2(0.0f, 34.0f) };
        string id = minerId;
        action.Pressed += () => OnAutomationAction(id);''',
    '''        var action = new Button { CustomMinimumSize = new Vector2(0.0f, 34.0f) };
        ApplyRetroButton(action, new Color("#5fd8cf"));
        string id = minerId;
        action.Pressed += () => OnAutomationAction(id);''')

patch(
    "src/UI/MiningHud.cs",
    '''        float targetLeft = open ? -AutomationDrawerWidth : 0.0f;
        float targetRight = open ? -16.0f : AutomationDrawerWidth;''',
    '''        float targetLeft = open ? 14.0f : -AutomationDrawerWidth;
        float targetRight = open ? 14.0f + AutomationDrawerWidth : 0.0f;''')

# Special-resource presentation follows the same collection timing as ordinary resource pickups.
patch(
    "src/UI/IncrementalFeedbackView.cs",
    '''        // Special-resource inventory remains authoritative/direct, so its own chip still celebrates at
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
        }''',
    '''        // The inventory itself remains authoritative/direct, but presentation now waits for the same
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
        }''')

patch(
    "src/UI/IncrementalFeedbackView.cs",
    '''        QueuePickup(
            collected.BlockId,
            destination,
            Math.Max(1L, collected.BlocksRemoved),
            Math.Max(0L, collected.Amount),
            collected.ScreenPosition,
            hasSource: true,
            special: false);
    }''',
    '''        QueuePickup(
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
    }''')

# Keep the debug-only art comparison controls away from the shipping HUD zones during local testing.
patch(
    "src/Presentation/ReferenceVisualHarness.cs",
    '''        var panel = new PanelContainer
        {
            OffsetLeft = 16.0f,
            OffsetTop = 16.0f,
            OffsetRight = 810.0f,
            OffsetBottom = 108.0f,
            TooltipText = "Reference A/B harness. Camera [1-3], look [4-8], capture [F6]. RMB orbit, MMB pan, wheel zoom.",
        };''',
    '''        var panel = new PanelContainer
        {
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            OffsetLeft = -380.0f,
            OffsetTop = 14.0f,
            OffsetRight = 380.0f,
            OffsetBottom = 106.0f,
            TooltipText = "Reference A/B harness. Camera [1-3], look [4-8], capture [F6]. RMB orbit, MMB pan, wheel zoom.",
        };''')

print("Final retro HUD integration applied.")
