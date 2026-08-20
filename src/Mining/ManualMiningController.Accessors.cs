using TenMillionBlocks.Content;
using TenMillionBlocks.Presentation;
using TenMillionBlocks.Skills;

namespace TenMillionBlocks.Mining;

public partial class ManualMiningController
{
    public OrbitCameraController CameraController => _camera;
    public SkillTreeService SkillTree => _skills;
    public WorldProfile WorldProfile => _world.Profile;
}
