@echo off
setlocal
set "PROJECT_ROOT=%~dp0"
cd /d "%PROJECT_ROOT%"

call "%PROJECT_ROOT%tools\resolve_godot.bat"
if errorlevel 1 exit /b %errorlevel%

call "%PROJECT_ROOT%build_game.bat"
if errorlevel 1 exit /b %errorlevel%

"%GODOT_EXE%" --path "%PROJECT_ROOT%" --editor-pid 0
