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

:: Refuse a data dir that is a junction / symbolic link (a non-admin can pre-create one under
:: %ProgramData%): icacls and the config copy would otherwise act on whatever it points at.
:: The FOR "~a" modifier shows "l" for a reparse point; the "-" prefix keeps the value non-empty.
for %%A in ("%DATA_DIR%") do set "DATA_DIR_ATTR=-%%~aA"
if not "%DATA_DIR_ATTR:l=%"=="%DATA_DIR_ATTR%" (
  echo ERROR: %DATA_DIR% is a junction or symbolic link. Remove it and run this script again.
  pause & exit /b 1
)

:: config.json holds the product access key (#1279): make the data dir owned by Administrators and
:: SYSTEM + Administrators only, inheritance from %ProgramData% (BUILTIN\Users read) removed, BEFORE
:: the config is copied in. The service re-applies the same owner + DACL on every start. SIDs, not
:: names (localized Windows): S-1-5-18 = NT AUTHORITY\SYSTEM, S-1-5-32-544 = BUILTIN\Administrators.
:: The owner goes first (icacls cannot combine /setowner with /grant): it also takes back a dir that a
:: non-admin pre-created, whose owner would otherwise keep the right to rewrite the DACL.
icacls "%DATA_DIR%" /setowner *S-1-5-32-544 >nul
if %errorlevel% neq 0 (
  echo ERROR: could not make Administrators the owner of %DATA_DIR% ^(icacls failed^). Config not installed.
  pause & exit /b 1
)
icacls "%DATA_DIR%" /inheritance:r /grant *S-1-5-18:(OI)(CI)F /grant *S-1-5-32-544:(OI)(CI)F >nul
if %errorlevel% neq 0 (
  echo ERROR: could not restrict access to %DATA_DIR% ^(icacls failed^). Config not installed.
  pause & exit /b 1
)

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
