@echo off
setlocal
set "PROJECT_ROOT=%~dp0.."
call "%~dp0resolve_godot.bat"
if errorlevel 1 exit /b %errorlevel%

pushd "%PROJECT_ROOT%"
"%GODOT_EXE%" --path "%PROJECT_ROOT%" res://tools/skill_tree_editor/SkillTreeEditor.tscn
set "RESULT=%ERRORLEVEL%"
popd
exit /b %RESULT%
