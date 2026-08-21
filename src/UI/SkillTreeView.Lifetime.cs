namespace TenMillionBlocks.UI;

public partial class SkillTreeView
{
    public override void _ExitTree()
    {
        // SpecialResourceInventory is player-global and survives world/session switches. Without this
        // unsubscribe it would retain every old SkillTreeView and invoke Refresh on freed controls after
        // revisits/replays. Session-local SkillTreeService/MiningService subscriptions can die with their
        // publisher/view cycle; this persistent publisher must be detached explicitly.
        if (_skills is not null)
        {
            _skills.SpecialResources.Changed -= Refresh;
        }
    }
}
