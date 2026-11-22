<#
.SYNOPSIS
    编译 HistoryDeleter，并（默认）部署到 <游戏目录>\mods\HistoryDeleter\。

.EXAMPLE
    ./build.ps1
    使用 Local.props 里的 Sts2Dir。

.EXAMPLE
    ./build.ps1 -Sts2Dir "D:\Steam\steamapps\common\Slay the Spire 2"

.EXAMPLE
    ./build.ps1 -SkipDeploy
    只编译，不往游戏目录写文件（适合 CI）。
#>
param(
    [string]$Sts2Dir = $env:STS2_DIR,
    [switch]$SkipDeploy
)

$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'HistoryDeleter'
$dotnetArgs = @('build', $project)

if ($Sts2Dir) {
    $dotnetArgs += "-p:Sts2Dir=$Sts2Dir"
}

if ($SkipDeploy) {
    $dotnetArgs += '-p:SkipDeploy=true'
}

dotnet @dotnetArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build 失败（exit $LASTEXITCODE）"
}
