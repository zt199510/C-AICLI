[CmdletBinding()]
param(
    [string]$EvidenceRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$desktopRoot = Join-Path $repositoryRoot 'apps\desktop'
if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $EvidenceRoot = Join-Path $artifactsRoot 'week83-approval-projection-remediation'
}
$resolvedEvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)
if (-not $resolvedEvidenceRoot.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'EvidenceRoot must be below the repository artifacts directory.'
}
if ($env:CAICLI_WEEK83_PROVIDER_AUTHORIZED -ne 'read-only-recovery-resource') {
    throw 'Week83 provider read-only/recovery/resource authorization is required.'
}
$desktopPath = Join-Path $desktopRoot 'out\C-AICLI Desktop-win32-x64\caicli-desktop.exe'
$appHostPath = Join-Path $desktopRoot 'out\C-AICLI Desktop-win32-x64\resources\apphost\CSharpAiCli.AppHost.exe'
if ((Get-FileHash -LiteralPath $desktopPath -Algorithm SHA256).Hash -ne 'AB4B79A97C66041E1A478F217AA99EC75D1D1082FA42500AFDADBC13063B62D9' -or
    (Get-FileHash -LiteralPath $appHostPath -Algorithm SHA256).Hash -ne 'DC46DBFAD098D7E2F464F05F2C8383568DF733F619B3E45B9D70BAD4F9C13DFA') {
    throw 'Week83 provider recovery package identity mismatch.'
}

$evidencePath = Join-Path $resolvedEvidenceRoot 'provider-recovery.json'
$existing = Get-Content -LiteralPath $evidencePath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($existing.status -ne 'NotRun') {
    throw 'Provider recovery refuses to overwrite an existing attempt.'
}

Push-Location $desktopRoot
try {
    $env:CAICLI_WEEK83_EVIDENCE_DIR = $resolvedEvidenceRoot
    & npx playwright test --project=week82-provider-recovery --workers=1 --retries=0
    $exitCode = $LASTEXITCODE
}
finally {
    Pop-Location
    Remove-Item Env:CAICLI_WEEK83_EVIDENCE_DIR -ErrorAction SilentlyContinue
}

$evidence = Get-Content -LiteralPath $evidencePath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($exitCode -ne 0 -or $evidence.status -ne 'Passed' -or
    -not [bool]$evidence.checks.noAutomaticRestartOrReplay -or
    -not [bool]$evidence.checks.oldAttemptInterrupted -or
    -not [bool]$evidence.checks.turnIdentitySeparated -or
    -not [bool]$evidence.checks.modelIdentitySeparated -or
    -not [bool]$evidence.checks.toolIdentitySeparated -or
    -not [bool]$evidence.checks.approvalIdentitySeparated -or
    -not [bool]$evidence.checks.newAttemptCanceledBeforeApproval -or
    -not [bool]$evidence.checks.resyncBounded -or -not [bool]$evidence.checks.domAndListenersBounded -or
    [int]$evidence.counts.changedFiles -ne 0 -or
    [int]$evidence.cleanupDelta.process -ne 0 -or [int]$evidence.cleanupDelta.temporary -ne 0 -or
    [int]$evidence.cleanupDelta.configuration -ne 0) {
    throw 'Week83 packaged provider recovery Gate failed.'
}
Write-Output 'Week83 packaged provider recovery Gate passed.'
