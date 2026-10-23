<#
.SYNOPSIS
    Safe undo helpers - the operations GitEngine deliberately omits, gated.
.DESCRIPTION
    Provides the common "oops" recoveries without exposing a foot-gun:
      -Mode last     : undo the last commit but KEEP the changes (soft reset).
      -Mode unstage  : unstage everything (keep working-tree edits).
      -Mode discard  : discard uncommitted changes to tracked files (needs -Yes).
      -Mode hard     : hard reset to a ref (needs -Yes AND -Ref; last resort).
    A safety stash is taken before 'discard'/'hard' so nothing is truly lost.
.PARAMETER Mode  last | unstage | discard | hard
.PARAMETER Ref   Target ref for -Mode hard (e.g. origin/main, HEAD~1).
.PARAMETER Yes   Approve destructive modes (discard/hard).
.EXAMPLE
    ./git-undo.ps1 -Mode last
    ./git-undo.ps1 -Mode discard -Yes
    ./git-undo.ps1 -Mode hard -Ref origin/main -Yes
#>
param(
    [ValidateSet('last','unstage','discard','hard')][string]$Mode = 'last',
    [string]$Ref,
    [switch]$Yes
)
. "$PSScriptRoot\_common.ps1"
Assert-Repo

switch ($Mode) {
    'last' {
        $r = Invoke-Git reset --soft HEAD~1
        if ($r.Code -eq 0) { Write-Ok 'Last commit undone; changes kept & staged. Re-commit when ready.' } else { Write-Err $r.Out }
    }
    'unstage' {
        (Invoke-Git reset) | Out-Null
        Write-Ok 'Everything unstaged; working-tree edits preserved.'
    }
    'discard' {
        if (-not (Test-Dirty)) { Write-Info 'Nothing to discard.'; return }
        if (-not (Confirm-Destructive 'discard ALL uncommitted changes to tracked files' $Yes)) { exit 1 }
        (Invoke-Git stash push -m "git-undo safety $(git rev-parse --short HEAD)") | Out-Null   # recoverable
        Write-Warn 'A safety stash was taken (git stash list) - you can still recover.'
        (Invoke-Git checkout -- .) | Out-Null
        Write-Ok 'Working tree reset to HEAD (tracked files).'
    }
    'hard' {
        if (-not $Ref) { Write-Err '-Mode hard requires -Ref (e.g. origin/main).'; exit 1 }
        if (-not (Confirm-Destructive "HARD reset to '$Ref' (local commits after it will be orphaned)" $Yes)) { exit 1 }
        (Invoke-Git stash push --include-untracked -m "git-undo pre-hard $(git rev-parse --short HEAD)") | Out-Null
        Write-Warn 'A safety stash (incl. untracked) was taken before the hard reset.'
        $r = Invoke-Git reset --hard $Ref
        if ($r.Code -eq 0) { Write-Ok "Hard reset to $Ref. Prior HEAD reachable via 'git reflog'." } else { Write-Err $r.Out }
    }
}
