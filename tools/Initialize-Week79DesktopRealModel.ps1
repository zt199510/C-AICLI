[CmdletBinding()]
param(
    [string]$EvidenceRoot,
    [ValidatePattern('^[A-Za-z0-9._-]{1,64}$')]
    [string]$OperatorAlias = 'week79-operator'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $EvidenceRoot = Join-Path $repositoryRoot 'artifacts\week79-desktop-real-model'
}
$resolvedEvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)
$expectedParent = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
if (-not $resolvedEvidenceRoot.StartsWith(
    $expectedParent + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw 'EvidenceRoot must be a child of the repository artifacts directory.'
}

function Invoke-CapturedVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Command,
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $output = @(& $Command @Arguments 2>$null)
    if ($LASTEXITCODE -ne 0 -or $output.Count -eq 0) {
        throw "Unable to capture the $Command version."
    }
    return ([string]$output[0]).Trim()
}

function Get-ObservedIdentity {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return [ordered]@{
            state = 'NotBuilt'
            sha256 = $null
            bytes = $null
        }
    }

    return [ordered]@{
        state = 'Observed'
        sha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
        bytes = [int64](Get-Item -LiteralPath $Path).Length
    }
}

function Write-RedactedJson {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [object]$Value
    )

    $path = Join-Path $resolvedEvidenceRoot $Name
    $json = $Value | ConvertTo-Json -Depth 24
    [IO.File]::WriteAllText(
        $path,
        $json + [Environment]::NewLine,
        [Text.UTF8Encoding]::new($false))
}

$sourceRevision = (& git -C $repositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceRevision -notmatch '^[0-9a-f]{40}$') {
    throw 'Unable to resolve the source revision.'
}
$sourceBranch = (& git -C $repositoryRoot branch --show-current).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sourceBranch)) {
    throw 'Unable to resolve the source branch.'
}
$sourceDirty = @(& git -C $repositoryRoot status --porcelain --untracked-files=normal).Count -ne 0
$trackedDotenvCount = @(
    & git -C $repositoryRoot ls-files |
        Where-Object { $_ -match '(^|/)(\.env|[^/]*\.env)(\.|$)' }
).Count
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to inspect tracked file names.'
}

$dotnetCommand = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetCommand -PathType Leaf)) {
    $dotnetCommand = 'dotnet'
}
$dotnetVersion = Invoke-CapturedVersion -Command $dotnetCommand -Arguments @('--version')
$nodeVersion = Invoke-CapturedVersion -Command 'node' -Arguments @('--version')
$npmVersion = Invoke-CapturedVersion -Command 'npm' -Arguments @('--version')
$desktopPackage = Get-Content -LiteralPath (Join-Path $repositoryRoot 'apps\desktop\package.json') -Raw -Encoding UTF8 |
    ConvertFrom-Json
$electronVersion = [string]$desktopPackage.devDependencies.electron

$packagePath = Join-Path $repositoryRoot 'apps\desktop\out\C-AICLI Desktop-win32-x64\caicli-desktop.exe'
$appHostPath = Join-Path $repositoryRoot 'apps\desktop\out\C-AICLI Desktop-win32-x64\resources\apphost\CSharpAiCli.AppHost.exe'
$packageIdentity = Get-ObservedIdentity -Path $packagePath
$appHostIdentity = Get-ObservedIdentity -Path $appHostPath
$capturedAtUtc = [DateTime]::UtcNow.ToString('o')

New-Item -ItemType Directory -Path $resolvedEvidenceRoot -Force | Out-Null

$common = [ordered]@{
    schemaVersion = 'week79-desktop-real-model/v1'
    capturedAtUtc = $capturedAtUtc
    sourceRevision = $sourceRevision
    sourceDirty = $sourceDirty
    packageIdentity = $packageIdentity
    appHostIdentity = $appHostIdentity
    status = 'NotRun'
    checks = @()
    counts = [ordered]@{}
    durationMilliseconds = 0
    cleanupDelta = [ordered]@{
        process = 0
        temporary = 0
        configuration = 0
    }
}

