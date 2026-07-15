param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('success', 'missing', 'unexpected', 'oversized', 'secret-stderr', 'hang', 'child')]
    [string]$Mode,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    [string]$Value = 'controlled-output',

    [string]$PidPath,

    [string]$ChildPidPath
)

$ErrorActionPreference = 'Stop'

if (-not [string]::IsNullOrWhiteSpace($PidPath)) {
    [IO.File]::WriteAllText($PidPath, $PID.ToString([Globalization.CultureInfo]::InvariantCulture))
}

if ($Mode -eq 'hang') {
    Start-Sleep -Seconds 30
    exit 0
}

if ($Mode -eq 'child') {
    $child = Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -ArgumentList @(
        '-NoLogo', '-NoProfile', '-NonInteractive', '-Command', 'Start-Sleep -Seconds 30'
    ) -WindowStyle Hidden -PassThru
    if (-not [string]::IsNullOrWhiteSpace($ChildPidPath)) {
        [IO.File]::WriteAllText($ChildPidPath, $child.Id.ToString([Globalization.CultureInfo]::InvariantCulture))
    }
    [IO.File]::WriteAllText($OutputPath, $Value, [Text.Encoding]::UTF8)
    exit 0
}

if ($Mode -eq 'missing') {
    exit 0
}

if ($Mode -eq 'oversized') {
    [IO.File]::WriteAllText($OutputPath, ('X' * 4096), [Text.Encoding]::ASCII)
    exit 0
}

[IO.File]::WriteAllText($OutputPath, $Value, [Text.Encoding]::UTF8)

if ($Mode -eq 'unexpected') {
    [IO.File]::WriteAllText((Join-Path (Split-Path -Parent $OutputPath) 'unexpected.bin'), 'unexpected')
}

if ($Mode -eq 'secret-stderr') {
    [Console]::Error.WriteLine('apiKey=super-secret-value')
}

exit 0
