using System;
using Godot;

namespace TenMillionBlocks.UI;

public partial class PauseMenuView
{
    private Button? _worldsButton;

    public event Action? WorldsRequested;

    public void EnableWorldBrowserEntry()
    {
        if (_worldsButton is not null && GodotObject.IsInstanceValid(_worldsButton)) return;
        Callable.From(InstallWorldsButton).CallDeferred();
    }

    private void InstallWorldsButton()
    {
        if (_mainPanel is null || !GodotObject.IsInstanceValid(_mainPanel)) return;
        if (_worldsButton is not null && GodotObject.IsInstanceValid(_worldsButton)) return;

        VBoxContainer? column = FindFirstVBox(_mainPanel);
        if (column is null) return;

        _worldsButton = new Button
        {
            Text = "WORLDS",
            CustomMinimumSize = new Vector2(390.0f, 44.0f),
            TooltipText = "Revisit unlocked worlds or watch completed-world replays.",
        };
        _worldsButton.Pressed += () => WorldsRequested?.Invoke();
        column.AddChild(_worldsButton);

        // Main panel order is title, resume, settings, save/return, hint. Put Worlds directly after Resume.
        column.MoveChild(_worldsButton, Math.Min(2, column.GetChildCount() - 1));
    }

    private static VBoxContainer? FindFirstVBox(Node root)
    {
        if (root is VBoxContainer box) return box;
        foreach (Node child in root.GetChildren())
        {
            VBoxContainer? found = FindFirstVBox(child);
            if (found is not null) return found;
        }
        return null;
    }
}
