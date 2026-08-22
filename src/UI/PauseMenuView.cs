using System;
using Godot;
using TenMillionBlocks.Presentation;

namespace TenMillionBlocks.UI;

/// <summary>
/// Lightweight in-game pause/settings overlay. The overlay itself always processes so it can resume
/// the game while SceneTree.Paused is true; the world, automation, camera and event simulation remain
/// frozen underneath it. Presentation preferences reuse the persistent runtime shared with the main menu.
/// </summary>
public partial class PauseMenuView : CanvasLayer
{
    private GraphicsSettingsRuntime _graphics = null!;
    private Func<bool>? _canOpen;
    private Control _root = null!;
    private Control _mainPanel = null!;
    private Control _settingsPanel = null!;
    private Label _status = null!;

    public event Action? ReturnToMainMenuRequested;

    public bool IsOpen => _root is not null && _root.Visible;

    public void Initialize(GraphicsSettingsRuntime graphics, Func<bool>? canOpen = null)
    {
        _graphics = graphics ?? throw new ArgumentNullException(nameof(graphics));
        _canOpen = canOpen;
    }

    public override void _Ready()
    {
        Layer = 90;
        ProcessMode = ProcessModeEnum.Always;
        BuildUi();
        SetOpen(false);
    }

    public override void _ExitTree()
    {
        if (GetTree() is SceneTree tree)
        {
            tree.Paused = false;
        }
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo || key.Keycode != Key.Escape)
        {
            return;
        }

        if (IsOpen)
        {
            if (_settingsPanel.Visible)
            {
                ShowMainPanel();
            }
            else
            {
                SetOpen(false);
            }
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_canOpen is not null && !_canOpen()) return;

