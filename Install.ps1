[CmdletBinding()]
param([switch]$Start)

$ErrorActionPreference = 'Stop'
$sourceDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceExe = Join-Path $sourceDirectory 'LiveWallpaper.exe'
$sourceMpv = Join-Path $sourceDirectory 'mpv.exe'

if (-not (Test-Path -LiteralPath $sourceExe) -or -not (Test-Path -LiteralPath $sourceMpv)) {
    throw 'Run Install.ps1 from the extracted release folder containing LiveWallpaper.exe and mpv.exe.'
}

$installDirectory = Join-Path $env:LOCALAPPDATA 'Programs\HaS Studio\Live Wallpaper'
$startMenuDirectory = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\HaS Studio'
$shortcutPath = Join-Path $startMenuDirectory 'HaS Live Wallpaper.lnk'

New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
Copy-Item -Path (Join-Path $sourceDirectory '*') -Destination $installDirectory -Recurse -Force
New-Item -ItemType Directory -Path $startMenuDirectory -Force | Out-Null

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = Join-Path $installDirectory 'LiveWallpaper.exe'
$shortcut.WorkingDirectory = $installDirectory
$shortcut.IconLocation = "$($shortcut.TargetPath),0"
$shortcut.Description = 'HaS Live Wallpaper'
$shortcut.Save()

Write-Host "HaS Live Wallpaper installed to: $installDirectory" -ForegroundColor Green
Write-Host "Start Menu shortcut created: $shortcutPath" -ForegroundColor Green

if ($Start) {
    Start-Process -FilePath (Join-Path $installDirectory 'LiveWallpaper.exe') -WorkingDirectory $installDirectory
}
