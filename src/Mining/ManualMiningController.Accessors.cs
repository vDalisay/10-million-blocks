using TenMillionBlocks.Presentation;
using TenMillionBlocks.Skills;

namespace TenMillionBlocks.Mining;

public partial class ManualMiningController
{
    public OrbitCameraController CameraController => _camera;
    public SkillTreeService SkillTree => _skills;
}
