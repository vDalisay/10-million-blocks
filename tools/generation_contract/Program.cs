using System.Text.Json;
using Godot;
using TenMillionBlocks.Content;
using TenMillionBlocks.World;
using TenMillionBlocks.World.Authoring;
using TenMillionBlocks.World.Generation;

static WorldProfile MakeProfile(int seed, float baseRadius)
    => new()
    {
        Id = $"ci_generation_{seed}_{baseRadius:0.0}",
        DisplayName = "CI generation contract",
        WorldVersion = 1,
        GenerationVersion = 1,
        GenerationMode = "procedural",
        Seed = seed,
        LogicalWidth = 20,
        LogicalHeight = 20,
        LogicalDepth = 20,
        BaseRadius = baseRadius,
        TerrainAmplitude = 1.75f,
        DetailAmplitude = 0.65f,
        MacroFrequency = 1.10f,
        DetailFrequency = 4.25f,
        ClimateFrequency = 0.85f,
        ErosionFrequency = 1.10f,
        RidgeFrequency = 2.05f,
        OceanThreshold = -0.11f,
        SeaLevelOffset = 0.85f,
        ShoreBand = 0.20f,
        PlateauStep = 0.5f,
        ForestThreshold = -0.02f,
        WaterThreshold = 0.43f,
        TreeDensity = 0.12f,
        ChunkSize = 8,
        RegionSizeInChunks = 8,
        BlockSpacing = 2.0f,
        SurfaceBlock = "grass",
        SurfaceEdgeBlock = "dirt_grass",
        SoilBlock = "dirt",
        StoneBlock = "stone",
        DarkStoneBlock = "stone_dark",
        SandBlock = "sand",
        WaterBlock = "water",
        ShallowWaterBlock = "water_shallow",
        DeepWaterBlock = "water_deep",
        CopperBlock = "copper",
        SilverBlock = "silver",
        GoldBlock = "gold",
    };

static bool IsWater(WorldProfile p, string id)
    => WorldStructuralRules.IsWater(p, id);

static int Radial(Vector3I voxel, Vector3I normal)
    => normal.X != 0 ? voxel.X * normal.X : normal.Y != 0 ? voxel.Y * normal.Y : voxel.Z * normal.Z;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static bool TryFindRuntimeFaceSurface(
    VirtualWorld world,
    Vector3I face,
    int u,
    int v,
    out Vector3I voxel,
    out BlockSample sample)
{
    for (int radial = world.MaxCoordinate; radial >= 0; radial--)
    {
        Vector3I candidate = WorldStructuralRules.FaceVoxel(face, radial, u, v);
        if (WorldStructuralRules.DominantNormal(candidate) != face) continue;
        BlockSample candidateSample = world.SampleVoxel(candidate);
        if (!candidateSample.Present) continue;
        voxel = candidate;
        sample = candidateSample;
        return true;
    }

    voxel = default;
    sample = BlockSample.Empty;
    return false;
}

static void ValidateWaterComponents(
    WorldProfile profile,
    Vector3I face,
    Dictionary<(int U, int V), (Vector3I Voxel, BlockSample Sample)> cells)
{
    var waterKeys = new HashSet<(int U, int V)>(
        cells.Where(pair => IsWater(profile, pair.Value.Sample.BlockId)).Select(pair => pair.Key));
    var unvisited = new HashSet<(int U, int V)>(waterKeys);
    (int U, int V)[] steps = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    while (unvisited.Count > 0)
    {
        (int U, int V) start = unvisited.First();
        var queue = new Queue<(int U, int V)>();
        queue.Enqueue(start);
        unvisited.Remove(start);
        int count = 0;
        int minU = start.U;
        int maxU = start.U;
        int minV = start.V;
        int maxV = start.V;

        while (queue.Count > 0)
        {
            (int U, int V) current = queue.Dequeue();
            count++;
            minU = Math.Min(minU, current.U);
            maxU = Math.Max(maxU, current.U);
            minV = Math.Min(minV, current.V);
            maxV = Math.Max(maxV, current.V);

            foreach ((int du, int dv) in steps)
            {
                var next = (current.U + du, current.V + dv);
                if (!unvisited.Remove(next)) continue;
                queue.Enqueue(next);
            }
        }

        Require(
            count >= 3 && maxU > minU && maxV > minV,
            $"Water component on face {face} is a one-cell/narrow line: {count} cells, bounds U {minU}..{maxU}, V {minV}..{maxV}, world {profile.Id}, seed {profile.Seed}.");
    }
}

