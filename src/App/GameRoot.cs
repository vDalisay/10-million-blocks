using System;
using Godot;
using TenMillionBlocks.Content;
using TenMillionBlocks.Presentation;

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

            AddLightingAndEnvironment();

            Node3D planet = ReferencePlanetBuilder.Build(assets);
            AddChild(planet);

            var clouds = new CloudField { Name = "SpacePresentation" };
            AddChild(clouds);

            var camera = new OrbitCameraController { Name = "OrbitCamera" };
            AddChild(camera);

            var harness = new ReferenceVisualHarness { Name = "ReferenceVisualHarness" };
            harness.Initialize(camera);
            AddChild(harness);

            GD.Print("Reference visual slice ready. Use 1/2/3 for camera presets and F to recenter.");
        }
        catch (Exception exception)
        {
            GD.PushError($"Failed to initialize 10 Million Blocks reference slice:\n{exception}");
            ShowFatalError(exception.Message);
        }
    }

    private void AddLightingAndEnvironment()
    {
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.006f, 0.014f, 0.025f, 1.0f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.18f, 0.28f, 0.42f, 1.0f),
            AmbientLightEnergy = 0.42f,
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
            LightColor = new Color(0.92f, 0.97f, 1.0f),
            LightEnergy = 1.55f,
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 80.0f,
        };
        AddChild(keyLight);

        var rimLight = new DirectionalLight3D
        {
            Name = "RimLight",
            RotationDegrees = new Vector3(38.0f, 142.0f, 8.0f),
            LightColor = new Color(0.30f, 0.48f, 0.75f),
            LightEnergy = 0.46f,
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
