@echo off
setlocal
pushd "%~dp0"

where pwsh >nul 2>&1
if "%ERRORLEVEL%"=="0" (set "PS_EXE=pwsh") else (set "PS_EXE=powershell")

echo =========================================
echo  Time Never Stops Build
echo  Version: (from Version.props)
echo  Script : run-build.ps1
echo  Using  : %PS_EXE%
echo  Args   : %*
echo =========================================
echo.

"%PS_EXE%" -NoProfile -ExecutionPolicy Bypass -File ".\run-build.ps1" %*
set "EC=%ERRORLEVEL%"
echo.
echo Exit Code: %EC%
if not "%EC%"=="0" (echo [FAILED] Build/script returned an error.) else (echo [OK] Completed successfully.)

echo.
echo Press any key to close...
pause >nul
popd
endlocal