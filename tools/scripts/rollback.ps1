<#
.SYNOPSIS
Restores the previous MoyuDesk version backed up by install.ps1.

.DESCRIPTION
Official user task & config data (%LOCALAPPDATA%\InstantTaskboard) is shared across versions and never touched.
#>
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'MoyuDesk\app'),
    [string]$Version,
    [switch]$NoStart,
    [switch]$Sandbox
)
$ErrorActionPreference = 'Stop'

$targetDir = [System.IO.Path]::GetFullPath($InstallDir).TrimEnd('\', '/')
$installRoot = Split-Path -Parent $targetDir

# If executed from inside the directory being replaced, run a temporary copy so the folder is not locked.
if ($PSCommandPath -and $PSCommandPath.StartsWith($targetDir + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
    $tempCopy = Join-Path $env:TEMP ('moyudesk-rollback-' + [Guid]::NewGuid().ToString('N') + '.ps1')
    Copy-Item -LiteralPath $PSCommandPath -Destination $tempCopy -Force
    $forward = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $tempCopy, '-InstallDir', $targetDir)
    if ($Version) { $forward += @('-Version', $Version) }
    if ($NoStart) { $forward += '-NoStart' }
    if ($Sandbox) { $forward += '-Sandbox' }
    try {
        & powershell.exe @forward
        exit $LASTEXITCODE
    } finally {
        Remove-Item -LiteralPath $tempCopy -Force -ErrorAction SilentlyContinue
    }
}

$rollbackDir = $null
if ($Version) {
    $safe = ($Version -replace '[^0-9A-Za-z._-]', '_')
    $candidate = Join-Path $installRoot ".rollback-$safe"
    if (Test-Path -LiteralPath $candidate) { $rollbackDir = $candidate }
} else {
    $dirMarker = Join-Path $targetDir '.rollback-dir'
    if (Test-Path -LiteralPath $dirMarker) {
        $name = (Get-Content -Raw -Encoding UTF8 -LiteralPath $dirMarker).Trim()
        if ($name) {
            $candidate = Join-Path $installRoot $name
            if (Test-Path -LiteralPath $candidate) { $rollbackDir = $candidate }
        }
    }
    if (-not $rollbackDir) {
        $latest = Get-ChildItem -LiteralPath $installRoot -Directory -Filter '.rollback-*' -Force -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
        if ($latest) { $rollbackDir = $latest.FullName }
    }
}

if (-not $rollbackDir -or -not (Test-Path -LiteralPath (Join-Path $rollbackDir 'MoyuDesk.exe'))) {
    throw "No valid rollback backup was found under $installRoot; nothing was changed."
}

if (-not $Sandbox) {
    $prefix = $targetDir + '\'
    Get-Process -Name 'MoyuDesk', 'Taskboard' -ErrorAction SilentlyContinue | Where-Object {
        try { $_.Path -and $_.Path.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase) } catch { $false }
    } | ForEach-Object {
        try { $_.CloseMainWindow() | Out-Null } catch { }
        try { if (-not $_.WaitForExit(2500)) { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue } } catch { }
    }
    Start-Sleep -Milliseconds 300
}

$aside = Join-Path $installRoot ('.rollback-displaced-' + [Guid]::NewGuid().ToString('N'))
try {
    if (Test-Path -LiteralPath $targetDir) {
        Move-Item -LiteralPath $targetDir -Destination $aside
    }
    Move-Item -LiteralPath $rollbackDir -Destination $targetDir
    if (Test-Path -LiteralPath $aside) {
        Remove-Item -LiteralPath $aside -Recurse -Force -ErrorAction SilentlyContinue
    }
} catch {
    if ((Test-Path -LiteralPath $aside) -and -not (Test-Path -LiteralPath $targetDir)) {
        Move-Item -LiteralPath $aside -Destination $targetDir -ErrorAction SilentlyContinue
    }
    throw
}

$installedExe = Join-Path $targetDir 'MoyuDesk.exe'
if (-not $NoStart -and -not $Sandbox) {
    Start-Process -FilePath $installedExe -WorkingDirectory $targetDir
}

Write-Output "Rolled back MoyuDesk successfully from $rollbackDir -> $targetDir"
