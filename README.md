# TranslatorTray

Windows tray app for fast `RU <-> EN` layout switching, translit correction, selected text conversion, case inversion, and offline safe autocorrect.

## Features

- one-key `RU/EN` layout switch
- regular Windows layout switching still available
- auto layout/translit correction after delimiter
- suspend auto layout correction after manual switch until next delimiter
- toggle auto layout correction by hotkey
- convert selected text layout by hotkey
- invert selected text case by hotkey
- offline spelling/autocorrect in safe mode
- protected words and protected professional terms
- tray icon, settings UI, custom sounds

## Platform

- `Windows 10/11`
- `.NET 8`

## Run From Source

Requirements:

- `.NET SDK 8`

Run:

```powershell
dotnet run --project TranslatorTray.csproj
```

## Build

Debug build:

```powershell
dotnet build
```

Release publish:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
```

Published app:

```text
bin\Release\net8.0-windows\win-x64\publish\TranslatorTray.exe
```

## Download

Ready-to-use builds should be attached to GitHub Releases.

Download the latest release archive, extract it, and run:

```text
TranslatorTray.exe
```

Do not separate the executable from the release folder contents. The app uses bundled runtime data from:

- `Data`
- `src`

## Configuration

Settings are stored in:

```text
%LocalAppData%\TranslatorTray\settings.json
```

Protected domain terms are stored in:

```text
Data\protected_terms.txt
```

You can extend this file with your own terms.

## Project Structure

```text
Data/       dictionaries, protected terms, runtime data
Models/     settings and simple models
Native/     Win32 and COM interop
Services/   input, layout, spellcheck, sounds, app services
UI/         WinForms settings UI
src/        icons and user-provided sounds
```

## Current Scope

The project is optimized for standard Windows text fields and common desktop apps.

Known non-priority limitation:

- terminal/console behavior may differ from regular text controls

