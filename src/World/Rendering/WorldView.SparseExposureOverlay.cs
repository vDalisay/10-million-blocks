using System;
using System.Collections.Generic;
using Godot;
using TenMillionBlocks.World.Generation;

namespace TenMillionBlocks.World.Rendering;

/// <summary>
/// Full-surface worlds normally need only one generated surface sample per face column. Mining used to
/// switch an affected 16^3 chunk to RebuildEagerChunk so tunnel side walls were correct, but that turns
/// one click into thousands of generated voxel/exposure queries and was measured at 10-23 ms per chunk
/// in the 100^3 stress world.
///
/// Keep the cheap column renderer authoritative for the outward-facing surface and add only the sparse
/// newly-exposed frontier around mined voxels as a child overlay. The work is proportional to modified
/// state, coalesced per chunk and frame-budgeted. This preserves tunnel/cavity side walls without ever
/// rescanning an untouched chunk volume after every click.
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
    private readonly HashSet<Vector3I> _sparseOverlayCandidateScratch = new();
    private SparseExposureWorker? _sparseExposureWorker;
    private double _sparseOverlayBuildTotalMilliseconds;

    public int PendingSparseExposureOverlays => _sparseOverlayDirtyChunks.Count;
    public long SparseExposureOverlayBuilds { get; private set; }
    public double LastSparseExposureOverlayBuildMilliseconds { get; private set; }
    public double AverageSparseExposureOverlayBuildMilliseconds
        => SparseExposureOverlayBuilds == 0
            ? 0.0
            : _sparseOverlayBuildTotalMilliseconds / SparseExposureOverlayBuilds;

    /// <summary>
    /// Shared mining/automation dirty path. Full-surface worlds intentionally do not set forceExact:
    /// their base chunk is rebuilt through the cheap surface-column path while the sparse overlay below
    /// accounts for non-column tunnel walls. Other renderer modes retain their existing cheap behavior.
    /// </summary>
    private void MarkInteractiveChunkDirty(ChunkCoord chunk)
    {
        if (!ChunkInWorldBounds(chunk)) return;
        MarkChunkDirty(chunk, forceExact: false);
        if (FullSurfaceRenderer)
        {
            QueueSparseExposureOverlay(chunk);
        }
    }

    private void QueueSparseExposureOverlay(ChunkCoord chunk)
    {
        if (!FullSurfaceRenderer || !ChunkInWorldBounds(chunk)) return;
        _sparseOverlayDirtyChunks.Add(chunk);
        EnsureSparseExposureWorker();
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
        _sparseOverlayCandidateScratch.Clear();

        int chunkSize = _world.Profile.ChunkSize;
        int max = _world.MaxCoordinate;

        // Exposure in this render chunk can be caused by a mined voxel in the chunk itself or by one
        // in an immediately adjacent chunk at the shared boundary. Enumerate only those sparse mined
        // addresses; untouched voxels never enter this path.
        foreach (ChunkCoord offset in SparseOverlaySourceOffsets)
        {
            ChunkCoord sourceChunk = new(
                chunk.X + offset.X,
                chunk.Y + offset.Y,
                chunk.Z + offset.Z);
            if (!ChunkInWorldBounds(sourceChunk)) continue;

            IReadOnlyCollection<int> minedIndices = _world.State.GetMinedLocalIndices(sourceChunk);
            if (minedIndices.Count == 0) continue;

            Vector3I sourceMin = sourceChunk.MinVoxel(chunkSize);
            foreach (int index in minedIndices)
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
                        _sparseOverlayCandidateScratch.Add(candidate);
                    }
                }
            }
        }

        var batches = new Dictionary<string, List<Transform3D>>(StringComparer.Ordinal);
        var treeBatches = new Dictionary<string, List<Transform3D>>(StringComparer.Ordinal);

        foreach (Vector3I voxel in _sparseOverlayCandidateScratch)
        {
            if (Math.Abs(voxel.X) > max || Math.Abs(voxel.Y) > max || Math.Abs(voxel.Z) > max)
            {
                continue;
            }

            BlockSample sample = _world.SampleVoxel(voxel);
            if (!sample.Present || !_world.IsExposed(voxel, sample))
            {
                continue;
            }

            // The base surface-column chunk already renders the first surviving block in each outward
            // column. Only add candidates that column rendering cannot represent, i.e. tunnel/cavity
            // side walls and modified interior chunks. This avoids duplicate coplanar instances.
            if (IsRepresentedByFullSurfaceBase(chunk, voxel))
            {
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

        ReplaceSparseOverlayNode(chunk, batches, treeBatches);

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

            if (_camera?.Camera is not null)
            {
                int chunkSize = _world.Profile.ChunkSize;
                Vector3I minVoxel = chunk.MinVoxel(chunkSize);
                Vector3I centerVoxel = minVoxel + new Vector3I(chunkSize / 2, chunkSize / 2, chunkSize / 2);
                Vector3 toCamera = _camera.Camera.GlobalPosition - VoxelToWorld(centerVoxel);
                root.Visible = IsFullSurfaceChunkCameraFacing(chunk, centerVoxel, toCamera);
            }
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
