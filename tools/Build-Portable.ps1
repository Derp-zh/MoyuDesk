param([string]$Output = 'release/MoyuDesk')
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$destination = [System.IO.Path]::GetFullPath((Join-Path $repository $Output))
if (-not $destination.StartsWith($repository + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw '发布目录必须位于当前项目内。'
}
if (Test-Path -LiteralPath $destination) { throw '请选择新的发布目录，不覆盖现有运行包或数据。' }
$defaultSound = Join-Path $repository 'src/Taskboard.Desktop/Assets/Alarm04.wav'
if (-not (Test-Path -LiteralPath $defaultSound)) {
    $systemSound = Join-Path ([Environment]::GetFolderPath('Windows')) 'Media/Alarm04.wav'
    if (-not (Test-Path -LiteralPath $systemSound)) { throw '请在Windows上构建，或将可合法使用的Alarm04.wav放入src/Taskboard.Desktop/Assets；该系统音频不随公开源码提供。' }
    Copy-Item -LiteralPath $systemSound -Destination $defaultSound
}
$packageCache = Join-Path $repository 'test-output/nuget-packages'
$csprojPath = Join-Path $repository 'src/Taskboard.Desktop/Taskboard.Desktop.csproj'
dotnet publish $csprojPath -c Release -r win-x64 --self-contained true -p:NuGetAudit=false -p:UseSharedCompilation=false -p:MSBuildEnableWorkloadResolver=false -m:1 -nr:false -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false "-p:RestorePackagesPath=$packageCache" -o $destination --nologo
if ($LASTEXITCODE -ne 0) { throw '发布失败。' }
Copy-Item -LiteralPath (Join-Path $repository 'README.md') -Destination (Join-Path $destination '使用说明.md')
Copy-Item -LiteralPath (Join-Path $repository 'CHANGELOG.md') -Destination (Join-Path $destination '版本说明.md')
Copy-Item -LiteralPath (Join-Path $repository 'docs/THIRD-PARTY-NOTICES.txt') -Destination (Join-Path $destination 'THIRD-PARTY-NOTICES.txt')
Move-Item -LiteralPath (Join-Path $destination 'Taskboard.exe') -Destination (Join-Path $destination 'MoyuDesk.exe')

# Bundle fixed-directory install / upgrade / rollback / uninstall scripts + version.json
$csprojXml = [xml](Get-Content -Raw -Encoding UTF8 -LiteralPath $csprojPath)
$appVersion = [string]$csprojXml.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($appVersion)) { $appVersion = '0.0.0' }
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
$versionManifest = [ordered]@{
    version = $appVersion.Trim()
    builtUtc = (Get-Date).ToUniversalTime().ToString('o')
    executable = 'MoyuDesk.exe'
} | ConvertTo-Json -Depth 4
[System.IO.File]::WriteAllText((Join-Path $destination 'version.json'), $versionManifest, $utf8NoBom)

$scriptsTarget = Join-Path $destination 'scripts'
New-Item -ItemType Directory -Force -Path $scriptsTarget | Out-Null
foreach ($scriptName in 'install.ps1', 'rollback.ps1', 'uninstall.ps1') {
    Copy-Item -LiteralPath (Join-Path $repository "tools/scripts/$scriptName") -Destination (Join-Path $scriptsTarget $scriptName)
}
Copy-Item -LiteralPath (Join-Path $repository 'tools/scripts/安装或升级摸鱼台.cmd') -Destination (Join-Path $destination '安装或升级摸鱼台.cmd')

Get-FileHash -LiteralPath (Join-Path $destination 'MoyuDesk.exe') -Algorithm SHA256 | Format-List