        SetOpen(true);
        GetViewport().SetInputAsHandled();
    }

    public void Close()
    {
        SetOpen(false);
    }

    private void BuildUi()
    {
        _root = new Control
        {
            Name = "PauseMenuOverlay",
            MouseFilter = Control.MouseFilterEnum.Stop,
            Visible = false,
        };
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        var backdrop = new ColorRect
        {
            Color = new Color(0.002f, 0.006f, 0.015f, 0.88f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(backdrop);

        _mainPanel = BuildMainPanel();
        _root.AddChild(_mainPanel);

        _settingsPanel = BuildSettingsPanel();
        _settingsPanel.Visible = false;
        _root.AddChild(_settingsPanel);

        _status = new Label
        {
            Text = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = 0.5f,
            AnchorTop = 0.5f,
            AnchorRight = 0.5f,
            AnchorBottom = 0.5f,
            OffsetLeft = -320.0f,
            OffsetTop = 250.0f,
            OffsetRight = 320.0f,
            OffsetBottom = 286.0f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _root.AddChild(_status);
    }

    private Control BuildMainPanel()
    {
        PanelContainer panel = CenteredPanel(250.0f, 205.0f);
        var margin = StandardMargin();
        panel.AddChild(margin);
        var column = StandardColumn();
        margin.AddChild(column);

        var title = new Label
        {
            Text = "PAUSED",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 28);
        column.AddChild(title);

        var resume = new Button
        {
            Text = "RESUME",
            CustomMinimumSize = new Vector2(390.0f, 48.0f),
        };
        resume.Pressed += () => SetOpen(false);
        column.AddChild(resume);

        var settings = new Button
        {
            Text = "SETTINGS",
            CustomMinimumSize = new Vector2(390.0f, 44.0f),
        };
        settings.Pressed += ShowSettingsPanel;
        column.AddChild(settings);

        var mainMenu = new Button
        {
            Text = "SAVE & RETURN TO MAIN MENU",
            CustomMinimumSize = new Vector2(390.0f, 44.0f),
        };
        mainMenu.Pressed += () =>
        {
            mainMenu.Disabled = true;
            _status.Text = "Saving...";
            ReturnToMainMenuRequested?.Invoke();
        };
        column.AddChild(mainMenu);

        column.AddChild(new Label
        {
            Text = "Esc resumes",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(0.72f, 0.78f, 0.88f),
        });
        return panel;
    }

    private Control BuildSettingsPanel()
    {
        PanelContainer panel = CenteredPanel(285.0f, 350.0f);
        var margin = StandardMargin();
        panel.AddChild(margin);
        var column = StandardColumn();
        margin.AddChild(column);

        var title = new Label
        {
            Text = "GRAPHICS & PRESENTATION",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 23);
        column.AddChild(title);

        var resolution = new OptionButton
        {
            CustomMinimumSize = new Vector2(210.0f, 38.0f),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        resolution.AddItem("70%", 70);
        resolution.AddItem("85%", 85);
        resolution.AddItem("100%", 100);
        resolution.Select(ClosestResolutionIndex(_graphics.ResolutionScale));
        resolution.ItemSelected += index => _graphics.SetResolutionScale(index switch
        {
            0 => 0.70f,
            1 => 0.85f,
            _ => 1.00f,
        });
        column.AddChild(BuildSettingRow("3D Resolution", resolution));

        var detailDistance = BuildDetailDistanceButton(_graphics.DetailDistance);
        detailDistance.ItemSelected += index => _graphics.SetDetailDistance((int)index);
        column.AddChild(BuildSettingRow("Detail Distance", detailDistance));

        var msaa = new OptionButton
        {
            CustomMinimumSize = new Vector2(210.0f, 38.0f),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        msaa.AddItem("Off", 0);
        msaa.AddItem("2x", 2);
        msaa.AddItem("4x", 4);
        msaa.Select(_graphics.MsaaLevel == 4 ? 2 : _graphics.MsaaLevel == 2 ? 1 : 0);
        msaa.ItemSelected += index => _graphics.SetMsaaLevel(index switch
        {
            1 => 2,
            2 => 4,
            _ => 0,
        });
        column.AddChild(BuildSettingRow("MSAA", msaa));

        var ao = new CheckButton
        {
            Text = "Ambient Occlusion",
            ButtonPressed = _graphics.AmbientOcclusionEnabled,
            CustomMinimumSize = new Vector2(0.0f, 38.0f),
        };
        ao.Toggled += _graphics.SetAmbientOcclusionEnabled;
        column.AddChild(ao);

        var glow = new CheckButton
        {
            Text = "Glow",
            ButtonPressed = _graphics.GlowEnabled,
            CustomMinimumSize = new Vector2(0.0f, 38.0f),
        };
        glow.Toggled += _graphics.SetGlowEnabled;
        column.AddChild(glow);

        var idleOrbit = new CheckButton
        {
            Text = "Idle camera rotation",
            ButtonPressed = _graphics.IdleCameraOrbitEnabled,
            TooltipText = "After 30 seconds without mouse input, slowly rotate around the cube.",
            CustomMinimumSize = new Vector2(0.0f, 38.0f),
        };
        idleOrbit.Toggled += _graphics.SetIdleCameraOrbitEnabled;
        column.AddChild(idleOrbit);

        var reducedMotion = new CheckButton
        {
            Text = "Reduced motion",
            ButtonPressed = _graphics.ReducedMotionEnabled,
            TooltipText = "Reduce non-gameplay pulsing, rotation and transition motion.",
            CustomMinimumSize = new Vector2(0.0f, 38.0f),
        };
        reducedMotion.Toggled += _graphics.SetReducedMotionEnabled;
        column.AddChild(reducedMotion);

        var defaults = new Button
        {
            Text = "RESET PRESENTATION DEFAULTS",
            CustomMinimumSize = new Vector2(0.0f, 38.0f),
        };
        defaults.Pressed += () =>
        {
            _graphics.RestoreDefaults();
            resolution.Select(2);
            detailDistance.Select(1);
            msaa.Select(0);
            ao.SetPressedNoSignal(true);
            glow.SetPressedNoSignal(false);
            idleOrbit.SetPressedNoSignal(true);
            reducedMotion.SetPressedNoSignal(false);
        };
        column.AddChild(defaults);

        var back = new Button
        {
            Text = "BACK",
            CustomMinimumSize = new Vector2(0.0f, 42.0f),
        };
        back.Pressed += ShowMainPanel;
        column.AddChild(back);
        return panel;
    }

    private static OptionButton BuildDetailDistanceButton(int selected)
    {
        var button = new OptionButton
        {
            CustomMinimumSize = new Vector2(210.0f, 38.0f),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "Controls how far decorative trees render. Lower values improve performance.",
        };
        button.AddItem("Low", 0);
        button.AddItem("Medium", 1);
        button.AddItem("High", 2);
        button.Select(Math.Clamp(selected, 0, 2));
        return button;
    }

    private void SetOpen(bool open)
    {
        if (_root is null || _root.Visible == open) return;

        _root.Visible = open;
        if (open)
        {
            ShowMainPanel();
            _status.Text = string.Empty;
            GetTree().Paused = true;
        }
        else
        {
            GetTree().Paused = false;
            _status.Text = string.Empty;
        }
    }

    private void ShowMainPanel()
    {
        _mainPanel.Visible = true;
        _settingsPanel.Visible = false;
        _status.Text = string.Empty;
    }

    private void ShowSettingsPanel()
    {
        _mainPanel.Visible = false;
        _settingsPanel.Visible = true;
        _status.Text = string.Empty;
    }

    private static Control BuildSettingRow(string label, Control control)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        row.AddChild(new Label
        {
            Text = label,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.AddChild(control);
        return row;
    }

    private static int ClosestResolutionIndex(float scale)
    {
        if (scale < 0.775f) return 0;
        if (scale < 0.925f) return 1;
        return 2;
    }

    private static PanelContainer CenteredPanel(float halfWidth, float halfHeight)
        => new()
        {
            AnchorLeft = 0.5f,
            AnchorTop = 0.5f,
            AnchorRight = 0.5f,
            AnchorBottom = 0.5f,
            OffsetLeft = -halfWidth,
            OffsetTop = -halfHeight,
            OffsetRight = halfWidth,
            OffsetBottom = halfHeight,
        };

    private static MarginContainer StandardMargin()
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 24);
        margin.AddThemeConstantOverride("margin_top", 22);
        margin.AddThemeConstantOverride("margin_right", 24);
        margin.AddThemeConstantOverride("margin_bottom", 22);
        return margin;
    }

    private static VBoxContainer StandardColumn()
    {
        var column = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        column.AddThemeConstantOverride("separation", 10);
        return column;
    }
}
