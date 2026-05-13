# package.ps1
# --------------------------------------------------------------
# One-shot packaging script for Voice Typing Desktop.
#
# What it does:
#   1) (Re)generates Assets\app.ico from Tools\make-icon.ps1
#   2) Publishes the app as self-contained Release for win-x64
#      into Installer\Publish (so user does NOT need .NET installed)
#   3) Runs Inno Setup (ISCC.exe) to produce a setup.exe in
#      Installer\Output
#
# Run from the project root:
#   powershell -ExecutionPolicy Bypass -File Tools\package.ps1
# --------------------------------------------------------------

param(
    [string]$Configuration = "Release",
    [ValidateSet("self-contained","framework-dependent")]
    [string]$Mode          = "self-contained"
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root

Write-Host "`n[1/3] Refreshing app icon..."
powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "make-icon.ps1") | Write-Host

Write-Host "`n[2/3] Publishing $Configuration ($Mode) ..."
$publishDir = Join-Path $root "Installer\Publish"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

$selfContained = ($Mode -eq "self-contained").ToString().ToLower()
dotnet publish "$root\VoiceTypingDesktop.csproj" `
    -c $Configuration `
    -r win-x64 `
    --self-contained $selfContained `
    -p:PublishSingleFile=false `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None -p:DebugSymbols=false `
    -o $publishDir | Write-Host

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

Write-Host "`n[3/3] Building installer..."

# Locate ISCC.exe (Inno Setup compiler).
$iscCandidates = @(
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 5\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 5\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 5\ISCC.exe"
)
$iscc = $iscCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    Write-Warning "Inno Setup not found. Install it from https://jrsoftware.org/isdl.php"
    Write-Warning "Publish folder is ready at: $publishDir"
    Write-Warning "You can still zip that folder and share it; users run VoiceTypingDesktop.exe directly."
    return
}

& $iscc (Join-Path $root "Installer\VoiceTypingDesktop.iss") | Write-Host
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed (exit $LASTEXITCODE)" }

$out = Join-Path $root "Installer\Output"
Write-Host "`nDONE. Installer in: $out"
Get-ChildItem $out -Filter "*Setup*.exe" | ForEach-Object { Write-Host "  $($_.FullName)  ($([math]::Round($_.Length/1MB,1)) MB)" }
