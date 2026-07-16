param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src\CSharpAiCli.AppHost\CSharpAiCli.AppHost.csproj"
$output = Join-Path $repoRoot "apps\desktop\resources\apphost"
$dotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"

if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) {
    $dotnet = "dotnet"
}

& $dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $output

if ($LASTEXITCODE -ne 0) {
    throw "AppHost publish failed with exit code $LASTEXITCODE."
}

$appHost = Join-Path $output "CSharpAiCli.AppHost.exe"
if (-not (Test-Path -LiteralPath $appHost -PathType Leaf)) {
    throw "Published AppHost executable is missing."
}

$hash = Get-FileHash -Algorithm SHA256 -LiteralPath $appHost
[PSCustomObject]@{
    path = $appHost
    size = (Get-Item -LiteralPath $appHost).Length
    sha256 = $hash.Hash
} | ConvertTo-Json
