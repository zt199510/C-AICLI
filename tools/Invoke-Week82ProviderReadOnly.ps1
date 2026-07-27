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
    $EvidenceRoot = Join-Path $artifactsRoot 'week82-desktop-preview-requalification'
}
$resolvedEvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)
if (-not $resolvedEvidenceRoot.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'EvidenceRoot must be below the repository artifacts directory.'
}
if ($env:CAICLI_WEEK82_PROVIDER_AUTHORIZED -ne 'read-only-recovery-resource') {
    throw 'Week82 provider read-only/recovery/resource authorization is required.'
}
$desktopPath = Join-Path $desktopRoot 'out\C-AICLI Desktop-win32-x64\caicli-desktop.exe'
$appHostPath = Join-Path $desktopRoot 'out\C-AICLI Desktop-win32-x64\resources\apphost\CSharpAiCli.AppHost.exe'
if ((Get-FileHash -LiteralPath $desktopPath -Algorithm SHA256).Hash -ne 'C736C48B23B8971ED5DAD7F53EBF7BE6CE5CDC2BA6B24CC2CCAFE3DD9064CBB0' -or
    (Get-FileHash -LiteralPath $appHostPath -Algorithm SHA256).Hash -ne 'DC46DBFAD098D7E2F464F05F2C8383568DF733F619B3E45B9D70BAD4F9C13DFA') {
    throw 'Week82 provider read-only package identity mismatch.'
}

$evidencePath = Join-Path $resolvedEvidenceRoot 'provider-readonly.json'
$existing = Get-Content -LiteralPath $evidencePath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($existing.status -ne 'NotRun') {
    throw 'Provider read-only refuses to overwrite an existing attempt.'
}

Push-Location $desktopRoot
try {
    $env:CAICLI_WEEK82_PROVIDER_PROFILE = 'RO'
    $env:CAICLI_WEEK82_EVIDENCE_DIR = $resolvedEvidenceRoot
    & npx playwright test --project=week82-provider-resource --workers=1 --retries=0
    $exitCode = $LASTEXITCODE
}
finally {
    Pop-Location
    Remove-Item Env:CAICLI_WEEK82_PROVIDER_PROFILE -ErrorAction SilentlyContinue
    Remove-Item Env:CAICLI_WEEK82_EVIDENCE_DIR -ErrorAction SilentlyContinue
}

$evidence = Get-Content -LiteralPath $evidencePath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($exitCode -ne 0 -or $evidence.status -ne 'Passed' -or
    [int]$evidence.counts.providerTurns -ne 1 -or [int]$evidence.counts.readCalls -ne 1 -or
    [int]$evidence.counts.approvalRequests -ne 0 -or [int]$evidence.counts.writeCalls -ne 0 -or
    [int]$evidence.counts.shellCalls -ne 0 -or [int]$evidence.counts.changedFiles -ne 0 -or
    [int]$evidence.cleanupDelta.process -ne 0 -or [int]$evidence.cleanupDelta.temporary -ne 0 -or
    [int]$evidence.cleanupDelta.configuration -ne 0) {
    throw 'Week82 packaged provider read-only Gate failed.'
}
Write-Output 'Week82 packaged provider read-only Gate passed.'
