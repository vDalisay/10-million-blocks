using TenMillionBlocks.Collection;

namespace TenMillionBlocks.App;

public partial class GameRoot
{
    private ResourceCollectionField? _resourceCollection;

    private void OnPendingCollectionChanged()
    {
        MarkAutosaveDirty();
        if (_sessionPersists
            && _world is not null
            && _world.RemainingMineableBlocks == 0
            && (_resourceCollection?.PendingCount ?? 0) == 0
            && !_completionShown)
        {
            ShowCompletion(debugPreview: false);
        }
    }
}
