[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = "",
    [switch]$NoZip,
    [switch]$ReleaseAcceptance,
    [switch]$AllowDirtySource
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..")
$projectPath = Join-Path $repoRoot "src\CSharpAiCli.Cli\CSharpAiCli.Cli.csproj"
$propsPath = Join-Path $repoRoot "Directory.Build.props"
$globalJsonPath = Join-Path $repoRoot "global.json"

function Test-PathWithin {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Candidate
    )

    $separators = [char[]]@([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $normalizedRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd($separators)
    $normalizedCandidate = [System.IO.Path]::GetFullPath($Candidate).TrimEnd($separators)
    return $normalizedCandidate.Equals($normalizedRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
        $normalizedCandidate.StartsWith($normalizedRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-ArtifactInventory {
    param([Parameter(Mandatory = $true)][string]$Root)

    return @([System.IO.Directory]::EnumerateFiles($Root, "*", [System.IO.SearchOption]::AllDirectories) |
        ForEach-Object {
            $relativePath = $_.Substring($Root.Length).TrimStart(
                [System.IO.Path]::DirectorySeparatorChar,
                [System.IO.Path]::AltDirectorySeparatorChar).Replace('\', '/')
            [ordered]@{
                path = $relativePath
                size = (Get-Item -LiteralPath $_).Length
                sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash
            }
        } |
        Sort-Object { $_.path })
}

if (-not (Test-Path -LiteralPath $projectPath)) {
    throw "CLI project not found: $projectPath"
}

if (-not (Test-Path -LiteralPath $propsPath)) {
    throw "Version metadata not found: $propsPath"
}

if (-not (Test-Path -LiteralPath $globalJsonPath)) {
    throw "SDK lock not found: $globalJsonPath"
}

if ($ReleaseAcceptance -and $AllowDirtySource) {
    throw "ReleaseAcceptance cannot be combined with AllowDirtySource."
}

$sourceRevision = (& git -C $repoRoot rev-parse --verify HEAD 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceRevision -notmatch '^[0-9a-fA-F]{40}$') {
    throw "Release source revision could not be resolved from Git."
}
$sourceRevision = $sourceRevision.ToLowerInvariant()
$sourceStatus = (& git -C $repoRoot status --porcelain=v1 --untracked-files=all 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0) {
    throw "Release source cleanliness could not be checked."
}
$sourceDirty = -not [string]::IsNullOrWhiteSpace($sourceStatus)
if ($sourceDirty -and -not $AllowDirtySource) {
    throw "Release build requires a clean Git source tree. Use -AllowDirtySource only for non-acceptance validation artifacts."
}

$sdkVersion = (& dotnet --version 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sdkVersion)) {
    throw "dotnet SDK version could not be resolved."
}
$lockedSdkVersion = [string](Get-Content -Raw -LiteralPath $globalJsonPath | ConvertFrom-Json).sdk.version
if ($sdkVersion -ne $lockedSdkVersion) {
    throw "dotnet SDK $sdkVersion does not match global.json version $lockedSdkVersion."
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts\release"
}

[xml]$props = Get-Content -LiteralPath $propsPath -Raw
$version = [string]$props.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Directory.Build.props must define a Version property."
}

$releaseName = "caicli-$version-$Runtime"
$publishDir = Join-Path $OutputRoot $releaseName
$zipPath = Join-Path $OutputRoot "$releaseName.zip"

$resolvedOutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$resolvedPublishDir = [System.IO.Path]::GetFullPath($publishDir)
$resolvedRepoRoot = [System.IO.Path]::GetFullPath($repoRoot)
$resolvedZipPath = [System.IO.Path]::GetFullPath($zipPath)
$checksumPath = [System.IO.Path]::GetFullPath((Join-Path $OutputRoot "$releaseName.checksums.json"))

if (-not (Test-PathWithin -Root $resolvedRepoRoot -Candidate $resolvedOutputRoot)) {
    throw "OutputRoot must stay inside the repository: $resolvedOutputRoot"
}

New-Item -ItemType Directory -Path $resolvedOutputRoot -Force | Out-Null

foreach ($staleArtifact in @($resolvedZipPath, $checksumPath)) {
    if (Test-Path -LiteralPath $staleArtifact -PathType Leaf) {
        Remove-Item -LiteralPath $staleArtifact -Force
    }
}

if (Test-Path -LiteralPath $resolvedPublishDir) {
    if (-not (Test-PathWithin -Root $resolvedOutputRoot -Candidate $resolvedPublishDir)) {
        throw "Refusing to remove publish directory outside OutputRoot: $resolvedPublishDir"
    }

    Remove-Item -LiteralPath $resolvedPublishDir -Recurse -Force
}

dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    --output $resolvedPublishDir
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$exePath = Join-Path $resolvedPublishDir "caicli.exe"
if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Expected executable was not produced: $exePath"
}

$magickNetVersion = "14.15.0"
$nugetPackagesRoot = if ([string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES)) {
    Join-Path $env:USERPROFILE ".nuget\packages"
} else {
    $env:NUGET_PACKAGES
}
$magickNetNotice = Join-Path $nugetPackagesRoot "magick.net-q8-x64\$magickNetVersion\Notice.txt"
if (-not (Test-Path -LiteralPath $magickNetNotice -PathType Leaf)) {
    throw "Magick.NET third-party notice not found for package version $magickNetVersion. Restore packages before release build."
}
Copy-Item -LiteralPath $magickNetNotice `
    -Destination (Join-Path $resolvedPublishDir "THIRD-PARTY-NOTICES-MAGICK.NET.txt")

if (@([System.IO.Directory]::EnumerateFiles($resolvedPublishDir, "*.pdb", [System.IO.SearchOption]::AllDirectories)).Count -ne 0) {
    throw "Release PDB policy is excluded, but publish produced PDB files."
}

$payloadInventory = Get-ArtifactInventory -Root $resolvedPublishDir

$manifest = [ordered]@{
    schemaVersion = 1
    product = "C# AI CLI"
    command = "caicli"
    version = $version
    configuration = $Configuration
    runtime = $Runtime
    targetFramework = "net9.0"
    selfContained = $true
    executable = "caicli.exe"
    builtFromVersion = $version
    sourceRevision = $sourceRevision
    sourceDirty = $sourceDirty
    releaseAcceptance = [bool]$ReleaseAcceptance
    sdkVersion = $sdkVersion
    pdbPolicy = "excluded"
    thirdPartyNotices = @("THIRD-PARTY-NOTICES-MAGICK.NET.txt")
    artifactInventory = $payloadInventory
}

$manifestPath = Join-Path $resolvedPublishDir "release-manifest.json"
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

if (-not $NoZip) {
    if (Test-Path -LiteralPath $resolvedZipPath) {
        Remove-Item -LiteralPath $resolvedZipPath -Force
    }

    Add-Type -AssemblyName System.IO.Compression

    $fixedEntryTimestamp = [System.DateTimeOffset]::Parse("2020-01-01T00:00:00Z")
    $files = [string[]][System.IO.Directory]::EnumerateFiles($resolvedPublishDir, "*", [System.IO.SearchOption]::AllDirectories)
    $orderedFiles = [System.Linq.Enumerable]::OrderBy(
        $files,
        [System.Func[string, string]]{
            param($path)

            $relativePath = $path.Substring($resolvedPublishDir.Length).TrimStart(
                [System.IO.Path]::DirectorySeparatorChar,
                [System.IO.Path]::AltDirectorySeparatorChar)
            $relativePath.Replace('\', '/')
        },
        [System.StringComparer]::Ordinal)

    $zipFileStream = [System.IO.File]::Open(
        $resolvedZipPath,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::ReadWrite,
        [System.IO.FileShare]::None)

    try {
        $zipArchive = [System.IO.Compression.ZipArchive]::new(
            $zipFileStream,
            [System.IO.Compression.ZipArchiveMode]::Create,
            $false)

        try {
            foreach ($filePath in $orderedFiles) {
                $entryName = $filePath.Substring($resolvedPublishDir.Length).TrimStart(
                    [System.IO.Path]::DirectorySeparatorChar,
                    [System.IO.Path]::AltDirectorySeparatorChar)
                $entryName = $entryName.Replace('\', '/')

                $entry = $zipArchive.CreateEntry($entryName, [System.IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $fixedEntryTimestamp

                $sourceStream = [System.IO.File]::OpenRead($filePath)
                try {
                    $entryStream = $entry.Open()
                    try {
                        $sourceStream.CopyTo($entryStream)
                    }
                    finally {
                        $entryStream.Dispose()
                    }
                }
                finally {
                    $sourceStream.Dispose()
                }
            }
        }
        finally {
            $zipArchive.Dispose()
        }
    }
    finally {
        $zipFileStream.Dispose()
    }
}

$publishInventory = Get-ArtifactInventory -Root $resolvedPublishDir
$package = if ($NoZip) {
    $null
} else {
    [ordered]@{
        path = [System.IO.Path]::GetFileName($resolvedZipPath)
        size = (Get-Item -LiteralPath $resolvedZipPath).Length
        sha256 = (Get-FileHash -LiteralPath $resolvedZipPath -Algorithm SHA256).Hash
    }
}
$checksums = [ordered]@{
    schemaVersion = 1
    sourceRevision = $sourceRevision
    sourceDirty = $sourceDirty
    releaseAcceptance = [bool]$ReleaseAcceptance
    sdkVersion = $sdkVersion
    configuration = $Configuration
    runtime = $Runtime
    pdbPolicy = "excluded"
    publishInventory = $publishInventory
    package = $package
}
$checksums | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $checksumPath -Encoding UTF8

Write-Host "release: $releaseName"
Write-Host "sourceRevision: $sourceRevision"
Write-Host "sourceDirty: $($sourceDirty.ToString().ToLowerInvariant())"
Write-Host "sdkVersion: $sdkVersion"
Write-Host "publishDir: $resolvedPublishDir"
if (-not $NoZip) {
    Write-Host "zip: $resolvedZipPath"
}
Write-Host "checksums: $checksumPath"
