Write-Host "Syncro CLI: Initializing environment check..."
Write-Host "Current Platform: Windows"
Write-Host "Checking for Flutter..."
if (Get-Command flutter -ErrorAction SilentlyContinue) {
    Write-Host "✅ Flutter is installed."
} else {
    Write-Host "❌ Flutter not found in PATH."
}
