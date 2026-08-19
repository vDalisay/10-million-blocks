@echo off

rem Resolve Godot 4.6.1 .NET without tying the repository to one machine.
rem This file is intended to be called so GODOT_EXE remains available to callers.

if not defined PROJECT_ROOT set "PROJECT_ROOT=%~dp0.."
set "GODOT_EXE="

if defined GODOT_PATH (
  if exist "%GODOT_PATH%" (
    set "GODOT_EXE=%GODOT_PATH%"
    exit /b 0
  )

  >&2 echo GODOT_PATH does not point to an existing executable: "%GODOT_PATH%"
  exit /b 2
)

call :try "%PROJECT_ROOT%\..\Godot_v4.6.1-stable_mono_win64\Godot_v4.6.1-stable_mono_win64.exe"
if defined GODOT_EXE exit /b 0
call :try "%PROJECT_ROOT%\..\Godot_v4.6.1-stable_mono_win64\Godot_v4.6.1-stable_mono_win64\Godot_v4.6.1-stable_mono_win64.exe"
if defined GODOT_EXE exit /b 0
call :try "%USERPROFILE%\Downloads\Godot_v4.6.1-stable_mono_win64\Godot_v4.6.1-stable_mono_win64.exe"
if defined GODOT_EXE exit /b 0
call :try "%USERPROFILE%\Downloads\Godot_v4.6.1-stable_mono_win64\Godot_v4.6.1-stable_mono_win64\Godot_v4.6.1-stable_mono_win64.exe"
if defined GODOT_EXE exit /b 0

for %%N in (Godot_v4.6.1-stable_mono_win64.exe godot.exe godot4.exe) do (
  for %%G in ("%%~$PATH:N") do if not "%%~G"=="" if exist "%%~G" (
    set "GODOT_EXE=%%~G"
    exit /b 0
  )
)

>&2 echo Godot 4.6.1 .NET was not found.
>&2 echo Set GODOT_PATH to the full executable path, place the extracted editor next to this repository or in Downloads, or add it to PATH.
exit /b 2

:try
if exist "%~1" for %%G in ("%~1") do set "GODOT_EXE=%%~fG"
exit /b 0
