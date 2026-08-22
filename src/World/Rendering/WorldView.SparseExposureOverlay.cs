using System;
using System.Collections.Generic;
using Godot;
using TenMillionBlocks.World.Generation;

namespace TenMillionBlocks.World.Rendering;

/// <summary>
/// Full-surface worlds normally need only one generated surface sample per face column. Mining used to
/// switch an affected 16^3 chunk to RebuildEagerChunk so tunnel side walls were correct, but that turns
/// one click into thousands of generated voxel/exposure queries.
///
/// The overlay is now an incremental exposed-frontier cache. A block removal can only make its six
/// neighbours newly visible, so live mining records those neighbours directly instead of rescanning all
/// previously mined voxels in the chunk on every rebuild. Existing save/deferred state is bootstrapped
/// once, lazily, when that chunk next becomes presentation-relevant.
/// </summary>
public partial class WorldView
{
    private const int SparseOverlayBuildsPerFrame = 2;
    private const double SparseOverlayFrameBudgetMilliseconds = 1.75;
    private const string SparseOverlayNodeName = "SparseExposureOverlay";
    private static readonly ChunkCoord[] SparseOverlaySourceOffsets =
    {
        new(0, 0, 0),
        new(1, 0, 0),
        new(-1, 0, 0),
        new(0, 1, 0),
        new(0, -1, 0),
        new(0, 0, 1),
        new(0, 0, -1),
    };

    private readonly HashSet<ChunkCoord> _sparseOverlayDirtyChunks = new();
    private readonly Dictionary<ChunkCoord, HashSet<Vector3I>> _sparseExposureFrontierByChunk = new();
    private readonly HashSet<ChunkCoord> _sparseExposureInitializedChunks = new();
    private readonly List<Vector3I> _sparseExposureRemovalScratch = new();
    private readonly List<int> _sparseMinedIndexScratch = new();
    private SparseExposureWorker? _sparseExposureWorker;
    private double _sparseOverlayBuildTotalMilliseconds;
    private long _sparseExposureFrontierCandidateCount;

    public int PendingSparseExposureOverlays => _sparseOverlayDirtyChunks.Count;
    public long SparseExposureFrontierCandidateCount => _sparseExposureFrontierCandidateCount;
    public long SparseExposureOverlayBuilds { get; private set; }
    public double LastSparseExposureOverlayBuildMilliseconds { get; private set; }
    public double AverageSparseExposureOverlayBuildMilliseconds
        => SparseExposureOverlayBuilds == 0
            ? 0.0
            : _sparseOverlayBuildTotalMilliseconds / SparseExposureOverlayBuilds;

    /// <summary>
    /// Shared mining/automation dirty path. Full-surface shell chunks rebuild their cheap outward-column
    /// base while the sparse overlay accounts for tunnel/cavity walls. Interior chunks have no base face
    /// columns at all, so rebuilding them through RebuildFullSurfaceChunk only destroyed/recreated sparse
    /// roots and spent CPU sampling zero useful surface columns. Keep those chunks sparse-only.
    /// </summary>
    private void MarkInteractiveChunkDirty(ChunkCoord chunk)
    {
        if (!ChunkInWorldBounds(chunk)) return;

        if (FullSurfaceRenderer)
        {
            _desiredChunks.Add(chunk);
            QueueSparseExposureOverlay(chunk);
            int depth = Math.Max(1, _world.Profile.DetailedSurfaceDepthChunks);
            if (IsShellChunk(chunk, depth))
            {
                MarkChunkDirty(chunk, forceExact: false);
            }
            return;
        }

        MarkChunkDirty(chunk, forceExact: false);
    }

