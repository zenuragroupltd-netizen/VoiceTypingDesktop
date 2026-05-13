# Voice Typing Desktop

Lightweight Windows desktop voice-typing app. Speak into any text field
in any Windows program and have it transcribed (via OpenAI Whisper) and
pasted where your cursor is. Pair your phone for cross-device text
handoff, and translate between 60+ languages — all from one place.

- 🎤 **Voice Box** — dictation straight into the active window (Word,
  Chrome, WhatsApp, anywhere)
- 📱 **Mobile Sync** — scan a QR on your phone and send text from there
  straight into your desktop
- 🌐 **Translator** — Google Translate + MyMemory fallback, 60+ languages
  with real flag icons
- ⚙️ **Light / Dark theme**, tray icon, global hotkey, auto-update check

## Install (end user)

1. Grab the latest `VoiceTypingDesktop-Setup-X.Y.Z.exe` from the
   [Releases page](https://github.com/YOUR_USER/VoiceTypingDesktop/releases).
2. Double-click it. The installer is self-contained — no .NET install needed.
3. Follow the wizard. Pick Start-Menu + desktop shortcut options if you want.
4. Launch, go to **Voice Box → Settings**, paste your OpenAI API key,
   and start dictating.

Windows 10/11, x64 only.

## Develop / build from source

```powershell
# One-time setup
winget install Microsoft.DotNet.SDK.8
winget install JRSoftware.InnoSetup

# Restore + run
dotnet restore
dotnet run

# Build a signed-ready installer (produces VoiceTypingDesktop-Setup-X.Y.Z.exe)
powershell -ExecutionPolicy Bypass -File Tools\package.ps1
```

### Note on Windows Smart App Control

If you build inside `C:\Users\<you>\Desktop\…`, Windows Smart App Control
may refuse to launch the unsigned exe. Keep the project under a non-Desktop
path (e.g. `C:\Dev\VoiceTypingDesktop`) while iterating, or sign the
release build with a code-signing certificate.

## Release a new version

1. Bump versions:
   - `VoiceTypingDesktop.csproj`: `<Version>`, `<AssemblyVersion>`, `<FileVersion>`
   - `Installer\VoiceTypingDesktop.iss`: `#define MyAppVersion`
2. Run `Tools\package.ps1` — produces `Installer\Output\VoiceTypingDesktop-Setup-X.Y.Z.exe`.
3. `git tag vX.Y.Z && git push --tags`.
4. Create a GitHub Release for that tag and upload the `setup.exe`.
5. Update the Supabase `app_versions` table (see
   [docs/RELEASING.md](docs/RELEASING.md)) with the new download URL.

## Project layout

```
VoiceTypingDesktop/
├─ App.xaml / App.xaml.cs              WPF entry, theme + version-gate
├─ MainWindow.xaml(.cs)                Shell window + tab switching
├─ MainWindow.VoiceBox.cs              Dictation tab
├─ MainWindow.Translator.cs            Translator tab
├─ MainWindow.About.cs                 About tab + update check
├─ Themes/                             Light/Dark palettes + control styles
├─ Services/                           Recording, Translation, Supabase, Tray
├─ Views/                              Update-dialog windows
├─ Config/AppConfig.cs                 appsettings.json schema
├─ Assets/                             Icons, profile picture, country flags
├─ Tools/package.ps1                   One-shot installer builder
└─ Installer/                          Inno Setup script + output/
```

## License

Proprietary. © 2026 Muinol Islam — KinetiMart.
