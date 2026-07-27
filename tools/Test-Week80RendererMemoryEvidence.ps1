[CmdletBinding()]
param(
    [string]$EvidenceRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $EvidenceRoot = Join-Path $repositoryRoot 'artifacts\week80-renderer-private-bytes'
}
$resolvedEvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)
$expectedParent = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
if (-not $resolvedEvidenceRoot.StartsWith(
    $expectedParent + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw 'EvidenceRoot must be a child of the repository artifacts directory.'
}

$expectedFiles = @(
    'manifest.json',
    'week79-baseline.json',
    'control-idle.json',
    'fixture-control.json',
    'provider-1-turn.json',
    'provider-5-turn.json',
    'provider-10-turn.json',
    'resync-counts.json',
    'heap-summary.json',
    'diagnosis.json',
    'diagnosis-handoff.json'
)
$allowedStatuses = @('NotRun', 'Running', 'Passed', 'Failed', 'Blocked')
$fixedPackageSha256 = 'E2A5BE23B0598ACA5827FC3977C48418298AE888D9293D1D51B937528BBB7A38'
$fixedPackageBytes = 222753280
$appHostSha256 = 'DC46DBFAD098D7E2F464F05F2C8383568DF733F619B3E45B9D70BAD4F9C13DFA'
$appHostBytes = 79941168
$forbiddenPropertyPattern = '(?i)(secret|token|credential$|api.?key|raw.?prompt|raw.?response|endpoint.?query|request.?header|command.?environment|absolute.?path|raw.?diagnostic|raw.?stderr|workspace.?root|provider.?data)'
$forbiddenSensitiveValuePattern = '(?i)(sk-[a-z0-9_-]{12,}|bearer\s+[a-z0-9._-]{8,})'
$absolutePathPattern = '(^|[\s"''])([a-zA-Z]:[\\/]|\\\\[^\\])'

function Test-Node {
    param(
        [Parameter(Mandatory = $true)]
        [AllowNull()]
        [object]$Value,
        [Parameter(Mandatory = $true)]
        [string]$Location
    )
    if ($null -eq $Value) { return }
    if ($Value -is [string]) {
        if ($Value -match $forbiddenSensitiveValuePattern) { throw "Sensitive-looking value found at $Location." }
        if ($Value -match $absolutePathPattern) { throw "Rooted path found at $Location." }
        return
    }
    if ($Value -is [System.Collections.IDictionary]) {
        foreach ($key in $Value.Keys) {
            if ([string]$key -match $forbiddenPropertyPattern) { throw "Forbidden evidence property '$key' found at $Location." }
            Test-Node -Value $Value[$key] -Location "$Location.$key"
        }
        return
    }
    if ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [string]) {
        $index = 0
        foreach ($item in $Value) {
            Test-Node -Value $item -Location "$Location[$index]"
            $index++
        }
        return
    }
    foreach ($property in $Value.PSObject.Properties) {
        if ($property.Name -match $forbiddenPropertyPattern) { throw "Forbidden evidence property '$($property.Name)' found at $Location." }
        Test-Node -Value $property.Value -Location "$Location.$($property.Name)"
    }
}

function Read-EvidenceJson {
    param([Parameter(Mandatory = $true)][string]$FileName)
    $path = Join-Path $resolvedEvidenceRoot $FileName
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing expected evidence file: $FileName" }
    $value = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
    Test-Node -Value $value -Location $FileName
    return $value
}

function Test-Cleanup {
    param([Parameter(Mandatory = $true)][object]$Profile, [Parameter(Mandatory = $true)][string]$Name)
    foreach ($deltaName in @('process', 'temporary', 'configuration')) {
        if ($Profile.cleanupDelta.$deltaName -ne 0) { throw "$Name has non-zero cleanup delta '$deltaName'." }
    }
}

function Test-FrozenSettings {
    param([Parameter(Mandatory = $true)][object]$Profile, [Parameter(Mandatory = $true)][string]$Name)
    if ($Profile.settings.workers -ne 1 -or $Profile.settings.retries -ne 0 -or
        $Profile.settings.forcedGc -or $Profile.settings.rendererReloadUsedForGate) {
        throw "$Name changed a frozen Gate setting."
    }
}

function Test-ProviderBoundary {
    param([Parameter(Mandatory = $true)][object]$Profile, [Parameter(Mandatory = $true)][string]$Name)
    if ($Profile.boundary.toolCalls -ne $Profile.boundary.providerTurns -or
        $Profile.boundary.readTextToolCalls -ne $Profile.boundary.providerTurns -or
        $Profile.boundary.unauthorizedToolCalls -ne 0 -or
        $Profile.boundary.unauthorizedNetworkEvents -ne 0 -or
        $Profile.boundary.sensitiveDisclosureEvents -ne 0) {
        throw "$Name violated the workspace.read_text-only provider boundary."
    }
    Test-Cleanup -Profile $Profile -Name $Name
    Test-FrozenSettings -Profile $Profile -Name $Name
}

