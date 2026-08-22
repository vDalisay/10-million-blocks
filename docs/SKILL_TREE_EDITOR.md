# Skill Tree Editor

The skill tree is authored visually against `data/skills/skill_tree.json`.

## Launch

On Windows, run:

`tools\run_skill_tree_editor.bat`

The launcher resolves the same Godot 4.6.1 .NET editor used by the project and opens `tools/skill_tree_editor/SkillTreeEditor.tscn` directly.

## Fast layout workflow

- **LMB drag a card** — move a skill; release snaps it to the graph grid.
- **MMB drag** — pan the graph.
- **Mouse wheel** — zoom.
- **Connect** (or `Ctrl+L`) — click the prerequisite skill first, then the dependent skill. The connection is also the actual gameplay prerequisite.
- **Click a connection line** — select that prerequisite edge.
- **LMB an empty grid cell while a line is selected** — add a snapped route bend.
- **RMB a route bend** — remove that bend.
- **Clear Route** — return the selected connection to a direct line.
- **Ctrl+S / Save + Validate** — validate the complete graph and write the JSON layout.
- **Ctrl+D / Duplicate** — duplicate the selected skill.

Saving validates IDs, purchase modes, prerequisite ranks, missing prerequisite references, duplicate prerequisites, known effect types and circular dependency graphs before replacing the authored skill-tree file.

## Progressive reveal

Each node has an inspector toggle:

**Hide until prerequisites are unlocked**

When disabled, the node is visible as soon as its world/category staging allows it, but remains disabled until its prerequisites are met.

When enabled, the node and its incoming connection lines are completely hidden until **all authored prerequisite rank requirements** are met. Buying the prerequisite immediately reveals the child in the live skill tree. A root node with no prerequisites always remains visible.

This reveal setting is presentation only. The prerequisite connection itself remains the authoritative purchase requirement either way.

## World staging

World profiles still decide which categories or exact skill IDs belong in that world's tree. Progressive reveal happens after that staging decision, so the two systems can be combined:

1. world says a skill is eligible to exist in this stage;
2. prerequisite reveal rules decide whether the player can see it yet;
3. prerequisite ranks decide whether it can be purchased;
4. cost/special-resource checks decide whether it is affordable.
