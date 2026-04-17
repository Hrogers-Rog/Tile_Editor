"""mod_project.codegen
C# template generation: generate_csharp_template, generate_harmony_patch,
generate_umm_entry, bulletin_manifest_json.
"""

import json
import uuid
from pathlib import Path

from .layer import _save_json


def bulletin_manifest_json(entries: list) -> str:
    """Generate the JSON content for a mod's updateUrl endpoint.

    BulletinManifest.Entries is an array of BulletinEntry objects.
    Each entry can target specific mod versions and display a message.

    entries -- list of dicts, each with:
      'message'       -- str  -- notification text shown to users
      'mods'          -- list of {'id': str, 'notBefore': str, 'notAfter': str}
                        (which mod versions this entry applies to)
      'requires'      -- list of {'id': str, ...}  (optional)
      'conflictsWith' -- list of {'id': str, ...}  (optional)
      'forceFail'     -- bool -- if True, causes mod loading to fail (use for
                        critical incompatibilities)
      'url'           -- str  -- optional link shown alongside the message

    Example entry:
      {
        'message': 'Version 2.0 is available with major improvements!',
        'mods': [{'id': 'MyMod', 'notAfter': '1.9.99'}],
        'url': 'https://example.com/mymods/releases',
      }

    Returns a formatted JSON string to serve from your updateUrl endpoint.
    Host this at the https:// URL specified in Definition.json 'updateUrl'.
    """
    bulletin = {'entries': []}
    for e in entries:
        entry: dict = {'message': e.get('message', '')}
        if e.get('mods'):
            entry['mods'] = e['mods']
        if e.get('requires'):
            entry['requires'] = e['requires']
        if e.get('conflictsWith'):
            entry['conflictsWith'] = e['conflictsWith']
        if e.get('forceFail'):
            entry['forceFail'] = True
        if e.get('url'):
            entry['url'] = e['url']
        bulletin['entries'].append(entry)
    return json.dumps(bulletin, indent=2, ensure_ascii=False)


def generate_csharp_template(folder: Path, mod_id: str, mod_name: str,
                              author: str = 'Author',
                              version: str = '1.0.0',
                              include_example_patch: bool = True,
                              requirements: list = None):
    """Generate a complete UMM C# mod scaffold.

    Creates:
      Info.json                 -- UMM mod metadata (replaces Railloader Definition.json)
      UMM/Mod.cs                -- UMM entry point (Load/Unload/OnGUI/OnSaveGUI)
      UMM/Settings.cs           -- settings class (XML persistence via UMM)
      Patches/ExamplePatch.cs   -- example Harmony patch (if include_example_patch)
      Properties/AssemblyInfo.cs
      {mod_id}.csproj           -- references game DLLs and UnityModManager

    Loader: UMM (Unity Mod Manager) -- confirmed working in Railroader.
    Entry point pattern from AlinasUtils/UMM/Mod.cs (Alina's UMM implementation).
    Harmony patterns from 0Harmony source + Mapeditor/AlinasMapMod reference mods.

    Covers Section E (Harmony) and D13 (template generation).
    """
    folder.mkdir(parents=True, exist_ok=True)
    safe_id = mod_id.replace('.', '_').replace('-', '_')

    # Generate UMM entry point (E1/E11/E14/E15 + Info.json)
    generate_umm_entry(folder, mod_id, mod_name,
                       author=author, version=version,
                       requirements=requirements)

    # Example Harmony patch (E2/E3/E4/E7/E12/E13)
    if include_example_patch:
        patches_dir = folder / 'Patches'
        patches_dir.mkdir(exist_ok=True)
        generate_harmony_patch(patches_dir, mod_id,
                                target_class='Track.Graph',
                                target_method='RebuildCollections',
                                patch_type='prefix',
                                class_name='GraphRebuildCollectionsPatch')

    # --- Properties/AssemblyInfo.cs ---
    props_dir = folder / 'Properties'
    props_dir.mkdir(exist_ok=True)
    import uuid as _uuid
    assembly_cs = f'''using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("{mod_name}")]
[assembly: AssemblyVersion("{version}")]
[assembly: AssemblyFileVersion("{version}")]
[assembly: Guid("{_uuid.uuid4()}")]
'''
    (props_dir / 'AssemblyInfo.cs').write_text(assembly_cs, encoding='utf-8')

    # --- .csproj -- UMM-based, references UnityModManager instead of Railloader ---
    csproj = f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <AssemblyName>{mod_id}</AssemblyName>
    <RootNamespace>{safe_id}</RootNamespace>
  </PropertyGroup>

  <!-- Adjust GameDir to your Railroader installation path -->
  <PropertyGroup>
    <GameDir Condition="$(GameDir) == ''">/path/to/Railroader</GameDir>
  </PropertyGroup>

  <ItemGroup Label="Game references (adjust GameDir)">
    <Reference Include="Assembly-CSharp">
      <HintPath>$(GameDir)/Railroader_Data/Managed/Assembly-CSharp.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityModManager">
      <HintPath>$(GameDir)/Mods/UnityModManager/UnityModManager.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="0Harmony">
      <HintPath>$(GameDir)/Railroader_Data/Managed/0Harmony.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="Serilog">
      <HintPath>$(GameDir)/Railroader_Data/Managed/Serilog.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.CoreModule">
      <HintPath>$(GameDir)/Railroader_Data/Managed/UnityEngine.CoreModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="GalaSoft.MvvmLight.Messaging">
      <HintPath>$(GameDir)/Railroader_Data/Managed/GalaSoft.MvvmLight.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
