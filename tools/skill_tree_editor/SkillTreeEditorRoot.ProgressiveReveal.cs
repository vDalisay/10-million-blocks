using Godot;
using TenMillionBlocks.Skills;

namespace TenMillionBlocks.Tools.SkillTreeEditor;

public partial class SkillTreeEditorRoot
{
    private CheckButton? _progressiveRevealToggle;
    private SkillNodeDefinition? _progressiveRevealInspectorNode;
    private bool _syncingProgressiveReveal;

    public override void _Process(double delta)
    {
        _ = delta;
        EnsureProgressiveRevealControls();
        SyncProgressiveRevealInspector();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo || !key.CtrlPressed)
        {
            return;
        }

        switch (key.Keycode)
        {
            case Key.S:
                Save();
                GetViewport().SetInputAsHandled();
                break;
            case Key.D:
                DuplicateSelected();
                GetViewport().SetInputAsHandled();
                break;
            case Key.L:
                BeginConnect();
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    private void EnsureProgressiveRevealControls()
    {
        if (_progressiveRevealToggle is not null && GodotObject.IsInstanceValid(_progressiveRevealToggle)) return;
        if (_prerequisiteSummary is null || !GodotObject.IsInstanceValid(_prerequisiteSummary)) return;
        if (_prerequisiteSummary.GetParent() is not VBoxContainer column) return;

        _progressiveRevealToggle = new CheckButton
        {
            Text = "Hide until prerequisites are unlocked",
            TooltipText = "When enabled, this skill and its incoming connection lines are completely hidden until all prerequisite rank requirements are met. Root skills remain visible.",
            CustomMinimumSize = new Vector2(0.0f, 34.0f),
        };
        _progressiveRevealToggle.Toggled += OnProgressiveRevealToggled;
        column.AddChild(_progressiveRevealToggle);
        column.MoveChild(_progressiveRevealToggle, _prerequisiteSummary.GetIndex() + 1);

        var hint = new Label
        {
            Text = "Editor shortcuts: Ctrl+S save, Ctrl+D duplicate, Ctrl+L connect. Drag cards to place them; select a line and click grid cells to route it.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color(0.72f, 0.78f, 0.88f),
        };
        column.AddChild(hint);
    }

    private void SyncProgressiveRevealInspector()
    {
        if (_progressiveRevealToggle is null || !GodotObject.IsInstanceValid(_progressiveRevealToggle)) return;
        if (ReferenceEquals(_progressiveRevealInspectorNode, _selected)) return;

        _progressiveRevealInspectorNode = _selected;
        _syncingProgressiveReveal = true;
        _progressiveRevealToggle.Disabled = _selected is null;
        _progressiveRevealToggle.SetPressedNoSignal(_selected?.HideUntilPrerequisitesMet ?? false);
        _syncingProgressiveReveal = false;
    }

    private void OnProgressiveRevealToggled(bool enabled)
    {
        if (_syncingProgressiveReveal || _selected is null) return;

        _selected.HideUntilPrerequisitesMet = enabled;
        _canvas.QueueRedraw();
        _status.Text = enabled
            ? $"{_selected.DisplayName}: hidden until prerequisite ranks are met"
            : $"{_selected.DisplayName}: always shown when staged in this world";
    }
}
