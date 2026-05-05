# Verifies that no main-repo files have been modified by PinnedSplits work.
# Exit 0 = pass; 1 = violation found.
$ErrorActionPreference = 'Stop'

Push-Location (Join-Path $PSScriptRoot "..\..\..")
try {
    # Anything outside components/LiveSplit.PinnedSplits/ is forbidden
    $forbiddenPaths = @(
        'src/',
        'components/LiveSplit.Splits/',
        'components/LiveSplit.Subsplits/',
        'components/LiveSplit.Counter/',
        'components/LiveSplit.Title/',
        'components/LiveSplit.Timer/',
        'components/LiveSplit.PreviousSegment/',
        'components/LiveSplit.SumOfBest/',
        'components/LiveSplit.Delta/',
        'components/LiveSplit.Graph/',
        'components/LiveSplit.DetailedTimer/',
        'lib/',
        'props/',
        'test/'
        # Note: components/Directory.Build.props is shared but should NOT be modified
    )
    $forbiddenFiles = @(
        'LiveSplit.sln',
        '.gitmodules',
        'components/Directory.Build.props',
        'AGENTS.md'
    )

    # Suppress git stderr (LF/CRLF warnings, etc.) so PowerShell does not wrap them as
    # ErrorRecord objects, which would then fail the `.Trim()` call below. We only care
    # about the file-name listings on stdout.
    $changes = git diff --name-only HEAD 2>$null
    $untracked = git ls-files --others --exclude-standard 2>$null
    $allChanged = @($changes) + @($untracked) | ForEach-Object { "$_" } | Where-Object { $_ -and $_.Trim() }

    $violations = @()
    foreach ($file in $allChanged) {
        $isForbidden = $false
        foreach ($p in $forbiddenPaths) { if ($file -like "$p*") { $isForbidden = $true; break } }
        foreach ($f in $forbiddenFiles) { if ($file -eq $f) { $isForbidden = $true; break } }
        if ($isForbidden) { $violations += $file }
    }

    if ($violations.Count -gt 0) {
        Write-Error "Forbidden files modified or added:`n$($violations -join "`n")"
        exit 1
    }
    Write-Host "No main-repo modifications detected. PASS."
    exit 0
} finally {
    Pop-Location
}
