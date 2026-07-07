# add-to-path.ps1 — Syncro elevated PATH registration
# Must be run as Administrator. Called automatically by syncro install.

#Requires -RunAsAdministrator

param(
    [Parameter(Mandatory=$true)] [string]$DirectoryToAdd,
    [switch]$Remove
)

$varName = "PATH"
$scope   = [System.EnvironmentVariableTarget]::Machine
$current = [System.Environment]::GetEnvironmentVariable($varName, $scope)
$parts   = $current -split ';' | Where-Object { $_.Trim() -ne '' }

if ($Remove) {
    $newParts = $parts | Where-Object { $_ -ne $DirectoryToAdd }
    $newPath  = $newParts -join ';'
    [System.Environment]::SetEnvironmentVariable($varName, $newPath, $scope)
    Write-Host "✅  Removed from PATH: $DirectoryToAdd" -ForegroundColor Green
} else {
    if ($parts -contains $DirectoryToAdd) {
        Write-Host "✅  Already in PATH: $DirectoryToAdd" -ForegroundColor Green
        exit 0
    }
    $newPath = ($parts + $DirectoryToAdd) -join ';'
    [System.Environment]::SetEnvironmentVariable($varName, $newPath, $scope)
    Write-Host "✅  Added to PATH: $DirectoryToAdd" -ForegroundColor Green
}

# Broadcast WM_SETTINGCHANGE so open terminals pick up the change
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class EnvBroadcast {
    [DllImport("user32.dll", SetLastError=true, CharSet=CharSet.Auto)]
    public static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint Msg, IntPtr wParam, string lParam,
        uint fuFlags, uint uTimeout, out IntPtr result);
}
"@
$res = [IntPtr]::Zero
[EnvBroadcast]::SendMessageTimeout([IntPtr]0xFFFF, 0x001A, [IntPtr]::Zero, "Environment", 2, 5000, [ref]$res) | Out-Null
Write-Host "  PATH change broadcast to open terminals." -ForegroundColor Cyan
