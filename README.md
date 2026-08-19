# 10 Million Blocks

A Godot 4.6 C# playable prototype for an incremental voxel-mining game.

The progression starts with a single block, then grows through worlds containing **100**, **1,000**, and **10,000** mineable blocks. Each world is generated from a deterministic seed as a floating, rounded voxel planet with grass, cliffs, lowland water/sand, hidden crystal, trees, a small ruin, and surrounding voxel clouds.

## Play

On Windows, run **`play_game.bat`** from the repository root. It resolves Godot 4.6.1 .NET, builds the C# project, and launches the main scene. `GODOT_PATH` can be set to the full Godot executable path if auto-discovery does not find it.

Alternatively, open `project.godot` in the **Godot 4.6.1 .NET** editor and press **F6/F5**. Run `build_game.bat` when you only want to compile the C# project.

Controls:

- **Left mouse / hold:** mine the block under the cursor.
- **Right mouse drag:** orbit the world.
- **Mouse wheel:** zoom.
- Use the upgrade panel to buy **Pickaxe Power**, **Mining Speed**, and **Auto Miners** with mined blocks.

Clearing a world awards a completion bonus and automatically grows the next world. Clearing the 10,000-block demo stage enters an endless 10,000-block loop with a new seed.

## Technical direction

This prototype deliberately does **not** create one Godot node or physics body per voxel. Logical voxels live in a dictionary; visible faces are merged into chunk meshes; selection uses grid DDA ray traversal; destroyed blocks only dirty nearby chunks. That makes the demo responsive now and gives the project a credible route toward substantially larger worlds.

A manual GitHub Actions build workflow is included in `.github/workflows/build.yml`. The repository's hosted Actions runner currently terminates before executing any job steps, so local `build_game.bat`/Godot validation is the authoritative check until runner access is available.

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for the scaling plan.
