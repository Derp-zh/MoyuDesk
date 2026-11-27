<#
.SYNOPSIS
Installs or upgrades MoyuDesk (摸鱼台) into a fixed per-user directory without administrator rights.

.DESCRIPTION
Default fixed install directory: %LOCALAPPDATA%\MoyuDesk\app
Official user task & config data (%LOCALAPPDATA%\InstantTaskboard) is NEVER modified or deleted.
When upgrading an existing installation, the previous version is backed up whole to:
  <InstallRoot>\.rollback-<oldVersion>
so scripts\rollback.ps1 can restore it in one click.
#>
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'MoyuDesk\app'),
    [string]$SourceDir = (Split-Path -Parent $PSScriptRoot),
    [switch]$NoStart,
    [switch]$NoAutoStart,
    [switch]$Sandbox
)
$ErrorActionPreference = 'Stop'

$sourceRoot = [System.IO.Path]::GetFullPath($SourceDir).TrimEnd('\', '/')
$targetDir = [System.IO.Path]::GetFullPath($InstallDir).TrimEnd('\', '/')
$installRoot = Split-Path -Parent $targetDir

if ($sourceRoot.Equals($targetDir, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Source directory and target install directory are identical ($targetDir). Run install.ps1 from an extracted release package."
}

$sourceExe = Join-Path $sourceRoot 'MoyuDesk.exe'
if (-not (Test-Path -LiteralPath $sourceExe)) {
    throw "MoyuDesk.exe not found in source package: $sourceRoot"
}

$utf8NoBom = New-Object System.Text.UTF8Encoding $false

function Get-AppVersion([string]$dir) {
    $manifest = Join-Path $dir 'version.json'
    if (Test-Path -LiteralPath $manifest) {
        try {
            $parsed = Get-Content -Raw -Encoding UTF8 -LiteralPath $manifest | ConvertFrom-Json
            if ($parsed.version) { return [string]$parsed.version }
        } catch { }
    }
    $dll = Join-Path $dir 'Taskboard.dll'
    if (Test-Path -LiteralPath $dll) {
        $ver = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($dll).FileVersion
        if ($ver) { return $ver }
    }
    $exe = Join-Path $dir 'MoyuDesk.exe'
    if (Test-Path -LiteralPath $exe) {
        $ver = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion
        if ($ver) { return $ver }
    }
    return 'unknown'
}

function Stop-InstalledMoyuDesk([string]$dir) {
    if ($Sandbox) { return }
    $prefix = $dir + '\'
    $targets = Get-Process -Name 'MoyuDesk', 'Taskboard' -ErrorAction SilentlyContinue | Where-Object {
        try {
            $_.Path -and $_.Path.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)
        } catch { $false }
    }
    foreach ($proc in $targets) {
        try { $proc.CloseMainWindow() | Out-Null } catch { }
        try {
            if (-not $proc.WaitForExit(2500)) {
                Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
            }
        } catch { }
    }
    if ($targets) { Start-Sleep -Milliseconds 300 }
}

function Write-Shortcut([string]$shortcutPath, [string]$targetExe, [string]$description) {
    $parent = Split-Path -Parent $shortcutPath
    if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
    $shell = $null
    $link = $null
    try {
        $shell = New-Object -ComObject WScript.Shell
        $link = $shell.CreateShortcut($shortcutPath)
        $link.TargetPath = $targetExe
        $link.WorkingDirectory = Split-Path -Parent $targetExe
        $link.IconLocation = "$targetExe,0"
        $link.Description = $description
        $link.Save()
    } finally {
        if ($link) { [System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($link) | Out-Null }
        if ($shell) { [System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) | Out-Null }
    }
}

$newVersion = Get-AppVersion $sourceRoot
$hasExisting = Test-Path -LiteralPath $targetDir
$oldVersion = if ($hasExisting) { Get-AppVersion $targetDir } else { $null }
$mode = if ($hasExisting) { "upgrade from $oldVersion" } else { 'fresh install' }

Write-Output "MoyuDesk $newVersion ($mode) -> $targetDir"
New-Item -ItemType Directory -Force -Path $installRoot | Out-Null

# Stage full package into a sibling temporary directory first for atomic swap.
$stageDir = Join-Path $installRoot ('.staging-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $stageDir | Out-Null

$rollbackDir = $null
$movedOldToRollback = $false
try {
    robocopy $sourceRoot $stageDir /E /XD .git test-output .staging* /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -gt 7) { throw "Staging copy failed (robocopy exit code $LASTEXITCODE)." }

    # Ensure version.json exists in staged copy
    $stageManifest = Join-Path $stageDir 'version.json'
    if (-not (Test-Path -LiteralPath $stageManifest)) {
        $manifestJson = [ordered]@{
            version = $newVersion
            installedUtc = (Get-Date).ToUniversalTime().ToString('o')
            executable = 'MoyuDesk.exe'
        } | ConvertTo-Json -Depth 4
        [System.IO.File]::WriteAllText($stageManifest, $manifestJson, $utf8NoBom)
    }

    Stop-InstalledMoyuDesk $targetDir

    if ($hasExisting) {
        $safeOld = ($oldVersion -replace '[^0-9A-Za-z._-]', '_')
        if ([string]::IsNullOrWhiteSpace($safeOld)) { $safeOld = 'previous' }
        $rollbackName = ".rollback-$safeOld"
        $rollbackDir = Join-Path $installRoot $rollbackName
        if (Test-Path -LiteralPath $rollbackDir) {
            Remove-Item -LiteralPath $rollbackDir -Recurse -Force
        }
        Move-Item -LiteralPath $targetDir -Destination $rollbackDir
        $movedOldToRollback = $true
        [System.IO.File]::WriteAllText((Join-Path $stageDir '.rollback-version'), $oldVersion, $utf8NoBom)
        [System.IO.File]::WriteAllText((Join-Path $stageDir '.rollback-dir'), $rollbackName, $utf8NoBom)
        Write-Output "Backed up previous version ($oldVersion) to $rollbackDir"
    }

    Move-Item -LiteralPath $stageDir -Destination $targetDir
} catch {
    if (Test-Path -LiteralPath $stageDir) {
        Remove-Item -LiteralPath $stageDir -Recurse -Force -ErrorAction SilentlyContinue
    }
    if ($movedOldToRollback -and $rollbackDir -and (Test-Path -LiteralPath $rollbackDir) -and -not (Test-Path -LiteralPath $targetDir)) {
        Move-Item -LiteralPath $rollbackDir -Destination $targetDir -ErrorAction SilentlyContinue
        Write-Warning "Install failed; restored previous version from $rollbackDir"
    }
    throw
}

$installedExe = Join-Path $targetDir 'MoyuDesk.exe'

# Update Desktop & Start Menu shortcuts (sandbox writes into <InstallRoot>\.sandbox-shortcuts).
$desktopDir = if ($Sandbox) { Join-Path $installRoot '.sandbox-shortcuts\Desktop' } else { [Environment]::GetFolderPath('DesktopDirectory') }
$programsDir = if ($Sandbox) { Join-Path $installRoot '.sandbox-shortcuts\Programs' } else { [Environment]::GetFolderPath('Programs') }
$desktopShortcut = Join-Path $desktopDir 'MoyuDesk.lnk'
$startShortcut = Join-Path $programsDir 'MoyuDesk\MoyuDesk.lnk'
Write-Shortcut $desktopShortcut $installedExe 'MoyuDesk'
Write-Shortcut $startShortcut $installedExe 'MoyuDesk'

# Update Windows HKCU Run auto-start entry to fixed install path (skipped in -Sandbox or -NoAutoStart).
if (-not $Sandbox -and -not $NoAutoStart) {
    $runKeyPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    $entryName = 'TaskboardPortable'
    $existingRun = $null
    try { $existingRun = (Get-ItemProperty -Path $runKeyPath -Name $entryName -ErrorAction SilentlyContinue).$entryName } catch { }
    if (-not $hasExisting -or $existingRun) {
        if (-not (Test-Path $runKeyPath)) { New-Item -Path $runKeyPath -Force | Out-Null }
        Set-ItemProperty -Path $runKeyPath -Name $entryName -Value "`"$installedExe`" --startup"
        Write-Output "Auto-start updated -> `"$installedExe`" --startup"
    } else {
        Write-Output 'Auto-start kept disabled (respecting existing user preference).'
    }
}

if (-not $NoStart -and -not $Sandbox) {
    Start-Process -FilePath $installedExe -WorkingDirectory $targetDir
}

Write-Output "Installed MoyuDesk $newVersion at $targetDir"
