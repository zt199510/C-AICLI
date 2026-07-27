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
    'diagnosis.json'
)
$allowedStatuses = @('NotRun', 'Running', 'Passed', 'Failed', 'Blocked')
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
        if ($Value -match $forbiddenSensitiveValuePattern) {
            throw "Sensitive-looking value found at $Location."
        }
        if ($Value -match $absolutePathPattern) {
            throw "Rooted path found at $Location."
        }
        return
    }
    if ($Value -is [System.Collections.IDictionary]) {
        foreach ($key in $Value.Keys) {
            if ([string]$key -match $forbiddenPropertyPattern) {
                throw "Forbidden evidence property '$key' found at $Location."
            }
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
        if ($property.Name -match $forbiddenPropertyPattern) {
            throw "Forbidden evidence property '$($property.Name)' found at $Location."
        }
        Test-Node -Value $property.Value -Location "$Location.$($property.Name)"
    }
}

$documents = @{}
foreach ($fileName in $expectedFiles) {
    $path = Join-Path $resolvedEvidenceRoot $fileName
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing expected evidence file: $fileName"
    }
    $document = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
    $documents[$fileName] = $document
    if ($document.schemaVersion -ne 'week80-renderer-private-bytes/v1') {
        throw "Unexpected schemaVersion in $fileName."
    }
    if ([string]::IsNullOrWhiteSpace([string]$document.evidenceKind)) {
        throw "Missing evidenceKind in $fileName."
    }
    if ($allowedStatuses -notcontains $document.status) {
        throw "Invalid status in $fileName."
    }
    if ($document.productRevision -ne '8e227a4ca050e9bdff5d25d61bf89725fed26104') {
        throw "Unexpected productRevision in $fileName."
    }
    if ($document.baselineHead -ne '962d5dda4ae875299a96ba2c825bd13ec683240a') {
        throw "Unexpected baselineHead in $fileName."
    }
    if ($document.packageIdentity.sha256 -ne '6BDB9203C0ACCB8D0E3B90EC1F4218A02BF9E05A21E068654F1C2B9B0E82DE29' -or
        $document.packageIdentity.bytes -ne 222753280) {
        throw "Unexpected package identity in $fileName."
    }
    if ($document.appHostIdentity.sha256 -ne 'DC46DBFAD098D7E2F464F05F2C8383568DF733F619B3E45B9D70BAD4F9C13DFA' -or
        $document.appHostIdentity.bytes -ne 79941168) {
        throw "Unexpected AppHost identity in $fileName."
    }
    if ($document.durationMilliseconds -lt 0) {
        throw "Invalid duration in $fileName."
    }
    foreach ($deltaName in @('process', 'temporary', 'configuration')) {
        if ($null -eq $document.cleanupDelta.$deltaName -or $document.cleanupDelta.$deltaName -lt 0) {
            throw "Invalid cleanup delta '$deltaName' in $fileName."
        }
    }
    if ([string]::IsNullOrWhiteSpace([string]$document.summary)) {
        throw "Missing summary in $fileName."
    }
    Test-Node -Value $document -Location $fileName
}

