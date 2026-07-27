[CmdletBinding()]
param(
    [string]$EvidenceRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $EvidenceRoot = Join-Path $repositoryRoot 'artifacts\week81-renderer-memory-remediation'
}
$resolvedEvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
if (-not $resolvedEvidenceRoot.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'EvidenceRoot must be below the repository artifacts directory.'
}

$candidate = 'e9e062d985545377aa373767f563de6a2bb30a64'
$desktopSha = 'C736C48B23B8971ED5DAD7F53EBF7BE6CE5CDC2BA6B24CC2CCAFE3DD9064CBB0'
$desktopBytes = 222753280
$appHostSha = 'DC46DBFAD098D7E2F464F05F2C8383568DF733F619B3E45B9D70BAD4F9C13DFA'
$appHostBytes = 79941168

function Read-Json([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Missing evidence: $Path" }
    return Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
}

$summary = Read-Json (Join-Path $resolvedEvidenceRoot 'final-summary.json')
$handoff = Read-Json (Join-Path $resolvedEvidenceRoot 'week82-handoff.json')
$performance = Read-Json (Join-Path $resolvedEvidenceRoot 'week77-performance-candidate.json')

if ($summary.status -ne 'Candidate Ready for Requalification' -or $summary.sourceRevision -ne $candidate -or $summary.sourceDirty) {
    throw 'Week81 final summary is not bound to the clean candidate.'
}
if ($handoff.status -ne 'Candidate Ready for Requalification' -or -not $handoff.candidateReady -or $handoff.exactCandidateRevision -ne $candidate) {
    throw 'Week82 handoff is not candidate-ready or has the wrong revision.'
}
foreach ($document in @($summary, $handoff)) {
    if ($document.packageIdentity.sha256 -ne $desktopSha -or [long]$document.packageIdentity.bytes -ne $desktopBytes) {
        throw 'Evidence Desktop identity mismatch.'
    }
    if ($document.appHostIdentity.sha256 -ne $appHostSha -or [long]$document.appHostIdentity.bytes -ne $appHostBytes) {
        throw 'Evidence AppHost identity mismatch.'
    }
}

if ($performance.status -ne 'Passed' -or $performance.sourceRevision -ne $candidate -or $performance.source.dirty -or
    -not $performance.gates.packageGatePassed -or -not $performance.gates.profileGatePassed) {
    throw 'Week77 performance evidence did not pass on the clean candidate.'
}
$profiles = @($performance.profiles)
if ($profiles.Count -ne 5) { throw 'Week77 performance evidence must contain five profiles.' }
foreach ($profile in $profiles) {
    if ($profile.status -ne 'Passed' -or $profile.retention.idleWorkingSetPercent -gt 15 -or
        $profile.retention.idlePrivateBytesPercent -gt 15 -or $profile.cleanup.processDelta -ne 0 -or
        $profile.cleanup.tempDelta -ne 0) {
        throw "Week77 profile $($profile.profile) failed a retention or cleanup Gate."
    }
}

$controlRoot = Join-Path $resolvedEvidenceRoot 'week80-controls'
foreach ($profileName in @('c0','c1','c2','c3','c4','c5','c6','c7')) {
    $control = Read-Json (Join-Path $controlRoot "profile-$profileName.json")
    if ($control.status -ne 'Passed' -or $control.productRevision -ne $candidate -or
        $control.packageIdentity.sha256 -ne $desktopSha -or $control.cleanupDelta.process -ne 0 -or
        $control.cleanupDelta.temporary -ne 0 -or $control.cleanupDelta.configuration -ne 0) {
        throw "Week80 control $profileName failed status, identity, or cleanup validation."
    }
}

$packageRoot = Join-Path $repositoryRoot 'apps\desktop\out\C-AICLI Desktop-win32-x64'
$desktopPath = Join-Path $packageRoot 'caicli-desktop.exe'
$appHostPath = Join-Path $packageRoot 'resources\apphost\CSharpAiCli.AppHost.exe'
if ((Get-FileHash -LiteralPath $desktopPath -Algorithm SHA256).Hash -ne $desktopSha -or
    (Get-Item -LiteralPath $desktopPath).Length -ne $desktopBytes) {
    throw 'Rebuilt Desktop executable identity mismatch.'
}
if ((Get-FileHash -LiteralPath $appHostPath -Algorithm SHA256).Hash -ne $appHostSha -or
    (Get-Item -LiteralPath $appHostPath).Length -ne $appHostBytes) {
    throw 'Rebuilt AppHost identity mismatch.'
}

$review = Get-Content -LiteralPath (Join-Path $repositoryRoot 'docs_md\weekly\81_week_review.md') -Raw -Encoding UTF8
if (-not $review.Contains('Candidate Ready for Requalification') -or -not $review.Contains($candidate)) {
    throw 'Week81 review is not bound to the candidate-ready revision.'
}

Write-Output 'Week81 evidence validation passed: candidate identity, five Week77 profiles, C0-C7 controls, cleanup, review, and Week82 handoff are consistent.'