'''
    (folder / f'{mod_id}.csproj').write_text(csproj, encoding='utf-8')

    print(f"[csharp] scaffold written to {folder}")
    return folder



# ---------------------------------------------------------------------------
# E1-E16 -- Harmony patch template generation
# ---------------------------------------------------------------------------

# E14: Harmony ID convention for Railroader mods.
# Format: "Author.ModId" matching the UMM mod Id in Info.json.
# Confirmed from AlinasUtils (UMM/Mod.cs): new Harmony(modEntry.Info.Id)
# and Mapeditor (EditorMod.cs): new Harmony("AlinaNova21.AlinasMapMod.Editor")
# Always use modEntry.Info.Id from UMM rather than a hardcoded string.

def generate_harmony_patch(folder: Path, mod_id: str,
                            target_class: str, target_method: str,
                            patch_type: str = 'postfix',
                            class_name: str = None) -> Path:
    """Generate a Harmony patch class file.

    Covers E1-E16 -- all confirmed from 0Harmony source and reference mods.

    target_class  -- fully qualified game class, e.g. 'Track.TrackSegment'
    target_method -- method name, e.g. 'RebuildBezier'
    patch_type    -- 'prefix', 'postfix', 'transpiler', or 'property_getter'
    class_name    -- output class name; defaults to TargetClassTargetMethod

    Patch patterns confirmed from:
      - Mapeditor/HarmonyPatches/TrackSegmentRebuildBezier.cs  (Postfix)
      - Mapeditor/HarmonyPatches/PatchEditorAddOrUpdateScenery.cs (Prefix, return false)
      - AlinasMapMod/Patches/GraphRebuildCollections.cs  (Prefix, category)
      - AlinasMapMod/Map/Patches.cs  (method-level attributes, ref __result)
    """
    import uuid as _uuid
    folder.mkdir(parents=True, exist_ok=True)
    safe_id   = mod_id.replace('.', '_').replace('-', '_')
    short_cls = target_class.split('.')[-1]
    cls_name  = class_name or f"{short_cls}{target_method}Patch"

    if patch_type == 'prefix':
        patch_body = f'''    // E3: Prefix -- runs BEFORE the original method.
    // Return false to SKIP the original; return true to RUN it.
    // Use __instance to access the target object (instance methods only).
    // Use ref __result to override the return value when skipping.
    //
    // Confirmed pattern from Mapeditor/HarmonyPatches/PatchEditorAddOrUpdateScenery.cs
    [HarmonyPrefix]
    private static bool Prefix({short_cls} __instance)
    {{
        // TODO: implement patch logic
        // return false;  // skip original
        return true;      // run original
    }}'''

    elif patch_type == 'postfix':
        patch_body = f'''    // E4: Postfix -- runs AFTER the original method.
    // __instance is the target object (instance methods only).
    // Use ref __result to modify the return value.
    //
    // Confirmed pattern from Mapeditor/HarmonyPatches/TrackSegmentRebuildBezier.cs
    // and AlinasMapMod/Map/Patches.cs (ref __result variant)
    [HarmonyPostfix]
    private static void Postfix({short_cls} __instance)
    {{
        // TODO: implement patch logic
    }}'''

    elif patch_type == 'transpiler':
        patch_body = f'''    // E5: Transpiler -- modifies IL instructions directly.
    // Most powerful but complex. Use CodeMatcher for readable transpilers.
    // E6: CodeMatcher -- fluent API confirmed from 0Harmony/HarmonyLib/CodeMatcher.cs
    //
    // CodeMatcher methods used in practice:
    //   .MatchForward(useEnd, params CodeMatch[])  -- search forward from current pos
    //   .MatchBack(useEnd, params CodeMatch[])     -- search backward
    //   .Insert(params CodeInstruction[])           -- insert before current pos
    //   .SetInstruction(CodeInstruction)            -- replace current instruction
    //   .SetInstructionAndAdvance(CodeInstruction)  -- replace and move forward
    //   .RemoveInstruction()                        -- delete current instruction
    //   .ThrowIfNotMatch(explanation, matches)      -- assert match or throw
    //   .InstructionEnumeration()                   -- return final instruction list
    //
    // CodeMatch(OpCode, operand, name) -- match by opcode, optional operand/name
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions)
    {{
        return new CodeMatcher(instructions)
            // Example: find a specific method call and replace it
            .MatchForward(false,
                new CodeMatch(OpCodes.Call,
                    AccessTools.Method(typeof({short_cls}), "{target_method}")))
            .ThrowIfNotMatch("Could not find {target_method} call")
            // .SetInstruction(new CodeInstruction(OpCodes.Call,
            //     AccessTools.Method(typeof(MyClass), "MyReplacement")))
            .InstructionEnumeration();
    }}'''

    elif patch_type == 'property_getter':
        patch_body = f'''    // E10: Property getter patch.
    // MethodType.Getter patches the get accessor of a property.
    // MethodType enum (confirmed from 0Harmony/HarmonyLib/MethodType.cs):
    //   Normal, Getter, Setter, Constructor, StaticConstructor, Enumerator, Async
    [HarmonyPostfix]
    private static void Postfix(ref bool __result)
    {{
        // TODO: modify __result to change the property's return value
    }}'''
    else:
        raise ValueError(f"patch_type must be prefix/postfix/transpiler/property_getter, got {patch_type!r}")

    # Property getter needs different HarmonyPatch attribute
    if patch_type == 'property_getter':
        patch_attr = (f'[HarmonyPatch(typeof({target_class}), '
                      f'"{target_method}", MethodType.Getter)]')
    else:
        patch_attr = f'[HarmonyPatch(typeof({target_class}), "{target_method}")]'

    patch_cs = f'''using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Serilog;

