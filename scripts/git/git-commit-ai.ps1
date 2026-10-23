<#
.SYNOPSIS
    Stage changes and generate a Conventional-Commits message with Groq, then commit.
.DESCRIPTION
    The scripted twin of GitCommitDrafter: diffs the working tree (bounded to
    12k chars like the engine), asks Groq for a conventional-commit message, shows
    it, and commits. -DryRun prints the message without committing.
.PARAMETER Path     Repo dir (default: current).
.PARAMETER All      Stage all changes (git add -A) before diffing. Default: on.
.PARAMETER Sign     GPG-sign the commit (-S).
.PARAMETER DryRun   Generate and print the message only; do not commit.
.PARAMETER Yes      Commit without the confirm prompt.
.EXAMPLE
    ./git-commit-ai.ps1
    ./git-commit-ai.ps1 -DryRun
#>
param(
    [string]$Path = (Get-Location).Path,
    [switch]$Sign,
    [switch]$DryRun,
    [switch]$Yes
)
. "$PSScriptRoot\_common.ps1"
Set-Location $Path
Assert-Repo

Write-Step 'Staging changes'
git add -A
if (-not (Test-Dirty) -and (Invoke-Git diff --cached --quiet).Code -eq 0) { Write-Warn 'Nothing staged to commit.'; exit 0 }

$diff = (git diff --cached) -join "`n"
if ($diff.Length -gt 12000) { $diff = $diff.Substring(0,12000) + "`n...[truncated]" }

Write-Step 'Asking Groq for a commit message'
$sys = 'You are a senior engineer. Write ONE Conventional Commits message (type(scope): subject) for the given staged diff. ' +
       'Imperative mood, <=72 char subject, optional short body. Output ONLY the message, no code fences, no commentary.'
try { $msg = (Invoke-Groq -System $sys -User $diff -Temperature 0.2).Trim() }
catch { Write-Err $_.Exception.Message; exit 1 }
$msg = $msg -replace '^```.*?$','' -replace '```',''   # strip stray fences
$msg = $msg.Trim()

Write-Host "`n--- proposed commit message ---" -ForegroundColor Cyan
Write-Host $msg -ForegroundColor White
Write-Host "-------------------------------`n" -ForegroundColor Cyan

if ($DryRun) { Write-Info 'Dry run - not committing.'; return }
if (-not $Yes -and -not (Confirm-Destructive 'commit with this message' $false)) { Write-Warn 'Aborted.'; exit 1 }

$signArg = if ($Sign) { '-S' } else { $null }
$tmp = New-TemporaryFile; Set-Content -Path $tmp -Value $msg -Encoding UTF8
$r = Invoke-Git commit $signArg -F $tmp
Remove-Item $tmp -Force
if ($r.Code -eq 0) { Write-Ok 'Committed.' } else { Write-Err $r.Out; exit 1 }
