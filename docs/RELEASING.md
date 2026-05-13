# Release workflow

Step-by-step recipe for cutting a new public release of
**Voice Typing Desktop**. Takes ~5 minutes once the code is ready.

---

## 1. Bump the version

Pick a new SemVer number (e.g. `1.1.0`). Update **two** files to match:

| File | Fields |
|---|---|
| `VoiceTypingDesktop.csproj` | `<Version>`, `<AssemblyVersion>`, `<FileVersion>` |
| `Installer/VoiceTypingDesktop.iss` | `#define MyAppVersion` |

> The assembly version is what the app reports under About → "Your version"
> and what the Supabase version-gate compares against.

## 2. Build the installer

From the project root (must be somewhere OTHER than `Desktop\`, e.g.
`C:\Dev\VoiceTypingDesktop`, so Windows Smart App Control doesn't block
the unsigned binary during your own testing):

```powershell
powershell -ExecutionPolicy Bypass -File Tools\package.ps1
```

Output: `Installer\Output\VoiceTypingDesktop-Setup-X.Y.Z.exe` (≈ 50 MB).

Smoke-test it: double-click the exe, install into the default location,
open the app, Voice Box → click the mic to verify transcription still works.

## 3. Commit + tag

```powershell
cd C:\Users\moina\Desktop\VoiceTypingDesktop
git add .
git commit -m "Release v1.1.0 — premium flags, mic hallucination filter, Play Store button"
git tag v1.1.0
git push
git push --tags
```

## 4. Create the GitHub Release

1. Go to your repo on GitHub → **Releases** → **Draft a new release**.
2. Pick the `v1.1.0` tag you just pushed.
3. Title: `v1.1.0 — <short summary>`.
4. Drop the changelog in the description (see template below).
5. Drag `VoiceTypingDesktop-Setup-1.1.0.exe` into the **Attach binaries**
   box.
6. Click **Publish release**.

GitHub will give you a stable direct-download URL that looks like:

```
https://github.com/<USER>/VoiceTypingDesktop/releases/download/v1.1.0/VoiceTypingDesktop-Setup-1.1.0.exe
```

This URL is what you paste into Supabase in the next step.

### Changelog template

```markdown
## What's new in 1.1.0

- ✨ Real country flag PNGs in the Translator language picker
- ✨ Google Play CTA button on the Mobile Sync pair card
- ✨ Paste button on every Mobile Sync history item
- 🐛 Whisper no longer hallucinates "নমস্কার" on silent audio
- 🎨 Default theme is now Light
- 🎨 Profile picture renders as a true circle (fixes square crop bug)

## Install

1. Download `VoiceTypingDesktop-Setup-1.1.0.exe` below.
2. Double-click to run the installer.
3. If SmartScreen warns: More info → Run anyway.
```

## 5. Flip the Supabase switch

Open Supabase Dashboard → **Table Editor** → `app_versions` → the row
where `id = 'desktop'`. Update these fields:

| Column | Value |
|---|---|
| `latest_version` | `1.1.0` |
| `minimum_version` | Choose one: `1.1.0` to **force** all older installs to update. `1.0.0` to offer an **optional** update. |
| `download_url` | The GitHub Release download URL from Step 4. |
| `release_notes` | Copy the changelog from Step 4. |
| `force_update` | Usually `false`. Set `true` only for emergency lockout. |

Save. Every desktop app on the next launch (or next click of **About →
Check for Updates**) will pick up the new row.

## 6. Verify

1. On your dev machine, open the installed 1.0.0 build.
2. About → Check for Updates → should see an Update Available dialog
   for 1.1.0 with your release notes.
3. Click Download → installer opens in your browser via the GitHub URL.

Done. Tell users.
