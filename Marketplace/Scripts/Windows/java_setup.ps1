Write-Host "Syncro: Checking Java JDK installation..." -ForegroundColor Cyan

if (Get-Command java -ErrorAction SilentlyContinue) {
    $version = java -version 2>&1 | Select-Object -First 1
    Write-Host "✅ Java found: $version" -ForegroundColor Green
} else {
    Write-Host "❌ JDK not found. Installing OpenJDK 21 via Winget..." -ForegroundColor Yellow
    winget install -e --id Microsoft.OpenJDK.21 --silent --accept-package-agreements
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✅ JDK 21 installed. Setting up JAVA_HOME..." -ForegroundColor Green
    } else {
        Write-Host "❌ Failed to install JDK." -ForegroundColor Red
    }
}
