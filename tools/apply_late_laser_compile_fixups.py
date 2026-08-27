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
    "src/Diagnostics/CompletionParticleBenchmark.cs",
    "using Godot;\nusing TenMillionBlocks.Presentation;",
    "using Godot;\nusing TenMillionBlocks.Content;\nusing TenMillionBlocks.Presentation;",
)

print("Applied late laser compile fixups.")
