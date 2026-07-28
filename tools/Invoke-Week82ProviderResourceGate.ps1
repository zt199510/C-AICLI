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

$candidate = '348dd4f30064a70751ae2a55ec5e37a95c49ec87'
$desktopSha = '160668DED8D58C80F6215BF5B899CD1C568439F5E4D8B8880D0CEBB068F43383'
$desktopBytes = 222753280L
$treeSha = '7D8C690876B53E12AC976C7B12C1291456C1DB3EB13F6E98A08D6A305BC6F7A2'
$treeBytes = 464708884L
$appHostSha = 'DC46DBFAD098D7E2F464F05F2C8383568DF733F619B3E45B9D70BAD4F9C13DFA'
$appHostBytes = 79941168L
$packageRoot = Join-Path $desktopRoot 'out\C-AICLI Desktop-win32-x64'
$desktopPath = Join-Path $packageRoot 'caicli-desktop.exe'
$appHostPath = Join-Path $packageRoot 'resources\apphost\CSharpAiCli.AppHost.exe'

if ((Get-FileHash -LiteralPath $desktopPath -Algorithm SHA256).Hash -ne $desktopSha -or
    (Get-Item -LiteralPath $desktopPath).Length -ne $desktopBytes -or
    (Get-FileHash -LiteralPath $appHostPath -Algorithm SHA256).Hash -ne $appHostSha -or
    (Get-Item -LiteralPath $appHostPath).Length -ne $appHostBytes) {
    throw 'Week83 provider resource Gate package identity mismatch.'
}
& git -C $repositoryRoot cat-file -e "$candidate^{commit}"
if ($LASTEXITCODE -ne 0) { throw 'Week83 exact candidate revision is unavailable.' }

$profilePaths = 1..5 | ForEach-Object {
    Join-Path $resolvedEvidenceRoot "provider-resource-profile-$_.json"
}
foreach ($path in $profilePaths) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'Missing resource profile envelope.' }
    $existing = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($existing.status -ne 'NotRun') {
        throw 'Provider resource Gate refuses to overwrite an existing profile attempt. Use a separate diagnostic evidence root.'
    }
}

$started = [DateTimeOffset]::UtcNow
$results = [Collections.Generic.List[object]]::new()
Push-Location $desktopRoot
try {
    for ($index = 1; $index -le 5; $index++) {
        $env:CAICLI_WEEK83_PROVIDER_PROFILE = "R$index"
        $env:CAICLI_WEEK83_EVIDENCE_DIR = $resolvedEvidenceRoot
        & npx playwright test --project=week82-provider-resource --workers=1 --retries=0
        $exitCode = $LASTEXITCODE
        $profile = Get-Content -LiteralPath $profilePaths[$index - 1] -Raw -Encoding UTF8 | ConvertFrom-Json
        $results.Add([ordered]@{
            profile = $index
            exitCode = $exitCode
            status = [string]$profile.status
            workingSetPercent = $profile.retention.workingSetPercent
            privateBytesPercent = $profile.retention.privateBytesPercent
            jsHeapUsedPercent = $profile.retention.jsHeapUsedPercent
            sustainedMonotonicGrowth = [bool]$profile.sustainedMonotonicGrowth
            providerTurns = $profile.counts.providerTurns
            readCalls = $profile.counts.readCalls
            processDelta = $profile.cleanupDelta.process
            temporaryDelta = $profile.cleanupDelta.temporary
            configurationDelta = $profile.cleanupDelta.configuration
        })
    }
}
finally {
    Pop-Location
    Remove-Item Env:CAICLI_WEEK83_PROVIDER_PROFILE -ErrorAction SilentlyContinue
    Remove-Item Env:CAICLI_WEEK83_EVIDENCE_DIR -ErrorAction SilentlyContinue
}

$passed = @($results | Where-Object {
    $_.exitCode -eq 0 -and $_.status -eq 'Passed' -and
    [double]$_.workingSetPercent -le 15 -and [double]$_.privateBytesPercent -le 15 -and
    [double]$_.jsHeapUsedPercent -le 15 -and -not $_.sustainedMonotonicGrowth -and
    [int]$_.processDelta -eq 0 -and [int]$_.temporaryDelta -eq 0 -and [int]$_.configurationDelta -eq 0
}).Count -eq 5
$completed = [DateTimeOffset]::UtcNow
$summary = [ordered]@{
    schemaVersion = 'week83-approval-projection-remediation/v1'
    evidenceKind = 'resource-summary'
    status = $(if ($passed) { 'Passed' } else { 'Failed' })
    exactCandidateRevision = $candidate
    packageIdentity = [ordered]@{
        sha256 = $desktopSha
        bytes = $desktopBytes
        treeSha256 = $treeSha
        treeBytes = $treeBytes
    }
    appHostIdentity = [ordered]@{
        sha256 = $appHostSha
        bytes = $appHostBytes
    }
    settings = [ordered]@{
        consecutiveIndependentProfiles = 5
        warmupTurns = 1
        measuredTurns = 5
        warmWindowSeconds = 30
        postWindowSeconds = 30
        sampleIntervalSeconds = 5
        settleSeconds = 20
        settledSampleCount = 3
        workers = 1
        retries = 0
        forcedGc = $false
        rendererReloadUsedForGate = $false
    }
    profiles = @($results)
    counts = [ordered]@{
        expectedProfiles = 5
        passedProfiles = @($results | Where-Object status -eq 'Passed').Count
        providerTurns = [int](@($results | Measure-Object -Property providerTurns -Sum).Sum)
        readCalls = [int](@($results | Measure-Object -Property readCalls -Sum).Sum)
    }
    durationMilliseconds = [long]($completed - $started).TotalMilliseconds
    cleanupDelta = [ordered]@{
        process = [int](@($results | Measure-Object -Property processDelta -Maximum).Maximum)
        temporary = [int](@($results | Measure-Object -Property temporaryDelta -Maximum).Maximum)
        configuration = [int](@($results | Measure-Object -Property configurationDelta -Maximum).Maximum)
    }
    summary = $(if ($passed) {
        'Five consecutive independent Week83 provider resource profiles passed all retention, Renderer-boundary, authorization, and cleanup Gates.'
    } else {
        'The single five-profile Week83 provider resource Gate failed; no individual profile may be rerun into this evidence root.'
    })
}
$summary | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $resolvedEvidenceRoot 'resource-summary.json') -Encoding UTF8
if (-not $passed) { throw 'Week83 provider resource five-profile Gate failed.' }
Write-Output 'Week83 provider resource five-profile Gate passed.'