function Get-Profile {
    param([Parameter(Mandatory = $true)][string]$FileName)
    $profile = Read-EvidenceJson -FileName $FileName
    if ($profile.status -ne 'Passed') { throw "$FileName did not complete." }
    return $profile
}

$documents = @{}
foreach ($fileName in $expectedFiles) {
    $document = Read-EvidenceJson -FileName $fileName
    $documents[$fileName] = $document
    if ($document.schemaVersion -ne 'week80-renderer-private-bytes/v1') { throw "Unexpected schemaVersion in $fileName." }
    if ($allowedStatuses -notcontains $document.status) { throw "Invalid status in $fileName." }
    if ($document.productRevision -ne '960b230683226e7b313f31fbb771065702a54bc5') { throw "Unexpected productRevision in $fileName." }
    if ($document.baselineHead -ne '962d5dda4ae875299a96ba2c825bd13ec683240a') { throw "Unexpected baselineHead in $fileName." }
    if ($document.packageIdentity.sha256 -ne $fixedPackageSha256 -or $document.packageIdentity.bytes -ne $fixedPackageBytes) {
        throw "Unexpected fixed package identity in $fileName."
    }
    if ($document.appHostIdentity.sha256 -ne $appHostSha256 -or $document.appHostIdentity.bytes -ne $appHostBytes) {
        throw "Unexpected AppHost identity in $fileName."
    }
    if ($document.durationMilliseconds -lt 0 -or [string]::IsNullOrWhiteSpace([string]$document.summary)) {
        throw "Invalid duration or summary in $fileName."
    }
    Test-Cleanup -Profile $document -Name $fileName
}

$manifest = $documents['manifest.json']
if ($manifest.authorization.providerBackedPhase3 -ne 'Granted' -or
    $manifest.authorization.selectedConfigurationKeys -ne 3 -or
    $manifest.authorization.parentEnvironmentInjected -or
    -not $manifest.authorization.packagedChildInjected -or
    $manifest.authorization.configurationValuesPersisted -or
    $manifest.authorization.providerTurns -ne $manifest.authorization.readTextToolCalls -or
    $manifest.authorization.unauthorizedToolCalls -ne 0 -or
    $manifest.authorization.unauthorizedNetworkEvents -ne 0 -or
    $manifest.authorization.sensitiveDisclosureEvents -ne 0) {
    throw 'Provider authorization counters or injection boundary are invalid.'
}
if ($manifest.settings.gatePercent -ne 15 -or $manifest.settings.retries -ne 0 -or $manifest.settings.workers -ne 1 -or
    $manifest.settings.forcedGc -or $manifest.settings.rendererReloadUsedForGate) {
    throw 'Frozen Gate settings changed or a prohibited shortcut was recorded.'
}
if ($manifest.counts.expectedEvidenceFiles -ne $expectedFiles.Count) { throw 'Manifest expected file count is inconsistent.' }

foreach ($profileName in @('C0', 'C1', 'C2', 'C3', 'C4', 'C5', 'C6', 'C7', 'C8')) {
    $profile = Get-Profile -FileName "profile-$($profileName.ToLowerInvariant()).json"
    Test-Cleanup -Profile $profile -Name $profileName
    Test-FrozenSettings -Profile $profile -Name $profileName
}

$legacyProviderFiles = @(
    'provider-profile-p1.json',
    'provider-profile-p5.json',
    'provider-profile-p10.json',
    'provider-profile-p0-retained.json',
    'provider-profile-p5-retained.json'
)
foreach ($fileName in $legacyProviderFiles) {
    $profile = Get-Profile -FileName $fileName
    Test-ProviderBoundary -Profile $profile -Name $fileName
}

$p5q = Get-Profile -FileName 'provider-profile-p5-one-shot-observer.json'
$p5t = Get-Profile -FileName 'provider-profile-p5-memory-infra.json'
$p0t = Get-Profile -FileName 'provider-profile-p0-memory-infra.json'
$p5dt = Get-Profile -FileName 'provider-profile-p5-direct-memory-infra.json'
$p5tgc = Get-Profile -FileName 'provider-profile-p5-turn-groups-census.json'
$p5cfc = Get-Profile -FileName 'provider-profile-p5-bounded-dom-census.json'
$p5el = Get-Profile -FileName 'provider-profile-p5-event-listener-census.json'
foreach ($entry in @(
    @{ Name = 'P5Q'; Value = $p5q }, @{ Name = 'P5T'; Value = $p5t }, @{ Name = 'P0T'; Value = $p0t },
    @{ Name = 'P5DT'; Value = $p5dt }, @{ Name = 'P5TGC'; Value = $p5tgc },
    @{ Name = 'P5CFC'; Value = $p5cfc }, @{ Name = 'P5EL'; Value = $p5el }
)) {
    Test-ProviderBoundary -Profile $entry.Value -Name $entry.Name
}

