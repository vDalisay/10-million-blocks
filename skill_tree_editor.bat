@echo off
setlocal
set "PROJECT_ROOT=%~dp0"

call "%PROJECT_ROOT%tools\resolve_godot.bat"
if errorlevel 1 exit /b %errorlevel%

call "%PROJECT_ROOT%build_game.bat"
if errorlevel 1 exit /b %errorlevel%

set "GODOT_GUI_EXE=%GODOT_EXE:_console.exe=.exe%"
if /I not "%GODOT_GUI_EXE%"=="%GODOT_EXE%" if exist "%GODOT_GUI_EXE%" set "GODOT_EXE=%GODOT_GUI_EXE%"

pushd "%PROJECT_ROOT%"
"%GODOT_EXE%" --path . "res://tools/skill_tree_editor/SkillTreeEditor.tscn"
set "RESULT=%ERRORLEVEL%"
popd
exit /b %RESULT%
