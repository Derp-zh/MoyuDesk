<#
.SYNOPSIS
Uninstalls MoyuDesk program binaries and shortcuts from the fixed install directory.

.DESCRIPTION
Preserves user tasks, drafts, and settings in %LOCALAPPDATA%\InstantTaskboard untouched.
#>
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'MoyuDesk\app'),
    [switch]$Sandbox
)
$ErrorActionPreference = 'Stop'

$targetDir = [System.IO.Path]::GetFullPath($InstallDir).TrimEnd('\', '/')
$installRoot = Split-Path -Parent $targetDir

if ($PSCommandPath -and $PSCommandPath.StartsWith($targetDir + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
    $tempCopy = Join-Path $env:TEMP ('moyudesk-uninstall-' + [Guid]::NewGuid().ToString('N') + '.ps1')
    Copy-Item -LiteralPath $PSCommandPath -Destination $tempCopy -Force
    $forward = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $tempCopy, '-InstallDir', $targetDir)
    if ($Sandbox) { $forward += '-Sandbox' }
    try {
        & powershell.exe @forward
        exit $LASTEXITCODE
    } finally {
        Remove-Item -LiteralPath $tempCopy -Force -ErrorAction SilentlyContinue
    }
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

if (Test-Path -LiteralPath $targetDir) {
    Remove-Item -LiteralPath $targetDir -Recurse -Force
}
Get-ChildItem -LiteralPath $installRoot -Directory -Filter '.rollback-*' -Force -ErrorAction SilentlyContinue |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

$desktopDir = if ($Sandbox) { Join-Path $installRoot '.sandbox-shortcuts\Desktop' } else { [Environment]::GetFolderPath('DesktopDirectory') }
$programsDir = if ($Sandbox) { Join-Path $installRoot '.sandbox-shortcuts\Programs' } else { [Environment]::GetFolderPath('Programs') }
Remove-Item -LiteralPath (Join-Path $desktopDir 'MoyuDesk.lnk') -Force -ErrorAction SilentlyContinue
$startFolder = Join-Path $programsDir 'MoyuDesk'
if (Test-Path -LiteralPath $startFolder) {
    Remove-Item -LiteralPath $startFolder -Recurse -Force -ErrorAction SilentlyContinue
}

if (-not $Sandbox) {
    $runKeyPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    try { Remove-ItemProperty -Path $runKeyPath -Name 'TaskboardPortable' -ErrorAction SilentlyContinue } catch { }
}

Write-Output "Uninstalled MoyuDesk from $targetDir. User data in %LOCALAPPDATA%\InstantTaskboard was preserved."
