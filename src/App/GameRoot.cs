using System;
using Godot;
using TenMillionBlocks.Content;
using TenMillionBlocks.Mining;
using TenMillionBlocks.Presentation;
using TenMillionBlocks.UI;
using TenMillionBlocks.World;
using TenMillionBlocks.World.Rendering;

namespace TenMillionBlocks.App;

public partial class GameRoot : Node3D
{
    public override void _Ready()
    {
        try
        {
            ContentDatabase content = ContentDatabase.Load();
            var assets = new BlockAssetRegistry(content);
            assets.ValidateAndPreload();

            WorldCatalog worlds = WorldCatalog.Load();
            WorldSelfTest.Run(worlds);

            WorldProfile profile = worlds.Get("reference_natural");
            var world = new VirtualWorld(profile);
            long blockCount = world.CountMineableBlocksExact();
            GD.Print($"World '{profile.Id}' contains {blockCount:N0} exact mineable blocks.");

            AddLightingAndEnvironment();

            var worldView = new WorldView { Name = "WorldView" };
            AddChild(worldView);
            worldView.Initialize(assets, world);

            var clouds = new CloudField { Name = "SpacePresentation" };
            AddChild(clouds);

            var camera = new OrbitCameraController { Name = "OrbitCamera" };
            AddChild(camera);

            var harness = new ReferenceVisualHarness { Name = "ReferenceVisualHarness" };
            harness.Initialize(camera);
            AddChild(harness);

            var mining = new MiningService(world, content);

            var manualMining = new ManualMiningController { Name = "ManualMining" };
            manualMining.Initialize(world, camera, worldView, mining);
            AddChild(manualMining);

            var hud = new MiningHud { Name = "MiningHud" };
            hud.Initialize(world, mining, worldView);
            AddChild(hud);

            GD.Print("Procedural mining slice ready. LMB click mines; LMB drag orbits; RMB/MMB pans; wheel zooms.");
        }
        catch (Exception exception)
        {
            GD.PushError($"Failed to initialize 10 Million Blocks gameplay slice:\n{exception}");
            ShowFatalError(exception.Message);
        }
    }

    private void AddLightingAndEnvironment()
    {
        RenderingServer.SetDefaultClearColor(new Color(0.003f, 0.008f, 0.025f));

        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.003f, 0.008f, 0.025f, 1.0f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.13f, 0.20f, 0.32f, 1.0f),
            AmbientLightEnergy = 0.30f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
        };

        AddChild(new WorldEnvironment
        {
            Name = "WorldEnvironment",
            Environment = environment,
        });

        var keyLight = new DirectionalLight3D
        {
            Name = "KeyLight",
            RotationDegrees = new Vector3(-48.0f, -36.0f, 0.0f),
            LightColor = new Color(0.93f, 0.96f, 1.0f),
            LightEnergy = 1.15f,
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 120.0f,
        };
        AddChild(keyLight);

        var coolFill = new OmniLight3D
        {
            Name = "CoolFill",
            Position = new Vector3(-30.0f, 22.0f, 35.0f),
            LightColor = new Color(0.22f, 0.38f, 0.68f),
            LightEnergy = 2.0f,
            OmniRange = 85.0f,
            ShadowEnabled = false,
        };
        AddChild(coolFill);

        var rimLight = new DirectionalLight3D
        {
            Name = "RimLight",
            RotationDegrees = new Vector3(38.0f, 142.0f, 8.0f),
            LightColor = new Color(0.22f, 0.38f, 0.64f),
            LightEnergy = 0.30f,
            ShadowEnabled = false,
        };
        AddChild(rimLight);
    }

    private void ShowFatalError(string message)
    {
        var canvas = new CanvasLayer();
        AddChild(canvas);

        var panel = new PanelContainer
        {
            OffsetLeft = 32.0f,
            OffsetTop = 32.0f,
            OffsetRight = 760.0f,
            OffsetBottom = 190.0f,
        };
        canvas.AddChild(panel);

        var label = new Label
        {
            Text = "Startup validation failed\n\n" + message,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        panel.AddChild(label);
    }
}
