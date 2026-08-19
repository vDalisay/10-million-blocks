using System;
using System.Collections.Generic;
using TenMillionBlocks.Automation;
using TenMillionBlocks.Automation.MiningPatterns;
using TenMillionBlocks.Skills;

namespace TenMillionBlocks.Content;

public static class ContentCrossValidator
{
    public static void Validate(
        MinerCatalog miners,
        MiningPatternRegistry patterns,
        SkillTreeCatalog skills)
    {
        var errors = new List<string>();

        foreach (MinerDefinition miner in miners.Miners.Values)
        {
            if (!patterns.Contains(miner.PatternId))
            {
                errors.Add($"Miner '{miner.Id}' references unknown pattern '{miner.PatternId}'.");
            }
        }

        foreach (SkillNodeDefinition node in skills.Nodes.Values)
        {
            foreach (SkillEffectDefinition effect in node.Effects)
            {
                switch (effect.Type)
                {
                    case "unlock_miner" when !miners.Miners.ContainsKey(effect.StringValue):
                        errors.Add($"Skill '{node.Id}' unlocks unknown miner '{effect.StringValue}'.");
                        break;
                    case "unlock_pattern" when !patterns.Contains(effect.StringValue):
                        errors.Add($"Skill '{node.Id}' unlocks unknown mining pattern '{effect.StringValue}'.");
                        break;
                }
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException("Cross-content validation failed:\n - " + string.Join("\n - ", errors));
        }
    }
}
