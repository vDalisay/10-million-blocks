using System;
using Godot;
using TenMillionBlocks.Content;
using TenMillionBlocks.Replay;

namespace TenMillionBlocks.UI;

public partial class ReplayView : CanvasLayer
{
    private ReplayPlayer _player = null!;
    private WorldProfile _profile = null!;
    private Label _status = null!;
    private Button _playPause = null!;
    private HSlider _speedSlider = null!;

    public event Action? ExitRequested;

    public void Initialize(ReplayPlayer player, WorldProfile profile)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _player.Changed += Refresh;
    }

    public override void _Ready()
    {
        Layer = 30;
        var root = new Control
        {
            Name = "ReplayControlsRoot",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        var panel = new PanelContainer
        {
            AnchorLeft = 1.0f,
            AnchorRight = 1.0f,
            OffsetLeft = -500.0f,
            OffsetTop = 18.0f,
            OffsetRight = -18.0f,
            OffsetBottom = 270.0f,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        root.AddChild(panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 14);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_right", 14);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        panel.AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        margin.AddChild(column);

        var title = new Label
        {
            Text = $"REPLAY — {_profile.DisplayName.ToUpperInvariant()}",
        };
        title.AddThemeFontSizeOverride("font_size", 20);
        column.AddChild(title);

        _status = new Label();
        column.AddChild(_status);

        var transport = new HBoxContainer();
        transport.AddThemeConstantOverride("separation", 6);
        column.AddChild(transport);

        _playPause = new Button { CustomMinimumSize = new Vector2(112.0f, 34.0f) };
        _playPause.Pressed += _player.TogglePlaying;
        transport.AddChild(_playPause);

        var restart = new Button
        {
            Text = "Restart [R]",
            CustomMinimumSize = new Vector2(112.0f, 34.0f),
        };
        restart.Pressed += () => _player.Restart(autoplay: true);
        transport.AddChild(restart);

        var sliderRow = new HBoxContainer();
        sliderRow.AddThemeConstantOverride("separation", 8);
        column.AddChild(sliderRow);

        sliderRow.AddChild(new Label
        {
            Text = "Speed",
            CustomMinimumSize = new Vector2(54.0f, 0.0f),
        });

        _speedSlider = new HSlider
        {
            MinValue = ReplayPlayer.MinSpeed,
            MaxValue = ReplayPlayer.MaxSpeed,
            Step = 1.0,
            Value = _player.Speed,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(300.0f, 28.0f),
        };
        _speedSlider.ValueChanged += value => _player.SetSpeed(value);
        sliderRow.AddChild(_speedSlider);

        var presets = new HBoxContainer();
        presets.AddThemeConstantOverride("separation", 6);
        column.AddChild(presets);

        foreach (double speed in new[] { 1.0, 2.0, 4.0, 8.0, 16.0, 32.0, 64.0 })
        {
            var button = new Button
            {
                Text = $"{speed:0}x",
                CustomMinimumSize = new Vector2(54.0f, 34.0f),
            };
            double selected = speed;
            button.Pressed += () => _player.SetSpeed(selected);
            presets.AddChild(button);
        }

        var exit = new Button
        {
            Text = "Exit Replay [Esc]",
            CustomMinimumSize = new Vector2(0.0f, 34.0f),
        };
        exit.Pressed += () => ExitRequested?.Invoke();
        column.AddChild(exit);

        Refresh();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo)
        {
            return;
        }

        if (key.Keycode == Key.Space)
        {
            _player.TogglePlaying();
            GetViewport().SetInputAsHandled();
        }
        else if (key.Keycode == Key.R)
        {
            _player.Restart(autoplay: true);
            GetViewport().SetInputAsHandled();
        }
        else if (key.Keycode == Key.Escape)
        {
            ExitRequested?.Invoke();
            GetViewport().SetInputAsHandled();
        }
    }

    private void Refresh()
    {
        if (_status is null || _playPause is null) return;

        string state = _player.IsFinished
            ? "Finished"
            : _player.IsPlaying ? "Playing" : "Paused";
        _status.Text =
            $"{state}  |  {_player.Speed:0}x  |  {_player.CurrentSeconds:0.0}s / {_player.DurationSeconds:0.0}s  |  " +
            $"{_player.AppliedEventCount:N0}/{_player.EventCount:N0} removals";
        _playPause.Text = _player.IsPlaying ? "Pause [Space]" : (_player.IsFinished ? "Replay [Space]" : "Play [Space]");
        if (_speedSlider is not null)
        {
            _speedSlider.SetValueNoSignal(_player.Speed);
        }
    }
}
