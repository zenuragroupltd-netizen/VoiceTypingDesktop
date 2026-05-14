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

# --------------------------------------------------------------
# Safety net: scan the publish folder for anything that looks like
# a leaked secret (OpenAI keys, AWS keys, private keys, etc.) and
# ABORT the build if found. The Supabase anon key is designed to be
# public so we explicitly allow it.
# --------------------------------------------------------------
Write-Host "`n[Safety] Scanning publish output for leaked secrets..."

# Patterns intentionally do not include the Supabase ANON key prefix
# (`eyJ...` is a JWT, normal for the anon key in any client app).
$secretPatterns = @(
    @{ Name = "OpenAI key (sk-proj-)";  Re = 'sk-proj-[A-Za-z0-9_\-]{20,}' }
    @{ Name = "OpenAI key (sk-)";       Re = 'sk-[A-Za-z0-9]{20,}' }
    @{ Name = "Anthropic key";          Re = 'sk-ant-[A-Za-z0-9_\-]{20,}' }
    @{ Name = "AWS access key";         Re = 'AKIA[0-9A-Z]{16}' }
    @{ Name = "Google API key";         Re = 'AIza[0-9A-Za-z_\-]{35}' }
    @{ Name = "GitHub PAT";             Re = 'ghp_[A-Za-z0-9]{36,}' }
    @{ Name = "Private key block";      Re = '-----BEGIN [A-Z ]*PRIVATE KEY-----' }
)

# Only inspect text-shaped files. We deliberately skip .dll/.exe/.pdb to
# avoid noise from random byte sequences inside binaries.
$textFiles = Get-ChildItem $publishDir -Recurse -File `
             -Include *.json,*.xml,*.config,*.txt,*.md,*.ini,*.yml,*.yaml,*.runtimeconfig.json,*.deps.json `
             -ErrorAction SilentlyContinue

$leaks = New-Object System.Collections.Generic.List[object]
foreach ($file in $textFiles) {
    $content = Get-Content -LiteralPath $file.FullName -Raw -ErrorAction SilentlyContinue
    if (-not $content) { continue }
    foreach ($p in $secretPatterns) {
        if ([regex]::IsMatch($content, $p.Re)) {
            $leaks.Add([pscustomobject]@{ File = $file.FullName; Kind = $p.Name })
        }
    }
}

if ($leaks.Count -gt 0) {
    Write-Host ""
    Write-Host "BUILD ABORTED: suspected secret(s) found in publish output:" -ForegroundColor Red
    $leaks | ForEach-Object { Write-Host "  - $($_.Kind) in $($_.File)" -ForegroundColor Red }
    Write-Host ""
    Write-Host "Remove the secret(s) and re-run. The installer was NOT built." -ForegroundColor Red
    throw "Secret scan failed. See list above."
}
Write-Host "OK - no obvious secrets detected."

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
