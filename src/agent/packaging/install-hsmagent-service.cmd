@echo off
setlocal
:: HSM Agent — fresh install or reinstall.
:: Requires Administrator: right-click > Run as administrator.
::
:: Expected layout (cmake copies exe here automatically after build):
::   install-hsmagent-service.cmd  <- this file
::   update-hsmagent-service.cmd
::   uninstall-hsmagent-service.cmd
::   hsm-agent.exe                 <- built by cmake, gitignored
::   config.json                   <- edit before first install if needed

net session >nul 2>&1
if %errorlevel% neq 0 (
  echo ERROR: Run this script as Administrator ^(right-click ^> Run as administrator^).
  pause
  exit /b 1
)

if not exist "%~dp0hsm-agent.exe" (
  echo ERROR: hsm-agent.exe not found next to this script.
  echo Build the project first ^(cmake --build^) — it copies the exe here automatically.
  pause & exit /b 1
)

set "INSTALL_DIR=%ProgramFiles%\HSM Agent"
set "DATA_DIR=%ProgramData%\HSM Agent"

:: Stop gracefully if already running (handles reinstall/upgrade).
sc query HSMAgent >nul 2>&1
if %errorlevel% equ 0 (
  echo Stopping existing HSMAgent...
  sc stop HSMAgent >nul 2>&1
  timeout /t 3 /nobreak >nul
)

if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"
if not exist "%DATA_DIR%"  mkdir "%DATA_DIR%"

echo Installing version:
"%~dp0hsm-agent.exe" --version
echo.
copy /Y "%~dp0hsm-agent.exe" "%INSTALL_DIR%\hsm-agent.exe" >nul

:: Copy config only on first install — never overwrite a user-customised config.
if exist "%~dp0config.json" (
  if not exist "%DATA_DIR%\config.json" (
    copy /Y "%~dp0config.json" "%DATA_DIR%\config.json" >nul
    echo Config installed to %DATA_DIR%\config.json
  ) else (
    echo Keeping existing config at %DATA_DIR%\config.json
  )
)

"%INSTALL_DIR%\hsm-agent.exe" --install
if %errorlevel% neq 0 (
  echo HSM Agent --install failed. & pause & exit /b 1
)
sc start HSMAgent
if %errorlevel% neq 0 (
  echo HSM Agent failed to start. & pause & exit /b 1
)
echo HSM Agent installed and started.
pause
endlocal
