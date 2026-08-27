using Godot;
using TenMillionBlocks.UI;

namespace TenMillionBlocks.App;

public partial class MainMenuRoot
{
    private bool _incrementalSkinApplied;

    public override void _Process(double delta)
    {
        _ = delta;
        if (_incrementalSkinApplied || _mainPanel is null || _settingsPanel is null || _confirmPanel is null) return;
        IncrementalUiSkin.ApplyMenu(_mainPanel);
        IncrementalUiSkin.ApplyMenu(_settingsPanel);
        IncrementalUiSkin.ApplyMenu(_confirmPanel);
        _incrementalSkinApplied = true;
    }
}
