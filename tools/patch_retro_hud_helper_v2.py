#!/usr/bin/env python3
from pathlib import Path

path = Path(__file__).resolve().parent / "apply_retro_futuristic_hud.py"
text = path.read_text(encoding="utf-8")
start_marker = '''replace_once(\n    path,\n    \'\'\'        string detail = selected is null'''
end_marker = '''            : $"ATTENTION  {count}  //  {code} + OTHERS  //  CLICK TO CYCLE";\'\'\')\n\n'''
start = text.find(start_marker)
if start < 0:
    raise SystemExit("automation attention copy patch start not found")
end = text.find(end_marker, start)
if end < 0:
    raise SystemExit("automation attention copy patch end not found")
end += len(end_marker)
path.write_text(text[:start] + text[end:], encoding="utf-8")
print("Removed optional automation-attention copy replacement.")
