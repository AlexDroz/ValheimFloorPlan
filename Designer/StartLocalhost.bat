@echo off
setlocal

set "PORT=5500"
cd /d "%~dp0"

echo.
echo Valheim Floor Plan Designer v1.0.5 - Local Server Launcher
echo Folder: %CD%
echo Port:   %PORT%
echo.

where py >nul 2>nul
if %ERRORLEVEL%==0 (
  echo Using Python launcher: py
  start "" "http://localhost:%PORT%/index.html"
  py -m http.server %PORT%
  goto :eof
)

where python >nul 2>nul
if %ERRORLEVEL%==0 (
  echo Using Python: python
  start "" "http://localhost:%PORT%/index.html"
  python -m http.server %PORT%
  goto :eof
)

where npx >nul 2>nul
if %ERRORLEVEL%==0 (
  echo Using Node: npx serve
  start "" "http://localhost:%PORT%/index.html"
  npx --yes serve . -l %PORT%
  goto :eof
)

echo ERROR: Could not find py, python, or npx.
echo Install one of these first:
echo   - Python (recommended)
echo   - Node.js
echo.
echo Press any key to close...
pause >nul