static long ValidateProfile(WorldProfile profile)
{
    var world = new VirtualWorld(profile);
    int max = profile.MaxCoordinate;
    long present = 0;
    long exposedSolid = 0;
    long water = 0;
    long sand = 0;
    int expectedWaterRadial = Math.Max(0, Mathf.FloorToInt(profile.BaseRadius + 0.001f) - 1);
    int faceBorder = Math.Max(0, Mathf.FloorToInt(profile.BaseRadius + 0.001f));

    for (int z = -max; z <= max; z++)
    for (int y = -max; y <= max; y++)
    for (int x = -max; x <= max; x++)
    {
        var voxel = new Vector3I(x, y, z);
        BlockSample sample = world.SampleVoxel(voxel);
        if (!sample.Present) continue;
        present++;
        if (IsWater(profile, sample.BlockId)) water++;
        if (sample.BlockId == profile.SandBlock) sand++;

        Vector3I normal = WorldStructuralRules.DominantNormal(voxel);
        WorldStructuralRules.GetFaceTangents(voxel, normal, out int u, out int v, out int radial);

        if (IsWater(profile, sample.BlockId))
        {
            Require(radial == expectedWaterRadial,
                $"Water at {voxel} is not inset to radial {expectedWaterRadial} in {profile.Id}, seed {profile.Seed}.");
            BlockSample outward = world.SampleVoxel(voxel + normal);
            Require(!outward.Present,
                $"Water at {voxel} has another block outward at {voxel + normal}; water must be the single inset surface layer.");
            BlockSample inward = world.SampleVoxel(voxel - normal);
            Require(inward.Present && !IsWater(profile, inward.BlockId) && inward.BlockId == profile.SandBlock,
                $"Water at {voxel} is not backed directly by sand; inward {voxel - normal} is '{inward.BlockId}'.");
            continue;
        }

        bool exposedOutward = !world.SampleVoxel(voxel + normal).Present;
        if (!exposedOutward) continue;
        exposedSolid++;

        Vector3I inwardSolid = voxel - normal;
        Require(
            world.SampleVoxel(inwardSolid).Present,
            $"Unsupported terrain at {voxel} ({sample.BlockId}); inward support {inwardSolid} is empty in {profile.Id}, seed {profile.Seed}.");
        Require(
            !(sample.BlockId == profile.SurfaceEdgeBlock && Math.Max(Math.Abs(u), Math.Abs(v)) >= faceBorder),
            $"Outer face border at {voxel} used dirt-sided surface-edge material in {profile.Id}, seed {profile.Seed}.");
    }

    Vector3I[] faces =
    [
        Vector3I.Right, Vector3I.Left, Vector3I.Up, Vector3I.Down, Vector3I.Back, Vector3I.Forward,
    ];
    int faceRange = Math.Max(2, (int)MathF.Ceiling(
        profile.BaseRadius + profile.TerrainAmplitude + profile.DetailAmplitude + 2.0f));

    foreach (Vector3I face in faces)
    {
        var cells = new Dictionary<(int U, int V), (Vector3I Voxel, BlockSample Sample)>();
        for (int v = -faceRange; v <= faceRange; v++)
        for (int u = -faceRange; u <= faceRange; u++)
        {
            if (!TryFindRuntimeFaceSurface(world, face, u, v, out Vector3I voxel, out BlockSample sample)) continue;
            cells[(u, v)] = (voxel, sample);
        }

        foreach (((int u, int v), (Vector3I voxel, BlockSample sample)) in cells)
        {
            if (sample.BlockId == profile.SurfaceEdgeBlock
                && Math.Max(Math.Abs(u), Math.Abs(v)) >= faceBorder)
            {
                throw new InvalidOperationException(
                    $"Visible outer line on face {face} at {voxel} is dirt-sided instead of uniform '{profile.SurfaceBlock}'.");
            }

            if (!IsWater(profile, sample.BlockId)) continue;
            bool boundary = false;
            int waterRadial = Radial(voxel, face);
            (int U, int V)[] neighbours = [(u + 1, v), (u - 1, v), (u, v + 1), (u, v - 1)];
            foreach ((int nu, int nv) in neighbours)
            {
                Require(cells.TryGetValue((nu, nv), out var neighbour),
                    $"Inset water at {voxel} has no surface block beside it at tangent ({nu},{nv}) on face {face}.");

                if (IsWater(profile, neighbour.Sample.BlockId))
                {
                    Require(Radial(neighbour.Voxel, face) == waterRadial,
                        $"Adjacent water cells {voxel} and {neighbour.Voxel} are on different radial levels.");
                    continue;
                }

                boundary = true;
                int dryRadial = Radial(neighbour.Voxel, face);
                Require(dryRadial >= waterRadial + 1,
                    $"Water at {voxel} is not visibly inset below shoreline {neighbour.Voxel} in {profile.Id}.");
                Require(neighbour.Sample.BlockId == profile.SandBlock,
                    $"Water at {voxel} touches non-sand shoreline {neighbour.Voxel} ({neighbour.Sample.BlockId}) in {profile.Id}, seed {profile.Seed}.");
            }

            Require(
                !(sample.BlockId == profile.DeepWaterBlock && boundary),
                $"Deep-water material reached shoreline at {voxel} in {profile.Id}, seed {profile.Seed}.");
        }

        ValidateWaterComponents(profile, face, cells);
    }

    Require(present > 0, $"Generation produced no terrain for {profile.Id}, seed {profile.Seed}.");
    Require(exposedSolid > 0, $"Generation produced no exposed solid terrain for {profile.Id}, seed {profile.Seed}.");
    Require(water > 0, $"Generation produced no water for {profile.Id}, seed {profile.Seed}; hydrology contract is not exercised.");
    Require(sand > 0, $"Generation produced no sand for {profile.Id}, seed {profile.Seed}; shoreline contract is not exercised.");

    Console.WriteLine(
        $"generation contract world={profile.Id} seed={profile.Seed} base={profile.BaseRadius:0.0}: " +
        $"blocks={present:N0}, exposed={exposedSolid:N0}, water={water:N0}, sand={sand:N0}");
    return present;
}

