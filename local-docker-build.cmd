@echo off
setlocal
:: Builds and runs HSMServer in Docker locally, mirroring the release build.
:: Thin wrapper around scripts/local-docker-build.ps1; all args are forwarded.
:: Examples:
::   local-docker-build.cmd                 - default: build from master, run container
::   local-docker-build.cmd -NoRun          - build image only, don't start container
::   local-docker-build.cmd -Branch feature/foo
::   local-docker-build.cmd -IncludeAgent   - also build HSM Agent (needs VCPKG_ROOT)

where pwsh >nul 2>nul
if %errorlevel% equ 0 (
  set "PS_EXE=pwsh"
) else (
  set "PS_EXE=powershell"
)

%PS_EXE% -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\local-docker-build.ps1" %*
set "RC=%errorlevel%"
echo.
if %RC% neq 0 echo Script failed with exit code %RC%.
pause
exit /b %RC%
