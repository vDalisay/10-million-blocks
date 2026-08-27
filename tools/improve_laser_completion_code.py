#!/usr/bin/env python3
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
path = ROOT / "src/Presentation/WorldCompletionCeremony.cs"
text = path.read_text(encoding="utf-8")

old = "        ArgumentNullException.ThrowIfNull(camera);"
new = "        ArgumentNullException.ThrowIfNull(profile);\n        ArgumentNullException.ThrowIfNull(assets);\n        ArgumentNullException.ThrowIfNull(camera);"
if text.count(old) != 1:
    raise RuntimeError("expected one completion initializer guard anchor")
text = text.replace(old, new, 1)

old = "    float appear = step(0.72 + delay, visual_time);"
new = "    float appear = step(scatter_start + delay, visual_time);"
if text.count(old) != 1:
    raise RuntimeError("expected one hard-coded particle appear threshold")
text = text.replace(old, new, 1)

path.write_text(text, encoding="utf-8")
print("Tightened completion presentation invariants.")
