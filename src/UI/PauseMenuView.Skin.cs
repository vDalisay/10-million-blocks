using Godot;

namespace TenMillionBlocks.UI;

public partial class PauseMenuView
{
    private bool _incrementalSkinApplied;

    public override void _Process(double delta)
    {
        _ = delta;
        if (_incrementalSkinApplied || _mainPanel is null || _settingsPanel is null) return;
        IncrementalUiSkin.ApplyMenu(_mainPanel);
        IncrementalUiSkin.ApplyMenu(_settingsPanel);
        _incrementalSkinApplied = true;
    }
}
