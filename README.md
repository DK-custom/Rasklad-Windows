# Rasklad for Windows — Russian/English keyboard layout switcher

**English** · [Русский](README.ru.md)

A free, open-source **Punto Switcher alternative for Windows 10/11**. Rasklad automatically switches the Russian and English keyboard layouts, fixes text typed in the wrong layout, corrects small typos and accidental Caps Lock, and works offline with Windows spelling dictionaries.

**[Download the latest Windows installer or portable ZIP](https://github.com/DK-custom/Rasklad-Windows/releases/latest)**

The Windows application is developed separately from [Rasklad for macOS](https://github.com/DK-custom/Rasklad-MacOS). Releases and platform-specific shortcuts are independent.

## Features

- **Automatic RU ↔ EN layout correction:** `ghbdtn` → `привет` after a word boundary. Short and ambiguous words use additional language rules.
- **Typo and case correction**, including mixed-layout words such as `cvj;ешь` → `сможешь`.
- **Copilot or Pause/Break:** convert selected text, manually convert a word, or undo the last correction. Repeated presses switch the same word or selection back and forth, including immediately after a space. The clipboard is not used.
- **Fn layout switching:** a single Fn press switches RU ↔ EN without changing existing text. Requires a compatible keyboard that reports the supported standalone ThinkPad Fn event (`VK=255`, extended scan code `E0 63`). Fn combinations keep their original functions.
- **Punctuation spacing:** `word ,` → `word,`. Copilot restores the removed space once. Double spaces, opening brackets and dashes are preserved.
- **Code and password protection:** recognized password fields, excluded applications, terminals, code editors and Markdown code are skipped.
- **Personal dictionary and application exceptions.** Repeated undo/redo of the same correction does not train the dictionary.
- **English and Russian interface**, selected from the Windows display language at startup. Other display languages use English. The menu language is independent of the active keyboard layout.
- **Caret-anchored hints**, light/dark themes, rounded tray menu sections and a 10 pt menu font.
- **Start with Windows** toggle in the tray menu, compatible with the installer's per-user startup shortcut.
- **Offline IT dictionary:** FOLDOC (~15,000 English terms) and Russian IT terms.

Typed text is not sent anywhere or written to the diagnostic log. Rasklad does not add spelling underlines to arbitrary applications; existing underlines belong to the editor itself.

## Requirements

- Windows 10 (build 18362+) or Windows 11, x64 compatible.
- .NET Framework 4.8 or newer.
- Windows Russian (`ru-RU`) and English (`en-US`) spelling tools installed.

## Install

1. Download `Rasklad-1.0.32-windows-x64-Setup.exe` from [Releases](https://github.com/DK-custom/Rasklad-Windows/releases/latest).
2. Choose startup and desktop shortcuts if needed. Installation is per-user, without administrator rights, under `%LOCALAPPDATA%\Programs\Rasklad`.
3. Start Rasklad and right-click its tray icon. Enable **Use the Copilot key** or **Fn — switch RU / EN** under **Keys** if supported by your keyboard.

Close Punto Switcher or other keyboard conversion utilities before using Rasklad to avoid competing input hooks.

**Portable:** extract the ZIP into a writable folder and run `Rasklad.exe`. Keep the entire folder when moving it.

### Updates and uninstalling

Exit Rasklad before updating. The installer keeps settings and personal dictionary files during upgrades and uninstalling. An older copy in a custom folder is not migrated automatically.

Files beside `Rasklad.exe`:

- `Rasklad-settings.txt` — settings;
- `Rasklad-words.txt` — personal words;
- `Rasklad-exclusions.txt` — excluded applications;
- `Rasklad-replacements.txt` — optional custom replacement rules;
- `Rasklad-diagnostics.log` — diagnostic event metadata, without typed text.

### Signing

Release installers and executables are signed with the **self-signed** `Rasklad (DK-custom)` SHA-256 certificate. It is not a trusted-publisher certificate: Windows may display a warning. The public certificate is included as `Rasklad-signing.cer`; the installer does not modify trust stores. Verify downloads with `SHA256SUMS.txt`. GitHub Actions builds without the private key are unsigned.

## Shortcuts and limitations

Copilot normally sends left Win+Shift+F23. The built-in handler suppresses that chord and the Start menu; the physical key has been tested on a ThinkPad. Other keyboards may report different codes. The optional [AutoHotkey example](docs/copilot.ahk) is provided for other setups, but is not installed or configured automatically.

Fn support is hardware-specific. Custom password controls that do not expose their password state may not be recognized. Some editors do not expose a reliable caret or selection. Ambiguous short words may need manual correction. Mixed-DPI multi-monitor setups have not been verified separately.

## Build

Uses the Windows .NET Framework compiler, without NuGet or an additional .NET SDK:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

Output: `build\Rasklad.exe`. To regenerate icons, use `make-icon.ps1` and `make-icon.ps1 -Tray`.

Installer packages require [Inno Setup 6.7.3](https://jrsoftware.org/isdl.php) or a compatible version:

```powershell
.\build-release.ps1 -IsccPath 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
```

The release script creates the installer, portable ZIP, source ZIP and SHA-256 manifest in `dist`. Pass `-SigningCertificateThumbprint` to sign with a code-signing certificate in the current user's store. Never commit private keys. See [RELEASING.md](RELEASING.md) for the release procedure.

### Verification

Run UI checks separately from the working copy, without keyboard or mouse input:

```powershell
.\build\Rasklad.exe --self-test
.\build\Rasklad.exe --menu-test-ru
.\build\Rasklad.exe --menu-test-en
.\build\Rasklad.exe --menu-test-fallback
.\build\Rasklad.exe --copilot-test
.\build\Rasklad.exe --fn-test
.\build\Rasklad.exe --punctuation-test
.\build\Rasklad.exe --startup-test
```

Language checks use simulated UI cultures without changing the Windows display language. Startup checks use temporary shortcuts and preserve the real startup registration. Fn handler checks replay the observed event into the handler; physical hardware is tested separately.

## License

Rasklad code and Russian IT terms: [MIT](LICENSE), Copyright (c) 2026 DK.

The Free On-line Dictionary of Computing, [FOLDOC](https://foldoc.org/), Editor Denis Howe, Copyright 1985 Denis Howe: GNU FDL 1.1 or later, with no invariant sections or cover texts. The unchanged dictionary source and license are included. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).