# ============================================================================
#  _common.ps1  -  Shared helpers for Syncro git workflow scripts
# ----------------------------------------------------------------------------
#  Dot-source this at the top of every git-*.ps1 / enable-groq.ps1 script:
#       . "$PSScriptRoot\_common.ps1"
#
#  Design rules (match Services/Git/GitEngine.cs):
#    * Never hang on a credential prompt   -> GIT_TERMINAL_PROMPT=0
#    * Never bake secrets into a script    -> read from env / SecureString
#    * Non-throwing git wrapper            -> callers inspect $LASTEXITCODE
#    * Optional progress bridge to the app -> named pipe 'syncro-agent-bridge'
#      (same pipe Syncro.CLI/Scripts/clone-repo.ps1 uses)
# ============================================================================

$ErrorActionPreference = 'Stop'
$env:GIT_TERMINAL_PROMPT = '0'   # fail fast instead of blocking on auth prompts
$env:GCM_INTERACTIVE     = 'Never'

# ---- pretty logging --------------------------------------------------------
function Write-Step($m) { Write-Host "`n== $m" -ForegroundColor Cyan;  Send-Bridge "step:$m" }
function Write-Ok  ($m) { Write-Host "OK   $m"  -ForegroundColor Green; Send-Bridge "ok:$m" }
function Write-Warn($m) { Write-Host "WARN $m"  -ForegroundColor Yellow;Send-Bridge "warn:$m" }
function Write-Err ($m) { Write-Host "ERR  $m"  -ForegroundColor Red;   Send-Bridge "err:$m" }
function Write-Info($m) { Write-Host "     $m"   -ForegroundColor Gray }

# ---- optional progress bridge to the desktop app ---------------------------
# Silently no-ops when the app is not listening. Best-effort only.
function Send-Bridge([string]$message) {
    try {
        $pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', 'syncro-agent-bridge', [System.IO.Pipes.PipeDirection]::Out)
        $pipe.Connect(150)  # 150ms; give up quietly if app isn't up
        $sw = New-Object System.IO.StreamWriter($pipe)
        $sw.AutoFlush = $true
        $sw.WriteLine($message)
        $sw.Dispose(); $pipe.Dispose()
    } catch { }  # bridge is optional; never fail a git op because the app is closed
}

# ---- non-throwing git wrapper ---------------------------------------------
# Usage:  $r = Invoke-Git status --porcelain=v2 ; if ($r.Code -ne 0) { ... }
#         $r.Out is stdout+stderr joined; $r.Code is exit code.
# Resolve the real git executable ONCE. Calling 'git' unqualified would re-enter
# this function (PowerShell command lookup is case-insensitive) -> recursion.
$script:GitExe = (Get-Command git.exe -CommandType Application -ErrorAction SilentlyContinue |
                  Select-Object -First 1).Source
if (-not $script:GitExe) {
    $script:GitExe = (Get-Command git -CommandType Application -ErrorAction SilentlyContinue |
                      Select-Object -First 1).Source
}
if (-not $script:GitExe) { throw 'git executable not found on PATH.' }
function Invoke-Git {
    $out = & $script:GitExe @args 2>&1
    [pscustomobject]@{ Code = $LASTEXITCODE; Out = ($out -join "`n").Trim(); Args = ($args -join ' ') }
}

# ---- repo / state helpers --------------------------------------------------
function Assert-Repo {
    $r = Invoke-Git rev-parse --is-inside-work-tree
    if ($r.Code -ne 0 -or $r.Out -ne 'true') {
        Write-Err "Not a git repository: $(Get-Location)"
        exit 1
    }
}
function Get-Branch      { (Invoke-Git rev-parse --abbrev-ref HEAD).Out }
function Get-Upstream    { $r = Invoke-Git rev-parse --abbrev-ref '--symbolic-full-name' '@{u}'; if ($r.Code -eq 0) { $r.Out } else { $null } }
function Test-Detached   { (Get-Branch) -eq 'HEAD' }
function Test-Dirty      { -not [string]::IsNullOrWhiteSpace((Invoke-Git status --porcelain).Out) }
function Get-AheadBehind {
    $u = Get-Upstream
    if (-not $u) { return $null }
    $r = Invoke-Git rev-list --left-right --count "$u...HEAD"
    if ($r.Code -ne 0) { return $null }
    $p = $r.Out -split '\s+'
    [pscustomobject]@{ Behind = [int]$p[0]; Ahead = [int]$p[1] }
}
function Get-RepoRoot    { (Invoke-Git rev-parse --show-toplevel).Out }
function Test-InProgress {
    $g = Get-RepoRoot
    [pscustomobject]@{
        Merge      = Test-Path "$g/.git/MERGE_HEAD"
        Rebase     = (Test-Path "$g/.git/rebase-merge") -or (Test-Path "$g/.git/rebase-apply")
        CherryPick = Test-Path "$g/.git/CHERRY_PICK_HEAD"
        Revert     = Test-Path "$g/.git/REVERT_HEAD"
        IndexLock  = Test-Path "$g/.git/index.lock"
    }
}

# ---- confirmation gate (destructive ops) -----------------------------------
# Honours a -Yes switch passed by the caller so scripts stay non-interactive
# when run head-less through ShellMcp / GitWorkflowRunner.
function Confirm-Destructive([string]$what, [bool]$auto) {
    if ($auto) { Write-Warn "auto-approved: $what"; return $true }
    $ans = Read-Host "About to $what. Type 'yes' to continue"
    return ($ans -eq 'yes')
}

# ---- secret resolution (never hardcode) ------------------------------------
# Resolve a token from (1) explicit param, (2) env var, in that order.
function Resolve-Secret([string]$explicit, [string]$envName) {
    if ($explicit) { return $explicit }
    $v = [Environment]::GetEnvironmentVariable($envName)
    if ($v) { return $v }
    return $null
}

# ---- Groq chat completion (matches Services/Engine/LLM/GroqProvider.cs) -----
$script:GroqUrl   = 'https://api.groq.com/openai/v1/chat/completions'
$script:GroqModel = 'llama-3.1-8b-instant'   # same default the app hardcodes
function Invoke-Groq {
    param(
        [Parameter(Mandatory)][string]$System,
        [Parameter(Mandatory)][string]$User,
        [string]$Model = $script:GroqModel,
        [double]$Temperature = 0.2
    )
    $key = Resolve-Secret $null 'GROQ_API_KEY'
    if (-not $key) { throw 'GROQ_API_KEY is not set. Run ./enable-groq.ps1 first.' }
    $body = @{
        model    = $Model
        messages = @(
            @{ role = 'system'; content = $System },
            @{ role = 'user';   content = $User }
        )
        temperature = $Temperature
    } | ConvertTo-Json -Depth 6
    $resp = Invoke-RestMethod -Uri $script:GroqUrl -Method Post -Headers @{ Authorization = "Bearer $key" } `
                              -ContentType 'application/json' -Body $body
    return $resp.choices[0].message.content
}