namespace {safe_id}.Patches
{{
    // {patch_attr}
    //
    // E2: [HarmonyPatch] class-level attribute targets a specific method.
    // The patch class must be static (or inherit HarmonyPatch for nested patches).
    // Prefix/Postfix/Transpiler methods are discovered automatically by PatchAll().
    //
    // E12: [HarmonyPriority] -- controls patch ordering when multiple mods
    //   patch the same method. Confirmed from 0Harmony/HarmonyLib/Priority.cs:
    //   Priority.Last=0, VeryLow=100, Low=200, LowerThanNormal=300,
    //   Normal=400 (default), HigherThanNormal=500, High=600, VeryHigh=700, First=800
    //   LOWER priority number = runs FIRST for Prefix; LAST for Postfix.
    //
    // E13: [HarmonyBefore("other.mod.id")] / [HarmonyAfter("other.mod.id")]
    //   Ensures relative ordering against a specific other mod's patch.
    //   Both confirmed from 0Harmony/HarmonyLib/HarmonyBefore.cs and HarmonyAfter.cs
    //   Example: [HarmonyBefore("AlinaNova21.AlinasMapMod")]
    //
    // E16: HARMONY_DEBUG=1 environment variable enables Harmony debug output.
    //   Log written to %TEMP%/HarmonyLog.txt (confirmed from Harmony.cs constructor).
    //   FileLog.Log(string) for manual entries during development.
    {patch_attr}
    // [HarmonyPriority(Priority.Normal)]       // E12: optional priority
    // [HarmonyBefore("other.mod.id")]          // E13: optional relative ordering
    // [HarmonyPatchCategory("{mod_id}")]       // category for selective patching
    internal static class {cls_name}
    {{
        private static readonly ILogger log = Log.ForContext<{cls_name}>();

        // E7: AccessTools -- reflection helpers that cache results.
        // Used to access private fields, methods, properties without raw reflection.
        // Confirmed from 0Harmony/HarmonyLib/AccessTools.cs:
        //
        //   AccessTools.Field(typeof(T), "fieldName")         -- get FieldInfo
        //   AccessTools.Method(typeof(T), "methodName")       -- get MethodInfo
        //   AccessTools.Property(typeof(T), "propName")       -- get PropertyInfo
        //   AccessTools.TypeByName("FullClassName")           -- find type by name (E9)
        //
        // E8: FieldRefAccess -- fast field delegate, safer than raw reflection.
        //   var getField = AccessTools.FieldRefAccess<TargetClass, FieldType>("_fieldName");
        //   ref FieldType value = ref getField(instance);  // access by reference
        //   Confirmed used in StrangeCustoms: _upper.Invoke(trackSpan)

{patch_body}
    }}
}}
'''
    out_path = folder / f'{cls_name}.cs'
    out_path.write_text(patch_cs, encoding='utf-8')
    print(f"[harmony] patch written to {out_path}")
    return out_path




def generate_umm_entry(folder: Path, mod_id: str, mod_name: str,
                        author: str = 'Author',
                        version: str = '1.0.0',
                        game_version: str = '0.0.0',
                        requirements: list = None) -> Path:
    """Generate a complete UMM mod entry point.

    Creates:
      UMM/Mod.cs           -- UMM entry point (Load/Unload/OnGUI/OnSaveGUI)
      UMM/Settings.cs      -- settings class extending UnityModManager.ModSettings
      Info.json            -- UMM mod metadata

    E1:  Harmony initialised with modEntry.Info.Id (the UMM mod ID).
         Confirmed from AlinasUtils/UMM/Mod.cs: new Harmony(modEntry.Info.Id).PatchAll()
    E11: PatchAll(Assembly.GetExecutingAssembly()) -- explicit assembly form.
         More reliable than parameterless PatchAll().
         Confirmed from 0Harmony source: parameterless PatchAll() uses StackTrace
         to find the calling assembly, which can fail in some edge cases.
    E14: Harmony ID = modEntry.Info.Id -- matches Info.json "Id" field.
         Convention confirmed from AlinasUtils and Mapeditor reference mods.
    E15: UnpatchAll(harmonyId) on unload.
         Confirmed from AlinasUtils/UMM/Mod.cs: new Harmony(id).UnpatchAll(id)
         and 0Harmony/HarmonyLib/Harmony.cs: UnpatchAll(string harmonyID = null)
    """
    safe_id = mod_id.replace('.', '_').replace('-', '_')
    folder.mkdir(parents=True, exist_ok=True)
    umm_dir = folder / 'UMM'
    umm_dir.mkdir(exist_ok=True)

    # --- Info.json ---
    # UMM mod metadata format confirmed from UnityModManager/UnityModManager.cs ModInfo class:
    # Id, DisplayName, Author, Version, ManagerVersion, GameVersion,
    # Requirements (string[]), AssemblyName, EntryMethod, Repository, IsCheat
    info = {
        'Id':             mod_id,
        'DisplayName':    mod_name,
        'Author':         author,
        'Version':        version,
        'ManagerVersion': '0.27.0',
        'GameVersion':    game_version,
        'AssemblyName':   f'{mod_id}.dll',
        'EntryMethod':    f'{safe_id}.UMM.Mod.Load',
    }
    if requirements:
        info['Requirements'] = requirements
    _save_json(folder / 'Info.json', info)

    # --- UMM/Settings.cs ---
    # ModSettings persisted as XML to Mods/{ModId}/Settings.xml
    # Confirmed from UnityModManager/UnityModManager.cs ModSettings.GetPath()
    settings_cs = f'''using UnityModManagerNet;

