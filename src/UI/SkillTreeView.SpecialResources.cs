using System;
using TenMillionBlocks.Economy;
using TenMillionBlocks.Mining;
using TenMillionBlocks.Skills;

namespace TenMillionBlocks.UI;

public partial class SkillTreeView
{
    /// <summary>
    /// Keeps GameRoot explicit about the player special-resource inventory while the view reads the
    /// same authoritative instance through SkillTreeService.SpecialResources. No second inventory is
    /// stored in the UI.
    /// </summary>
    public void Initialize(
        SkillTreeService skills,
        MiningService mining,
        ManualMiningController manual,
        SpecialResourceInventory specialResources)
    {
        if (!ReferenceEquals(skills.SpecialResources, specialResources))
        {
            throw new InvalidOperationException("SkillTreeView received a special-resource inventory different from SkillTreeService.");
        }

        Initialize(skills, mining, manual);
    }
}
