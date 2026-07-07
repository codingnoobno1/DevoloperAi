# install-node.ps1 — Syncro Node.js installer for Windows
# Checks for Node.js, installs via winget or nvm-windows if missing.

$ErrorActionPreference = "Stop"

Write-Host "Checking Node.js..." -ForegroundColor Cyan

$nodeVersion = & node --version 2>$null
if ($LASTEXITCODE -eq 0) {
    Write-Host "✅ Node.js already installed: $nodeVersion" -ForegroundColor Green
    $npmVersion = & npm --version 2>$null
    Write-Host "   npm: $npmVersion"
    exit 0
}

Write-Host "Node.js not found. Installing via winget..." -ForegroundColor Yellow

# Try winget
$wingetCheck = Get-Command winget -ErrorAction SilentlyContinue
if ($wingetCheck) {
    winget install -e --id OpenJS.NodeJS --silent --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✅ Node.js installed via winget." -ForegroundColor Green
        Write-Host "   Restart your terminal to use 'node' globally."
        exit 0
    }
}

# Fallback: nvm-windows
Write-Host "Attempting nvm-windows install..." -ForegroundColor Yellow
$nvmUrl = "https://github.com/coreybutler/nvm-windows/releases/latest/download/nvm-setup.exe"
$nvmInstaller = "$env:TEMP\nvm-setup.exe"
Invoke-WebRequest -Uri $nvmUrl -OutFile $nvmInstaller
Start-Process -FilePath $nvmInstaller -Wait
Write-Host "nvm-windows installed. Run 'nvm install lts' then 'nvm use lts'." -ForegroundColor Cyan
