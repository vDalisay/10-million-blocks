using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using TenMillionBlocks.Content;
using TenMillionBlocks.Presentation;
using TenMillionBlocks.World;
using TenMillionBlocks.World.Authoring;
using TenMillionBlocks.World.Rendering;

namespace TenMillionBlocks.Tools.WorldAuthoring;

public partial class WorldAuthoringRoot : Node
{
    private ContentDatabase _content = null!;
    private BlockAssetRegistry _assets = null!;
    private WorldCatalog _worlds = null!;
    private OrbitCameraController _camera = null!;
    private CloudField _clouds = null!;
    private Node3D? _previewRoot;

    private OptionButton _profilePicker = null!;
    private SpinBox _seed = null!;
    private Label _status = null!;
    private Label _metrics = null!;
    private VBoxContainer _candidateList = null!;
    private readonly List<WorldProfile> _profiles = new();

    public override void _Ready()
    {
        try
        {
            _content = ContentDatabase.Load();
            _assets = new BlockAssetRegistry(_content);
            _assets.ValidateAndPreload();
            _worlds = WorldCatalog.Load();

            BuildEnvironment();
            BuildCameraAndClouds();
            BuildUi();
            PopulateProfiles();
            if (_profiles.Count == 0)
            {
                throw new InvalidOperationException("No authorable procedural profiles were found.");
            }

            SelectPreferredProfile("reference_natural");
            LoadSelectedProfileSeed();
            RegeneratePreview(analyze: true);
        }
        catch (Exception exception)
        {
            GD.PushError($"World authoring tool failed to initialize:\n{exception}");
            ShowFatal(exception.Message);
        }
    }

