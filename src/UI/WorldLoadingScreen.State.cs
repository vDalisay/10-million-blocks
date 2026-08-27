using Godot;

namespace TenMillionBlocks.UI;

public partial class WorldLoadingScreen
{
    public static bool IsActive
        => _instance is not null
            && GodotObject.IsInstanceValid(_instance)
            && _instance.Visible;
}