$manifest = $documents['manifest.json']
if ($manifest.authorization.providerBackedPhase3 -ne 'Granted') {
    throw 'Phase 3 authorization was not recorded.'
}
if ($manifest.authorization.selectedConfigurationKeys -ne 3 -or
    $manifest.authorization.parentEnvironmentInjected -or
    -not $manifest.authorization.packagedChildInjected -or
    $manifest.authorization.configurationValuesPersisted) {
    throw 'Provider configuration selection or injection boundary is invalid.'
}
if ($manifest.authorization.providerTurns -ne 19 -or
    $manifest.authorization.readTextToolCalls -ne 19 -or
    $manifest.authorization.unauthorizedToolCalls -ne 0 -or
    $manifest.authorization.unauthorizedNetworkEvents -ne 0 -or
    $manifest.authorization.sensitiveDisclosureEvents -ne 0) {
    throw 'Provider authorization counters are invalid.'
}
if ($manifest.settings.gatePercent -ne 15 -or $manifest.settings.retries -ne 0 -or $manifest.settings.workers -ne 1) {
    throw 'Frozen gate, retry, or worker settings changed.'
}
if ($manifest.settings.forcedGc -or $manifest.settings.rendererReloadUsedForGate) {
    throw 'A prohibited memory shortcut was recorded.'
}
if ($manifest.counts.expectedEvidenceFiles -ne $expectedFiles.Count) {
    throw 'Manifest expected file count is inconsistent.'
}
foreach ($profileName in @('C0', 'C1', 'C2', 'C3', 'C4', 'C5', 'C6', 'C7')) {
    $profilePath = Join-Path $resolvedEvidenceRoot "profile-$($profileName.ToLowerInvariant()).json"
    if (-not (Test-Path -LiteralPath $profilePath -PathType Leaf)) {
        throw "Missing raw aggregate profile: $profileName"
    }
    $profile = Get-Content -LiteralPath $profilePath -Raw -Encoding UTF8 | ConvertFrom-Json
    Test-Node -Value $profile -Location "profile-$profileName"
    if ($profile.status -ne 'Passed') {
        throw "Credential-free profile $profileName did not pass measurement and cleanup integrity."
    }
    if ($profile.settings.workers -ne 1 -or $profile.settings.retries -ne 0 -or
        $profile.settings.forcedGc -or $profile.settings.rendererReloadUsedForGate) {
        throw "Credential-free profile $profileName changed a frozen setting."
    }
    foreach ($deltaName in @('process', 'temporary', 'configuration')) {
        if ($profile.cleanupDelta.$deltaName -ne 0) {
            throw "Credential-free profile $profileName has non-zero cleanup delta '$deltaName'."
        }
    }
}

foreach ($profileName in @('P1', 'P5', 'P10')) {
    $profilePath = Join-Path $resolvedEvidenceRoot "provider-profile-$($profileName.ToLowerInvariant()).json"
    if (-not (Test-Path -LiteralPath $profilePath -PathType Leaf)) {
        throw "Missing provider aggregate profile: $profileName"
    }
    $profile = Get-Content -LiteralPath $profilePath -Raw -Encoding UTF8 | ConvertFrom-Json
    Test-Node -Value $profile -Location "provider-profile-$profileName"
    if ($profile.status -ne 'Passed') {
        throw "Provider profile $profileName did not complete measurement and cleanup integrity."
    }
    if ($profile.settings.workers -ne 1 -or $profile.settings.retries -ne 0 -or
        $profile.settings.forcedGc -or $profile.settings.rendererReloadUsedForGate) {
        throw "Provider profile $profileName changed a frozen setting."
    }
    if ($profile.boundary.toolCalls -ne $profile.boundary.providerTurns -or
        $profile.boundary.readTextToolCalls -ne $profile.boundary.providerTurns -or
        $profile.boundary.unauthorizedToolCalls -ne 0 -or
        $profile.boundary.unauthorizedNetworkEvents -ne 0 -or
        $profile.boundary.sensitiveDisclosureEvents -ne 0) {
        throw "Provider profile $profileName violated the read-only boundary."
    }
    foreach ($deltaName in @('process', 'temporary', 'configuration')) {
        if ($profile.cleanupDelta.$deltaName -ne 0) {
            throw "Provider profile $profileName has non-zero cleanup delta '$deltaName'."
        }
    }
}

if ($documents['provider-1-turn.json'].status -ne 'Passed' -or
    $documents['provider-1-turn.json'].result.gate15Percent.privateBytesWithinLimit -ne $true) {
    throw 'P1 envelope is inconsistent.'
}
foreach ($fileName in @('provider-5-turn.json', 'provider-10-turn.json')) {
    if ($documents[$fileName].status -ne 'Failed' -or
        $documents[$fileName].result.gate15Percent.privateBytesWithinLimit -ne $false) {
        throw "$fileName did not preserve the private-bytes failure."
    }
}
if ($documents['diagnosis.json'].status -ne 'Blocked' -or
    $documents['diagnosis.json'].conclusion -ne 'Blocked' -or
    $documents['diagnosis.json'].productFixImplemented -ne $false -or
    $documents['diagnosis.json'].gateAssessment.repeatedControlledRootCause -ne $false) {
    throw 'Diagnosis must remain Blocked without an unsupported product fix.'
}

Write-Output "Week 80 evidence validation passed: $($expectedFiles.Count) envelopes, 8 credential-free profiles, and 3 authorized provider profiles; identities, settings, boundaries, cleanup, and redaction are valid."
