param(
    [string]$GameDir = "",
    [switch]$Deploy
)

$ErrorActionPreference = "Stop"
$sourceDir = $PSScriptRoot
$repoRoot = (Resolve-Path (Join-Path $sourceDir "..")).Path
$version = (Get-Content -LiteralPath (Join-Path $sourceDir "VERSION.txt") -Raw).Trim()
$packageId = "Hrogers.TileEditorBridge"
$releaseRoot = Join-Path $repoRoot "dist\releases\$version"
$stageDir = Join-Path $releaseRoot $packageId
$zipPath = Join-Path $releaseRoot "Hrogers.TileEditorSuite-$version.zip"

if ([string]::IsNullOrWhiteSpace($version)) {
    throw "VERSION.txt is empty."
}

$info = Get-Content -LiteralPath (Join-Path $sourceDir "Info.json") -Raw | ConvertFrom-Json
$manifest = Get-Content -LiteralPath (Join-Path $sourceDir "PackageManifest.json") -Raw | ConvertFrom-Json
if ($info.Version -ne $version -or $manifest.version -ne $version) {
    throw "VERSION.txt, Info.json, and PackageManifest.json must use the same version."
}

if ([string]::IsNullOrWhiteSpace($GameDir)) {
    $defaultGameDir = "C:\Steam\steamapps\common\Railroader"
    if (Test-Path -LiteralPath (Join-Path $defaultGameDir "Railroader.exe")) {
        $GameDir = $defaultGameDir
    }
}

if ([string]::IsNullOrWhiteSpace($GameDir)) {
    throw "Specify -GameDir with the Railroader installation path."
}

$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
$managedDir = Join-Path $GameDir "Railroader_Data\Managed"
$ummDir = Join-Path $managedDir "UnityModManager"
if (!(Test-Path -LiteralPath (Join-Path $GameDir "Railroader.exe"))) {
    throw "Railroader.exe was not found in '$GameDir'."
}
if (!(Test-Path -LiteralPath (Join-Path $ummDir "UnityModManager.dll"))) {
    throw "UnityModManager.dll was not found in '$ummDir'."
}

if (Test-Path -LiteralPath $releaseRoot) {
    throw "Release $version already exists at '$releaseRoot'. Bump VERSION.txt before building another immutable release."
}

$pythonFinder = Join-Path $sourceDir "Find Tile Editor Python.ps1"
$pythonExe = & $pythonFinder | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($pythonExe)) {
    throw "A compatible 64-bit Python 3.10 or newer installation was not found."
}

Write-Host "[1/5] Running Python regression tests..."
$testExitCode = 1
Push-Location -LiteralPath $repoRoot
try {
    & $pythonExe -m unittest discover -s "tests"
    $testExitCode = $LASTEXITCODE
}
finally {
    Pop-Location
}
if ($testExitCode -ne 0) {
    throw "Python regression tests failed."
}

Write-Host "[2/5] Building UMM bridge..."
$projectPath = Join-Path $sourceDir "Hrogers.TileEditorBridge.csproj"
& dotnet build $projectPath -c Release "-p:GameDir=$GameDir"
if ($LASTEXITCODE -ne 0) {
    throw "UMM bridge build failed."
}

Write-Host "[3/5] Assembling complete mod folder..."
New-Item -ItemType Directory -Path $stageDir -Force | Out-Null
$tileEditorDir = Join-Path $stageDir "TileEditor"
New-Item -ItemType Directory -Path $tileEditorDir -Force | Out-Null

$bridgeOutput = Join-Path $sourceDir "bin\Release\net48\Hrogers.TileEditorBridge.dll"
Copy-Item -LiteralPath $bridgeOutput -Destination $stageDir
foreach ($name in @(
    "Info.json",
    "VERSION.txt",
    "PackageManifest.json",
    "README.md",
    "CHANGELOG.md",
    "Launch Tile Editor.bat",
    "Repair Tile Editor Environment.bat",
    "Find Tile Editor Python.ps1"
)) {
    Copy-Item -LiteralPath (Join-Path $sourceDir $name) -Destination $stageDir
}

foreach ($packageName in @("edit_tiles", "mod_project")) {
    $sourcePackage = Join-Path $repoRoot $packageName
    $targetPackage = Join-Path $tileEditorDir $packageName
    New-Item -ItemType Directory -Path $targetPackage -Force | Out-Null
    Get-ChildItem -LiteralPath $sourcePackage -File -Filter "*.py" |
        Copy-Item -Destination $targetPackage
}

foreach ($name in @(
    "railroader_bridge.py",
    "osm_to_graph.py",
    "requirements.txt",
    "run_editor.bat",
    "README.md",
    "TRACK_TOOL_ROADMAP.md",
    "HunterR_Map_Editor_Guide.pdf"
)) {
    Copy-Item -LiteralPath (Join-Path $repoRoot $name) -Destination $tileEditorDir
}

