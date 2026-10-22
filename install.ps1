
# AI Bridge — installer for Windows (PowerShell)
# Usage: irm https://raw.githubusercontent.com/arpanvgm/ai-bridge/main/install.ps1 | iex

$ErrorActionPreference = 'Stop'

$Repo      = "arpanvgm/ai-bridge"
$BinName   = "ai-bridge.exe"
$InstallDir = Join-Path $env:USERPROFILE ".ai-bridge\bin"
$AssetName = "ai-bridge-win-x64.zip"

# ── Fetch latest release version ──────────────────────────────
Write-Host "🔍 Fetching latest AI Bridge release..." -ForegroundColor Cyan
$Release = Invoke-RestMethod "https://api.github.com/repos/$Repo/releases/latest"
$LatestTag = $Release.tag_name

if (-not $LatestTag) {
    Write-Error "❌ Could not determine latest release. Check your internet connection."
    exit 1
}

Write-Host "📦 Latest version: $LatestTag" -ForegroundColor Cyan

# ── Download ───────────────────────────────────────────────────
$DownloadUrl = "https://github.com/$Repo/releases/download/$LatestTag/$AssetName"
$TmpDir  = Join-Path $env:TEMP "ai-bridge-install"
$TmpZip  = Join-Path $TmpDir $AssetName

New-Item -ItemType Directory -Force -Path $TmpDir | Out-Null
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null

Write-Host "⬇️  Downloading $AssetName..." -ForegroundColor Cyan
Invoke-WebRequest -Uri $DownloadUrl -OutFile $TmpZip -UseBasicParsing

# ── Extract ────────────────────────────────────────────────────
Write-Host "📂 Extracting..." -ForegroundColor Cyan
Expand-Archive -Path $TmpZip -DestinationPath $TmpDir -Force
Copy-Item (Join-Path $TmpDir $BinName) (Join-Path $InstallDir $BinName) -Force
Remove-Item $TmpDir -Recurse -Force

# ── Add to user PATH ───────────────────────────────────────────
$CurrentPath = [Environment]::GetEnvironmentVariable("PATH", "User")
if ($CurrentPath -notlike "*\.ai-bridge\bin*") {
    [Environment]::SetEnvironmentVariable(
        "PATH",
        "$InstallDir;$CurrentPath",
        "User"
    )
    Write-Host "✅ Added AI Bridge to your user PATH." -ForegroundColor Green
} else {
    Write-Host "ℹ️  AI Bridge bin directory already in PATH." -ForegroundColor Yellow
}

# ── Done ───────────────────────────────────────────────────────
Write-Host ""
Write-Host "✅ AI Bridge $LatestTag installed to $InstallDir\$BinName" -ForegroundColor Green
Write-Host ""
Write-Host "👉 Restart your terminal, then verify with:  ai-bridge --version"
