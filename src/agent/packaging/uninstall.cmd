@echo off
setlocal
net session >nul 2>&1
if %errorlevel% neq 0 (
  echo Requesting administrator privileges...
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs -Wait"
  exit /b
)
set "INSTALL_DIR=%ProgramFiles%\HSM Agent"
if exist "%INSTALL_DIR%\hsm-agent.exe" "%INSTALL_DIR%\hsm-agent.exe" --uninstall
if exist "%INSTALL_DIR%\hsm-agent.exe" del /Q "%INSTALL_DIR%\hsm-agent.exe"
echo HSM Agent uninstalled.
endlocal
