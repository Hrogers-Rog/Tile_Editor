param(
    [string]$PythonExe = ""
)

$ErrorActionPreference = "Stop"
$sourceDir = $PSScriptRoot
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $sourceDir "..")).Path
$version = (Get-Content -LiteralPath (Join-Path $sourceDir "VERSION.txt") -Raw).Trim()
$packageId = "Hrogers.TileEditorBridge"
$baseStage = Join-Path $repoRoot "dist\releases\$version\$packageId"
$portableRoot = Join-Path $repoRoot "dist\downloads\$version-portable-windows-x64"
$portableStage = Join-Path $portableRoot $packageId
$zipPath = Join-Path $portableRoot "Hrogers.TileEditorSuite-$version-Portable-Windows-x64.zip"

if (!(Test-Path -LiteralPath $baseStage -PathType Container)) {
    throw "Build the normal $version release first; missing '$baseStage'."
}
if (Test-Path -LiteralPath $portableRoot) {
    throw "Portable download $version already exists at '$portableRoot'."
}
if (![Environment]::Is64BitProcess) {
    throw "The portable package must be built from a 64-bit PowerShell process."
}
if ([string]::IsNullOrWhiteSpace($PythonExe)) {
    $finder = Join-Path $sourceDir "Find Tile Editor Python.ps1"
    $PythonExe = & $finder | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($PythonExe) -or
    !(Test-Path -LiteralPath $PythonExe -PathType Leaf)) {
    throw "A compatible 64-bit Python interpreter was not found."
}

& $PythonExe -c "import PyInstaller,pygame,numpy,PIL,requests,scipy"
if ($LASTEXITCODE -ne 0) {
    throw "PyInstaller and all Tile Editor dependencies must be installed in the build interpreter."
}

$buildTag = "$version-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
$buildRoot = Join-Path $repoRoot "dist\portable-builds\$buildTag"
$buildDist = Join-Path $buildRoot "dist"
$buildWork = Join-Path $buildRoot "work"
$buildSpec = Join-Path $buildRoot "spec"
New-Item -ItemType Directory -Path $buildDist,$buildWork,$buildSpec -Force | Out-Null

Write-Host "[1/6] Freezing the desktop editor and Python runtime..."
Push-Location -LiteralPath $repoRoot
try {
    & $PythonExe -m PyInstaller `
        --noconfirm `
        --clean `
        --onedir `
        --console `
        --name TileEditor `
        --distpath $buildDist `
        --workpath $buildWork `
        --specpath $buildSpec `
        --paths $repoRoot `
        --hidden-import scipy.ndimage `
        --collect-data certifi `
        (Join-Path $repoRoot "tile_editor_portable.py")
}
finally {
    Pop-Location
}
if ($LASTEXITCODE -ne 0) {
    throw "PyInstaller failed."
}

$frozenDir = Join-Path $buildDist "TileEditor"
$frozenExe = Join-Path $frozenDir "TileEditor.exe"
if (!(Test-Path -LiteralPath $frozenExe -PathType Leaf)) {
    throw "Frozen TileEditor.exe was not produced."
}

Write-Host "[2/6] Running the frozen-runtime smoke test..."
& $frozenExe --portable-smoke-test
if ($LASTEXITCODE -ne 0) {
    throw "The frozen Tile Editor runtime failed its smoke test."
}

Write-Host "[3/6] Assembling the portable mod package..."
New-Item -ItemType Directory -Path $portableStage -Force | Out-Null
Copy-Item -Path (Join-Path $baseStage "*") -Destination $portableStage -Recurse -Force
Copy-Item -LiteralPath (Join-Path $sourceDir "Launch Tile Editor.bat") `
    -Destination (Join-Path $portableStage "Launch Tile Editor.bat") -Force
$runtimeDir = Join-Path $portableStage "TileEditor\PortableRuntime"
New-Item -ItemType Directory -Path $runtimeDir -Force | Out-Null
Copy-Item -Path (Join-Path $frozenDir "*") -Destination $runtimeDir -Recurse -Force
Copy-Item -LiteralPath (Join-Path $sourceDir "PORTABLE_RUNTIME_NOTICES.md") `
    -Destination (Join-Path $runtimeDir "PORTABLE_RUNTIME_NOTICES.md") -Force

