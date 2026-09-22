[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$installDirectory = Join-Path $env:LOCALAPPDATA 'Programs\HaS Studio\Live Wallpaper'
$startMenuDirectory = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\HaS Studio'
$shortcutPath = Join-Path $startMenuDirectory 'HaS Live Wallpaper.lnk'

Get-Process -Name LiveWallpaper -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
if (Test-Path -LiteralPath $shortcutPath) { Remove-Item -LiteralPath $shortcutPath -Force }
if (Test-Path -LiteralPath $installDirectory) { Remove-Item -LiteralPath $installDirectory -Recurse -Force }
if (Test-Path -LiteralPath $startMenuDirectory) {
    if (-not (Get-ChildItem -LiteralPath $startMenuDirectory -Force)) {
        Remove-Item -LiteralPath $startMenuDirectory -Force
    }
}

Write-Host 'HaS Live Wallpaper has been uninstalled.' -ForegroundColor Green