    /// <summary>
    /// Records the only cells whose exposure can change after one exact block removal: the removed cell
    /// itself (which must disappear from any old overlay) and its six neighbours. This is the same local
    /// invalidation principle used by mature voxel/tile engines: mutation work is proportional to the
    /// changed frontier, not to the total amount of excavation already stored in the chunk.
    /// </summary>
    private void RecordSparseExposureMutation(Vector3I minedVoxel)
    {
        if (!FullSurfaceRenderer) return;

        int chunkSize = _world.Profile.ChunkSize;
        ChunkCoord minedChunk = ChunkCoord.FromVoxel(minedVoxel, chunkSize);
        RemoveSparseExposureCandidate(minedChunk, minedVoxel);
        QueueSparseExposureOverlay(minedChunk);

        foreach (Vector3I direction in VoxelMath.Neighbors)
        {
            Vector3I candidate = minedVoxel + direction;
            if (Math.Abs(candidate.X) > _world.MaxCoordinate
                || Math.Abs(candidate.Y) > _world.MaxCoordinate
                || Math.Abs(candidate.Z) > _world.MaxCoordinate)
            {
                continue;
            }

            ChunkCoord candidateChunk = ChunkCoord.FromVoxel(candidate, chunkSize);
            if (!ChunkInWorldBounds(candidateChunk)) continue;
            AddSparseExposureCandidate(candidateChunk, candidate);
            QueueSparseExposureOverlay(candidateChunk);
        }
    }

    /// <summary>
    /// Hidden automation should not maintain millions of per-voxel presentation candidates. Mark its
    /// affected chunks stale instead. When a stale chunk becomes visible again, one lazy bootstrap from
    /// compact mined-state reconstructs the frontier and then returns to incremental updates.
    /// </summary>
    private void InvalidateSparseExposureFrontier(ChunkCoord chunk)
    {
        if (!FullSurfaceRenderer || !ChunkInWorldBounds(chunk)) return;
        _sparseExposureInitializedChunks.Remove(chunk);
        if (_sparseExposureFrontierByChunk.Remove(chunk, out HashSet<Vector3I>? candidates))
        {
            _sparseExposureFrontierCandidateCount -= candidates.Count;
        }
    }

    private void InvalidateSparseExposureFrontierForMutation(Vector3I voxel)
    {
        if (!FullSurfaceRenderer) return;

        int chunkSize = _world.Profile.ChunkSize;
        ChunkCoord chunk = ChunkCoord.FromVoxel(voxel, chunkSize);
        InvalidateSparseExposureFrontier(chunk);

        int localX = VoxelMath.PositiveMod(voxel.X, chunkSize);
        int localY = VoxelMath.PositiveMod(voxel.Y, chunkSize);
        int localZ = VoxelMath.PositiveMod(voxel.Z, chunkSize);

        if (localX == 0) InvalidateSparseExposureFrontier(new ChunkCoord(chunk.X - 1, chunk.Y, chunk.Z));
        else if (localX == chunkSize - 1) InvalidateSparseExposureFrontier(new ChunkCoord(chunk.X + 1, chunk.Y, chunk.Z));

        if (localY == 0) InvalidateSparseExposureFrontier(new ChunkCoord(chunk.X, chunk.Y - 1, chunk.Z));
        else if (localY == chunkSize - 1) InvalidateSparseExposureFrontier(new ChunkCoord(chunk.X, chunk.Y + 1, chunk.Z));

        if (localZ == 0) InvalidateSparseExposureFrontier(new ChunkCoord(chunk.X, chunk.Y, chunk.Z - 1));
        else if (localZ == chunkSize - 1) InvalidateSparseExposureFrontier(new ChunkCoord(chunk.X, chunk.Y, chunk.Z + 1));
    }

    private void AddSparseExposureCandidate(ChunkCoord chunk, Vector3I candidate)
    {
        if (!_sparseExposureFrontierByChunk.TryGetValue(chunk, out HashSet<Vector3I>? frontier))
        {
            frontier = new HashSet<Vector3I>();
            _sparseExposureFrontierByChunk.Add(chunk, frontier);
        }

        if (frontier.Add(candidate))
        {
            _sparseExposureFrontierCandidateCount++;
        }
    }

    private void RemoveSparseExposureCandidate(ChunkCoord chunk, Vector3I candidate)
    {
        if (!_sparseExposureFrontierByChunk.TryGetValue(chunk, out HashSet<Vector3I>? frontier)
            || !frontier.Remove(candidate))
        {
            return;
        }

        _sparseExposureFrontierCandidateCount--;
        if (frontier.Count == 0)
        {
            _sparseExposureFrontierByChunk.Remove(chunk);
        }
    }

    private void QueueSparseExposureOverlay(ChunkCoord chunk)
    {
        if (!FullSurfaceRenderer || !ChunkInWorldBounds(chunk)) return;
        _sparseOverlayDirtyChunks.Add(chunk);
        EnsureSparseExposureWorker();
    }

