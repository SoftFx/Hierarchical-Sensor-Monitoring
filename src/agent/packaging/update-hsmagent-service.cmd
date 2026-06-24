@echo off
setlocal
:: HSM Agent — in-place update (service must already be installed).
:: Stops the service, swaps the exe, restarts. Config is NOT touched.
:: Requires Administrator: right-click > Run as administrator.
::
:: cmake copies hsm-agent.exe here automatically after every build.

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

echo Installed version:
if exist "%INSTALL_DIR%\hsm-agent.exe" (
  "%INSTALL_DIR%\hsm-agent.exe" --version
) else (
  echo   ^(not installed^)
)

echo New version:
"%~dp0hsm-agent.exe" --version

echo.
echo Stopping HSMAgent...
sc stop HSMAgent >nul 2>&1
timeout /t 3 /nobreak >nul

echo Copying new binary...
copy /Y "%~dp0hsm-agent.exe" "%INSTALL_DIR%\hsm-agent.exe" >nul
if %errorlevel% neq 0 (
  echo ERROR: copy failed — is the exe locked? & pause & exit /b 1
)

echo Starting HSMAgent...
sc start HSMAgent >nul
if %errorlevel% neq 0 (
  echo ERROR: sc start failed. & pause & exit /b 1
)

echo.
echo HSM Agent updated successfully.
pause
endlocal
