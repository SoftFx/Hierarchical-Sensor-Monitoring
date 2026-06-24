@echo off
setlocal
net session >nul 2>&1
if %errorlevel% neq 0 (
  echo ERROR: Run this script as Administrator ^(right-click ^> Run as administrator^).
  pause
  exit /b 1
)
set "INSTALL_DIR=%ProgramFiles%\HSM Agent"
if exist "%INSTALL_DIR%\hsm-agent.exe" "%INSTALL_DIR%\hsm-agent.exe" --uninstall
if exist "%INSTALL_DIR%\hsm-agent.exe" del /Q "%INSTALL_DIR%\hsm-agent.exe"
echo HSM Agent uninstalled.
endlocal
