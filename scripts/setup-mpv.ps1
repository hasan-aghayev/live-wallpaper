[CmdletBinding()]
param(
    [string]$DestinationDirectory = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

if (-not [Environment]::Is64BitOperatingSystem) {
    throw 'HaS Live Wallpaper requires 64-bit Windows.'
}

$destinationDirectory = [IO.Path]::GetFullPath($DestinationDirectory)
$mpvPath = Join-Path $destinationDirectory 'mpv.exe'
$tempDirectory = Join-Path ([IO.Path]::GetTempPath()) ('has-livewallpaper-mpv-' + [Guid]::NewGuid().ToString('N'))

function Copy-MpvRuntime([string]$sourceDirectory) {
    New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
    Copy-Item -Path (Join-Path $sourceDirectory '*') -Destination $destinationDirectory -Recurse -Force
}

try {
    if (Test-Path -LiteralPath $mpvPath) {
        Write-Host "[OK] mpv runtime already exists: $mpvPath" -ForegroundColor Green
        exit 0
    }

    $pathMpv = Get-Command mpv.exe -ErrorAction SilentlyContinue
    if ($null -ne $pathMpv) {
        Write-Host "[INFO] Found mpv.exe on PATH: $($pathMpv.Source)" -ForegroundColor Yellow
        Copy-MpvRuntime (Split-Path -Parent $pathMpv.Source)
        Write-Host "[OK] mpv runtime copied to $destinationDirectory" -ForegroundColor Green
        exit 0
    }

    Write-Host '[INFO] Downloading the current 64-bit Windows mpv build...' -ForegroundColor Cyan
    New-Item -ItemType Directory -Path $tempDirectory -Force | Out-Null

    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $apiUrl = 'https://api.github.com/repos/shinchiro/mpv-winbuild-cmake/releases/latest'
    $release = Invoke-RestMethod -Uri $apiUrl -Headers @{ 'User-Agent' = 'HaS-Live-Wallpaper-Setup' }
    $asset = $release.assets |
        Where-Object { $_.name -match '^mpv-x86_64-v3-.*\.(7z|zip)$' } |
        Select-Object -First 1

    if ($null -eq $asset) {
        $asset = $release.assets |
            Where-Object { $_.name -match '^mpv-x86_64-.*\.(7z|zip)$' } |
            Select-Object -First 1
    }

    if ($null -eq $asset) {
        throw 'The mpv release did not contain a compatible x86_64 archive.'
    }

    $archivePath = Join-Path $tempDirectory $asset.name
    Write-Host "[INFO] Downloading $($asset.name)..." -ForegroundColor Cyan
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $archivePath -UseBasicParsing

    $extractDirectory = Join-Path $tempDirectory 'extracted'
    New-Item -ItemType Directory -Path $extractDirectory -Force | Out-Null

    if ($asset.name.EndsWith('.zip', [StringComparison]::OrdinalIgnoreCase)) {
        Expand-Archive -Path $archivePath -DestinationPath $extractDirectory -Force
    }
    else {
        $sevenZipCommand = Get-Command 7z.exe -ErrorAction SilentlyContinue
        if ($null -eq $sevenZipCommand) { $sevenZipCommand = Get-Command 7zz.exe -ErrorAction SilentlyContinue }
        $sevenZipPath = if ($null -ne $sevenZipCommand) {
            $sevenZipCommand.Source
        } elseif (Test-Path 'C:\Program Files\7-Zip\7z.exe') {
            'C:\Program Files\7-Zip\7z.exe'
        } else {
            $null
        }
        if ([string]::IsNullOrWhiteSpace($sevenZipPath)) {
            throw 'The downloaded mpv archive is 7z. Install 7-Zip, then run this script again.'
        }
        & $sevenZipPath x $archivePath "-o$extractDirectory" -y | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "7-Zip could not extract $($asset.name)."
        }
    }

    $mpvFile = Get-ChildItem -Path $extractDirectory -Filter 'mpv.exe' -File -Recurse | Select-Object -First 1
    if ($null -eq $mpvFile) {
        throw 'The downloaded archive did not contain mpv.exe.'
    }

    Copy-MpvRuntime $mpvFile.DirectoryName
    if (-not (Test-Path -LiteralPath $mpvPath)) {
        throw 'mpv.exe was not copied to the destination directory.'
    }

    Write-Host "[SUCCESS] mpv runtime is ready: $mpvPath" -ForegroundColor Green
}
catch {
    Write-Error "mpv setup failed: $($_.Exception.Message)"
    exit 1
}
finally {
    if (Test-Path -LiteralPath $tempDirectory) {
        Remove-Item -LiteralPath $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}
