using Godot;

namespace TenMillionBlocks.World.Interaction;

public partial class SelectionHighlight : MeshInstance3D
{
    private float _spacing = 2.0f;

    public void Initialize(float spacing)
    {
        _spacing = spacing;

        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(1.0f, 0.94f, 0.42f, 0.23f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            NoDepthTest = false,
        };

        Mesh = new BoxMesh
        {
            Size = Vector3.One * spacing * 1.055f,
            Material = material,
        };

        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        Visible = false;
    }

    public void ShowVoxel(Vector3I voxel)
    {
        Position = (Vector3)voxel * _spacing;
        Visible = true;
    }

    public void HideVoxel()
    {
        Visible = false;
    }
}
