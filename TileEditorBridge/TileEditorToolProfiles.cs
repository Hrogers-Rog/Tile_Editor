using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace Hrogers.TileEditorBridge
{
    internal sealed partial class TileEditorBridgePanel
    {
        [Serializable]
        private sealed class ArcProfile
        {
            public string Name = string.Empty;
            public float Radius = 60f;
            public float Degrees = 30f;
            public int ControlNodes = 3;
            public float TargetGrade;
            public bool TurnRight = true;
        }

        [Serializable]
        private sealed class TurnoutProfile
        {
            public string Name = string.Empty;
            public float LeadLength = 25f;
            public float DivergenceDegrees = 10f;
            public float TargetGrade;
            public bool TurnRight = true;
        }

        [Serializable]
        private sealed class WyeProfile
        {
            public string Name = string.Empty;
            public float ThroughLength = 140f;
            public float TriangleDepth = 75f;
            public float StubLength = 50f;
            public float ExitLength = 35f;
            public float MainlineGrade;
            public bool TailRight = true;
        }

        [Serializable]
        private sealed class TrackToolProfileStore
        {
            public int FormatVersion = 1;
            public List<ArcProfile> Arcs =
                new List<ArcProfile>();
            public List<TurnoutProfile> Turnouts =
                new List<TurnoutProfile>();
            public List<WyeProfile> Wyes =
                new List<WyeProfile>();
        }

        private const string TrackProfileFileName =
            "tile_editor_track_profiles.json";
        private TrackToolProfileStore _trackProfiles =
            new TrackToolProfileStore();
        private string _trackProfilePath = string.Empty;
        private string _arcProfileName = string.Empty;
        private string _turnoutProfileName = string.Empty;
        private string _wyeProfileName = string.Empty;
        private int _arcProfileIndex;
        private int _turnoutProfileIndex;
        private int _wyeProfileIndex;
        private bool _showArcProfiles;
        private bool _showTurnoutProfiles;
        private bool _showWyeProfiles;
        private string _profileDeleteConfirmKey = string.Empty;

        private void InitializeTrackToolProfiles()
        {
            _trackProfilePath = Path.Combine(
                _bridgeDirectory, TrackProfileFileName);
            if (!File.Exists(_trackProfilePath))
            {
                _trackProfiles = new TrackToolProfileStore();
                return;
            }

            try
            {
                var loaded = JsonConvert.DeserializeObject<TrackToolProfileStore>(
                    File.ReadAllText(_trackProfilePath));
                _trackProfiles = loaded ?? new TrackToolProfileStore();
                if (_trackProfiles.Arcs == null)
                    _trackProfiles.Arcs = new List<ArcProfile>();
                if (_trackProfiles.Turnouts == null)
                    _trackProfiles.Turnouts = new List<TurnoutProfile>();
                if (_trackProfiles.Wyes == null)
                    _trackProfiles.Wyes = new List<WyeProfile>();
                SortTrackProfiles();
            }
            catch (Exception ex)
            {
                _trackProfiles = new TrackToolProfileStore();
                _logger?.Warning(
                    "Could not load track tool profiles: " + ex.Message);
            }
        }

        private void DrawArcProfileControls()
        {
            if (GUILayout.Button(
                    _showArcProfiles
                        ? "Hide Arc Profiles"
                        : "Profiles (" + _trackProfiles.Arcs.Count + ")...",
                    GUILayout.Height(27f)))
            {
                _showArcProfiles = !_showArcProfiles;
                _profileDeleteConfirmKey = string.Empty;
            }
            if (!_showArcProfiles)
                return;

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("Saved arc profiles", _titleStyle);
            DrawProfilePicker(
                _trackProfiles.Arcs.Select(profile => profile.Name).ToArray(),
                ref _arcProfileIndex,
                ref _arcProfileName,
                "arc");
            DrawTextField("Profile name", ref _arcProfileName);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save Current", GUILayout.Height(29f)))
            {
                RunProfileAction(
                    "Saved arc profile",
                    SaveCurrentArcProfile);
            }
            GUI.enabled = _trackProfiles.Arcs.Count > 0;
            if (GUILayout.Button("Load", GUILayout.Height(29f)))
            {
                RunProfileAction(
                    "Loaded arc profile",
                    LoadSelectedArcProfile);
            }
            DrawProfileDeleteButton(
                "arc",
                _trackProfiles.Arcs.Count > 0,
                DeleteSelectedArcProfile);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Label(
                "Profiles include radius, angle, nodes, grade, and side.",
                _mutedStyle);
            GUILayout.EndVertical();
        }

        private void DrawTurnoutProfileControls()
        {
            if (GUILayout.Button(
                    _showTurnoutProfiles
                        ? "Hide Turnout Profiles"
                        : "Profiles (" + _trackProfiles.Turnouts.Count + ")...",
                    GUILayout.Height(27f)))
            {
                _showTurnoutProfiles = !_showTurnoutProfiles;
                _profileDeleteConfirmKey = string.Empty;
            }
            if (!_showTurnoutProfiles)
                return;

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("Saved turnout profiles", _titleStyle);
            DrawProfilePicker(
                _trackProfiles.Turnouts.Select(profile => profile.Name).ToArray(),
                ref _turnoutProfileIndex,
                ref _turnoutProfileName,
                "turnout");
            DrawTextField("Profile name", ref _turnoutProfileName);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save Current", GUILayout.Height(29f)))
            {
                RunProfileAction(
                    "Saved turnout profile",
                    SaveCurrentTurnoutProfile);
            }
            GUI.enabled = _trackProfiles.Turnouts.Count > 0;
            if (GUILayout.Button("Load", GUILayout.Height(29f)))
            {
                RunProfileAction(
                    "Loaded turnout profile",
                    LoadSelectedTurnoutProfile);
            }
            DrawProfileDeleteButton(
                "turnout",
                _trackProfiles.Turnouts.Count > 0,
                DeleteSelectedTurnoutProfile);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Label(
                "Save Current updates an existing profile with the same name.",
                _mutedStyle);
            GUILayout.EndVertical();
        }

        private void DrawWyeProfileControls()
        {
            if (GUILayout.Button(
                    _showWyeProfiles
                        ? "Hide Wye Profiles"
                        : "Profiles (" + _trackProfiles.Wyes.Count + ")...",
                    GUILayout.Height(27f)))
            {
                _showWyeProfiles = !_showWyeProfiles;
                _profileDeleteConfirmKey = string.Empty;
            }
            if (!_showWyeProfiles)
                return;

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("Saved complete-wye profiles", _titleStyle);
            DrawProfilePicker(
                _trackProfiles.Wyes.Select(profile => profile.Name).ToArray(),
                ref _wyeProfileIndex,
                ref _wyeProfileName,
                "wye");
            DrawTextField("Profile name", ref _wyeProfileName);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save Current", GUILayout.Height(29f)))
            {
                RunProfileAction(
                    "Saved wye profile",
                    SaveCurrentWyeProfile);
            }
            GUI.enabled = _trackProfiles.Wyes.Count > 0;
            if (GUILayout.Button("Load", GUILayout.Height(29f)))
            {
                RunProfileAction(
                    "Loaded wye profile",
                    LoadSelectedWyeProfile);
            }
            DrawProfileDeleteButton(
                "wye",
                _trackProfiles.Wyes.Count > 0,
                DeleteSelectedWyeProfile);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Label(
                "Profiles include dimensions, grade, and left/right side.",
                _mutedStyle);
            GUILayout.EndVertical();
        }

        private void DrawProfilePicker(
            string[] names,
            ref int selectedIndex,
            ref string profileName,
            string kind)
        {
            if (names.Length == 0)
            {
                GUILayout.Label(
                    "No saved " + kind + " profiles yet.",
                    _mutedStyle);
                selectedIndex = 0;
                return;
            }
            selectedIndex = Mathf.Clamp(
                selectedIndex, 0, names.Length - 1);
            var previous = selectedIndex;
            selectedIndex = GUILayout.SelectionGrid(
                selectedIndex,
                names,
                Math.Min(3, names.Length));
            if (selectedIndex != previous
                || string.IsNullOrWhiteSpace(profileName))
            {
                profileName = names[selectedIndex];
                _profileDeleteConfirmKey = string.Empty;
            }
        }

        private void DrawProfileDeleteButton(
            string kind,
            bool available,
            Action deletion)
        {
            var key = kind + ":"
                      + ProfileIndexForKind(kind);
            GUI.enabled = available;
            if (_profileDeleteConfirmKey == key)
            {
                var oldColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.85f, 0.28f, 0.20f);
                if (GUILayout.Button("CONFIRM DELETE", GUILayout.Height(29f)))
                {
                    RunProfileAction(
                        "Deleted " + kind + " profile",
                        deletion);
                    _profileDeleteConfirmKey = string.Empty;
                }
                GUI.backgroundColor = oldColor;
            }
            else if (GUILayout.Button("Delete", GUILayout.Height(29f)))
            {
                _profileDeleteConfirmKey = key;
            }
        }

        private int ProfileIndexForKind(string kind)
        {
            switch (kind)
            {
                case "arc":
                    return _arcProfileIndex;
                case "turnout":
                    return _turnoutProfileIndex;
                default:
                    return _wyeProfileIndex;
            }
        }

        private void SaveCurrentArcProfile()
        {
            var name = ValidateProfileName(_arcProfileName);
            var profile = new ArcProfile
            {
                Name = name,
                Radius = ValidateProfileValue(
                    ParseFloat(_arcRadius, "radius"),
                    5f, 5000f, "Arc radius"),
                Degrees = ValidateProfileValue(
                    ParseFloat(_arcDegrees, "arc angle"),
                    0.5f, 180f, "Arc angle"),
                ControlNodes = ValidateProfileInt(
                    ParseInt(_arcNodes, "arc nodes"),
                    1, 64, "Arc control nodes"),
                TargetGrade = ValidateProfileValue(
                    ParseFloat(_targetGrade, "target grade"),
                    -15f, 15f, "Target grade"),
                TurnRight = _turnRight,
            };
            _arcProfileIndex = UpsertProfile(
                _trackProfiles.Arcs,
                profile,
                item => item.Name);
            _arcProfileName = name;
            SaveTrackToolProfiles();
        }

        private void SaveCurrentTurnoutProfile()
        {
            var name = ValidateProfileName(_turnoutProfileName);
            var profile = new TurnoutProfile
            {
                Name = name,
                LeadLength = ValidateProfileValue(
                    ParseFloat(_turnoutLength, "turnout length"),
                    0.5f, 5000f, "Turnout length"),
                DivergenceDegrees = ValidateProfileValue(
                    ParseFloat(_turnoutDegrees, "turnout angle"),
                    0.5f, 45f, "Turnout angle"),
                TargetGrade = ValidateProfileValue(
                    ParseFloat(_targetGrade, "target grade"),
                    -15f, 15f, "Target grade"),
                TurnRight = _turnRight,
            };
            _turnoutProfileIndex = UpsertProfile(
                _trackProfiles.Turnouts,
                profile,
                item => item.Name);
            _turnoutProfileName = name;
            SaveTrackToolProfiles();
        }

        private void SaveCurrentWyeProfile()
        {
            var name = ValidateProfileName(_wyeProfileName);
            var profile = new WyeProfile
            {
                Name = name,
                ThroughLength = ValidateProfileValue(
                    ParseFloat(_wyeBaseLength, "through length"),
                    30f, 2000f, "Through length"),
                TriangleDepth = ValidateProfileValue(
                    ParseFloat(_wyeDepth, "triangle depth"),
                    10f, 1000f, "Triangle depth"),
                StubLength = ValidateProfileValue(
                    ParseFloat(_wyeStubLength, "tail stub length"),
                    5f, 1000f, "Tail stub length"),
                ExitLength = ValidateProfileValue(
                    ParseFloat(_wyeExitLength, "through exit length"),
                    5f, 1000f, "Through exit length"),
                MainlineGrade = ValidateProfileValue(
                    ParseFloat(_targetGrade, "target grade"),
                    -15f, 15f, "Mainline grade"),
                TailRight = _turnRight,
            };
            _wyeProfileIndex = UpsertProfile(
                _trackProfiles.Wyes,
                profile,
                item => item.Name);
            _wyeProfileName = name;
            SaveTrackToolProfiles();
        }

        private void LoadSelectedTurnoutProfile()
        {
            var profile = SelectedProfile(
                _trackProfiles.Turnouts,
                _turnoutProfileIndex,
                "turnout");
            _turnoutProfileName = profile.Name;
            _turnoutLength = FormatProfileValue(profile.LeadLength);
            _turnoutDegrees = FormatProfileValue(
                profile.DivergenceDegrees);
            _targetGrade = FormatProfileValue(profile.TargetGrade);
            _turnRight = profile.TurnRight;
        }

        private void LoadSelectedArcProfile()
        {
            var profile = SelectedProfile(
                _trackProfiles.Arcs,
                _arcProfileIndex,
                "arc");
            _arcProfileName = profile.Name;
            _arcRadius = FormatProfileValue(profile.Radius);
            _arcDegrees = FormatProfileValue(profile.Degrees);
            _arcNodes = profile.ControlNodes.ToString(
                CultureInfo.InvariantCulture);
            _targetGrade = FormatProfileValue(profile.TargetGrade);
            _turnRight = profile.TurnRight;
        }

        private void LoadSelectedWyeProfile()
        {
            var profile = SelectedProfile(
                _trackProfiles.Wyes,
                _wyeProfileIndex,
                "wye");
            _wyeProfileName = profile.Name;
            _wyeBaseLength = FormatProfileValue(profile.ThroughLength);
            _wyeDepth = FormatProfileValue(profile.TriangleDepth);
            _wyeStubLength = FormatProfileValue(profile.StubLength);
            _wyeExitLength = FormatProfileValue(profile.ExitLength);
            _targetGrade = FormatProfileValue(profile.MainlineGrade);
            _turnRight = profile.TailRight;
        }

        private void DeleteSelectedTurnoutProfile()
        {
            SelectedProfile(
                _trackProfiles.Turnouts,
                _turnoutProfileIndex,
                "turnout");
            _trackProfiles.Turnouts.RemoveAt(_turnoutProfileIndex);
            _turnoutProfileIndex = Mathf.Clamp(
                _turnoutProfileIndex,
                0,
                Math.Max(0, _trackProfiles.Turnouts.Count - 1));
            _turnoutProfileName = _trackProfiles.Turnouts.Count == 0
                ? string.Empty
                : _trackProfiles.Turnouts[_turnoutProfileIndex].Name;
            SaveTrackToolProfiles();
        }

        private void DeleteSelectedArcProfile()
        {
            SelectedProfile(
                _trackProfiles.Arcs,
                _arcProfileIndex,
                "arc");
            _trackProfiles.Arcs.RemoveAt(_arcProfileIndex);
            _arcProfileIndex = Mathf.Clamp(
                _arcProfileIndex,
                0,
                Math.Max(0, _trackProfiles.Arcs.Count - 1));
            _arcProfileName = _trackProfiles.Arcs.Count == 0
                ? string.Empty
                : _trackProfiles.Arcs[_arcProfileIndex].Name;
            SaveTrackToolProfiles();
        }

        private void DeleteSelectedWyeProfile()
        {
            SelectedProfile(
                _trackProfiles.Wyes,
                _wyeProfileIndex,
                "wye");
            _trackProfiles.Wyes.RemoveAt(_wyeProfileIndex);
            _wyeProfileIndex = Mathf.Clamp(
                _wyeProfileIndex,
                0,
                Math.Max(0, _trackProfiles.Wyes.Count - 1));
            _wyeProfileName = _trackProfiles.Wyes.Count == 0
                ? string.Empty
                : _trackProfiles.Wyes[_wyeProfileIndex].Name;
            SaveTrackToolProfiles();
        }

        private void SaveTrackToolProfiles()
        {
            SortTrackProfiles();
            _arcProfileIndex = FindProfileIndex(
                _trackProfiles.Arcs,
                _arcProfileName,
                item => item.Name);
            _turnoutProfileIndex = FindProfileIndex(
                _trackProfiles.Turnouts,
                _turnoutProfileName,
                item => item.Name);
            _wyeProfileIndex = FindProfileIndex(
                _trackProfiles.Wyes,
                _wyeProfileName,
                item => item.Name);
            var json = JsonConvert.SerializeObject(
                _trackProfiles,
                Formatting.Indented);
            AtomicWrite(_trackProfilePath, json + Environment.NewLine);
        }

        private void SortTrackProfiles()
        {
            _trackProfiles.Arcs.Sort(
                (left, right) => string.Compare(
                    left.Name,
                    right.Name,
                    StringComparison.OrdinalIgnoreCase));
            _trackProfiles.Turnouts.Sort(
                (left, right) => string.Compare(
                    left.Name,
                    right.Name,
                    StringComparison.OrdinalIgnoreCase));
            _trackProfiles.Wyes.Sort(
                (left, right) => string.Compare(
                    left.Name,
                    right.Name,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static int UpsertProfile<T>(
            IList<T> profiles,
            T replacement,
            Func<T, string> name)
        {
            var replacementName = name(replacement);
            for (var index = 0; index < profiles.Count; index++)
            {
                if (!string.Equals(
                        name(profiles[index]),
                        replacementName,
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                profiles[index] = replacement;
                return index;
            }
            profiles.Add(replacement);
            return profiles.Count - 1;
        }

        private static int FindProfileIndex<T>(
            IList<T> profiles,
            string requestedName,
            Func<T, string> name)
        {
            for (var index = 0; index < profiles.Count; index++)
            {
                if (string.Equals(
                        name(profiles[index]),
                        requestedName,
                        StringComparison.OrdinalIgnoreCase))
                    return index;
            }
            return 0;
        }

        private static T SelectedProfile<T>(
            IList<T> profiles,
            int selectedIndex,
            string kind)
        {
            if (profiles.Count == 0)
            {
                throw new InvalidOperationException(
                    "There are no saved " + kind + " profiles.");
            }
            if (selectedIndex < 0 || selectedIndex >= profiles.Count)
            {
                throw new InvalidOperationException(
                    "Select a saved " + kind + " profile first.");
            }
            return profiles[selectedIndex];
        }

        private static string ValidateProfileName(string value)
        {
            var name = (value ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                throw new InvalidOperationException(
                    "Enter a profile name before saving.");
            }
            if (name.Length > 48)
            {
                throw new InvalidOperationException(
                    "Profile names may not exceed 48 characters.");
            }
            return name;
        }

        private static float ValidateProfileValue(
            float value,
            float minimum,
            float maximum,
            string label)
        {
            if (value < minimum || value > maximum)
            {
                throw new InvalidOperationException(
                    label + " must be between "
                    + minimum.ToString("0.###", CultureInfo.InvariantCulture)
                    + " and "
                    + maximum.ToString("0.###", CultureInfo.InvariantCulture)
                    + ".");
            }
            return value;
        }

        private static int ValidateProfileInt(
            int value,
            int minimum,
            int maximum,
            string label)
        {
            if (value < minimum || value > maximum)
            {
                throw new InvalidOperationException(
                    label + " must be between "
                    + minimum.ToString(CultureInfo.InvariantCulture)
                    + " and "
                    + maximum.ToString(CultureInfo.InvariantCulture)
                    + ".");
            }
            return value;
        }

        private static string FormatProfileValue(float value)
        {
            return value.ToString(
                "0.###", CultureInfo.InvariantCulture);
        }

        private void RunProfileAction(
            string successMessage,
            Action action)
        {
            try
            {
                action();
                _lastPanelMessage = successMessage;
            }
            catch (Exception ex)
            {
                _lastPanelMessage = ex.Message;
                _logger?.Warning(
                    "Track profile action failed: " + ex);
            }
        }
    }
}
