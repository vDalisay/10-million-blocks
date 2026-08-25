using System;
using Godot;

namespace TenMillionBlocks.Presentation;

/// <summary>
/// Player presentation preferences that live above individual scenes. The root viewport keeps 2D/UI
/// at full resolution while Scaling3DScale only changes the 3D buffer. The same persistent runtime
/// reapplies the authored shipping look and quality toggles whenever a gameplay scene creates a new
/// WorldEnvironment, and exposes lightweight motion preferences used by presentation controllers.
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
    public bool GlowEnabled { get; private set; } = true;
    public int DetailDistance { get; private set; } = 1;
    public bool IdleCameraOrbitEnabled { get; private set; } = true;
    public bool ReducedMotionEnabled { get; private set; }

    public static GraphicsSettingsRuntime? Current
        => _instance is not null && GodotObject.IsInstanceValid(_instance) ? _instance : null;

    public static GraphicsSettingsRuntime Ensure(SceneTree tree)
    {
        if (_instance is not null && GodotObject.IsInstanceValid(_instance)) return _instance;

        GraphicsSettingsRuntime instance = new GraphicsSettingsRuntime
        {
            Name = "PersistentGraphicsSettings",
            ProcessMode = ProcessModeEnum.Always,
        };
        _instance = instance;
        instance.Load();
        Callable.From(() => tree.Root.AddChild(instance)).CallDeferred();
        return instance;
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

    public void SetDetailDistance(int level)
    {
        int next = Math.Clamp(level, 0, 2);
        if (DetailDistance == next) return;
        DetailDistance = next;
        Save();
    }

    public void SetIdleCameraOrbitEnabled(bool enabled)
    {
        if (IdleCameraOrbitEnabled == enabled) return;
        IdleCameraOrbitEnabled = enabled;
        Save();
    }

    public void SetReducedMotionEnabled(bool enabled)
    {
        if (ReducedMotionEnabled == enabled) return;
        ReducedMotionEnabled = enabled;
        Save();
    }

    public void RestoreDefaults()
    {
        ResolutionScale = 1.0f;
        MsaaLevel = 0;
        AmbientOcclusionEnabled = true;
        GlowEnabled = true;
        DetailDistance = 1;
        IdleCameraOrbitEnabled = true;
        ReducedMotionEnabled = false;
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
        GlowEnabled = (bool)config.GetValue(Section, "glow", true);
        DetailDistance = Math.Clamp((int)config.GetValue(Section, "detail_distance", 1), 0, 2);
        IdleCameraOrbitEnabled = (bool)config.GetValue(Section, "idle_camera_orbit", true);
        ReducedMotionEnabled = (bool)config.GetValue(Section, "reduced_motion", false);
    }

    private void Save()
    {
        var config = new ConfigFile();
        config.SetValue(Section, "resolution_scale", ResolutionScale);
        config.SetValue(Section, "msaa_samples", MsaaLevel);
        config.SetValue(Section, "ambient_occlusion", AmbientOcclusionEnabled);
        config.SetValue(Section, "glow", GlowEnabled);
        config.SetValue(Section, "detail_distance", DetailDistance);
        config.SetValue(Section, "idle_camera_orbit", IdleCameraOrbitEnabled);
        config.SetValue(Section, "reduced_motion", ReducedMotionEnabled);
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
        Node? scene = GetTree().CurrentScene;
        WorldEnvironment? worldEnvironment = FindWorldEnvironment(scene);
        if (worldEnvironment?.Environment is not Godot.Environment environment)
        {
            _lastEnvironmentId = 0;
            return;
        }

        ulong id = environment.GetInstanceId();
        if (!force && id == _lastEnvironmentId) return;
        _lastEnvironmentId = id;

        // Apply the authored look first, then quality preferences. This makes GameRoot's initial values
        // merely safe construction defaults instead of a second, drifting source of art-direction truth.
        DirectionalLight3D? key = FindNamedDirectionalLight(scene, "KeyLight");
        DirectionalLight3D? fill = FindNamedDirectionalLight(scene, "FillLight");
        VisualLookProfiles.ApplyShipping(environment, key, fill);

        // AO and glow are the player-facing quality escape hatches. Disabling either does not alter the
        // rest of the grade, lighting or tonemap, so screenshots remain visually comparable.
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

    private static DirectionalLight3D? FindNamedDirectionalLight(Node? node, string name)
    {
        if (node is null) return null;
        if (node is DirectionalLight3D light && string.Equals(light.Name.ToString(), name, StringComparison.Ordinal))
            return light;

        foreach (Node child in node.GetChildren())
        {
            DirectionalLight3D? found = FindNamedDirectionalLight(child, name);
            if (found is not null) return found;
        }
        return null;
    }
}
