param(
    [string]$GameDir = "C:\Steam\steamapps\common\Railroader",
    [switch]$Deploy
)

$ErrorActionPreference = "Stop"
$sourceDir = $PSScriptRoot
$repoRoot = (Resolve-Path (Join-Path $sourceDir "..")).Path
$version = "1.7.0"
$packageId = "Hrogers.SignalRuntime"
$releaseRoot = Join-Path $repoRoot "dist\signal-runtime\$version"
$stageDir = Join-Path $releaseRoot $packageId
$zipPath = Join-Path $releaseRoot "$packageId-$version.zip"

if (Test-Path -LiteralPath $releaseRoot) {
    throw "Signal Runtime release $version already exists."
}

& dotnet build (Join-Path $sourceDir "Hrogers.SignalRuntime.csproj") `
    -c Release "-p:GameDir=$GameDir"
if ($LASTEXITCODE -ne 0) {
    throw "Signal Runtime build failed."
}

New-Item -ItemType Directory -Path $stageDir -Force | Out-Null
$output = Join-Path $sourceDir "bin\Release\net48"
foreach ($name in @(
    "Hrogers.SignalRuntime.dll",
    "Info.json",
    "README.md",
    "ctc-system.schema.json"
)) {
    Copy-Item -LiteralPath (Join-Path $output $name) `
        -Destination $stageDir -Force
}
Compress-Archive -LiteralPath $stageDir -DestinationPath $zipPath

if ($Deploy) {
    $deployDir = Join-Path $GameDir "Mods\$packageId"
    New-Item -ItemType Directory -Path $deployDir -Force | Out-Null
    Get-ChildItem -LiteralPath $stageDir -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $deployDir -Force
    }
}

Write-Host "Signal Runtime folder: $stageDir"
Write-Host "Signal Runtime zip:    $zipPath"
if ($Deploy) {
    Write-Host "Live mod:              $deployDir"
}
