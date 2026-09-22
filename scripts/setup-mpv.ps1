# scripts/setup-mpv.ps1
# Automatically downloads and configures mpv.exe for Live Wallpaper Launcher

Continue = 'Stop'
 = Split-Path -Parent System.Management.Automation.InvocationInfo.MyCommand.Path
 = Split-Path -Parent 
 = Join-Path  mpv.exe

Write-Host === Live Wallpaper Launcher · mpv Setup === -ForegroundColor Cyan

if (Test-Path ) {
    Write-Host [OK] mpv.exe is already present at:  -ForegroundColor Green
    exit 0
}

# Check if mpv is available on PATH
 = Get-Command mpv.exe -ErrorAction SilentlyContinue
if () {
    Write-Host [INFO] Found mpv.exe in system PATH:  -ForegroundColor Yellow
    Write-Host [INFO] Copying to project root...
    Copy-Item -Path .Source -Destination  -Force
    Write-Host [OK] mpv.exe copied successfully! -ForegroundColor Green
    exit 0
}

Write-Host [INFO] mpv.exe not found. Fetching latest release from GitHub (zhongfly/mpv-winbuild)... -ForegroundColor Yellow

 = https://api.github.com/repos/zhongfly/mpv-winbuild/releases/latest
try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13
     = Invoke-RestMethod -Uri  -Headers @{ User-Agent = LiveWallpaper-Setup }
     = .assets | Where-Object { .name -like mpv-x86_64-*.7z -or .name -like mpv-x86_64-*.zip } | Select-Object -First 1

    if (-not ) {
        throw Could not find compatible mpv Windows build in latest release.
    }

     = Join-Path C:\Users\hasan\AppData\Local\Temp .name
    Write-Host [INFO] Downloading (0 MB)... -ForegroundColor Cyan
    Invoke-WebRequest -Uri .browser_download_url -OutFile  -UseBasicParsing

    Write-Host [INFO] Extracting mpv.exe... -ForegroundColor Cyan
    if (.name.EndsWith(.zip)) {
        Expand-Archive -Path  -DestinationPath (Join-Path C:\Users\hasan\AppData\Local\Temp mpv_extracted) -Force
        Copy-Item (Join-Path (Join-Path C:\Users\hasan\AppData\Local\Temp mpv_extracted) mpv.exe) -Destination  -Force
    }
    else {
        # 7z format: check if tar or 7z is available in Windows 10/11
        if (Get-Command tar.exe -ErrorAction SilentlyContinue) {
             = Join-Path C:\Users\hasan\AppData\Local\Temp mpv_extract
            if (!(Test-Path )) { New-Item -ItemType Directory -Path  | Out-Null }
            tar.exe -xf  -C 
             = Get-ChildItem -Path  -Filter mpv.exe -Recurse | Select-Object -First 1
            if () {
                Copy-Item .FullName -Destination  -Force
            }
        }
    }

    if (Test-Path ) {
        Write-Host [SUCCESS] mpv.exe is ready at ! -ForegroundColor Green
    } else {
        Write-Host [NOTE] Download complete. Please extract mpv.exe from: and place it in  -ForegroundColor Yellow
    }
}
catch {
    Write-Warning Automated download failed: 
    Write-Host Please download mpv.exe manually from https://mpv.io/installation/ or https://github.com/zhongfly/mpv-winbuild/releases and place it in the LiveWallpaper root directory. -ForegroundColor Yellow
}
