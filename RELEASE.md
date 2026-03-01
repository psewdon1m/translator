# Release Process

This repository keeps:

- source code in the repo
- ready-to-download app builds in GitHub Releases

## 1. Build Release

From repository root:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
```

Output:

```text
bin\Release\net8.0-windows\win-x64\publish
```

## 2. Prepare Release Archive

Archive the full `publish` directory contents, not just `TranslatorTray.exe`.

Recommended archive name:

```text
TranslatorTray-win-x64-vX.Y.Z.zip
```

The archive must keep:

- `TranslatorTray.exe`
- `Data/`
- `src/`
- any other files produced in `publish/`

## 3. Create GitHub Release

Suggested flow:

1. Push code to `main`
2. Create tag, for example:
   - `v0.1.0`
3. Open GitHub `Releases`
4. Create a new release from that tag
5. Upload the release zip

## 4. Suggested Release Notes

Include at least:

- new features
- hotkey changes
- known limitations
- upgrade notes if settings changed

## 5. Verify Before Publishing

Minimum manual checks:

- tray icon appears
- one-key `RU/EN` switch works
- auto layout/translit correction works
- selected text conversion works
- invert case works
- offline autocorrect works
- settings window opens and saves
- custom sounds load

