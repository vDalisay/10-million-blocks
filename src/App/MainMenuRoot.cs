using System;
using Godot;
using TenMillionBlocks.Presentation;
using TenMillionBlocks.Save;
using TenMillionBlocks.UI;

namespace TenMillionBlocks.App;

/// <summary>
/// Minimal pre-demo shell. It intentionally keeps presentation simple while giving local playtests a
/// reliable way to start the game, tune graphics and wipe progression/replay data without touching
/// user folders manually.
/// </summary>
public partial class MainMenuRoot : Node
{
    private Control _mainPanel = null!;
    private Control _settingsPanel = null!;
    private Control _confirmPanel = null!;
    private Label _status = null!;
    private Button _playButton = null!;
    private GraphicsSettingsRuntime _graphics = null!;

    public override void _Ready()
    {
        RenderingServer.SetDefaultClearColor(new Color(0.003f, 0.008f, 0.025f));
        _graphics = GraphicsSettingsRuntime.Ensure(GetTree());

        var canvas = new CanvasLayer { Layer = 100 };
        AddChild(canvas);

        var backdrop = new ColorRect
        {
            Color = new Color(0.008f, 0.014f, 0.030f, 1.0f),
        };
        backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        canvas.AddChild(backdrop);

        var title = new Label
        {
            Text = "10 MILLION BLOCKS",
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = 0.5f,
            AnchorTop = 0.5f,
            AnchorRight = 0.5f,
            AnchorBottom = 0.5f,
            OffsetLeft = -280,
            OffsetTop = -220,
            OffsetRight = 280,
            OffsetBottom = -165,
        };
        title.AddThemeFontSizeOverride("font_size", 34);
        canvas.AddChild(title);

        _mainPanel = BuildMainPanel();
        canvas.AddChild(_mainPanel);

        _settingsPanel = BuildSettingsPanel();
        _settingsPanel.Visible = false;
        canvas.AddChild(_settingsPanel);

        _confirmPanel = BuildConfirmPanel();
        _confirmPanel.Visible = false;
        canvas.AddChild(_confirmPanel);

        _status = new Label
        {
            Text = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = 0.5f,
            AnchorTop = 0.5f,
            AnchorRight = 0.5f,
            AnchorBottom = 0.5f,
            OffsetLeft = -340,
            OffsetTop = 290,
            OffsetRight = 340,
            OffsetBottom = 330,
        };
        canvas.AddChild(_status);

        RefreshPlayButton();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo || key.Keycode != Key.Escape) return;

