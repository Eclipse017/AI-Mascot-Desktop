@echo off
setlocal
set "PWSH=%ProgramFiles%\PowerShell\7\pwsh.exe"
if not exist "%PWSH%" (
  set "PWSH="
  for /f "delims=" %%P in ('where pwsh.exe 2^>nul') do if not defined PWSH set "PWSH=%%P"
)
if not defined PWSH (
  echo PowerShell 7 is required. See README.txt for installation instructions.
  pause
  exit /b 1
)
"%PWSH%" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Install.ps1" -PayloadDirectory "%~dp0app" %*
set "RESULT=%ERRORLEVEL%"
if not "%RESULT%"=="0" (
  echo Installation failed. The error above explains what is missing.
) else (
  echo Installation complete. Open Gemini on your desktop to choose an app.
  echo Keep the backup directory shown above for restoration.
)
pause
exit /b %RESULT%