    private void BuildEnvironment()
    {
        RenderingServer.SetDefaultClearColor(new Color(0.003f, 0.008f, 0.025f));
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.003f, 0.008f, 0.025f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.74f, 0.78f, 0.84f),
            AmbientLightEnergy = 0.42f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapWhite = 2.0f,
            SsaoEnabled = true,
            SsaoRadius = 1.6f,
            SsaoIntensity = 2.4f,
        };
        AddChild(new WorldEnvironment { Environment = environment });

        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-52.0f, -34.0f, 0.0f),
            LightColor = new Color(1.0f, 0.98f, 0.94f),
            LightEnergy = 1.05f,
            LightSpecular = 0.0f,
            ShadowEnabled = true,
        });
        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(24.0f, 146.0f, 0.0f),
            LightColor = new Color(0.72f, 0.82f, 1.0f),
            LightEnergy = 0.45f,
            LightSpecular = 0.0f,
            ShadowEnabled = false,
        });
    }

    private void BuildCameraAndClouds()
    {
        _clouds = new CloudField { Name = "AuthoringClouds" };
        AddChild(_clouds);

        _camera = new OrbitCameraController { Name = "AuthoringCamera" };
        AddChild(_camera);
    }

    private void BuildUi()
    {
        var layer = new CanvasLayer { Layer = 50 };
        AddChild(layer);

        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(root);

        var panel = new PanelContainer
        {
            AnchorBottom = 1.0f,
            OffsetLeft = 16.0f,
            OffsetTop = 16.0f,
            OffsetRight = 390.0f,
            OffsetBottom = -16.0f,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        root.AddChild(panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 14);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_right", 14);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        panel.AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        margin.AddChild(column);

        var title = new Label { Text = "WORLD AUTHORING" };
        title.AddThemeFontSizeOverride("font_size", 23);
        column.AddChild(title);
        column.AddChild(new Label
        {
            Text = "Runtime-backed candidate preview. Camera: RMB orbit, MMB pan, wheel zoom.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });

        _profilePicker = new OptionButton();
        _profilePicker.ItemSelected += _ =>
        {
            LoadSelectedProfileSeed();
            RegeneratePreview(analyze: true);
        };
        column.AddChild(new Label { Text = "Base profile" });
        column.AddChild(_profilePicker);

        _seed = new SpinBox
        {
            MinValue = 1,
            MaxValue = int.MaxValue,
            Step = 1,
            AllowGreater = false,
            CustomMinimumSize = new Vector2(0, 36),
        };
        column.AddChild(new Label { Text = "Candidate seed" });
        column.AddChild(_seed);

        var buttonRow = new HBoxContainer();
        buttonRow.AddThemeConstantOverride("separation", 6);
        column.AddChild(buttonRow);

        var regenerate = new Button { Text = "Regenerate", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        regenerate.Pressed += () => RegeneratePreview(analyze: true);
        buttonRow.AddChild(regenerate);

        var randomize = new Button { Text = "Random seed", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        randomize.Pressed += RandomizeSeed;
        buttonRow.AddChild(randomize);

        var browse = new Button
        {
            Text = "Browse 8 candidate seeds",
            CustomMinimumSize = new Vector2(0, 38),
        };
        browse.Pressed += BrowseCandidates;
        column.AddChild(browse);

        var export = new Button
        {
            Text = "Export current draft",
            CustomMinimumSize = new Vector2(0, 38),
        };
        export.Pressed += ExportDraft;
        column.AddChild(export);

        _status = new Label
        {
            Text = "Ready",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        column.AddChild(_status);

        _metrics = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0, 150),
        };
        column.AddChild(_metrics);

        column.AddChild(new HSeparator());
        column.AddChild(new Label { Text = "Candidate ranking" });

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 180),
        };
        column.AddChild(scroll);
        _candidateList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _candidateList.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_candidateList);
    }

    private void PopulateProfiles()
    {
        foreach (WorldProfile profile in _worlds.Worlds.Values
                     .Where(profile => !profile.UsesSingleBlockGenerator && !profile.UsesSolidCubeGenerator)
                     .Where(profile => profile.MaxCoordinate <= WorldAuthoringAnalyzer.MaximumExactAuthoringCoordinate)
                     .Where(profile => Math.Max(profile.LogicalWidth, Math.Max(profile.LogicalHeight, profile.LogicalDepth)) <= 50)
                     .OrderBy(profile => profile.LogicalWidth)
                     .ThenBy(profile => profile.Id, StringComparer.Ordinal))
        {
            _profiles.Add(profile);
            _profilePicker.AddItem($"{profile.DisplayName}  ({profile.LogicalWidth}³)");
        }
    }

    private void SelectPreferredProfile(string id)
    {
        int index = _profiles.FindIndex(profile => profile.Id == id);
        _profilePicker.Select(index >= 0 ? index : 0);
    }

    private WorldProfile SelectedProfile()
    {
        int index = Math.Clamp(_profilePicker.Selected, 0, _profiles.Count - 1);
        return _profiles[index];
    }

    private void LoadSelectedProfileSeed()
    {
        if (_profiles.Count == 0) return;
        _seed.Value = SelectedProfile().Seed;
    }

    private WorldProfile CandidateProfile(int? seedOverride = null)
    {
        WorldProfile source = SelectedProfile();
        string json = JsonSerializer.Serialize(source);
        WorldProfile? clone = JsonSerializer.Deserialize<WorldProfile>(json);
        if (clone is null) throw new InvalidOperationException("Could not clone selected world profile.");
        clone.Seed = seedOverride ?? checked((int)_seed.Value);
        clone.OverrideFile = string.Empty;
        return clone;
    }

    private void RegeneratePreview(bool analyze)
    {
        WorldProfile profile = CandidateProfile();
        _status.Text = $"Generating {profile.Id} seed {profile.Seed:N0}...";

        if (_previewRoot is not null)
        {
            RemoveChild(_previewRoot);
            _previewRoot.QueueFree();
        }

        _previewRoot = new Node3D { Name = "WorldAuthoringPreview" };
        AddChild(_previewRoot);
        var world = new VirtualWorld(profile);
        long blocks = world.InitializeMineableBlockCount();
        var view = new WorldView { Name = "WorldView" };
        _previewRoot.AddChild(view);
        view.Initialize(_assets, world, _camera);

        float extent = profile.BlockSpacing * (
            profile.BaseRadius + profile.TerrainAmplitude + profile.DetailAmplitude + MathF.Max(0.0f, profile.SeaLevelOffset));
        _camera.ConfigureWorldExtent(extent);
        _camera.ApplyPreset(OrbitCameraController.MediumPreset, immediate: true);
        _clouds.Visible = true;
        _clouds.SetWorldExtent(extent);

        _status.Text = $"Preview: {profile.Id}, seed {profile.Seed:N0}, authoritative count {blocks:N0}.";
        if (analyze)
        {
            ShowMetrics(profile, WorldAuthoringAnalyzer.Analyze(profile));
        }
    }

    private void ShowMetrics(WorldProfile profile, WorldAuthoringMetrics metrics)
    {
        double score = WorldAuthoringAnalyzer.ScoreVerdantCandidate(metrics);
        _metrics.Text =
            $"Exact candidate metrics\n" +
            $"Mineable: {metrics.MineableBlocks:N0}   Exposed: {metrics.ExposedBlocks:N0}\n" +
            $"Water surface: {metrics.WaterCoverage:P1}   Soft terrain: {metrics.SoftTerrainCoverage:P1}\n" +
            $"Exposed stone: {metrics.ExposedStoneCoverage:P1}   Trees: {metrics.TreeCount:N0}\n" +
            $"Generated gems: {metrics.GemCount:N0}   Verdant mix score: {score:P0}\n" +
            $"Profile dimensions metadata: {profile.LogicalWidth} x {profile.LogicalHeight} x {profile.LogicalDepth}";
    }

    private void RandomizeSeed()
    {
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        _seed.Value = rng.RandiRange(1, int.MaxValue);
        RegeneratePreview(analyze: true);
    }

    private void BrowseCandidates()
    {
        foreach (Node child in _candidateList.GetChildren()) child.QueueFree();
        var rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)SelectedProfile().Seed * 6364136223846793005UL + 1442695040888963407UL);
        var candidates = new List<(int Seed, double Score, WorldAuthoringMetrics Metrics)>();

        _status.Text = "Scanning 8 deterministic candidate seeds with the runtime generator...";
        for (int i = 0; i < 8; i++)
        {
            int seed = rng.RandiRange(1, int.MaxValue);
            WorldProfile profile = CandidateProfile(seed);
            WorldAuthoringMetrics metrics = WorldAuthoringAnalyzer.Analyze(profile);
            candidates.Add((seed, WorldAuthoringAnalyzer.ScoreVerdantCandidate(metrics), metrics));
        }

        foreach ((int seed, double score, WorldAuthoringMetrics metrics) in candidates.OrderByDescending(item => item.Score))
        {
            var button = new Button
            {
                Text = $"Seed {seed:N0}  ·  score {score:P0}  ·  water {metrics.WaterCoverage:P0}  ·  trees {metrics.TreeCount:N0}",
                Alignment = HorizontalAlignment.Left,
                TooltipText = $"Soft {metrics.SoftTerrainCoverage:P1}, exposed stone {metrics.ExposedStoneCoverage:P1}, gems {metrics.GemCount:N0}",
            };
            int selectedSeed = seed;
            button.Pressed += () =>
            {
                _seed.Value = selectedSeed;
                RegeneratePreview(analyze: true);
            };
            _candidateList.AddChild(button);
        }

        (int Seed, double Score, WorldAuthoringMetrics Metrics) best = candidates.OrderByDescending(item => item.Score).First();
        _status.Text = $"Candidate scan complete. Best broad-mix seed: {best.Seed:N0} ({best.Score:P0}). Visual review still decides.";
    }

    private void ExportDraft()
    {
        WorldProfile profile = CandidateProfile();
        string directory = ProjectSettings.GlobalizePath("user://world_authoring_drafts");
        System.IO.Directory.CreateDirectory(directory);
        string relative = $"user://world_authoring_drafts/{profile.Id}_seed_{profile.Seed}.json";
        string json = JsonSerializer.Serialize(profile, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        });
        using Godot.FileAccess file = Godot.FileAccess.Open(relative, Godot.FileAccess.ModeFlags.Write);
        if (file is null) throw new InvalidOperationException($"Could not write authoring draft '{relative}'.");
        file.StoreString(json);
        _status.Text = $"Draft exported to {relative}. Freeze-for-shipping remains a deliberate later step.";
    }

    private void ShowFatal(string message)
    {
        var layer = new CanvasLayer { Layer = 100 };
        AddChild(layer);
        var label = new Label
        {
            Position = new Vector2(24, 24),
            Text = "World authoring startup failed\n\n" + message,
        };
        layer.AddChild(label);
    }
}