        if (_confirmPanel.Visible)
        {
            _confirmPanel.Visible = false;
            _settingsPanel.Visible = true;
            GetViewport().SetInputAsHandled();
        }
        else if (_settingsPanel.Visible)
        {
            ShowMain();
            GetViewport().SetInputAsHandled();
        }
    }

    private Control BuildMainPanel()
    {
        PanelContainer panel = CenteredPanel(230, 155);
        var margin = StandardMargin();
        panel.AddChild(margin);
        var column = StandardColumn();
        margin.AddChild(column);

        _playButton = new Button
        {
            Text = "START GAME",
            CustomMinimumSize = new Vector2(360, 54),
        };
        _playButton.Pressed += OnPlayPressed;
        column.AddChild(_playButton);

        var settings = new Button
        {
            Text = "SETTINGS",
            CustomMinimumSize = new Vector2(360, 46),
        };
        settings.Pressed += () =>
        {
            _mainPanel.Visible = false;
            _settingsPanel.Visible = true;
            _confirmPanel.Visible = false;
            _status.Text = string.Empty;
        };
        column.AddChild(settings);
        return panel;
    }

    private Control BuildSettingsPanel()
    {
        PanelContainer panel = CenteredPanel(285, 320);
        var margin = StandardMargin();
        panel.AddChild(margin);
        var column = StandardColumn();
        margin.AddChild(column);

        var header = new Label
        {
            Text = "SETTINGS",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        header.AddThemeFontSizeOverride("font_size", 23);
        column.AddChild(header);

        var graphicsHeader = new Label
        {
            Text = "Graphics & Presentation",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        graphicsHeader.AddThemeFontSizeOverride("font_size", 17);
        column.AddChild(graphicsHeader);

        var resolution = new OptionButton
        {
            CustomMinimumSize = new Vector2(210, 38),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        resolution.AddItem("70%", 70);
        resolution.AddItem("85%", 85);
        resolution.AddItem("100%", 100);
        resolution.Select(ClosestResolutionIndex(_graphics.ResolutionScale));
        resolution.ItemSelected += index =>
        {
            float scale = index switch
            {
                0 => 0.70f,
                1 => 0.85f,
                _ => 1.00f,
            };
            _graphics.SetResolutionScale(scale);
        };
        column.AddChild(BuildSettingRow("3D Resolution", resolution));

        var msaa = new OptionButton
        {
            CustomMinimumSize = new Vector2(210, 38),
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
            CustomMinimumSize = new Vector2(0, 38),
        };
        ao.Toggled += _graphics.SetAmbientOcclusionEnabled;
        column.AddChild(ao);

        var glow = new CheckButton
        {
            Text = "Glow",
            ButtonPressed = _graphics.GlowEnabled,
            CustomMinimumSize = new Vector2(0, 38),
        };
        glow.Toggled += _graphics.SetGlowEnabled;
        column.AddChild(glow);

        var idleOrbit = new CheckButton
        {
            Text = "Idle camera rotation",
            ButtonPressed = _graphics.IdleCameraOrbitEnabled,
            TooltipText = "After 30 seconds without mouse input, slowly rotate around the cube.",
            CustomMinimumSize = new Vector2(0, 38),
        };
        idleOrbit.Toggled += _graphics.SetIdleCameraOrbitEnabled;
        column.AddChild(idleOrbit);

        var defaults = new Button
        {
            Text = "RESET PRESENTATION DEFAULTS",
            CustomMinimumSize = new Vector2(0, 38),
        };
        defaults.Pressed += () =>
        {
            _graphics.RestoreDefaults();
            resolution.Select(2);
            msaa.Select(0);
            ao.SetPressedNoSignal(true);
            glow.SetPressedNoSignal(false);
            idleOrbit.SetPressedNoSignal(true);
        };
        column.AddChild(defaults);

        column.AddChild(new HSeparator());
        column.AddChild(new Label
        {
            Text = "Playtest data",
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        var clear = new Button
        {
            Text = "CLEAR SAVE DATA",
            CustomMinimumSize = new Vector2(0, 44),
        };
        clear.Pressed += () =>
        {
            _settingsPanel.Visible = false;
            _confirmPanel.Visible = true;
            _status.Text = string.Empty;
        };
        column.AddChild(clear);

        var back = new Button
        {
            Text = "BACK",
            CustomMinimumSize = new Vector2(0, 42),
        };
        back.Pressed += ShowMain;
        column.AddChild(back);
        return panel;
    }

    private Control BuildConfirmPanel()
    {
        PanelContainer panel = CenteredPanel(285, 205);
        var margin = StandardMargin();
        panel.AddChild(margin);
        var column = StandardColumn();
        margin.AddChild(column);

        var header = new Label
        {
            Text = "CLEAR ALL SAVE DATA?",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        header.AddThemeFontSizeOverride("font_size", 22);
        column.AddChild(header);

        column.AddChild(new Label
        {
            Text = "This removes progression, per-world state and replay files.\nPresentation preferences are kept. This cannot be undone.",
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        var confirm = new Button
        {
            Text = "CONFIRM CLEAR",
            CustomMinimumSize = new Vector2(390, 48),
        };
        confirm.Pressed += ClearSaves;
        column.AddChild(confirm);

        var cancel = new Button
        {
            Text = "CANCEL",
            CustomMinimumSize = new Vector2(390, 42),
        };
        cancel.Pressed += () =>
        {
            _confirmPanel.Visible = false;
            _settingsPanel.Visible = true;
        };
        column.AddChild(cancel);
        return panel;
    }

    private void ClearSaves()
    {
        try
        {
            SaveDataMaintenance.ClearAllLocalData();
            _confirmPanel.Visible = false;
            _settingsPanel.Visible = true;
            _status.Text = "Save data cleared. The next Start Game begins from the first block.";
            _status.Modulate = new Color(0.65f, 1.0f, 0.72f);
            RefreshPlayButton();
        }
        catch (Exception exception)
        {
            GD.PushError($"Could not clear save data: {exception}");
            _status.Text = "Could not clear save data. See the Godot output log.";
            _status.Modulate = new Color(1.0f, 0.58f, 0.52f);
        }
    }

    private void OnPlayPressed()
    {
        WorldLoadingScreen.RunTransition(this, "LOADING WORLD", () =>
        {
            Error result = GetTree().ChangeSceneToFile("res://scenes/Game.tscn");
            if (result == Error.Ok) return;

            WorldLoadingScreen.CancelGlobal();
            _status.Text = $"Could not start gameplay ({result}).";
            _status.Modulate = new Color(1.0f, 0.58f, 0.52f);
        });
    }

    private void RefreshPlayButton()
    {
        if (_playButton is null) return;
        bool hasSave = Godot.FileAccess.FileExists(SaveService.DefaultPath)
            || Godot.FileAccess.FileExists(SaveService.LegacyV2Path);
        _playButton.Text = hasSave ? "CONTINUE" : "START GAME";
    }

    private void ShowMain()
    {
        _mainPanel.Visible = true;
        _settingsPanel.Visible = false;
        _confirmPanel.Visible = false;
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