# Public release files must not contain the build machine's user or source
# directory. Release builds omit PDB metadata, and this audit fails closed if
# a future build setting reintroduces a local absolute path.
$privatePathTokens = @(
    $repoRoot,
    $env:USERPROFILE
) | Where-Object {
    -not [string]::IsNullOrWhiteSpace($_)
} | Select-Object -Unique
$privatePathMatches = New-Object System.Collections.Generic.List[string]
Get-ChildItem -LiteralPath $stageDir -Recurse -File |
    ForEach-Object {
        $content = [System.Text.Encoding]::ASCII.GetString(
            [System.IO.File]::ReadAllBytes($_.FullName))
        foreach ($token in $privatePathTokens) {
            if ($content.IndexOf(
                    $token,
                    [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
                $relative = $_.FullName.Substring($stageDir.Length + 1)
                $privatePathMatches.Add("$relative -> $token")
            }
        }
    }
if ($privatePathMatches.Count -gt 0) {
    throw "Release contains build-machine paths:`n$($privatePathMatches -join "`n")"
}

$checksumPath = Join-Path $stageDir "checksums.sha256"
$checksumLines = Get-ChildItem -LiteralPath $stageDir -Recurse -File |
    Where-Object { $_.FullName -ne $checksumPath } |
    Sort-Object FullName |
    ForEach-Object {
        $relative = $_.FullName.Substring($stageDir.Length + 1).Replace("\", "/")
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $relative"
    }
$checksumLines | Set-Content -LiteralPath $checksumPath -Encoding ASCII

Write-Host "[4/5] Creating versioned zip..."
Compress-Archive -LiteralPath $stageDir -DestinationPath $zipPath

if ($Deploy) {
    Write-Host "[5/5] Deploying release to live Mods folder..."
    $modsDir = (Resolve-Path -LiteralPath (Join-Path $GameDir "Mods")).Path
    $deployDir = Join-Path $modsDir $packageId
    if (Test-Path -LiteralPath $deployDir) {
        $backupRoot = Join-Path $modsDir "_TileEditorBridge_Backups"
        New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
        $backupName = "$packageId-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
        $backupDir = Join-Path $backupRoot $backupName
        New-Item -ItemType Directory -Path $backupDir -Force | Out-Null

        # Back up packaged files without moving the live directory. The
        # desktop editor may be running from TileEditor\.venv, and moving its
        # parent can otherwise leave the installed mod only partly present.
        Get-ChildItem -LiteralPath $deployDir -Recurse -File |
            Where-Object {
                $_.FullName -notmatch '[\\/]\.venv[\\/]' -and
                $_.FullName -notmatch '[\\/]__pycache__[\\/]'
            } |
            ForEach-Object {
                $relative = $_.FullName.Substring($deployDir.Length + 1)
                $target = Join-Path $backupDir $relative
                New-Item -ItemType Directory -Path (
                    Split-Path -Parent $target
                ) -Force | Out-Null
                Copy-Item -LiteralPath $_.FullName -Destination $target -Force
            }
    } else {
        New-Item -ItemType Directory -Path $deployDir -Force | Out-Null
    }

    $resolvedDeployDir = [System.IO.Path]::GetFullPath($deployDir)
    $resolvedModsDir = [System.IO.Path]::GetFullPath($modsDir).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar)
    if (!$resolvedDeployDir.StartsWith(
            $resolvedModsDir + [System.IO.Path]::DirectorySeparatorChar,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean cache files outside the Railroader Mods folder."
    }
    $activeRailroaderPids = @(
        Get-Process -Name "Railroader" -ErrorAction SilentlyContinue |
            ForEach-Object { $_.Id.ToString() }
    )
    Get-ChildItem -LiteralPath $resolvedDeployDir -File `
        -Filter "Hrogers.TileEditorBridge.dll.*.cache" `
        -ErrorAction SilentlyContinue |
        ForEach-Object {
            $cachePid = ""
            if ($_.Name -match '\.(\d+)\.cache$') {
                $cachePid = $Matches[1]
            }
            if ($activeRailroaderPids -contains $cachePid) {
                Write-Host "Preserving active UMM cache: $($_.Name)"
            } else {
                Remove-Item -LiteralPath $_.FullName -Force
            }
        }

    # Merge release files in place. This preserves the desktop virtual
    # environment while safely updating Python sources and the UMM assembly.
    Get-ChildItem -LiteralPath $stageDir -Recurse -File |
        ForEach-Object {
            $relative = $_.FullName.Substring($stageDir.Length + 1)
            $target = Join-Path $deployDir $relative
            New-Item -ItemType Directory -Path (
                Split-Path -Parent $target
            ) -Force | Out-Null
            Copy-Item -LiteralPath $_.FullName -Destination $target -Force
        }
} else {
    Write-Host "[5/5] Deployment skipped. Pass -Deploy to push to the live Mods folder."
}

Write-Host ""
Write-Host "Release folder: $stageDir"
Write-Host "Release zip:    $zipPath"
if ($Deploy) {
    Write-Host "Live mod:       $(Join-Path $GameDir "Mods\$packageId")"
}
