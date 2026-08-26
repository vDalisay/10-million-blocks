from pathlib import Path

root = Path(__file__).resolve().parents[1]

p = root / "src/UI/SkillTreeIncrementalTheme.cs"
s = p.read_text(encoding="utf-8")
old = '''    public static Vector2 OpticalOffsetForSkill(string skillId)\n    {\n        int index = Indices.GetValueOrDefault(skillId, 4);\n        return OpticalOffsets.GetValueOrDefault(index, Vector2.Zero);\n    }\n'''
new = '''    public static Vector2 OpticalOffsetForSkill(string skillId, float renderedSize = 64.0f)\n    {\n        int index = Indices.GetValueOrDefault(skillId, 4);\n        Vector2 sourceOffset = OpticalOffsets.GetValueOrDefault(index, Vector2.Zero);\n        return sourceOffset * (renderedSize / CellSize);\n    }\n'''
if s.count(old) != 1:
    raise RuntimeError("OpticalOffsetForSkill anchor mismatch")
p.write_text(s.replace(old, new, 1), encoding="utf-8")

p = root / "src/UI/SkillTreeSpaceVisuals.cs"
s = p.read_text(encoding="utf-8")
old = 'Vector2 opticalOffset = SkillTreeIconAtlas.OpticalOffsetForSkill(node.Id);'
new = 'Vector2 opticalOffset = SkillTreeIconAtlas.OpticalOffsetForSkill(node.Id, iconSize);'
if s.count(old) != 1:
    raise RuntimeError("space icon offset call anchor mismatch")
p.write_text(s.replace(old, new, 1), encoding="utf-8")

print("Scaled measured atlas centering offsets to the 42px rendered constellation icon size.")
