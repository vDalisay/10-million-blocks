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

print("Applied world ceremony compile fixups.")
