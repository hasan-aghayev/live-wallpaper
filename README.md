# HaS Live Wallpaper

Лёгкие живые обои для Windows 10/11 x64. Приложение ставит видео за значки рабочего стола, использует аппаратное декодирование через Direct3D 11 и не перекодирует исходный файл.

Проект сделан и поддерживается Hasan Aghayev / HaS Studio.

## Что умеет приложение

- MP4, MKV, MOV, WEBM, AVI, WMV, M4V, MPEG, TS и другие распространённые форматы, которые понимает mpv.
- Аппаратное воспроизведение через Direct3D 11 с автоматическим fallback на программное декодирование.
- Циклическое воспроизведение, пауза, громкость и отключение звука.
- Три режима кадрирования: заполнить экран, показать весь кадр, растянуть.
- Плавное чёрное затемнение поверх видео, чтобы иконки рабочего стола оставались читаемыми.
- Автоматическая пауза, когда рабочий стол скрыт, и освобождение ресурсов через заданное время.
- Drag & drop из Проводника.
- Системный трей, запуск вместе с Windows и восстановление последнего видео.
- Поддержка виртуального рабочего стола и изменения конфигурации мониторов.
- Все настройки и логи хранятся в профиле пользователя, поэтому установка в защищённую папку Windows работает корректно.

## Скачать и установить

1. Откройте раздел [Releases](https://github.com/hasan-aghayev/live-wallpaper/releases) и скачайте `HaS-Live-Wallpaper-win-x64.zip`.
2. Распакуйте архив в любую папку.
3. Для обычного portable-запуска запустите `Run.bat` или `LiveWallpaper.exe`.
4. Для установки ярлыка в меню «Пуск» запустите PowerShell в распакованной папке:

```powershell
powershell -ExecutionPolicy Bypass -File .\Install.ps1 -Start
```

Установка пользовательская и не требует прав администратора. Она копирует приложение в `%LOCALAPPDATA%\Programs\HaS Studio\Live Wallpaper`. Удаление выполняется командой:

```powershell
powershell -ExecutionPolicy Bypass -File .\Uninstall.ps1
```

Релиз уже содержит `mpv.exe` и нужные файлы движка, поэтому отдельная установка .NET или mpv для пользователя не нужна.

## Быстрый старт

1. Запустите приложение.
2. Перетащите видео в большую область или нажмите `Choose video`.
3. Выберите громкость, режим масштабирования, затемнение и экономию GPU. Настройки применяются автоматически; кнопка `Save settings` внизу сохраняет и повторно применяет их все сразу.
4. Нажмите `Apply wallpaper`.
5. Закрытие окна по умолчанию сворачивает приложение в системный трей. Для полного выхода выберите `Exit` в меню иконки возле часов.

## Сборка из исходников

Для сборки нужен Windows 10/11 x64 и [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
git clone https://github.com/hasan-aghayev/live-wallpaper.git
cd live-wallpaper

# Подготовить mpv в корне проекта, если его ещё нет
powershell -ExecutionPolicy Bypass -File .\scripts\setup-mpv.ps1

# Проверить исходники
dotnet build -c Release

# Собрать готовый self-contained архив для Windows x64
pwsh -NoProfile -File .\scripts\package.ps1
```

`package.ps1` создаёт:

- `dist\HaS-Live-Wallpaper-win-x64.zip` — готовый архив;
- `dist\HaS-Live-Wallpaper-win-x64.zip.sha256` — checksum SHA-256 для проверки загрузки;
- `artifacts\publish` — содержимое архива до сжатия.

Если `mpv.exe` отсутствует, `setup-mpv.ps1` и `package.ps1` скачивают 64-битную сборку из [Windows builds by shinchiro](https://github.com/shinchiro/mpv-winbuild-cmake/releases). Для `.7z`-архива нужен 7-Zip. На GitHub Actions он доступен на Windows runner.

## Архитектура

```text
WPF launcher (UI, settings, tray)
        |
        +-- WallpaperWindow (WinForms HWND, no border, no activation)
        |       |
        |       +-- DesktopManager -> Progman / WorkerW / SHELLDLL_DefView
        |       |
        |       +-- mpv.exe -> Direct3D 11 / gpu-next -> video frame
        |               |
        |               +-- named pipe IPC, asynchronous command queue
        |
        +-- ConfigManager -> %LOCALAPPDATA%\HaS Studio\Live Wallpaper\config.json
        +-- shader cache -> %LOCALAPPDATA%\HaS Studio\Live Wallpaper\shaders
        +-- diagnostic log -> %LOCALAPPDATA%\HaS Studio\Live Wallpaper\debug.log
```

`mpv` запускается как дочерний процесс приложения. При остановке закрывается только этот процесс — другие экземпляры mpv на компьютере не затрагиваются. Команды громкости, паузы и масштабирования отправляются через именованный канал Windows, чтобы не блокировать интерфейс.

## Надёжность и ограничения

- Поддерживается только 64-битная Windows. Это связано с x64-сборкой приложения и видеодвижка.
- Обои зависят от способа, которым текущая версия Windows строит `Progman`/`WorkerW`. Для нестандартных оболочек Windows приложение оставляет диагностическую запись в `debug.log`.
- Если GPU не поддерживает аппаратный путь, mpv переключается на безопасный режим декодирования. Производительность в этом случае зависит от разрешения видео и CPU.
- Приложение не конвертирует видео и не создаёт копию медиафайла.
- Логи не отправляются в интернет.

### Если видео не запускается

1. Проверьте, что файл существует и открывается обычным видеоплеером.
2. Попробуйте MP4/H.264 как контрольный файл.
3. Проверьте наличие `mpv.exe` рядом с `LiveWallpaper.exe`.
4. Посмотрите `%LOCALAPPDATA%\HaS Studio\Live Wallpaper\debug.log`.
5. Если проблема остаётся, приложите к issue версию Windows, видеокарту, расширение видео и лог. Само видео загружать не нужно.

## Структура проекта

```text
LiveWallpaper/
├── App.xaml(.cs)             # запуск, single-instance, обработка завершения
├── MainWindow.xaml(.cs)      # интерфейс и пользовательские настройки
├── WallpaperWindow.cs        # окно вывода видео
├── Core/
│   ├── AppPaths.cs            # безопасные пути профиля пользователя
│   ├── ConfigManager.cs       # нормализация и атомарное сохранение настроек
│   ├── DesktopManager.cs      # интеграция с рабочим столом Windows
│   ├── MpvPlayer.cs           # изолированный mpv-процесс и IPC
│   ├── VideoFileValidator.cs  # проверка входного видео
│   ├── TrayManager.cs         # системный трей
│   └── AutoStartManager.cs    # запуск вместе с Windows
├── scripts/
│   ├── setup-mpv.ps1          # загрузка mpv для исходной сборки
│   └── package.ps1            # self-contained публикация и ZIP-релиз
├── Install.ps1 / Uninstall.ps1
├── THIRD-PARTY-NOTICES.md
└── .github/workflows/build.yml
```

## Лицензии

Исходный код проекта распространяется по [MIT License](LICENSE). В комплекте также поставляется внешний runtime `mpv`; его лицензия и источник сборки описаны в [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Автор

**Hasan Aghayev** — HaS Studio

- GitHub: [@hasan-aghayev](https://github.com/hasan-aghayev)
- Email: [hasan.agaev@gmail.com](mailto:hasan.agaev@gmail.com)
