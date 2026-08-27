namespace TenMillionBlocks.World.Rendering;

public partial class WorldView
{
    public string DiagnosticWorldId => _world?.Profile.Id ?? string.Empty;
    public long DiagnosticMinedVoxelCount => _world?.State.MinedVoxelCount ?? 0L;
    public long DiagnosticRemainingMineableBlocks => _world?.RemainingMineableBlocks ?? 0L;
    public int DiagnosticModifiedChunkCount => _world?.State.ModifiedChunkCount ?? 0;
    public long DiagnosticSparseVoxelOverrideCount => _world?.State.SparseVoxelOverrideCount ?? 0L;
    public long DiagnosticGeneratedSampleCacheHits => _world?.GeneratedSampleCacheHits ?? 0L;
    public long DiagnosticGeneratedSampleCacheMisses => _world?.GeneratedSampleCacheMisses ?? 0L;
}
