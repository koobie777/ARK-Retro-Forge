@echo off
setlocal enabledelayedexpansion

REM Run ARK-Retro-Forge CLI from the repo root without manual dotnet commands.
set "REPO_ROOT=%~dp0"

REM Default to Debug for faster iterations; override via ARKRF_CONFIGURATION env var.
if not defined ARKRF_CONFIGURATION (
  set "ARKRF_CONFIGURATION=Debug"
)

dotnet run --project "%REPO_ROOT%src\Cli\ARK.Cli.csproj" --configuration "%ARKRF_CONFIGURATION%" -- %*

endlocal
