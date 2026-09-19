param(
    [switch]$FrameworkDependent
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $projectRoot 'CpuNetWidget\CpuNetWidget.csproj'
$iconPath = Join-Path $projectRoot 'CpuNetWidget\Assets\app.ico'
$publishPath = Join-Path $projectRoot 'publish'
$localDotnet = Join-Path $projectRoot '.dotnet\dotnet.exe'
$dotnetHome = Join-Path $projectRoot '.dotnet-home'
$nugetPackages = Join-Path $projectRoot '.packages'

New-Item -ItemType Directory -Force -Path $dotnetHome, $nugetPackages | Out-Null
$env:DOTNET_CLI_HOME = $dotnetHome
$env:NUGET_PACKAGES = $nugetPackages
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

if (Test-Path -LiteralPath $localDotnet) {
    $dotnet = $localDotnet
}
elseif (Get-Command dotnet -ErrorAction SilentlyContinue) {
    $dotnet = 'dotnet'
}
else {
    throw '未找到 dotnet。请安装 .NET 8 SDK 后重试。'
}

$sdkList = & $dotnet --list-sdks
if (-not $sdkList) {
    throw '已找到 dotnet 运行时，但没有 .NET SDK。请安装 .NET 8 SDK 后重试。'
}

# Generate a compact application icon using only built-in .NET drawing APIs.
Add-Type -AssemblyName System.Drawing
$bitmap = New-Object System.Drawing.Bitmap 64, 64
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$stream = $null
$icon = $null
try {
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::FromArgb(17, 24, 39))
    $greenBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(84, 214, 167))
    $whitePen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 4
    try {
        $graphics.FillEllipse($greenBrush, 8, 8, 48, 48)
        $graphics.DrawArc($whitePen, 18, 18, 28, 28, 200, 140)
        $graphics.DrawLine($whitePen, 32, 32, 43, 22)
    }
    finally {
        $greenBrush.Dispose()
        $whitePen.Dispose()
    }

    $iconHandle = $bitmap.GetHicon()
    $icon = [System.Drawing.Icon]::FromHandle($iconHandle)
    $stream = [System.IO.File]::Create($iconPath)
    $icon.Save($stream)
}
finally {
    if ($stream) { $stream.Dispose() }
    if ($icon) { $icon.Dispose() }
    $graphics.Dispose()
    $bitmap.Dispose()
}

$arguments = @(
    'publish', $projectPath,
    '-c', 'Release',
    '-r', 'win-x64',
    '-o', $publishPath,
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=None',
    '-p:DebugSymbols=false'
)

if ($FrameworkDependent) {
    $arguments += '--self-contained'
    $arguments += 'false'
}
else {
    $arguments += '--self-contained'
    $arguments += 'true'
}

& $dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "发布失败，dotnet 退出代码：$LASTEXITCODE"
}

Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.txt') -Destination $publishPath -Force

$exe = Join-Path $publishPath 'CpuNetWidget.exe'
Write-Host ''
Write-Host '编译完成：' -ForegroundColor Green
Write-Host $exe
