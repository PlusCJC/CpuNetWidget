param(
    [switch]$FrameworkDependent,
    [switch]$SkipPackageAudit,
    [switch]$SkipAppBuild,
    [string]$IsccPath
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$installerScript = Join-Path $projectRoot 'Installer\CpuNetWidget.iss'
$publishedExe = Join-Path $projectRoot 'publish\CpuNetWidget.exe'
$publishedNotices = Join-Path $projectRoot 'publish\THIRD-PARTY-NOTICES.txt'

if (-not $SkipAppBuild) {
    $buildArguments = @()
    if ($FrameworkDependent) { $buildArguments += '-FrameworkDependent' }
    if ($SkipPackageAudit) { $buildArguments += '-SkipPackageAudit' }

    & (Join-Path $projectRoot 'Build.ps1') @buildArguments
    if ($LASTEXITCODE -ne 0) {
        throw "应用发布失败，退出代码：$LASTEXITCODE"
    }
}

foreach ($requiredFile in @($publishedExe, $publishedNotices, $installerScript)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "安装器缺少必需文件：$requiredFile"
    }
}

if ($IsccPath) {
    $compiler = $IsccPath
}
else {
    $command = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($command) {
        $compiler = $command.Source
    }
    else {
        $knownPaths = @(
            (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
            (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
        ) | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) }
        $compiler = $knownPaths | Select-Object -First 1
    }
}

if (-not $compiler -or -not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw @'
未找到 Inno Setup 6 编译器 ISCC.exe。
请从 https://jrsoftware.org/isdl.php 安装 Inno Setup 6，或使用：
  .\BuildSetup.ps1 -IsccPath "C:\完整路径\ISCC.exe"
应用本体已经发布完成，安装器源码位于 Installer\CpuNetWidget.iss。
'@
}

& $compiler $installerScript
if ($LASTEXITCODE -ne 0) {
    throw "安装器编译失败，ISCC 退出代码：$LASTEXITCODE"
}

$setupExe = Join-Path $projectRoot 'setup\CpuNetWidget-Setup.exe'
if (-not (Test-Path -LiteralPath $setupExe -PathType Leaf)) {
    throw "ISCC 已结束，但未找到安装器：$setupExe"
}

Write-Host ''
Write-Host 'Setup 编译完成：' -ForegroundColor Green
Write-Host $setupExe
