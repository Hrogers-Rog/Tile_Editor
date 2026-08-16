<#
.SYNOPSIS
    Mirrors the user-facing docs/ pages into the Tile Editor GitHub Wiki.

.DESCRIPTION
    The repo is the source of truth. This script clones (or updates) the wiki
    repo, copies docs/ into it under wiki page names, rewrites relative Markdown
    links to wiki links, regenerates Home and the sidebar, then commits and
    pushes.

    Run this after merging a docs change to main.

.PARAMETER DryRun
    Build the wiki content and report the diff without committing or pushing.

.EXAMPLE
    .\scripts\Sync-Wiki.ps1 -DryRun
    .\scripts\Sync-Wiki.ps1
#>
[CmdletBinding()]
param(
    [string] $WikiUrl = 'https://github.com/Hrogers-Rog/Tile_Editor.wiki.git',
    [string] $WorkDir = (Join-Path $env:TEMP 'tile-editor-wiki-sync'),
    [switch] $DryRun
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$docsDir = Join-Path $repoRoot 'docs'
$blobBase = 'https://github.com/Hrogers-Rog/Tile_Editor/blob/main'

# docs/ file -> wiki page name. Order drives the sidebar.
$pageMap = [ordered]@{
    'GETTING_STARTED.md'  = 'Getting-Started'
    'KEYBINDS.md'         = 'Keybind-Reference'
    'TERRAIN_EDITING.md'  = 'Terrain-Editing'
    'TRACK_EDITING.md'    = 'Track-Editing'
    'MOD_TOOLS.md'        = 'Mod-Tools'
    'IN_GAME_GEO.md'      = 'In-Game-Geo-Workspace'
    'SCHEMA_EXAMPLES.md'  = 'Data-Formats-And-Examples'
}

$sections = [ordered]@{
    'Start here' = @('Getting-Started', 'Keybind-Reference')
    'Editing'    = @('Terrain-Editing', 'Track-Editing', 'Mod-Tools', 'In-Game-Geo-Workspace')
    'Data'       = @('Data-Formats-And-Examples')
}

function Convert-Links {
    param([string] $Text)

    # Links that escape docs/ (../README.md, ../TRACK_TOOL_ROADMAP.md, ...)
    $Text = [regex]::Replace($Text, '\]\(\.\./([^)#]+)(#[^)]*)?\)', {
        param($m)
        "]($blobBase/$($m.Groups[1].Value)$($m.Groups[2].Value))"
    })

    # Sibling docs/ links -> wiki pages
    $Text = [regex]::Replace($Text, '\]\(([A-Za-z0-9_]+\.md)(#[^)]*)?\)', {
        param($m)
        $file = $m.Groups[1].Value
        $anchor = $m.Groups[2].Value
        if ($pageMap.Contains($file)) { "]($($pageMap[$file])$anchor)" }
        else { "]($blobBase/docs/$file$anchor)" }
    })

    return $Text
}

Write-Host 'Tile Editor wiki sync' -ForegroundColor Cyan

if (Test-Path $WorkDir) {
    Write-Host "Updating existing checkout at $WorkDir"
    git -C $WorkDir fetch --quiet origin
    git -C $WorkDir reset --quiet --hard origin/master
}
else {
    Write-Host "Cloning wiki into $WorkDir"
    git clone --quiet $WikiUrl $WorkDir
    if ($LASTEXITCODE -ne 0) {
        throw "Could not clone $WikiUrl. Create the wiki first by adding one page through the GitHub UI - GitHub does not create the wiki repo until it has a page."
    }
}

Get-ChildItem -Path $WorkDir -Filter '*.md' -File | Remove-Item -Force

$copied = 0
foreach ($entry in $pageMap.GetEnumerator()) {
    $source = Join-Path $docsDir $entry.Key
    if (-not (Test-Path $source)) {
        Write-Warning "Missing source doc: $($entry.Key) - skipped."
        continue
    }

    $body = Convert-Links (Get-Content -Raw -Path $source)
    $footer = @"

---

*Mirrored from [``docs/$($entry.Key)``]($blobBase/docs/$($entry.Key)) - edit there, not here.*
"@
    Set-Content -Path (Join-Path $WorkDir "$($entry.Value).md") -Value ($body.TrimEnd() + "`n" + $footer) -Encoding utf8
    $copied++
}

# NOTE: not $home - that is a read-only PowerShell automatic variable.
$homePage = @"
# Railroader Tile Editor

A Python desktop terrain and map editor for Railroader mods, paired with an
in-game **F9** workspace that edits the same data live.

**New here?** Start with [Getting Started](Getting-Started), then keep
[the keybind reference](Keybind-Reference) open.

## Start here

- [Getting Started](Getting-Started) - install, run, first session
- [Keybind Reference](Keybind-Reference) - every keyboard and mouse binding

## Editing

- [Terrain Editing](Terrain-Editing) - brushes, clamps, selection, tile generation, OSM
- [Track Editing](Track-Editing) - nodes, segments, connecting, gauge, geometry tools
- [Mod Tools](Mod-Tools) - layers, progression, areas, scenery, spans, calculators
- [In-Game Geo Workspace](In-Game-Geo-Workspace) - the F9 editor, signals, two-way sync

## Data

- [Data Formats And Examples](Data-Formats-And-Examples) - every JSON format, with worked examples

The runtimes that consume this data - ``Hrogers.CrossingRuntime`` for grade
crossings, Railroad Operations for signals - do **not** require the Tile Editor.
Players install the small runtime; you ship the JSON inside your map mod.

## Offline manuals

- [Tile Editor User Manual (PDF)]($blobBase/docs/pdf/Tile-Editor-User-Manual.pdf)
- [Tile Editor Modding Guide (PDF)]($blobBase/docs/pdf/Tile-Editor-Modding-Guide.pdf)

## Related projects

- [FUSE](https://github.com/F-U-S-E-E/FuseDevelopmentGroup) - the modding layer
- [FUSE Narrow Gauge](https://github.com/Hrogers-Rog/Narrow_Gauge) - narrow and dual-gauge rendering
- [Toolshed](https://github.com/Hrogers-Rog/TheToolShed) - service facilities and operations

## Project

- [Repository](https://github.com/Hrogers-Rog/Tile_Editor)
- [Issues](https://github.com/Hrogers-Rog/Tile_Editor/issues)

---

*This wiki is generated from ``docs/`` in the repository. Edits made here are
overwritten on the next sync - change the repo instead.*
"@
Set-Content -Path (Join-Path $WorkDir 'Home.md') -Value $homePage -Encoding utf8

$sidebar = New-Object System.Text.StringBuilder
[void]$sidebar.AppendLine('**[Tile Editor](Home)**')
foreach ($section in $sections.GetEnumerator()) {
    [void]$sidebar.AppendLine()
    [void]$sidebar.AppendLine("**$($section.Key)**")
    [void]$sidebar.AppendLine()
    foreach ($page in $section.Value) {
        [void]$sidebar.AppendLine("- [$($page -replace '-', ' ')]($page)")
    }
}
Set-Content -Path (Join-Path $WorkDir '_Sidebar.md') -Value $sidebar.ToString() -Encoding utf8

Write-Host "Wrote $copied page(s) plus Home and _Sidebar." -ForegroundColor Green

Push-Location $WorkDir
try {
    $status = git status --porcelain
    if (-not $status) {
        Write-Host 'Wiki already up to date - nothing to push.' -ForegroundColor Green
        return
    }

    Write-Host 'Changes:'
    git -c color.status=always status --short

    if ($DryRun) {
        Write-Host "`nDry run - nothing committed or pushed." -ForegroundColor Yellow
        return
    }

    $sha = (git -C $repoRoot rev-parse --short HEAD)
    git add -A
    git commit --quiet -m "docs: sync wiki from repo @ $sha"
    git push --quiet origin HEAD
    Write-Host "Pushed wiki update (source $sha)." -ForegroundColor Green
}
finally {
    Pop-Location
}
