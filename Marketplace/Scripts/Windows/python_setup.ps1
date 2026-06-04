Write-Host "Syncro: Checking Python installation..." -ForegroundColor Cyan

if (Get-Command python -ErrorAction SilentlyContinue) {
    $version = python --version
    Write-Host "✅ Python found: $version" -ForegroundColor Green
} else {
    Write-Host "❌ Python not found. Initiating Winget installation..." -ForegroundColor Yellow
    winget install -e --id Python.Python.3 --silent --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✅ Python installed successfully. Please restart your terminal." -ForegroundColor Green
    } else {
        Write-Host "❌ Failed to install Python via Winget." -ForegroundColor Red
    }
}
