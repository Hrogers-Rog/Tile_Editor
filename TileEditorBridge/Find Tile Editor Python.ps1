param(
    [Version]$MinimumVersion = [Version]"3.10"
)

$ErrorActionPreference = "SilentlyContinue"
$candidates = New-Object System.Collections.Generic.List[string]
$seen = @{}

function Add-PythonCandidate {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) {
        return
    }
    $expanded = [Environment]::ExpandEnvironmentVariables(
        $Path.Trim().Trim('"')
    )
    $key = $expanded.ToLowerInvariant()
    if ($seen.ContainsKey($key)) {
        return
    }
    $seen[$key] = $true
    $candidates.Add($expanded)
}

# An advanced user or support helper can always provide an exact interpreter.
Add-PythonCandidate $env:TILE_EDITOR_PYTHON

# Prefer the official Python Launcher because it knows installs that are not
# on PATH. Resolve it to the real interpreter before adding the candidate.
$pyLauncher = Get-Command py.exe -CommandType Application
if ($pyLauncher) {
    $launcherPython = & $pyLauncher.Source -3 -c (
        "import sys; print(sys.executable)"
    ) 2>$null
    if ($LASTEXITCODE -eq 0) {
        Add-PythonCandidate ($launcherPython | Select-Object -First 1)
    }
}

# PATH applications, including portable distributions.
foreach ($commandName in @("python.exe", "python3.exe")) {
    foreach ($command in @(Get-Command $commandName -All -CommandType Application)) {
        Add-PythonCandidate $command.Source
    }
}

# Standard CPython registry entries, both per-user and machine-wide.
$registryRoots = @(
    "Registry::HKEY_CURRENT_USER\Software\Python\PythonCore",
    "Registry::HKEY_LOCAL_MACHINE\Software\Python\PythonCore",
    "Registry::HKEY_LOCAL_MACHINE\Software\WOW6432Node\Python\PythonCore"
)
foreach ($root in $registryRoots) {
    foreach ($installKey in @(Get-ChildItem $root -ErrorAction SilentlyContinue)) {
        $pathKey = Join-Path $installKey.PSPath "InstallPath"
        $pathItem = Get-Item $pathKey -ErrorAction SilentlyContinue
        if (!$pathItem) {
            continue
        }
        Add-PythonCandidate (
            $pathItem.GetValue("ExecutablePath")
        )
        $installDirectory = $pathItem.GetValue("")
        if (![string]::IsNullOrWhiteSpace($installDirectory)) {
            Add-PythonCandidate (
                Join-Path $installDirectory "python.exe"
            )
        }
    }
}

# Common locations used by python.org, Conda, Scoop, and portable installs.
$searchPatterns = @(
    (Join-Path $env:LOCALAPPDATA "Programs\Python\Python*\python.exe"),
    (Join-Path $env:LOCALAPPDATA "Microsoft\WindowsApps\python*.exe"),
    (Join-Path $env:USERPROFILE "scoop\apps\python\current\python.exe"),
    (Join-Path $env:USERPROFILE "miniconda3\python.exe"),
    (Join-Path $env:USERPROFILE "anaconda3\python.exe"),
    (Join-Path $env:ProgramData "Miniconda3\python.exe"),
    (Join-Path $env:ProgramData "Anaconda3\python.exe"),
    (Join-Path $env:ProgramFiles "Python*\python.exe"),
    "C:\Python3*\python.exe"
)
if (${env:ProgramFiles(x86)}) {
    $searchPatterns += Join-Path (
        ${env:ProgramFiles(x86)}
    ) "Python*\python.exe"
}
foreach ($pattern in $searchPatterns) {
    foreach ($match in @(
        Get-ChildItem -Path $pattern -File -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending
    )) {
        Add-PythonCandidate $match.FullName
    }
}

foreach ($candidate in $candidates) {
    if (!(Test-Path -LiteralPath $candidate -PathType Leaf)) {
        continue
    }
    $probeCode = 'import ensurepip,pathlib,struct,sys,venv;print(sys.version_info.major,sys.version_info.minor,sys.version_info.micro,struct.calcsize(chr(80))*8,pathlib.Path(sys.executable).resolve(),sep=chr(124))'
    $probe = & $candidate -c $probeCode 2>$null
    if ($LASTEXITCODE -ne 0 -or !$probe) {
        continue
    }
    $parts = ($probe | Select-Object -Last 1).Trim().Split("|")
    if ($parts.Count -ne 5) {
        continue
    }
    $version = $null
    $versionText = $parts[0] + "." + $parts[1] + "." + $parts[2]
    if (![Version]::TryParse($versionText, [ref]$version)) {
        continue
    }
    if ($version -lt $MinimumVersion -or $parts[3] -ne "64") {
        continue
    }
    Write-Output $parts[4]
    exit 0
}

Write-Error (
    "No compatible 64-bit Python {0} or newer installation was found." -f
    $MinimumVersion
)
exit 1
