# clone-repo.ps1 — Syncro safe git clone with progress reporting via Named Pipe
# Usage: clone-repo.ps1 -Url <repo-url> -Dest <destination-path> [-PipeName <pipe>]

param(
    [Parameter(Mandatory=$true)]  [string]$Url,
    [Parameter(Mandatory=$false)] [string]$Dest   = (Split-Path $Url -LeafBase),
    [Parameter(Mandatory=$false)] [string]$PipeName = "syncro-agent-bridge"
)

$ErrorActionPreference = "Continue"

function Send-PipeMessage($pipe, $msg) {
    try {
        if ($pipe -and $pipe.IsConnected) {
            $bytes = [System.Text.Encoding]::UTF8.GetBytes($msg + "`n")
            $pipe.Write($bytes, 0, $bytes.Length)
            $pipe.Flush()
        }
    } catch {}
}

# Try to connect to Named Pipe (optional — CLI continues if Desktop not running)
$pipe = $null
try {
    $pipe = New-Object System.IO.Pipes.NamedPipeClientStream(".", $PipeName, "Out")
    $pipe.Connect(1000)
    Write-Host "  Connected to Syncro Desktop bridge." -ForegroundColor Cyan
} catch {
    Write-Host "  (Desktop bridge not connected — running standalone)" -ForegroundColor DarkGray
}

Write-Host "Cloning: $Url" -ForegroundColor Cyan
Write-Host "    To:  $Dest"

Send-PipeMessage $pipe (@{type="status";level="info";message="Cloning $Url"} | ConvertTo-Json -Compress)

# Run git clone with progress
$job = Start-Job -ScriptBlock {
    param($u, $d)
    & git clone $u $d --progress 2>&1
} -ArgumentList $Url, $Dest

# Stream job output
while ($job.State -eq "Running") {
    $out = Receive-Job $job
    if ($out) {
        foreach ($line in $out) {
            Write-Host "  $line"
            Send-PipeMessage $pipe (@{type="output";line=$line} | ConvertTo-Json -Compress)
        }
    }
    Start-Sleep -Milliseconds 500
}

$remaining = Receive-Job $job
foreach ($line in $remaining) { Write-Host "  $line" }
Remove-Job $job

if ($LASTEXITCODE -eq 0 -or (Test-Path $Dest)) {
    Write-Host "✅  Clone complete: $Dest" -ForegroundColor Green
    Send-PipeMessage $pipe (@{type="status";level="success";message="Clone complete: $Dest"} | ConvertTo-Json -Compress)
} else {
    Write-Host "❌  Clone failed." -ForegroundColor Red
    Send-PipeMessage $pipe (@{type="error";code="GIT_CLONE_FAILED";message="Clone failed for $Url"} | ConvertTo-Json -Compress)
}

if ($pipe) { try { $pipe.Close() } catch {} }
