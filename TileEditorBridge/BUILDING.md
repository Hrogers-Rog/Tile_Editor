# Building a versioned complete release

`VERSION.txt`, `Info.json`, `PackageManifest.json`, `SuiteVersion.cs`, and the
version properties in `Hrogers.TileEditorBridge.csproj` identify a release.
Keep them synchronized when incrementing the version.

Build an immutable package:

```powershell
.\package_complete_mod.ps1 -GameDir "C:\Steam\steamapps\common\Railroader"
```

Build and deploy that exact package to the live game:

```powershell
.\package_complete_mod.ps1 `
  -GameDir "C:\Steam\steamapps\common\Railroader" `
  -Deploy
```

The script runs the Python regression tests, builds the UMM DLL against the
installed game, creates the documented folder structure, writes SHA-256
checksums, creates a versioned zip, and optionally deploys it. Existing live
installations are moved into `Mods\_TileEditorBridge_Backups` before update.

Release directories are immutable. Increment the version before rebuilding a
release that already exists.
