namespace TenMillionBlocks.UI;

public partial class SkillTreeView
{
    public override void _ExitTree()
    {
        // SpecialResourceInventory is player-global and survives world/session switches. Always detach
        // it explicitly; detach the session-local publishers too now that the handlers are named rather
        // than anonymous delegates.
        if (_skills is not null)
        {
            _skills.Changed -= RequestRefresh;
            _skills.SpecialResources.Changed -= RequestRefresh;
        }
        if (_mining is not null)
        {
            _mining.CurrencyChanged -= OnCurrencyChanged;
        }
    }
}
