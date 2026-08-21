using System.Text.Json;
using Godot;
using TenMillionBlocks.Content;
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
    => id == p.WaterBlock || id == p.ShallowWaterBlock || id == p.DeepWaterBlock;

static int Radial(Vector3I voxel, Vector3I normal)
    => normal.X != 0 ? voxel.X * normal.X : normal.Y != 0 ? voxel.Y * normal.Y : voxel.Z * normal.Z;

static bool IsSeam(Vector3I voxel)
{
    int ax = Math.Abs(voxel.X);
    int ay = Math.Abs(voxel.Y);
    int az = Math.Abs(voxel.Z);
    int max = Math.Max(ax, Math.Max(ay, az));
    int ties = (ax == max ? 1 : 0) + (ay == max ? 1 : 0) + (az == max ? 1 : 0);
    return max > 0 && ties >= 2;
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
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

static void ValidateProfile(WorldProfile profile)
{
    var source = new ProceduralWorldSource(profile);
    int max = profile.MaxCoordinate;
    long present = 0;
    long exposedSolid = 0;
    long water = 0;
    long sand = 0;

    for (int z = -max; z <= max; z++)
    for (int y = -max; y <= max; y++)
    for (int x = -max; x <= max; x++)
    {
        var voxel = new Vector3I(x, y, z);
        BlockSample sample = source.SampleVoxel(voxel);
        if (!sample.Present) continue;
        present++;
        if (IsWater(profile, sample.BlockId)) water++;
        if (sample.BlockId == profile.SandBlock) sand++;

        Vector3I normal = source.GetOutwardNormal(voxel);
        bool exposedOutward = !source.SampleVoxel(voxel + normal).Present;
        if (!exposedOutward || IsWater(profile, sample.BlockId)) continue;
        exposedSolid++;

        // Minecraft-like support invariant: natural terrain can step, but cannot float. Every exposed
        // terrain cube must have a present block immediately inward toward the cube body.
        Vector3I inward = voxel - normal;
        Require(
            source.SampleVoxel(inward).Present,
            $"Unsupported terrain at {voxel} ({sample.BlockId}); inward support {inward} is empty in {profile.Id}, seed {profile.Seed}.");
        Require(
            !(IsSeam(voxel) && sample.BlockId == profile.SurfaceEdgeBlock),
            $"Cube seam at {voxel} used dirt-sided surface-edge material in {profile.Id}, seed {profile.Seed}.");
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
            if (!source.TrySampleOutermostSurfaceVoxel(face, u, v, out Vector3I voxel, out BlockSample sample))
            {
                continue;
            }

            // A seam coordinate can be reachable through two face projections. Runtime rendering keeps
            // it only on the face that actually owns the resolved voxel, so the art contract must test
            // that same visible topology instead of comparing a cross-face alias against unrelated
            // neighbours on the losing projection.
            if (source.GetOutwardNormal(voxel) != face) continue;
            cells[(u, v)] = (voxel, sample);
        }

        foreach (((int u, int v), (Vector3I voxel, BlockSample sample)) in cells)
        {
            if (!IsWater(profile, sample.BlockId)) continue;
            bool boundary = false;
            (int U, int V)[] neighbours = [(u + 1, v), (u - 1, v), (u, v + 1), (u, v - 1)];
            foreach ((int nu, int nv) in neighbours)
            {
                if (!cells.TryGetValue((nu, nv), out var neighbour))
                {
                    boundary = true;
                    continue;
                }
                if (IsWater(profile, neighbour.Sample.BlockId)) continue;

                boundary = true;
                int waterRadial = Radial(voxel, face);
                int dryRadial = Radial(neighbour.Voxel, face);
                if (Math.Abs(dryRadial - waterRadial) <= 1)
                {
                    Require(
                        neighbour.Sample.BlockId == profile.SandBlock,
                        $"Water at {voxel} touches non-sand shoreline {neighbour.Voxel} ({neighbour.Sample.BlockId}) in {profile.Id}, seed {profile.Seed}.");
                }
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
}

static IReadOnlyList<WorldProfile> LoadCommittedProfiles()
{
    string? current = Directory.GetCurrentDirectory();
    while (!string.IsNullOrWhiteSpace(current))
    {
        string candidate = Path.Combine(current, "data", "worlds", "worlds.json");
        if (File.Exists(candidate))
        {
            string json = File.ReadAllText(candidate);
            ProfileDocument? document = JsonSerializer.Deserialize<ProfileDocument>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            return document?.Worlds ?? Array.Empty<WorldProfile>();
        }
        current = Directory.GetParent(current)?.FullName;
    }

    throw new InvalidOperationException("Could not locate data/worlds/worlds.json for shipped-generation contracts.");
}

// Include both user-observed candidate seeds plus canonical Verdant. Validate compact authoring scale
// and actual first-main-world radius so regressions cannot hide at one cube size.
foreach (int seed in new[] { 73021, 73323, 1939109028 })
{
    ValidateProfile(MakeProfile(seed, 5.0f));
    ValidateProfile(MakeProfile(seed, 9.5f));
}

// The same structural invariants must hold for every committed procedural Steam-demo world, not just
// compact reproductions. This is intentionally exact and makes a bad reviewed seed/profile a CI failure
// before a player can ever see floating terrain, water ribbons, grass shorelines or deep-water edges.
IReadOnlyList<WorldProfile> committedProfiles = LoadCommittedProfiles();
foreach (string worldId in new[] { "reference_natural", "reference_lakes", "reference_ridges" })
{
    WorldProfile profile = committedProfiles.Single(item => item.Id == worldId);
    ValidateProfile(profile);
}

Console.WriteLine("deterministic generation contracts passed");

internal sealed class ProfileDocument
{
    public List<WorldProfile> Worlds { get; set; } = new();
}
