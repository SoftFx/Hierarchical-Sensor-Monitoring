@echo off
setlocal
:: Self-elevate if not already running as administrator.
net session >nul 2>&1
if %errorlevel% neq 0 (
  echo Requesting administrator privileges...
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
set "INSTALL_DIR=%ProgramFiles%\HSM Agent"
set "DATA_DIR=%ProgramData%\HSM Agent"
if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"
if not exist "%DATA_DIR%" mkdir "%DATA_DIR%"
copy /Y "%~dp0hsm-agent.exe" "%INSTALL_DIR%\hsm-agent.exe" >nul
copy /Y "%~dp0config.json" "%DATA_DIR%\config.json" >nul
"%INSTALL_DIR%\hsm-agent.exe" --install
sc start HSMAgent
echo HSM Agent installed and started.
endlocal
