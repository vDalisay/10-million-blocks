using TenMillionBlocks.Collection;

namespace TenMillionBlocks.App;

public partial class GameRoot
{
    private ResourceCollectionField? _resourceCollection;

    private void OnPendingCollectionChanged()
    {
        MarkAutosaveDirty();
        TryCompleteWorld();
    }

    private void TryCompleteWorld()
    {
        if (!_sessionPersists || _world is null || _world.RemainingMineableBlocks != 0 || _completionShown) return;
        if (_runPhase != WorldRunPhase.Playing) return;

        _resourceCollection?.CollectAllPending();
        if ((_resourceCollection?.PendingCount ?? 0) != 0 || _runPhase != WorldRunPhase.Playing) return;

        FreezeCompletionResultAndSave();
        BeginCompletionCinematic();
    }
}
