@echo off
setlocal
:: HSM Agent — in-place update (service must already be installed).
:: Stops the service, swaps the exe, restarts. Config is NOT touched.
:: Requires Administrator: right-click > Run as administrator.

net session >nul 2>&1
if %errorlevel% neq 0 (
  echo ERROR: Run this script as Administrator ^(right-click ^> Run as administrator^).
  pause
  exit /b 1
)

:: Locate new exe: next to this script first, then known cmake build-output paths.
set "EXE=%~dp0hsm-agent.exe"
if not exist "%EXE%" set "EXE=%~dp0..\..\..\build\agent-static\Release\hsm-agent.exe"
if not exist "%EXE%" set "EXE=%~dp0..\build\release\Release\hsm-agent.exe"
if not exist "%EXE%" (
  echo ERROR: hsm-agent.exe not found.
  echo Build the project first ^(cmake --build^), or copy hsm-agent.exe next to this script.
  pause & exit /b 1
)
echo Using exe: %EXE%

set "INSTALL_DIR=%ProgramFiles%\HSM Agent"

echo Stopping HSMAgent...
sc stop HSMAgent >nul 2>&1
timeout /t 3 /nobreak >nul

echo Copying new binary...
copy /Y "%EXE%" "%INSTALL_DIR%\hsm-agent.exe" >nul
if %errorlevel% neq 0 (
  echo ERROR: copy failed — is the exe locked? & pause & exit /b 1
)

echo Starting HSMAgent...
sc start HSMAgent >nul
if %errorlevel% neq 0 (
  echo ERROR: sc start failed. & pause & exit /b 1
)

echo HSM Agent updated and restarted.
pause
endlocal
