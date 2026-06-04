Write-Host "Syncro: Checking Rust installation..." -ForegroundColor Cyan

if (Get-Command rustc -ErrorAction SilentlyContinue) {
    $version = rustc --version
    Write-Host "✅ Rust found: $version" -ForegroundColor Green
} else {
    Write-Host "🚀 Installing Rust via rustup..." -ForegroundColor Yellow
    Invoke-WebRequest -Uri "https://static.rust-lang.org/rustup/dist/x86_64-pc-windows-msvc/rustup-init.exe" -OutFile "$env:TEMP\rustup-init.exe"
    Start-Process -FilePath "$env:TEMP\rustup-init.exe" -ArgumentList "-y", "--default-toolchain", "stable" -Wait
    Write-Host "✅ Rust installation initiated. Please restart your shell." -ForegroundColor Green
}