static IReadOnlyList<WorldProfile> LoadCommittedProfiles()
{
    string? current = Directory.GetCurrentDirectory();
    while (!string.IsNullOrWhiteSpace(current))
    {
        string candidate = Path.Combine(current, "data", "worlds", "worlds.json");
        if (File.Exists(candidate))
        {
            // Standalone .NET contract tools do not initialize Godot's res:// virtual filesystem.
            // Give runtime content loaders an explicit managed resource root before constructing any
            // committed profile that owns an authored override.
            System.Environment.SetEnvironmentVariable(WorldOverrideSet.ResourceRootEnvironmentVariable, current);

            string json = File.ReadAllText(candidate);
            ProfileDocument? document = JsonSerializer.Deserialize<ProfileDocument>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            return document?.Worlds is { } worlds ? worlds : Array.Empty<WorldProfile>();
        }
        current = Directory.GetParent(current)?.FullName;
    }

    throw new InvalidOperationException("Could not locate data/worlds/worlds.json for shipped-generation contracts.");
}

static void ValidateExactTutorialCompletion(IEnumerable<WorldProfile> profiles)
{
    foreach (WorldProfile profile in profiles.Where(item => item.Id.StartsWith("tutorial_", StringComparison.Ordinal)))
    {
        var world = new VirtualWorld(profile);
        long initial = world.InitializeMineableBlockCount();
        if (profile.TargetMineableBlocks > 0)
        {
            Require(initial == profile.TargetMineableBlocks,
                $"Tutorial {profile.Id} target is {profile.TargetMineableBlocks:N0}, exact world contains {initial:N0}.");
        }

        int max = world.MaxCoordinate;
        long removed = 0;
        for (int z = -max; z <= max; z++)
        for (int y = -max; y <= max; y++)
        for (int x = -max; x <= max; x++)
        {
            var voxel = new Vector3I(x, y, z);
            BlockSample sample = world.SampleVoxel(voxel);
            if (!sample.Present || !sample.Mineable) continue;
            if (world.TryMine(voxel, requireExposed: false, out _)) removed++;
        }

        Require(removed == initial,
            $"Tutorial {profile.Id} removed only {removed:N0}/{initial:N0}; exact region accounting hid or rejected blocks.");
        Require(world.RemainingMineableBlocks == 0 && world.State.MinedVoxelCount == initial,
            $"Tutorial {profile.Id} cannot reach a real zero-block completion state after all physical voxels are mined.");
        Console.WriteLine($"tutorial completion contract world={profile.Id}: exact clear {removed:N0}/{initial:N0}");
    }
}

