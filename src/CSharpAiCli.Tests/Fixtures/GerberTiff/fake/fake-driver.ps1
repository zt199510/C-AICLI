param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('success', 'failure', 'partial-output', 'timeout')]
    [string]$Mode,

    [string]$PidPath
)

$ErrorActionPreference = 'Stop'

if (-not [string]::IsNullOrWhiteSpace($PidPath)) {
    [IO.File]::WriteAllText($PidPath, $PID.ToString([Globalization.CultureInfo]::InvariantCulture))
}

if ($Mode -eq 'timeout') {
    Start-Sleep -Seconds 10
    exit 0
}

$fixture = Join-Path $PSScriptRoot (Join-Path 'protocol-v1' ($Mode + '.json'))
[Console]::Out.Write([IO.File]::ReadAllText($fixture, [Text.Encoding]::UTF8))
[Console]::Out.Flush()

if ($Mode -eq 'success') {
    exit 0
}

exit 1
