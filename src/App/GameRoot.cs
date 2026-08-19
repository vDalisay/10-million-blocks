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
        RenderingServer.SetDefaultClearColor(new Color(0.006f, 0.014f, 0.025f));

        var keyLight = new DirectionalLight3D
        {
            Name = "KeyLight",
            RotationDegrees = new Vector3(-48.0f, -36.0f, 0.0f),
            LightColor = new Color(0.98f, 0.97f, 0.90f),
            LightEnergy = 1.42f,
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 90.0f,
        };
        AddChild(keyLight);

        var coolFill = new OmniLight3D
        {
            Name = "CoolFill",
            Position = new Vector3(-20.0f, 14.0f, 19.0f),
            LightColor = new Color(0.30f, 0.52f, 1.0f),
            LightEnergy = 4.6f,
            OmniRange = 60.0f,
            ShadowEnabled = false,
        };
        AddChild(coolFill);

        var greenRim = new OmniLight3D
        {
            Name = "GreenRim",
            Position = new Vector3(18.0f, -7.0f, -16.0f),
            LightColor = new Color(0.34f, 0.74f, 0.57f),
            LightEnergy = 2.2f,
            OmniRange = 52.0f,
            ShadowEnabled = false,
        };
        AddChild(greenRim);
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
