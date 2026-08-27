#!/usr/bin/env python3
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def replace_once(path: str, old: str, new: str) -> None:
    target = ROOT / path
    text = target.read_text(encoding="utf-8")
    if old not in text:
        raise SystemExit(f"anchor missing in {path}")
    target.write_text(text.replace(old, new, 1), encoding="utf-8")


replace_once(
    "src/Mining/LaserMiningController.cs",
    "        if (_activeRemaining > 0.0)\n"
    "        {\n"
    "            _activeRemaining = Math.Max(0.0, _activeRemaining - dt);\n"
    "            FireLaser(dt);\n",
    "        if (_activeRemaining > 0.0)\n"
    "        {\n"
    "            // Consume only the authored natural-burst slice that actually remains. A long or final\n"
    "            // render frame must not turn a 5.0-second burst into 5.0s + one frame of free damage.\n"
    "            double activeDt = Math.Min(dt, _activeRemaining);\n"
    "            _activeRemaining = Math.Max(0.0, _activeRemaining - activeDt);\n"
    "            FireLaser(activeDt);\n",
)

replace_once(
    "tools/validate_laser_contract.py",
    "require(\"private const double DamageTickSeconds = 0.10\" in laser,\n"
    "        \"laser gameplay damage cadence must remain bounded at 10 Hz\")\n",
    "require(\"private const double DamageTickSeconds = 0.10\" in laser,\n"
    "        \"laser gameplay damage cadence must remain bounded at 10 Hz\")\n"
    "require(\"double activeDt = Math.Min(dt, _activeRemaining);\" in laser\n"
    "        and \"FireLaser(activeDt);\" in laser,\n"
    "        \"natural burst must clamp its final frame to the exact authored remaining duration\")\n",
)

print("Applied exact Flux Laser natural-burst time slicing.")
