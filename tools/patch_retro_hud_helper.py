#!/usr/bin/env python3
from pathlib import Path

path = Path(__file__).resolve().parent / "apply_retro_futuristic_hud.py"
text = path.read_text(encoding="utf-8")
old = '''replace_once(
    path,
    \'\'\'        _feedback.Visible = true;\n    }\n\n    private void Refresh()\'\'\',
    \'\'\'        _feedback.Visible = true;\n        ShowRetroEvent(_feedback.Text, _feedbackTime);\n    }\n\n    private void Refresh()\'\'\')

'''
if old not in text:
    raise SystemExit("target brittle feedback patch not found")
path.write_text(text.replace(old, "", 1), encoding="utf-8")
print("Removed brittle feedback anchor from retro HUD helper.")