    /// <summary>
    /// Conservative visibility hint for excavated interior chunks. Their cavity walls are not aligned to
    /// the original cube face, so chunk-level cube-backface culling is invalid once mining reaches them.
    /// This uses compact chunk-state membership only; no mined-index lists are allocated.
    /// </summary>
    private bool HasSparseExposurePotential(ChunkCoord chunk)
    {
        if (_sparseExposureFrontierByChunk.TryGetValue(chunk, out HashSet<Vector3I>? frontier)
            && frontier.Count > 0)
        {
            return true;
        }

        foreach (ChunkCoord offset in SparseOverlaySourceOffsets)
        {
            ChunkCoord sourceChunk = new(
                chunk.X + offset.X,
                chunk.Y + offset.Y,
                chunk.Z + offset.Z);
            if (ChunkInWorldBounds(sourceChunk) && _world.State.HasMinedVoxels(sourceChunk))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// When loading an existing save, or when off-screen automation was deliberately collapsed to a
    /// chunk marker, we do not have the live mutation frontier. Reconstruct it once from the compact
    /// mined bitsets in this chunk and its six neighbours. Subsequent mining is incremental again.
    /// </summary>
    private void EnsureSparseExposureFrontierInitialized(ChunkCoord chunk)
    {
        if (!_sparseExposureInitializedChunks.Add(chunk)) return;

        int chunkSize = _world.Profile.ChunkSize;
        foreach (ChunkCoord offset in SparseOverlaySourceOffsets)
        {
            ChunkCoord sourceChunk = new(
                chunk.X + offset.X,
                chunk.Y + offset.Y,
                chunk.Z + offset.Z);
            if (!ChunkInWorldBounds(sourceChunk)) continue;

            if (_world.State.CopyMinedLocalIndices(sourceChunk, _sparseMinedIndexScratch) == 0) continue;

            Vector3I sourceMin = sourceChunk.MinVoxel(chunkSize);
            foreach (int index in _sparseMinedIndexScratch)
            {
                int x = index % chunkSize;
                int yz = index / chunkSize;
                int y = yz % chunkSize;
                int z = yz / chunkSize;
                Vector3I minedVoxel = sourceMin + new Vector3I(x, y, z);

                foreach (Vector3I direction in VoxelMath.Neighbors)
                {
                    Vector3I candidate = minedVoxel + direction;
                    if (ChunkCoord.FromVoxel(candidate, chunkSize) == chunk)
                    {
                        AddSparseExposureCandidate(chunk, candidate);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Surface chunks loaded from a save need their old tunnel walls too. Avoid bootstrapping every
    /// shell chunk: only queue chunks that actually have exact sparse modifications in themselves or an
    /// adjacent chunk capable of exposing one of their cells.
    /// </summary>
    private void QueueSparseExposureOverlayForRestoredState(ChunkCoord chunk)
    {
        if (!FullSurfaceRenderer || _world.State.ModifiedChunkCount == 0) return;

        foreach (ChunkCoord offset in SparseOverlaySourceOffsets)
        {
            ChunkCoord sourceChunk = new(
                chunk.X + offset.X,
                chunk.Y + offset.Y,
                chunk.Z + offset.Z);
            if (!ChunkInWorldBounds(sourceChunk)) continue;
            if (!_world.State.HasMinedVoxels(sourceChunk)) continue;
            QueueSparseExposureOverlay(chunk);
            return;
        }
    }

    private void EnsureSparseExposureWorker()
    {
        if (_sparseExposureWorker is not null && GodotObject.IsInstanceValid(_sparseExposureWorker)) return;
        _sparseExposureWorker = new SparseExposureWorker(this)
        {
            Name = "SparseExposureWorker",
        };
        AddChild(_sparseExposureWorker);
    }

    private void ProcessSparseExposureQueue()
    {
        if (!FullSurfaceRenderer || _sparseOverlayDirtyChunks.Count == 0) return;

        ulong frameStarted = Time.GetTicksUsec();
        int builds = 0;
        while (builds < SparseOverlayBuildsPerFrame && TryPopSparseOverlayChunk(out ChunkCoord chunk))
        {
            RebuildSparseExposureOverlay(chunk);
            builds++;
            if (ElapsedMilliseconds(frameStarted) >= SparseOverlayFrameBudgetMilliseconds)
            {
                break;
            }
        }
    }

    private bool TryPopSparseOverlayChunk(out ChunkCoord chunk)
    {
        foreach (ChunkCoord candidate in _sparseOverlayDirtyChunks)
        {
            // A base surface rebuild destroys/replaces its chunk root. Do not attach a fresh sparse
            // overlay to the old root while that rebuild is pending, otherwise the next WorldView tick
            // frees the just-built tunnel walls and leaves a persistent see-through hole. Keeping the
            // candidate queued guarantees the overlay is attached after the replacement base root.
            if (_dirtyChunks.Contains(candidate) || _pendingVisibleAutomationChunks.Contains(candidate))
            {
                continue;
            }

            chunk = candidate;
            _sparseOverlayDirtyChunks.Remove(candidate);
            return true;
        }

        chunk = default;
        return false;
    }

    private void RebuildSparseExposureOverlay(ChunkCoord chunk)
    {
        ulong started = Time.GetTicksUsec();
        EnsureSparseExposureFrontierInitialized(chunk);

        if (!_sparseExposureFrontierByChunk.TryGetValue(chunk, out HashSet<Vector3I>? frontier)
            || frontier.Count == 0)
        {
            RemoveSparseOverlayNode(chunk);
            FinishSparseExposureBuild(started);
            return;
        }

        var batches = new Dictionary<string, List<Transform3D>>(StringComparer.Ordinal);
        var treeBatches = new Dictionary<string, List<Transform3D>>(StringComparer.Ordinal);
        _sparseExposureRemovalScratch.Clear();
        int max = _world.MaxCoordinate;

        foreach (Vector3I voxel in frontier)
        {
            if (Math.Abs(voxel.X) > max || Math.Abs(voxel.Y) > max || Math.Abs(voxel.Z) > max)
            {
                _sparseExposureRemovalScratch.Add(voxel);
                continue;
            }

            BlockSample sample = _world.SampleVoxel(voxel);
            if (!sample.Present || !_world.IsExposed(voxel, sample))
            {
                _sparseExposureRemovalScratch.Add(voxel);
                continue;
            }

            // The base surface-column chunk already renders the first surviving block in each outward
            // column. Keep only tunnel/cavity side walls in the sparse overlay to avoid coplanar copies.
            if (IsRepresentedByFullSurfaceBase(chunk, voxel))
            {
                _sparseExposureRemovalScratch.Add(voxel);
                continue;
            }

            Vector3I outward = _world.Source.GetOutwardNormal(voxel);
            string visualBlockId = ResolveSurfaceVisualBlockId(voxel, sample.BlockId);
            Basis blockBasis = ShouldOrientToCubeFace(visualBlockId)
                ? BasisForNormal(outward)
                : Basis.Identity;
            AddTransform(batches, visualBlockId, new Transform3D(blockBasis, VoxelToWorld(voxel)));

            if ((sample.BlockId == _world.Profile.SurfaceBlock || sample.BlockId == _world.Profile.SurfaceEdgeBlock)
                && _world.Source.TrySampleTree(voxel, out FeatureSample feature)
                && !_world.IsPresent(voxel + feature.OutwardNormal))
            {
                AddTransform(treeBatches, PickTree(voxel), TreeTransform(voxel, feature.OutwardNormal));
            }
        }

        foreach (Vector3I stale in _sparseExposureRemovalScratch)
        {
            RemoveSparseExposureCandidate(chunk, stale);
        }

        ReplaceSparseOverlayNode(chunk, batches, treeBatches);
        FinishSparseExposureBuild(started);
    }

    private void FinishSparseExposureBuild(ulong started)
    {
        LastSparseExposureOverlayBuildMilliseconds = (Time.GetTicksUsec() - started) / 1000.0;
        _sparseOverlayBuildTotalMilliseconds += LastSparseExposureOverlayBuildMilliseconds;
        SparseExposureOverlayBuilds++;
    }

    private bool IsRepresentedByFullSurfaceBase(ChunkCoord chunk, Vector3I voxel)
    {
        Vector3I normal = _world.Source.GetOutwardNormal(voxel);
        if (!IsFullSurfaceNormalRelevantToChunk(chunk, normal))
        {
            return false;
        }

        int tangentA;
        int tangentB;
        if (Math.Abs(normal.X) == 1)
        {
            tangentA = voxel.Y;
            tangentB = voxel.Z;
        }
        else if (Math.Abs(normal.Y) == 1)
        {
            tangentA = voxel.X;
            tangentB = voxel.Z;
        }
        else
        {
            tangentA = voxel.X;
            tangentB = voxel.Y;
        }

        if (!_world.Source.TrySampleOutermostSurfaceVoxel(
                normal,
                tangentA,
                tangentB,
                out Vector3I visibleVoxel,
                out BlockSample visibleSample))
        {
            return false;
        }

        if (!ResolveVisibleStreamedVoxel(normal, ref visibleVoxel, ref visibleSample))
        {
            return false;
        }

        return visibleVoxel == voxel
            && ChunkCoord.FromVoxel(visibleVoxel, _world.Profile.ChunkSize) == chunk
            && _world.Source.GetOutwardNormal(visibleVoxel) == normal;
    }

    private bool IsFullSurfaceNormalRelevantToChunk(ChunkCoord chunk, Vector3I normal)
    {
        int depth = Math.Max(1, _world.Profile.DetailedSurfaceDepthChunks);
        int min = _world.MinChunkCoordinate;
        int max = _world.MaxChunkCoordinate;

        if (normal == Vector3I.Right) return max - chunk.X < depth;
        if (normal == Vector3I.Left) return chunk.X - min < depth;
        if (normal == Vector3I.Up) return max - chunk.Y < depth;
        if (normal == Vector3I.Down) return chunk.Y - min < depth;
        if (normal == Vector3I.Back) return max - chunk.Z < depth;
        return chunk.Z - min < depth;
    }

    private void ReplaceSparseOverlayNode(
        ChunkCoord chunk,
        Dictionary<string, List<Transform3D>> batches,
        Dictionary<string, List<Transform3D>> treeBatches)
    {
        _chunkRoots.TryGetValue(chunk, out Node3D? root);
        Node3D? oldOverlay = root?.GetNodeOrNull<Node3D>(SparseOverlayNodeName);
        if (oldOverlay is not null)
        {
            root!.RemoveChild(oldOverlay);
            oldOverlay.QueueFree();
        }

        bool hasContent = batches.Count > 0 || treeBatches.Count > 0;
        if (!hasContent)
        {
            if (root is not null && root.Name.ToString().StartsWith("SparseExposureChunk_", StringComparison.Ordinal))
            {
                _chunkRoots.Remove(chunk);
                root.QueueFree();
            }
            return;
        }

        if (root is null)
        {
            root = new Node3D { Name = $"SparseExposureChunk_{chunk.X}_{chunk.Y}_{chunk.Z}" };
            AddChild(root);
            _chunkRoots[chunk] = root;
            _resolvedChunks.Add(chunk);
            root.Visible = IsChunkPresentationRelevant(chunk);
        }

        var overlay = new Node3D { Name = SparseOverlayNodeName };
        root.AddChild(overlay);
        foreach ((string blockId, List<Transform3D> transforms) in batches)
        {
            AddBatch(overlay, blockId, transforms, true);
        }
        foreach ((string variant, List<Transform3D> transforms) in treeBatches)
        {
            AddBatch(overlay, variant, transforms, true);
        }

        // Chunk count may not change when an overlay is replaced under an existing surface root, which
        // means the pose cache would otherwise skip the next visibility pass. Force a refresh so newly
        // created cavity geometry immediately receives the correct conservative culling/LOD policy.
        _visibilityPoseInitialized = false;
    }

    private void RemoveSparseOverlayNode(ChunkCoord chunk)
    {
        if (!_chunkRoots.TryGetValue(chunk, out Node3D? root)) return;
        Node3D? oldOverlay = root.GetNodeOrNull<Node3D>(SparseOverlayNodeName);
        if (oldOverlay is not null)
        {
            root.RemoveChild(oldOverlay);
            oldOverlay.QueueFree();
            _visibilityPoseInitialized = false;
        }

        if (root.Name.ToString().StartsWith("SparseExposureChunk_", StringComparison.Ordinal))
        {
            _chunkRoots.Remove(chunk);
            root.QueueFree();
        }
    }

    private sealed partial class SparseExposureWorker : Node
    {
        private readonly WorldView _owner;

        public SparseExposureWorker(WorldView owner)
        {
            _owner = owner;
        }

        public override void _Process(double delta)
        {
            _ = delta;
            _owner.ProcessSparseExposureQueue();
        }
    }
}
