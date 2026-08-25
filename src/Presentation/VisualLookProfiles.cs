using System;
using Godot;

namespace TenMillionBlocks.Presentation;

/// <summary>
/// One source of truth for the game's world-space lighting / post-processing look. Gameplay previously
/// authored the same values in GameRoot while the debug A/B harness independently re-authored them.
/// Keeping the tuned values here means the shipping scene, persistent graphics settings and screenshot
/// harness can compare presets without drifting into subtly different light rigs.
///
/// The Shipping preset is intentionally conservative: it keeps the existing dark-space identity and
/// Filmic/AO depth, then adds a small post-tonemap contrast/saturation lift. Godot 4.6 made glow's Screen
/// blend materially brighter, so shipping glow is kept subtle and remains player-disableable.
/// </summary>
public static class VisualLookProfiles
{
    public const string Shipping = "Shipping";
    public const string Raw = "Raw";
    public const string Depth = "Depth";
    public const string Punchy = "Punchy";
    public const string Soft = "Soft";

    public static void ApplyShipping(
        Godot.Environment environment,
        DirectionalLight3D? keyLight = null,
        DirectionalLight3D? fillLight = null)
        => Apply(Shipping, environment, keyLight, fillLight);

    public static void Apply(
        string preset,
        Godot.Environment environment,
        DirectionalLight3D? keyLight = null,
        DirectionalLight3D? fillLight = null)
    {
        ArgumentNullException.ThrowIfNull(environment);

        // Shared light rig. Presets should compare post/ambient treatment, not accidentally move the sun.
        if (keyLight is not null)
        {
            keyLight.LightColor = new Color(1.0f, 0.98f, 0.94f);
            keyLight.LightEnergy = 1.05f;
            keyLight.LightSpecular = 0.0f;
        }
        if (fillLight is not null)
        {
            fillLight.LightColor = new Color(0.72f, 0.82f, 1.0f);
            fillLight.LightEnergy = 0.45f;
            fillLight.LightSpecular = 0.0f;
        }

        environment.BackgroundMode = Godot.Environment.BGMode.Color;
        environment.BackgroundColor = new Color(0.003f, 0.008f, 0.025f, 1.0f);
        environment.AmbientLightSource = Godot.Environment.AmbientSource.Color;
        environment.AmbientLightColor = new Color(0.74f, 0.78f, 0.84f, 1.0f);
        environment.ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled;
        environment.TonemapWhite = 2.0f;

        // Always write all adjustment fields so changing A/B presets cannot inherit stale values.
        environment.AdjustmentEnabled = false;
        environment.AdjustmentBrightness = 1.0f;
        environment.AdjustmentContrast = 1.0f;
        environment.AdjustmentSaturation = 1.0f;
        environment.GlowEnabled = false;
        environment.GlowIntensity = 0.0f;

        switch (preset)
        {
            case Raw:
                environment.AmbientLightEnergy = 0.42f;
                environment.SsaoEnabled = false;
                environment.TonemapMode = Godot.Environment.ToneMapper.Linear;
                break;

            case Depth:
                environment.AmbientLightEnergy = 0.42f;
                ConfigureAo(environment, intensity: 2.25f);
                environment.TonemapMode = Godot.Environment.ToneMapper.Filmic;
                break;

            case Punchy:
                environment.AmbientLightEnergy = 0.40f;
                ConfigureAo(environment, intensity: 2.20f);
                environment.TonemapMode = Godot.Environment.ToneMapper.Filmic;
                ConfigureAdjustments(environment, brightness: 1.01f, contrast: 1.10f, saturation: 1.10f);
                break;

            case Soft:
                environment.AmbientLightEnergy = 0.48f;
                ConfigureAo(environment, intensity: 1.75f);
                environment.TonemapMode = Godot.Environment.ToneMapper.Filmic;
                ConfigureAdjustments(environment, brightness: 1.03f, contrast: 0.96f, saturation: 0.96f);
                break;

            default:
                // Shipping: retain strong voxel contact depth without the old AO looking dirty in dark
                // cavities, then use a modest BCS lift to separate grass/water/ore from the navy void.
                environment.AmbientLightEnergy = 0.42f;
                ConfigureAo(environment, intensity: 2.20f);
                environment.TonemapMode = Godot.Environment.ToneMapper.Filmic;
                ConfigureAdjustments(environment, brightness: 1.01f, contrast: 1.07f, saturation: 1.06f);
                environment.GlowEnabled = true;
                environment.GlowIntensity = 0.12f;
                break;
        }
    }

    private static void ConfigureAo(Godot.Environment environment, float intensity)
    {
        environment.SsaoEnabled = true;
        environment.SsaoRadius = 1.6f;
        environment.SsaoIntensity = intensity;
        environment.SsaoPower = 1.35f;
    }

    private static void ConfigureAdjustments(
        Godot.Environment environment,
        float brightness,
        float contrast,
        float saturation)
    {
        environment.AdjustmentEnabled = true;
        environment.AdjustmentBrightness = brightness;
        environment.AdjustmentContrast = contrast;
        environment.AdjustmentSaturation = saturation;
    }
}
