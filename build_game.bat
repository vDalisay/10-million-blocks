@echo off
setlocal

set "PROJECT_ROOT=%~dp0"
pushd "%PROJECT_ROOT%"

echo [10-million-blocks] Building C# project...
dotnet build "%PROJECT_ROOT%TenMillionBlocks.csproj" --configuration Debug --nologo --verbosity minimal
set "RESULT=%ERRORLEVEL%"

if "%RESULT%"=="0" (
  echo [10-million-blocks] Build succeeded.
) else (
  echo [10-million-blocks] Build failed with code %RESULT%.
)

popd
exit /b %RESULT%
