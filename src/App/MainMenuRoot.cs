using System;
using Godot;
using TenMillionBlocks.Save;

namespace TenMillionBlocks.App;

/// <summary>
/// Minimal pre-demo shell. It intentionally keeps presentation simple while giving local playtests a
/// reliable way to start the game and wipe all progression/replay data without touching user folders.
/// </summary>
public partial class MainMenuRoot : Node
{
    private Control _mainPanel = null!;
    private Control _settingsPanel = null!;
    private Control _confirmPanel = null!;
    private Label _status = null!;

    public override void _Ready()
    {
        RenderingServer.SetDefaultClearColor(new Color(0.003f, 0.008f, 0.025f));

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
            OffsetTop = 190,
            OffsetRight = 340,
            OffsetBottom = 230,
        };
        canvas.AddChild(_status);
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

        var play = new Button
        {
            Text = "PLAY GAME",
            CustomMinimumSize = new Vector2(360, 54),
        };
        play.Pressed += OnPlayPressed;
        column.AddChild(play);

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
        PanelContainer panel = CenteredPanel(265, 190);
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

        column.AddChild(new Label
        {
            Text = "Playtest data",
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        var clear = new Button
        {
            Text = "CLEAR SAVE DATA",
            CustomMinimumSize = new Vector2(390, 48),
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
            CustomMinimumSize = new Vector2(390, 42),
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
            Text = "This removes progression, per-world state and replay files.\nThis cannot be undone.",
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
            _status.Text = "Save data cleared. The next Play Game starts from the first block.";
            _status.Modulate = new Color(0.65f, 1.0f, 0.72f);
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
        Error result = GetTree().ChangeSceneToFile("res://scenes/Game.tscn");
        if (result == Error.Ok) return;

        _status.Text = $"Could not start gameplay ({result}).";
        _status.Modulate = new Color(1.0f, 0.58f, 0.52f);
    }

    private void ShowMain()
    {
        _mainPanel.Visible = true;
        _settingsPanel.Visible = false;
        _confirmPanel.Visible = false;
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
        column.AddThemeConstantOverride("separation", 12);
        return column;
    }
}
