using System;
using Godot;
using TenMillionBlocks.Content;

namespace TenMillionBlocks.UI;

public partial class WorldCompleteView : CanvasLayer
{
    private Control _root = null!;
    private Label _title = null!;
    private Label _stats = null!;
    private Label _next = null!;
    private Button _continue = null!;

    public event Action? ContinueRequested;
    public bool IsOpen => _root is not null && _root.Visible;

    public override void _Ready()
    {
        Layer = 20;
        _root = new Control
        {
            Name = "WorldCompleteOverlay",
            MouseFilter = Control.MouseFilterEnum.Stop,
            Visible = false,
        };
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        var backdrop = new ColorRect
        {
            Color = new Color(0.006f, 0.012f, 0.026f, 0.92f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(backdrop);

        var panel = new PanelContainer
        {
            AnchorLeft = 0.5f,
            AnchorTop = 0.5f,
            AnchorRight = 0.5f,
            AnchorBottom = 0.5f,
            OffsetLeft = -270,
            OffsetTop = -190,
            OffsetRight = 270,
            OffsetBottom = 190,
        };
        _root.AddChild(panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 28);
        margin.AddThemeConstantOverride("margin_top", 26);
        margin.AddThemeConstantOverride("margin_right", 28);
        margin.AddThemeConstantOverride("margin_bottom", 24);
        panel.AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 16);
        margin.AddChild(column);

        _title = new Label { Text = "WORLD CLEARED", HorizontalAlignment = HorizontalAlignment.Center };
        _title.AddThemeFontSizeOverride("font_size", 28);
        column.AddChild(_title);

        _stats = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        column.AddChild(_stats);

        _next = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        column.AddChild(_next);

        _continue = new Button
        {
            Text = "Continue",
            CustomMinimumSize = new Vector2(0, 48),
        };
        _continue.Pressed += () => ContinueRequested?.Invoke();
        column.AddChild(_continue);
    }

    public void ShowCompletion(
        WorldProfile completed,
        WorldProfile? next,
        long blocksMined,
        long resources,
        long manualBlocks,
        long automatedBlocks)
    {
        _title.Text = $"{completed.DisplayName.ToUpperInvariant()} CLEARED";
        _stats.Text =
            $"Blocks mined: {blocksMined:N0}\n" +
            $"Manual: {manualBlocks:N0}   Automation: {automatedBlocks:N0}\n" +
            $"Resources available: {resources:N0}";

        if (next is null)
        {
            _next.Text = "Current test progression complete.";
            _continue.Text = "Close";
        }
        else
        {
            _next.Text = $"Next world: {next.DisplayName}\nThe next world is generated from its own authored profile and seed.";
            _continue.Text = "Continue";
        }

        _root.Visible = true;
    }

    public void HideCompletion() => _root.Visible = false;
}