if ($p5q.settings.gateEligible -ne $true -or $p5q.retention.privateBytesPercent -le 15 -or
    $p5q.gate15Percent.privateBytesWithinLimit -ne $false -or $p5q.notificationFilter.observed -ne 40 -or
    $p5q.notificationFilter.forwarded -ne 40) {
    throw 'P5Q does not provide the bounded baseline failure.'
}
if ($p5t.memoryDumpAggregate.gateEligible -ne $false -or $p5t.retention.privateBytesPercent -le 15) {
    throw 'P5T did not reproduce the failure with non-Gate memory infrastructure.'
}
$blinkGc = $p5t.memoryDumpAggregate.allocatorDeltas | Where-Object { $_.name -eq 'blink_gc' }
$blinkObjects = $p5t.memoryDumpAggregate.allocatorDeltas | Where-Object { $_.name -eq 'blink_gc/main/allocated_objects' }
if ($blinkGc.deltaBytes -lt 8000000 -or $blinkObjects.deltaBytes -lt 3000000) {
    throw 'P5T did not preserve the Blink Oilpan allocation differential.'
}
if ($p0t.retention.privateBytesPercent -gt 15 -or $p0t.retention.nodesDelta -ne 0) {
    throw 'P0T idle control did not remain below Gate with zero node growth.'
}
if ($p5dt.notificationFilter.observed -ne 40 -or $p5dt.notificationFilter.forwarded -ne 0 -or
    $p5dt.notificationFilter.dropped -ne 40 -or $p5dt.retention.privateBytesPercent -gt 15 -or
    $p5dt.retention.nodesDelta -ne 0) {
    throw 'P5DT did not exclude provider/bridge work without Renderer projection.'
}
if ($p5tgc.mutationCensus.addedNodes -ne 285 -or $p5tgc.mutationCensus.removedNodes -ne 285 -or
    $p5cfc.mutationCensus.addedNodes -ne 0 -or $p5cfc.mutationCensus.removedNodes -ne 0) {
    throw 'Mutation census does not preserve the baseline/fixed structural differential.'
}
if ($p5el.eventListenerCensus.added -ne 0 -or $p5el.eventListenerCensus.removed -ne 0) {
    throw 'EventTarget control observed actual listener subscription churn.'
}

$fixedRuns = @()
foreach ($repeat in 1..3) {
    $fileName = "provider-profile-p5-lifecycle-resync-fix-repeat$repeat.json"
    $profile = Get-Profile -FileName $fileName
    Test-ProviderBoundary -Profile $profile -Name "P5RF repeat $repeat"
    if ($profile.settings.gateEligible -ne $true -or
        $profile.packageIdentity.sha256 -ne $fixedPackageSha256 -or $profile.packageIdentity.bytes -ne $fixedPackageBytes -or
        $profile.gate15Percent.privateBytesWithinLimit -ne $true -or $profile.gate15Percent.workingSetWithinLimit -ne $true -or
        $profile.retention.privateBytesPercent -gt 15 -or $profile.retention.workingSetPercent -gt 15 -or
        $profile.notificationFilter.observed -ne 40 -or $profile.notificationFilter.forwarded -ne 40 -or
        $profile.notificationFilter.dropped -ne 0 -or $profile.observer.observerBridgeGetThreadCalls -ne 6) {
        throw "P5RF repeat $repeat violated identity, Gate, notification, or observer invariants."
    }
    $fixedRuns += $profile
}

if ($documents['provider-5-turn.json'].status -ne 'Failed' -or
    $documents['diagnosis.json'].status -ne 'Passed' -or
    $documents['diagnosis.json'].conclusion -ne 'Diagnosis Complete' -or
    $documents['diagnosis.json'].productFixImplemented -ne $true -or
    $documents['diagnosis.json'].gateAssessment.repeatedControlledRootCause -ne $true -or
    $documents['diagnosis.json'].gateAssessment.deterministicRegression -ne $true -or
    $documents['diagnosis-handoff.json'].status -ne 'Passed' -or
    $documents['diagnosis-handoff.json'].deterministicRegression.baselineResult -ne 'Failed' -or
    $documents['diagnosis-handoff.json'].deterministicRegression.fixedResult -ne 'Passed' -or
    $documents['diagnosis-handoff.json'].gateRuns.Count -ne 3) {
    throw 'Diagnosis Complete or deterministic handoff invariants are incomplete.'
}

Write-Output "Week 80 evidence validation passed: baseline failure, independent controls, Blink allocation differential, deterministic regression, and 3 identical-package fixed Gate runs are valid."
