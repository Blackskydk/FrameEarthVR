@echo off
setlocal
pushd "%~dp0"

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\build-and-install-steam-frame.ps1" %*
set "FRAME_BUILD_EXIT=%ERRORLEVEL%"

echo.
if not "%FRAME_BUILD_EXIT%"=="0" (
    echo Build or installation failed. Check Logs\SteamFrameBuild.log for details.
) else (
    echo FrameEarthVR is installed on the Steam Frame.
)
echo.
pause

popd
exit /b %FRAME_BUILD_EXIT%