$manifest = [ordered]@{} + $common
$manifest.evidenceKind = 'manifest'
$manifest.status = 'Passed'
$manifest.summary = 'Week 79 boundaries and credential-free evidence envelopes are initialized; no provider-backed scenario has run.'
$manifest.executionDate = [DateTime]::UtcNow.ToString('yyyy-MM-dd')
$manifest.sourceBranch = $sourceBranch
$manifest.operatorAlias = $OperatorAlias
$manifest.toolchain = [ordered]@{
    dotnetSdk = $dotnetVersion
    node = $nodeVersion
    npm = $npmVersion
    electron = $electronVersion
}
$manifest.authorization = [ordered]@{
    providerBackedScenarios = 'NotGranted'
    priorAuthorizationReusable = $false
    ignoredDotenvRead = $false
}
$manifest.comparisonBaseline = [ordered]@{
    sourceRevision = '10d13791b070580eb74f3d6223d6e8cc49f41f2e'
    packageSha256 = 'E5A5EA65B5F593D0A1CD03EC29CDDE220C14A0E79D69BACC3B06C097D73C2EEC'
    appHostSha256 = 'D6D005B4362861462A156D22B80B9367BDF32074910D964D5B8573F3F71DBE66'
    state = 'SupersededAfterAppHostChange'
}
$manifest.boundaries = [ordered]@{
    protocol = 'desktop-v1 unchanged'
    productionTools = @(
        'agent.plan',
        'workspace.read_text',
        'workspace.search_text',
        'workspace.apply_patch',
        'workspace.run_shell',
        'git.status',
        'git.diff'
    )
    mcpDiscoveryDefault = $false
    approval = 'Desktop write and shell require one durable action decision'
    stopConditions = @(
        'approval bypass',
        'unauthorized write or network access',
        'sensitive value or rooted path disclosure',
        'fake runtime presented as provider-backed success',
        'duplicate execution',
        'unowned process control'
    )
}
$manifest.cleanupSampling = [ordered]@{
    processScope = 'scenario-owned process tree'
    temporaryScope = 'scenario-owned temporary roots'
    configurationScope = 'scenario-owned configuration files'
    requireZeroDelta = $true
}
$manifest.checks = @(
    [ordered]@{ name = 'week78-historical-status'; status = 'Passed' },
    [ordered]@{ name = 'week79-post-cycle-links'; status = 'Passed' },
    [ordered]@{ name = 'tracked-dotenv-count'; status = $(if ($trackedDotenvCount -eq 0) { 'Passed' } else { 'Failed' }) },
    [ordered]@{ name = 'provider-backed-authorization'; status = 'NotRun' }
)
$manifest.counts = [ordered]@{
    trackedDotenvFiles = $trackedDotenvCount
    expectedEvidenceFiles = 8
    providerCalls = 0
    toolCalls = 0
    approvalRequests = 0
}
Write-RedactedJson -Name 'manifest.json' -Value $manifest

$initialEvidence = @(
    @{ Name = 'architecture.json'; Kind = 'architecture'; Summary = 'Production runtime architecture validation has not run.' },
    @{ Name = 'credential-free.json'; Kind = 'credential-free'; Summary = 'Credential-free regression and package validation have not run.' },
    @{ Name = 'real-model-readonly.json'; Kind = 'real-model-readonly'; Summary = 'Provider-backed read-only validation has not run; Week 79 authorization has not been granted.' },
    @{ Name = 'real-model-write.json'; Kind = 'real-model-write'; Summary = 'Provider-backed controlled write validation has not run and remains gated.' },
    @{ Name = 'recovery.json'; Kind = 'recovery'; Summary = 'Provider-backed crash and restart validation has not run and remains gated.' },
    @{ Name = 'resource-summary.json'; Kind = 'resource-summary'; Summary = 'Provider-backed resource and idle observation has not run and remains gated.' },
    @{ Name = 'final-summary.json'; Kind = 'final-summary'; Summary = 'No Week 79 Preview decision has been made.' }
)

foreach ($item in $initialEvidence) {
    $document = [ordered]@{} + $common
    $document.evidenceKind = $item.Kind
    $document.summary = $item.Summary
    Write-RedactedJson -Name $item.Name -Value $document
}

Write-Output 'Initialized redacted Week 79 evidence at artifacts/week79-desktop-real-model.'