Write-Host "[4/6] Collecting runtime license notices..."
$licenseDir = Join-Path $runtimeDir "licenses"
New-Item -ItemType Directory -Path $licenseDir -Force | Out-Null
$pythonRoot = (& $PythonExe -c "import sys; print(sys.base_prefix)" | Select-Object -Last 1).Trim()
$sitePackages = (& $PythonExe -c "import sysconfig; print(sysconfig.get_path('purelib'))" | Select-Object -Last 1).Trim()
$pythonLicense = Join-Path $pythonRoot "LICENSE.txt"
if (Test-Path -LiteralPath $pythonLicense) {
    Copy-Item -LiteralPath $pythonLicense -Destination (Join-Path $licenseDir "CPython-LICENSE.txt")
}
$distributionPatterns = @(
    "pygame_ce-*.dist-info",
    "numpy-*.dist-info",
    "pillow-*.dist-info",
    "requests-*.dist-info",
    "scipy-*.dist-info",
    "pyinstaller-*.dist-info",
    "certifi-*.dist-info",
    "charset_normalizer-*.dist-info",
    "idna-*.dist-info",
    "urllib3-*.dist-info"
)
foreach ($pattern in $distributionPatterns) {
    foreach ($distribution in @(Get-ChildItem -Path (Join-Path $sitePackages $pattern) -Directory)) {
        $destination = Join-Path $licenseDir $distribution.Name
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        $metadata = Join-Path $distribution.FullName "METADATA"
        if (Test-Path -LiteralPath $metadata) {
            Copy-Item -LiteralPath $metadata -Destination $destination
        }
        Get-ChildItem -LiteralPath $distribution.FullName -Recurse -File |
            Where-Object { $_.Name -match '^(LICENSE|COPYING|NOTICE)' } |
            ForEach-Object {
                $relative = $_.FullName.Substring($distribution.FullName.Length + 1)
                $target = Join-Path $destination $relative
                New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
                Copy-Item -LiteralPath $_.FullName -Destination $target -Force
            }
    }
}

$runtimeReport = & $frozenExe --portable-smoke-test
$runtimeReport | Set-Content -LiteralPath (Join-Path $runtimeDir "RUNTIME_VERSION.txt") -Encoding UTF8

Write-Host "[5/6] Auditing and checksumming the portable download..."
$privateTokens = @($repoRoot,$env:USERPROFILE) |
    Where-Object { ![string]::IsNullOrWhiteSpace($_) } |
    Select-Object -Unique
$privateMatches = New-Object System.Collections.Generic.List[string]
Get-ChildItem -LiteralPath $portableStage -Recurse -File | ForEach-Object {
    $content = [System.Text.Encoding]::ASCII.GetString(
        [System.IO.File]::ReadAllBytes($_.FullName))
    foreach ($token in $privateTokens) {
        if ($content.IndexOf($token,[System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            $relative = $_.FullName.Substring($portableStage.Length + 1)
            $privateMatches.Add("$relative -> $token")
        }
    }
}
if ($privateMatches.Count -gt 0) {
    throw "Portable download contains build-machine paths:`n$($privateMatches -join "`n")"
}

$checksumPath = Join-Path $portableStage "checksums.sha256"
$checksumLines = Get-ChildItem -LiteralPath $portableStage -Recurse -File |
    Where-Object { $_.FullName -ne $checksumPath } |
    Sort-Object FullName |
    ForEach-Object {
        $relative = $_.FullName.Substring($portableStage.Length + 1).Replace("\", "/")
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $relative"
    }
$checksumLines | Set-Content -LiteralPath $checksumPath -Encoding ASCII

Write-Host "[6/6] Creating the downloadable zip..."
Compress-Archive -LiteralPath $portableStage -DestinationPath $zipPath

$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host ""
Write-Host "Portable folder: $portableStage"
Write-Host "Portable zip:    $zipPath"
Write-Host "SHA256:          $zipHash"
