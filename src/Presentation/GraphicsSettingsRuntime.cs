using System;
using Godot;

namespace TenMillionBlocks.Presentation;

/// <summary>
/// Player graphics preferences that live above individual scenes. The root viewport keeps 2D/UI at
/// full resolution while Scaling3DScale only changes the 3D buffer, which is exactly what the demo
/// quality plan calls for. A tiny persistent runtime also reapplies Environment toggles whenever a new
/// gameplay scene creates its WorldEnvironment.
/// </summary>
public partial class GraphicsSettingsRuntime : Node
{
    private const string SettingsPath = "user://graphics.cfg";
    private const string Section = "graphics";
    private const double EnvironmentScanIntervalSeconds = 0.25;

    private static GraphicsSettingsRuntime? _instance;

    private double _scanTimer;
    private ulong _lastEnvironmentId;

    public float ResolutionScale { get; private set; } = 1.0f;
    public int MsaaLevel { get; private set; }
    public bool AmbientOcclusionEnabled { get; private set; } = true;
    public bool GlowEnabled { get; private set; }

    public static GraphicsSettingsRuntime Ensure(SceneTree tree)
    {
        if (_instance is not null && GodotObject.IsInstanceValid(_instance)) return _instance;

        _instance = new GraphicsSettingsRuntime
        {
            Name = "PersistentGraphicsSettings",
            ProcessMode = ProcessModeEnum.Always,
        };
        tree.Root.AddChild(_instance);
        return _instance;
    }

    public override void _Ready()
    {
        Load();
        ApplyViewport();
        ApplyEnvironment(force: true);
    }

    public override void _Process(double delta)
    {
        _scanTimer += Math.Max(0.0, delta);
        if (_scanTimer < EnvironmentScanIntervalSeconds) return;
        _scanTimer = 0.0;
        ApplyEnvironment(force: false);
    }

    public void SetResolutionScale(float scale)
    {
        float next = Math.Clamp(scale, 0.50f, 1.00f);
        if (MathF.Abs(next - ResolutionScale) < 0.001f) return;
        ResolutionScale = next;
        ApplyViewport();
        Save();
    }

    /// <summary>
    /// Accepted sample counts are 0, 2 and 4. The stored integer is deliberately the user-facing
    /// sample count instead of Godot's enum ordinal so the config file stays stable and readable.
    /// </summary>
    public void SetMsaaLevel(int samples)
    {
        int next = samples switch
        {
            2 => 2,
            4 => 4,
            _ => 0,
        };
        if (MsaaLevel == next) return;
        MsaaLevel = next;
        ApplyViewport();
        Save();
    }

    public void SetAmbientOcclusionEnabled(bool enabled)
    {
        if (AmbientOcclusionEnabled == enabled) return;
        AmbientOcclusionEnabled = enabled;
        ApplyEnvironment(force: true);
        Save();
    }

    public void SetGlowEnabled(bool enabled)
    {
        if (GlowEnabled == enabled) return;
        GlowEnabled = enabled;
        ApplyEnvironment(force: true);
        Save();
    }

    public void RestoreDefaults()
    {
        ResolutionScale = 1.0f;
        MsaaLevel = 0;
        AmbientOcclusionEnabled = true;
        GlowEnabled = false;
        ApplyViewport();
        ApplyEnvironment(force: true);
        Save();
    }

    private void Load()
    {
        var config = new ConfigFile();
        Error result = config.Load(SettingsPath);
        if (result != Error.Ok && result != Error.FileNotFound)
        {
            GD.PushWarning($"Could not read graphics settings ({result}); using defaults.");
            return;
        }

        ResolutionScale = Math.Clamp(
            (float)config.GetValue(Section, "resolution_scale", 1.0f),
            0.50f,
            1.00f);
        int storedMsaa = (int)config.GetValue(Section, "msaa_samples", 0);
        MsaaLevel = storedMsaa is 2 or 4 ? storedMsaa : 0;
        AmbientOcclusionEnabled = (bool)config.GetValue(Section, "ambient_occlusion", true);
        GlowEnabled = (bool)config.GetValue(Section, "glow", false);
    }

    private void Save()
    {
        var config = new ConfigFile();
        config.SetValue(Section, "resolution_scale", ResolutionScale);
        config.SetValue(Section, "msaa_samples", MsaaLevel);
        config.SetValue(Section, "ambient_occlusion", AmbientOcclusionEnabled);
        config.SetValue(Section, "glow", GlowEnabled);
        Error result = config.Save(SettingsPath);
        if (result != Error.Ok)
        {
            GD.PushWarning($"Could not save graphics settings ({result}).");
        }
    }

    private void ApplyViewport()
    {
        if (!IsInsideTree()) return;
        Window root = GetTree().Root;
        root.Scaling3DScale = ResolutionScale;
        root.Msaa3D = MsaaLevel switch
        {
            2 => (Viewport.Msaa)1,
            4 => (Viewport.Msaa)2,
            _ => (Viewport.Msaa)0,
        };
    }

    private void ApplyEnvironment(bool force)
    {
        if (!IsInsideTree()) return;
        WorldEnvironment? worldEnvironment = FindWorldEnvironment(GetTree().CurrentScene);
        if (worldEnvironment?.Environment is not Godot.Environment environment)
        {
            _lastEnvironmentId = 0;
            return;
        }

        ulong id = environment.GetInstanceId();
        if (!force && id == _lastEnvironmentId) return;
        _lastEnvironmentId = id;

        // Godot 4.6 Compatibility supports a simplified SSAO/glow path. We intentionally only toggle
        // those effects here; GameRoot remains authoritative for the tuned radius/intensity/tonemap.
        environment.SsaoEnabled = AmbientOcclusionEnabled;
        environment.GlowEnabled = GlowEnabled;
    }

    private static WorldEnvironment? FindWorldEnvironment(Node? node)
    {
        if (node is null) return null;
        if (node is WorldEnvironment environment) return environment;

        foreach (Node child in node.GetChildren())
        {
            WorldEnvironment? found = FindWorldEnvironment(child);
            if (found is not null) return found;
        }
        return null;
    }
}
