#!/usr/bin/env python3
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def patch(path: str, old: str, new: str) -> None:
    target = ROOT / path
    text = target.read_text(encoding="utf-8")
    if old not in text:
        raise RuntimeError(f"Fixup anchor missing in {path}: {old!r}")
    target.write_text(text.replace(old, new, 1), encoding="utf-8")


patch(
    "src/App/GameRoot.WorldCeremony.cs",
    "using TenMillionBlocks.Automation;\nusing TenMillionBlocks.Presentation;",
    "using TenMillionBlocks.Automation;\nusing TenMillionBlocks.Content;\nusing TenMillionBlocks.Presentation;\nusing TenMillionBlocks.UI;",
)
patch(
    "src/World/Rendering/WorldView.IntroWave.cs",
    "using Godot;\n\nnamespace TenMillionBlocks.World.Rendering;",
    "using Godot;\nusing TenMillionBlocks.World.Generation;\n\nnamespace TenMillionBlocks.World.Rendering;",
)
patch(
    "TenMillionBlocks.csproj",
    "    <Compile Remove=\"tools/replay_contract/**/*.cs\" />",
    "    <Compile Remove=\"tools/replay_contract/**/*.cs\" />\n    <Compile Remove=\"tools/completion_contract/**/*.cs\" />",
)
# The implementation helper adds the permanent score-contract CI step so its transformed tree can
# validate it. Restore build.yml before the bot commit: GitHub Apps without workflow permission cannot
# push workflow-file changes. The connector will add this permanent step after the implementation lands.
patch(
    ".github/workflows/build.yml",
    "\n\n      - name: Validate completion score\n        run: dotnet run --project tools/completion_contract/CompletionContract.csproj --configuration Release",
    "",
)

print("Applied world ceremony compile/workflow fixups.")
