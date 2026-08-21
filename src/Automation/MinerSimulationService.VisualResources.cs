using Godot;

namespace TenMillionBlocks.Automation;

/// <summary>
/// Immutable geometry/material resources shared by every physical automation instance. Tool models are
/// assembled from the same small primitive vocabulary, so creating fresh Mesh/Material resources for
/// every Drill/Axe/Pickaxe only increases Godot resource count and managed/native allocation pressure.
/// Meshes are authored in one-block units and scaled by the current world's BlockSpacing on each node.
/// </summary>
public partial class MinerSimulationService
{
    private static readonly StandardMaterial3D PickaxeWoodMaterial = CreateToolMaterial(
        new Color(0.42f, 0.24f, 0.10f));
    private static readonly StandardMaterial3D AxeWoodMaterial = CreateToolMaterial(
        new Color(0.46f, 0.27f, 0.11f));
    private static readonly StandardMaterial3D PickaxeSteelMaterial = CreateToolMaterial(
        new Color(0.62f, 0.67f, 0.72f), 0.25f);
    private static readonly StandardMaterial3D AxeSteelMaterial = CreateToolMaterial(
        new Color(0.70f, 0.73f, 0.75f), 0.18f);
    private static readonly StandardMaterial3D DrillHousingMaterial = CreateToolMaterial(
        new Color(0.34f, 0.38f, 0.43f), 0.18f);
    private static readonly StandardMaterial3D DrillSteelMaterial = CreateToolMaterial(
        new Color(0.62f, 0.67f, 0.72f), 0.34f);
    private static readonly StandardMaterial3D DrillAccentMaterial = CreateDrillAccentMaterial();

    private static readonly BoxMesh PickaxeHandleMesh = new()
    {
        Size = new Vector3(0.12f, 0.95f, 0.12f),
        Material = PickaxeWoodMaterial,
    };
    private static readonly BoxMesh PickaxeHeadMesh = new()
    {
        Size = new Vector3(0.95f, 0.14f, 0.18f),
        Material = PickaxeSteelMaterial,
    };
    private static readonly BoxMesh AxeHandleMesh = new()
    {
        Size = new Vector3(0.12f, 0.92f, 0.12f),
        Material = AxeWoodMaterial,
    };
    private static readonly BoxMesh AxeHeadMesh = new()
    {
        Size = new Vector3(0.56f, 0.36f, 0.18f),
        Material = AxeSteelMaterial,
    };
    private static readonly CylinderMesh DrillHousingMesh = new()
    {
        TopRadius = 0.43f,
        BottomRadius = 0.43f,
        Height = 0.56f,
        RadialSegments = 16,
        Material = DrillHousingMaterial,
    };
    private static readonly CylinderMesh DrillAccentMesh = new()
    {
        TopRadius = 0.34f,
        BottomRadius = 0.39f,
        Height = 0.14f,
        RadialSegments = 16,
        Material = DrillAccentMaterial,
    };
    private static readonly CylinderMesh DrillShaftMesh = new()
    {
        TopRadius = 0.11f,
        BottomRadius = 0.11f,
        Height = 0.50f,
        RadialSegments = 12,
        Material = DrillSteelMaterial,
    };
    private static readonly CylinderMesh DrillConeMesh = new()
    {
        TopRadius = 0.0f,
        BottomRadius = 0.24f,
        Height = 0.42f,
        RadialSegments = 14,
        Material = DrillSteelMaterial,
    };
    private static readonly BoxMesh DrillBladeMesh = new()
    {
        Size = new Vector3(0.09f, 0.28f, 0.42f),
        Material = DrillSteelMaterial,
    };

    private static StandardMaterial3D CreateToolMaterial(Color color, float metallic = 0.0f)
        => new()
        {
            AlbedoColor = color,
            Roughness = 0.78f,
            Metallic = metallic,
        };

    private static StandardMaterial3D CreateDrillAccentMaterial()
    {
        StandardMaterial3D material = CreateToolMaterial(new Color(0.92f, 0.58f, 0.12f));
        material.EmissionEnabled = true;
        material.Emission = new Color(0.62f, 0.24f, 0.04f);
        material.EmissionEnergyMultiplier = 0.55f;
        return material;
    }
}
