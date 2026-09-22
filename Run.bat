@echo off
setlocal
if exist "%~dp0LiveWallpaper.exe" (
    start "HaS Live Wallpaper" /d "%~dp0" "%~dp0LiveWallpaper.exe"
    exit /b 0
)
if exist "%~dp0publish\LiveWallpaper.exe" (
    start "HaS Live Wallpaper" /d "%~dp0publish" "%~dp0publish\LiveWallpaper.exe"
    exit /b 0
)
echo LiveWallpaper.exe was not found. Build or extract the release package first.
pause
