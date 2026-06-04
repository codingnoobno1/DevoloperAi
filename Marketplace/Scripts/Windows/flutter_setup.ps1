Write-Host "Syncro: Checking Flutter SDK..." -ForegroundColor Cyan

if (Get-Command flutter -ErrorAction SilentlyContinue) {
    Write-Host "✅ Flutter is already in PATH." -ForegroundColor Green
    flutter --version
} else {
    Write-Host "📥 Flutter not found. Use the Syncro Dashboard to automate Flutter provisioning," -ForegroundColor Yellow
    Write-Host "or install manually from https://docs.flutter.dev/get-started/install/windows" -ForegroundColor White
}
