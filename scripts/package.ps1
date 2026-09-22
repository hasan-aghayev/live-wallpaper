[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'dist')
)

$ErrorActionPreference = 'Stop'
$rootDirectory = Split-Path -Parent $PSScriptRoot
$stageDirectory = Join-Path $rootDirectory 'artifacts\publish'
$projectFile = Join-Path $rootDirectory 'LiveWallpaper.csproj'
$outputDirectory = [IO.Path]::GetFullPath($OutputDirectory)

if (Test-Path -LiteralPath $stageDirectory) {
    Remove-Item -LiteralPath $stageDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $stageDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

Write-Host '[1/4] Publishing self-contained Windows x64 application...' -ForegroundColor Cyan
dotnet publish $projectFile -c $Configuration -r win-x64 --self-contained true -o $stageDirectory `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

$rootMpv = Join-Path $rootDirectory 'mpv.exe'
if (Test-Path -LiteralPath $rootMpv) {
    Write-Host '[2/4] Reusing the local mpv runtime...' -ForegroundColor Cyan
    Copy-Item -Path (Join-Path $rootDirectory 'mpv*') -Destination $stageDirectory -Force -ErrorAction SilentlyContinue
    $rootMpvData = Join-Path $rootDirectory 'mpv'
    if (Test-Path -LiteralPath $rootMpvData -PathType Container) {
        $stageMpvData = Join-Path $stageDirectory 'mpv'
        New-Item -ItemType Directory -Path $stageMpvData -Force | Out-Null
        Copy-Item -Path (Join-Path $rootMpvData '*') -Destination $stageMpvData -Recurse -Force
    }
}
else {
    Write-Host '[2/4] Downloading the mpv runtime...' -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot 'setup-mpv.ps1') -DestinationDirectory $stageDirectory
    if ($LASTEXITCODE -ne 0) { throw 'mpv setup failed.' }
}

if (-not (Test-Path -LiteralPath (Join-Path $stageDirectory 'LiveWallpaper.exe'))) {
    throw 'The publish directory does not contain LiveWallpaper.exe.'
}
if (-not (Test-Path -LiteralPath (Join-Path $stageDirectory 'mpv.exe'))) {
    throw 'The publish directory does not contain mpv.exe.'
}

Write-Host '[3/4] Adding user documentation and installer...' -ForegroundColor Cyan
Copy-Item (Join-Path $rootDirectory 'README.md') $stageDirectory -Force
Copy-Item (Join-Path $rootDirectory 'LICENSE') $stageDirectory -Force
Copy-Item (Join-Path $rootDirectory 'THIRD-PARTY-NOTICES.md') $stageDirectory -Force
Copy-Item (Join-Path $rootDirectory 'Run.bat') $stageDirectory -Force
Copy-Item (Join-Path $rootDirectory 'Install.ps1') $stageDirectory -Force
Copy-Item (Join-Path $rootDirectory 'Uninstall.ps1') $stageDirectory -Force

$archiveName = 'HaS-Live-Wallpaper-win-x64.zip'
$archivePath = Join-Path $outputDirectory $archiveName
if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}

Write-Host '[4/4] Creating a distributable ZIP and checksum...' -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $stageDirectory '*') -DestinationPath $archivePath -CompressionLevel Optimal
$hash = (Get-FileHash -Path $archivePath -Algorithm SHA256).Hash
Set-Content -Path (Join-Path $outputDirectory ($archiveName + '.sha256')) -Value "$hash  $archiveName"

Write-Host "[SUCCESS] Package: $archivePath" -ForegroundColor Green
Write-Host "[SUCCESS] SHA-256: $hash" -ForegroundColor Green
