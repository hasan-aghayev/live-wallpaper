# 🎬 Live Wallpaper Launcher

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20x64-0078D6.svg)](https://microsoft.com/windows)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![Direct3D 11](https://img.shields.io/badge/Renderer-Direct3D%2011%20%2F%20Libplacebo-brightgreen.svg)]()
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg)](https://github.com/hasan-aghayev/live-wallpaper/pulls)

A high-performance, lightweight, hardware-accelerated **Live Video Wallpaper Launcher** for Windows 10 and Windows 11 (x64). Set any video (MP4, MKV, MOV, WEBM) as a smooth desktop background behind your desktop icons with **100% original quality, zero compression, and customizable desktop dimming**.

Designed and crafted by **Hasan Aghayev** ([hasan.agaev@gmail.com](mailto:hasan.agaev@gmail.com)) adhering to the **HaS Studio** design system (`design.md`).

---

## ✨ Features

### 1. 100% Original Quality (Zero Compression)
- **Direct Stream Playback**: Videos are never transcoded, compressed, or downscaled upon loading.
- **Direct3D 11 & Libplacebo**: High-grade GPU decoding pipeline (`d3d11va`, `vo=gpu-next`) powered by mpv. Optimized for NVIDIA GeForce RTX, AMD Radeon, and Intel Arc.
- **4K, 120 FPS & 10-bit HDR**: Full native playback of high-bitrate files (15+ Mbps, `yuv422p10` / HDR) using **Spline36** scaling and automatic dithering (`dither-depth=auto`).

### 2. 🎨 Hardware Desktop Dimming & Color Overlay
- Real-time dimming opacity slider (**0% to 90%**) to ensure desktop icons and text labels remain 100% readable even behind bright videos.
- Curated color tones:
  - ⚫ **Deep Black** (`#000000`)
  - 🌑 **Slate** (`#0F172A`)
  - 🔵 **Navy Indigo** (`#0A192F`)
  - 🟣 **Cyberpunk** (`#1E1035`)
  - 🟢 **Emerald** (`#062419`)
  - 🎨 **Custom Color**: Full Windows RGB palette picker.
- Utilizes hardware-accelerated Windows DWM composition (`WS_EX_TRANSPARENT | WS_EX_LAYERED`). Zero CPU overhead; passes all mouse clicks through directly to the desktop icons.

### 3. 📂 Drag & Drop Simplicity
- Drag and drop any video file directly from Windows File Explorer into the launcher dropzone.
- Automatic file validation and instant preview.

### 4. 🔊 Audio & Scaling Controls
- **Volume**: 0% to 100% slider with a quick Mute checkbox (muted by default for distraction-free work).
- **Aspect Scaling**:
  - *Fill Screen (Aspect Fill)*: Edge-to-edge fill without black borders (recommended).
  - *Fit to Screen (Aspect Fit)*: 100% video frame visible without cropping.
  - *Stretch to Fill*: Full screen stretch.

### 5. 📥 System Tray & Windows Auto-start
- Closes and minimizes to the Windows System Tray near the taskbar clock.
- Right-click tray menu:
  - 🖥 *Open Launcher*
  - ⏸ *Pause / Resume*
  - 🔊 *Mute / Unmute*
  - ⏹ *Stop Wallpaper*
  - ❌ *Exit*
- **"Run live wallpaper on Windows startup"** automatically restores your active wallpaper when your PC boots.

---

## 🚀 Quick Start

### Option A: Run Pre-built Release
1. Download the latest release from the [Releases](https://github.com/hasan-aghayev/live-wallpaper/releases) page.
2. Extract the archive and run **`Run.bat`** (or `publish\LiveWallpaper.exe`).

### Option B: Build from Source
#### Prerequisites
- Windows 10 or Windows 11 (64-bit)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

#### Clone & Build
```powershell
# 1. Clone repository
git clone https://github.com/hasan-aghayev/live-wallpaper.git
cd live-wallpaper

# 2. Setup mpv binary (downloads official mpv.exe automatically)
.\scripts\setup-mpv.ps1

# 3. Build release
dotnet build -c Release

# 4. Publish distribution
dotnet publish -c Release -o ./publish

# 5. Run application
.\Run.bat
```

---

## 🏗 Architecture & Under the Hood

```
+--------------------------------------------------------------+
|                    Windows DWM Shell Layer                   |
+--------------------------------------------------------------+
  | (Z-Order Top)
  +--> [Desktop Icons] SHELLDLL_DefView
  |
  +--> [Dimming Layer] DesktopOverlayWindow (WS_EX_LAYERED | WS_EX_TRANSPARENT)
  |
  +--> [Video Surface] WallpaperWindow (WinForms HWND)
  |      ^
  |      |-- D3D11 Swapchain Render
  |      +-- mpv.exe process (--wid=HWND, --vo=gpu-next)
  |            ^
  |            |-- Real-time IPC via Named Pipe (JSON protocol)
  |
  +--> [Desktop Background] WorkerW / Progman
```

- **Zero Airspace Conflict**: Unlike WPF `MediaElement`, mpv renders via Direct3D 11 flip-model swapchain directly into a WinForms window hook behind `SHELLDLL_DefView`.
- **Inter-Process Communication**: High-frequency commands (volume, seek, pause, stretch) communicate via `\\.\pipe\livewallpaper_mpv_*` using asynchronous JSON-RPC.

---

## 📁 Project Structure

```text
LiveWallpaper/
├── App.xaml / App.xaml.cs          # HaS Studio design tokens & single-instance lifecycle
├── MainWindow.xaml / .cs           # Main launcher interface & controls
├── WallpaperWindow.cs              # Desktop canvas window for video rendering
├── DesktopOverlayWindow.cs         # Hardware layered color & dimming overlay
├── design.md                       # HaS Studio UI design system specification
├── app.ico                         # High-resolution multi-size application icon
├── Core/
│   ├── DesktopManager.cs           # Win32 desktop hooking (Progman / WorkerW)
│   ├── MpvPlayer.cs                # Libplacebo Direct3D 11 engine & IPC client
│   ├── TrayManager.cs              # Windows system tray integration
│   ├── ConfigManager.cs            # Persistent JSON user preferences
│   └── AutoStartManager.cs         # Windows registry auto-start controller
├── scripts/
│   ├── setup-mpv.ps1               # Automated mpv.exe fetch script
│   └── setup-mpv.bat               # 1-click batch wrapper for mpv setup
├── .github/workflows/
│   └── build.yml                   # GitHub Actions automated CI build
├── Run.bat                         # Quick launcher script
├── LICENSE                         # MIT License
└── README.md                       # Documentation
```

---

## 👤 Author & Maintainer

* **Hasan Aghayev**
  * Email: [hasan.agaev@gmail.com](mailto:hasan.agaev@gmail.com)
  * Organization: **HaS Studio**
  * GitHub: [@hasan-aghayev](https://github.com/hasan-aghayev)

---

## 📄 License

This project is licensed under the **MIT License** — see the [LICENSE](LICENSE) file for details.
All contributions and pull requests are welcome!