foreach (int seed in new[] { 73021, 73323, 1939109028 })
{
    _ = ValidateProfile(MakeProfile(seed, 5.0f));
    _ = ValidateProfile(MakeProfile(seed, 9.5f));
}

IReadOnlyList<WorldProfile> committedProfiles = LoadCommittedProfiles();
ValidateExactTutorialCompletion(committedProfiles);

var expectedPhysicalCounts = new Dictionary<string, long>(StringComparer.Ordinal)
{
    ["reference_natural"] = 7_728L,
    ["reference_lakes"] = 64_611L,
    ["reference_ridges"] = 125_934L,
};
var countMismatches = new List<string>();

foreach ((string worldId, long expectedCount) in expectedPhysicalCounts)
{
    WorldProfile profile = committedProfiles.Single(item => item.Id == worldId);
    long generatedPresent = ValidateProfile(profile);
    WorldAuthoringMetrics metrics = WorldAuthoringAnalyzer.Analyze(profile);

    Console.WriteLine(
        $"authoring contract world={worldId}: mineable={metrics.MineableBlocks:N0}, trees={metrics.TreeCount:N0}, " +
        $"gems={metrics.GemCount:N0}, water={metrics.WaterCoverage:P1}, soft={metrics.SoftTerrainCoverage:P1}, " +
        $"stone={metrics.ExposedStoneCoverage:P1}");

    if (generatedPresent != expectedCount || metrics.MineableBlocks != expectedCount)
    {
        countMismatches.Add(
            $"{worldId}: reviewed {expectedCount:N0}, generated {generatedPresent:N0}, runtime {metrics.MineableBlocks:N0}");
    }
    Require(metrics.TreeCount > 0,
        $"Reviewed generated world {worldId} contains no trees; the combined-tool ecosystem contract regressed.");
    Require(metrics.GemCount > 0,
        $"Reviewed generated world {worldId} contains no special gems; late progression would lose its special-resource loop.");
}

if (countMismatches.Count > 0)
{
    throw new InvalidOperationException(
        "Reviewed physical block counts changed after a structural generation revision. Version/review the new deterministic baselines:\n - " +
        string.Join("\n - ", countMismatches));
}

Console.WriteLine("deterministic generation contracts passed");

internal sealed class ProfileDocument
{
    public List<WorldProfile> Worlds { get; set; } = new();
}
