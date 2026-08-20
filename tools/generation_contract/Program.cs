using Godot;
using TenMillionBlocks.Content;
using TenMillionBlocks.World.Generation;

static WorldProfile MakeProfile(int seed, float baseRadius)
    => new()
    {
        Id = $"ci_generation_{seed}_{baseRadius:0.0}",
        DisplayName = "CI generation contract",
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

        // Minecraft-like terrain contract: natural terrain is emitted as face columns filled
        // continuously inward. A visually isolated bump may still be a deliberate height step, but a
        // truly floating/hanging terrain cube is impossible because its immediate inward support must
        // exist. Trees/features are separate and are intentionally not represented by SampleVoxel.
        Vector3I inward = voxel - normal;
        Require(
            source.SampleVoxel(inward).Present,
            $"Unsupported terrain at {voxel} ({sample.BlockId}); inward support {inward} is empty for seed {profile.Seed}.");

        Require(
            !(IsSeam(voxel) && sample.BlockId == profile.SurfaceEdgeBlock),
            $"Cube seam at {voxel} used dirt-sided surface-edge material for seed {profile.Seed}.");
    }

    // Validate lake topology on every cube face using the same outer-surface query the renderer and
    // authoring tool rely on. Boundary water must have a beach, deep-water material must remain in
    // the interior, and no isolated/one-cell water line is accepted.
    Vector3I[] faces =
    [
        Vector3I.Right, Vector3I.Left, Vector3I.Up, Vector3I.Down, Vector3I.Back, Vector3I.Forward,
    ];
    int faceRange = Math.Max(2, (int)MathF.Ceiling(profile.BaseRadius + profile.TerrainAmplitude + profile.DetailAmplitude + 2.0f));

    foreach (Vector3I face in faces)
    {
        var cells = new Dictionary<(int U, int V), (Vector3I Voxel, BlockSample Sample)>();
        for (int v = -faceRange; v <= faceRange; v++)
        for (int u = -faceRange; u <= faceRange; u++)
        {
            if (source.TrySampleOutermostSurfaceVoxel(face, u, v, out Vector3I voxel, out BlockSample sample))
            {
                cells[(u, v)] = (voxel, sample);
            }
        }

        foreach (((int u, int v), (Vector3I voxel, BlockSample sample)) in cells)
        {
            if (!IsWater(profile, sample.BlockId)) continue;

            (int U, int V)[] neighbours = [(u + 1, v), (u - 1, v), (u, v + 1), (u, v - 1)];
            int waterNeighbours = 0;
            bool boundary = false;
            foreach ((int nu, int nv) in neighbours)
            {
                if (!cells.TryGetValue((nu, nv), out var neighbour))
                {
                    boundary = true;
                    continue;
                }

                if (IsWater(profile, neighbour.Sample.BlockId))
                {
                    waterNeighbours++;
                    continue;
                }

                boundary = true;
                int waterRadial = Radial(voxel, face);
                int dryRadial = Radial(neighbour.Voxel, face);
                if (Math.Abs(dryRadial - waterRadial) <= 1)
                {
                    Require(
                        neighbour.Sample.BlockId == profile.SandBlock,
                        $"Water at {voxel} touches non-sand shoreline {neighbour.Voxel} ({neighbour.Sample.BlockId}) for seed {profile.Seed}.");
                }
            }

            Require(
                waterNeighbours >= 2,
                $"Water surface at {voxel} forms an isolated/narrow line ({waterNeighbours} water neighbours) for seed {profile.Seed}.");
            Require(
                !(sample.BlockId == profile.DeepWaterBlock && boundary),
                $"Deep-water material reached shoreline at {voxel} for seed {profile.Seed}.");
        }
    }

    Require(present > 0, $"Generation produced no terrain for seed {profile.Seed}.");
    Require(exposedSolid > 0, $"Generation produced no exposed solid terrain for seed {profile.Seed}.");
    Require(water > 0, $"Generation test profile produced no water for seed {profile.Seed}; hydrology contract is not being exercised.");
    Require(sand > 0, $"Generation test profile produced no sand for seed {profile.Seed}; shoreline contract is not being exercised.");

    Console.WriteLine(
        $"generation contract seed={profile.Seed} base={profile.BaseRadius:0.0}: blocks={present:N0}, exposed={exposedSolid:N0}, water={water:N0}, sand={sand:N0}");
}

// Include the two user-observed authoring seeds plus the canonical Verdant seed. Test both the old
// compact authoring radius and the actual ~20-block main-world radius so regressions cannot hide in
// one scale only.
foreach (int seed in new[] { 73021, 73323, 1939109028 })
{
    ValidateProfile(MakeProfile(seed, 5.0f));
    ValidateProfile(MakeProfile(seed, 9.5f));
}

Console.WriteLine("deterministic generation contracts passed");
