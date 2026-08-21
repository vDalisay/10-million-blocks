using System;
using Godot;
using TenMillionBlocks.Mining;
using TenMillionBlocks.World.Generation;

namespace TenMillionBlocks.UI;

public partial class IncrementalFeedbackView
{
    private static readonly string[] FeedbackTreeVariants =
    [
        "tree_1_a", "tree_1_b", "tree_2_a", "tree_2_b", "tree_2_c",
        "tree_3_a", "tree_3_b", "tree_4_a", "tree_4_b",
    ];

    private bool _treeFeedbackSubscribed;

    public override void _EnterTree()
    {
        // GameRoot initializes the view before adding it to the session tree. Keep this separate from
        // authoritative mining feedback: a harvested procedural feature is presentation-only and does
        // not award a second block/resource merely because its miniature also flies to the counter.
        if (_treeFeedbackSubscribed || _mining is null) return;
        _mining.BlockMined += OnTreeFeedbackBlockMined;
        _treeFeedbackSubscribed = true;
    }

    private void OnTreeFeedbackBlockMined(MiningResult result)
    {
        if (!result.Success || !result.Removed || result.Source == MiningSource.Offline)
        {
            return;
        }

        // Procedural tree resolution is significantly more expensive than a screen projection and is
        // only meaningful on the two grass surface block classes. Reject deep rock/ore and off-screen
        // automation first; previously every automated block could enter TrySampleTree before we knew
        // whether its feedback could contribute a pixel.
        if (result.BlockId != _world.Profile.SurfaceBlock
            && result.BlockId != _world.Profile.SurfaceEdgeBlock)
        {
            return;
        }
        if (!TryProjectSource(result.Voxel, out Vector2 source)
            || !_world.Source.TrySampleTree(result.Voxel, out _))
        {
            return;
        }

        if (_activeFlights.Count >= MaxActiveFlights)
        {
            DroppedFeedbackCount++;
            return;
        }

        string treeVariant = PickFeedbackTreeVariant(result.Voxel);
        PickupFlight flight = _flightPool.Count > 0 ? _flightPool.Pop() : CreateFlight();
        flight.Root.Visible = true;
        flight.Root.Modulate = Colors.White;
        flight.Root.Scale = Vector2.One * 1.08f;
        flight.Icon.Texture = GetPreviewTexture(treeVariant);
        flight.Amount.Text = string.Empty;
        flight.Target = _blocksChip.Root;
        flight.Start = source;
        flight.Age = 0.0f;
        flight.Duration = 0.76f;
        flight.Root.Position = source - new Vector2(26.0f, 26.0f);
        _activeFlights.Add(flight);
        SpawnedFeedbackCount++;
    }

    private string PickFeedbackTreeVariant(Vector3I voxel)
    {
        float roll = DeterministicNoise.Hash01(
            voxel.X,
            voxel.Y,
            voxel.Z,
            _world.Profile.Seed + 44017);
        int index = Math.Clamp((int)(roll * FeedbackTreeVariants.Length), 0, FeedbackTreeVariants.Length - 1);
        return FeedbackTreeVariants[index];
    }
}
