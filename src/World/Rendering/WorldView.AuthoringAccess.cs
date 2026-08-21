namespace TenMillionBlocks.World.Rendering;

public partial class WorldView
{
    /// <summary>
    /// Authoring/debug access to the immutable-generation + sparse-state world represented by this
    /// view. Gameplay mutation still goes through MiningService; the world-authoring tool uses this
    /// only for picking/inspection before rebuilding a candidate from a sparse override draft.
    /// </summary>
    public VirtualWorld WorldForAuthoring => _world;
}