namespace {safe_id}.UMM
{{
    // UMM settings -- serialized to Mods/{mod_id}/Settings.xml
    // Extend UnityModManager.ModSettings so UMM can save/load automatically.
    // Access in mod code via: Mod.Settings
    public class Settings : UnityModManager.ModSettings
    {{
        // Add your settings here, e.g.:
        // public bool MyOption = true;
        // public int MyValue = 42;

        // Override GetPath if you want a non-default save location.
        // Default: Mods/{mod_id}/Settings.xml
    }}
}}
'''
    (umm_dir / 'Settings.cs').write_text(settings_cs, encoding='utf-8')

    # --- UMM/Mod.cs ---
    # Entry point pattern confirmed from AlinasUtils/UMM/Mod.cs
    req_str = ', '.join(f'"{r}"' for r in (requirements or []))
    mod_cs = f'''using System.Reflection;
using GalaSoft.MvvmLight.Messaging;
using Game.Events;
using HarmonyLib;
using Serilog;
using UnityModManagerNet;

namespace {safe_id}.UMM
{{
    // E1/E11/E14/E15: UMM entry point.
    // Pattern confirmed from AlinasUtils/UMM/Mod.cs.
    // EntryMethod in Info.json points to this Load() method.
    internal static class Mod
    {{
        public static bool Loaded {{ get; private set; }} = false;
        public static Settings Settings {{ get; private set; }} = new Settings();

        private static readonly ILogger log = Log.ForContext(typeof(Mod));
        private static UnityModManager.ModEntry modEntry;

        // UMM calls this to load the mod. Return true = success, false = failure.
        public static bool Load(UnityModManager.ModEntry entry)
        {{
            if (Loaded)
            {{
                log.Information("Already loaded");
                return true;
            }}

            modEntry = entry;
            log.Information("Loading {mod_name}");

            // Load settings from Mods/{mod_id}/Settings.xml
            Settings = UnityModManager.ModSettings.Load<Settings>(entry) ?? new Settings();

            // E1/E11/E14: Harmony -- always use modEntry.Info.Id as the Harmony ID.
            // PatchAll(Assembly) is more reliable than parameterless PatchAll().
            new Harmony(entry.Info.Id).PatchAll(Assembly.GetExecutingAssembly());

            // Subscribe to game events via Messenger (game code, loader-agnostic)
            Messenger.Default.Register<MapDidLoadEvent>(entry, OnMapDidLoad);
            Messenger.Default.Register<GraphDidChangeEvent>(entry, OnGraphDidChange);

            // UMM lifecycle hooks
            entry.OnUnload  = Unload;
            entry.OnSaveGUI = OnSaveGUI;
            entry.OnGUI     = OnGUI;
            entry.OnToggle  = OnToggle;

            Loaded = true;
            log.Information("{mod_name} loaded successfully");
            return true;
        }}

        // E15: UnpatchAll on unload -- prevents patch conflicts on mod reload.
        // Confirmed from 0Harmony: UnpatchAll(harmonyID) removes only this mod's patches.
        private static bool Unload(UnityModManager.ModEntry entry)
        {{
            if (!Loaded) return true;

            Loaded = false;
            log.Information("Unloading {mod_name}");

            new Harmony(entry.Info.Id).UnpatchAll(entry.Info.Id);
            Messenger.Default.Unregister(entry);

            log.Information("{mod_name} unloaded");
            return true;
        }}

        private static void OnMapDidLoad(MapDidLoadEvent e)
        {{
            log.Debug("Map loaded");
            // TODO: initialize map-dependent systems here
        }}

        private static void OnGraphDidChange(GraphDidChangeEvent e)
        {{
            log.Debug("Graph changed");
            // TODO: respond to graph changes here
        }}

        // Called when user clicks Save in UMM settings UI
        private static void OnSaveGUI(UnityModManager.ModEntry entry)
        {{
            Settings.Save(entry);
        }}

        // Called every frame when this mod's settings panel is open in UMM UI
        private static void OnGUI(UnityModManager.ModEntry entry)
        {{
            // TODO: draw settings UI using UnityEngine.GUILayout
            // GUILayout.Label("My Setting:");
            // Settings.MyOption = GUILayout.Toggle(Settings.MyOption, "Enable");
        }}

        // Called when mod is toggled on/off in UMM. Return true to allow toggle.
        private static bool OnToggle(UnityModManager.ModEntry entry, bool active)
        {{
            return true;
        }}
    }}
}}
'''
    (umm_dir / 'Mod.cs').write_text(mod_cs, encoding='utf-8')

    print(f"[umm] entry point written to {folder}")
    return folder

