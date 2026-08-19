@echo off
setlocal
cd /d "%~dp0"

echo Building TenMillionBlocks...
dotnet build TenMillionBlocks.csproj --configuration Debug
if errorlevel 1 (
  echo.
  echo Build failed.
  exit /b 1
)

echo Build succeeded.
