# After Dork for Windows

A port of all eight modules and the control panel to Windows 10/11, in C#
(.NET 8) with SkiaSharp. The Swift drawing code was translated line by line
onto a small CoreGraphics look-alike (`src/AfterDork.Core/Graphics`), so the
savers look and behave like the Mac originals.

## Install

```powershell
.\build.ps1 -Install
```

This builds and runs the tests, then installs to `%LOCALAPPDATA%\Programs\AfterDork`
and adds **After Dork** to the Start Menu. Open it, pick a module, set the
options, then press **Demo** or **Set Screen Saver**. It needs the .NET 8
Desktop Runtime.

## Layout

| Path | What it is |
|---|---|
| `src/AfterDork.Core` | Graphics layer, the eight savers, settings, module catalog, updater logic, `/s /p /c` parsing |
| `src/AfterDork` | `AfterDork.exe`: the control panel and screen saver host. Each `<Module>.scr` is a copy of this launcher; the file name picks the module |
| `tools/AfterDork.Cli` | `afterdork-cli render/soak/icon`: headless previews, soak tests, icon generation |
| `tools/AfterDork.UiDriver` | Synthetic input and screen capture, used for live UI tests |
| `tools/ui-options-test.ps1` | Drives every option of every module through the real control panel |
| `tests/AfterDork.Tests` | Unit tests: graphics semantics, settings, catalog, arguments, updater, Castaway state machine, cache pixel-exactness |

## Mac → Windows mapping

| Mac | Windows |
|---|---|
| `.saver` in `~/Library/Screen Savers` | `<Module>.scr` in `%LOCALAPPDATA%\Programs\AfterDork`, selected via `HKCU\Control Panel\Desktop\SCRNSAVE.EXE` |
| Preferences plist | `%APPDATA%\AfterDork\settings.json` (same keys and types) |
| idleTime | Screen saver timeout (`SystemParametersInfo`) |
| `pmset displaysleep` (admin) | `powercfg /change monitor-timeout-ac/-dc` (no admin needed) |
| DisplayServices brightness | WMI for built-in panels, DDC/CI for monitors; "n/a" when neither responds |
| Sparkle | Daily and on-demand check of GitHub's latest release; offers the page if a newer Windows build exists |

Set `AFTERDORK_TRACE=<file>` to log why a screen saver session ended.
