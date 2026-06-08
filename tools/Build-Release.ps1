[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = "",
    [switch]$NoZip
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..")
$projectPath = Join-Path $repoRoot "src\CSharpAiCli.Cli\CSharpAiCli.Cli.csproj"
$propsPath = Join-Path $repoRoot "Directory.Build.props"

if (-not (Test-Path -LiteralPath $projectPath)) {
    throw "CLI project not found: $projectPath"
}

if (-not (Test-Path -LiteralPath $propsPath)) {
    throw "Version metadata not found: $propsPath"
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

if (-not $resolvedOutputRoot.StartsWith($resolvedRepoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputRoot must stay inside the repository: $resolvedOutputRoot"
}

New-Item -ItemType Directory -Path $resolvedOutputRoot -Force | Out-Null

if (Test-Path -LiteralPath $resolvedPublishDir) {
    if (-not $resolvedPublishDir.StartsWith($resolvedOutputRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
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
    --output $resolvedPublishDir

$exePath = Join-Path $resolvedPublishDir "caicli.exe"
if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Expected executable was not produced: $exePath"
}

$manifest = [ordered]@{
    product = "C# AI CLI"
    command = "caicli"
    version = $version
    configuration = $Configuration
    runtime = $Runtime
    targetFramework = "net9.0"
    selfContained = $true
    executable = "caicli.exe"
    builtFromVersion = $version
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

Write-Host "release: $releaseName"
Write-Host "publishDir: $resolvedPublishDir"
if (-not $NoZip) {
    Write-Host "zip: $resolvedZipPath"
}
