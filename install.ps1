# AI Bridge — installer for Windows (PowerShell)
# Usage: irm https://raw.githubusercontent.com/arpanvgm/ai-bridge/main/install.ps1 | iex

#Requires -Version 5.0
$ErrorActionPreference = 'Stop'

$Repo       = "arpanvgm/ai-bridge"
$BinName    = "ai-bridge.exe"
$InstallDir = Join-Path $env:USERPROFILE ".ai-bridge\bin"
$TmpDir     = Join-Path $env:TEMP "ai-bridge-install-$(New-Guid)"

# ── Architecture check ────────────────────────────────────────
$Arch = $env:PROCESSOR_ARCHITECTURE
if ($Arch -eq 'ARM64') {
    Write-Error "❌ Windows ARM64 is not yet supported. Please check https://github.com/$Repo/releases for updates."
    exit 1
}
# All other Windows builds (AMD64, x86 on 64-bit OS) use the x64 binary.
$AssetName = "ai-bridge-win-x64.zip"

try {
    # ── Fetch latest release version ──────────────────────────
    Write-Host "🔍 Fetching latest AI Bridge release..." -ForegroundColor Cyan
    $Release   = Invoke-RestMethod "https://api.github.com/repos/$Repo/releases/latest"
    $LatestTag = $Release.tag_name

    if (-not $LatestTag) {
        Write-Error "❌ Could not determine latest release. Check your internet connection."
        exit 1
    }

    Write-Host "📦 Latest version: $LatestTag" -ForegroundColor Cyan

    # ── Download ───────────────────────────────────────────────
    $DownloadUrl = "https://github.com/$Repo/releases/download/$LatestTag/$AssetName"
    $TmpZip      = Join-Path $TmpDir $AssetName

    New-Item -ItemType Directory -Force -Path $TmpDir    | Out-Null
    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null

    Write-Host "⬇️  Downloading $AssetName..." -ForegroundColor Cyan
    Invoke-WebRequest -Uri $DownloadUrl -OutFile $TmpZip -UseBasicParsing

    # ── Extract ────────────────────────────────────────────────
    Write-Host "📂 Extracting..." -ForegroundColor Cyan
    Expand-Archive -Path $TmpZip -DestinationPath $TmpDir -Force

    Copy-Item (Join-Path $TmpDir $BinName) (Join-Path $InstallDir $BinName) -Force

    # ── Add to PATH ────────────────────────────────────────────
    $UserPath = [Environment]::GetEnvironmentVariable("PATH", "User")
    if ($UserPath -notlike "*\.ai-bridge\bin*") {
        [Environment]::SetEnvironmentVariable(
            "PATH",
            "$InstallDir;$UserPath",
            "User"
        )
        Write-Host "✅ Added AI Bridge to your user PATH." -ForegroundColor Green
    } else {
        Write-Host "ℹ️  AI Bridge bin directory already in PATH." -ForegroundColor Yellow
    }

    # Update PATH for the current session so ai-bridge works immediately
    if ($env:PATH -notlike "*\.ai-bridge\bin*") {
        $env:PATH = "$InstallDir;$env:PATH"
    }

    # ── Done ───────────────────────────────────────────────────
    Write-Host ""
    Write-Host "✅ AI Bridge $LatestTag installed to $InstallDir\$BinName" -ForegroundColor Green
    Write-Host ""
    Write-Host "👉 Restart your terminal to make the PATH change permanent."
    Write-Host "   For this session, ai-bridge is already available."
    Write-Host ""
    Write-Host "Verify with:  ai-bridge --version"

} finally {
    # Always clean up the temp directory
    if (Test-Path $TmpDir) {
        Remove-Item $TmpDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
