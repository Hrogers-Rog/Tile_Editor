using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hrogers.TileEditorBridge
{
    internal sealed partial class TileEditorBridgePanel
    {
        private static readonly string[] FeatureTypes = { "bool", "enum", "number" };
        private static readonly string[] FeatureTypeLabels = { "ON / OFF", "CHOICE", "SLIDER" };
        private static readonly string[] FeatureScopes = { "profile", "user", "server" };
        private static readonly string[] FeatureOperators =
        {
            "equals", "notEquals", "greaterThan", "greaterThanOrEqual",
            "lessThan", "lessThanOrEqual"
        };

        private string _featureLoadedRuleId = string.Empty;
        private string _featureRuleId = "optionalFeature";
        private string _featureSettingId = "enableOptionalFeature";
        private string _featureLabel = "Optional Feature";
        private int _featureTypeIndex;
        private int _featureScopeIndex;
        private int _featureOperatorIndex;
        private bool _featureBoolDefault = true;
        private bool _featureBoolExpected = true;
        private string _featureChoiceValues = "standard,expanded";
        private string _featureDefaultValue = "standard";
        private string _featureExpectedValue = "expanded";
        private string _featureMin = "0";
        private string _featureMax = "10";
        private string _featureStep = "1";
        private int _featureTargetKindIndex;
        private int _featureTargetIndex;
        private readonly List<TileEditorGraphSession.FeatureTargetInfo> _featureSelectedTargets =
            new List<TileEditorGraphSession.FeatureTargetInfo>();
        private string _featureDeleteConfirmRuleId = string.Empty;

        private void DrawFeatureOptionsPanel()
        {
            if (_mapEditor == null || !_mapEditor.Available || !_mapEditor.GraphOpen)
                return;
            GUILayout.Label("PLAYER OPTIONS", _titleStyle);
            GUILayout.Label(
                "Build one package with player-facing switches, choices, and sliders. A matching rule includes its selected track, scenery, operations, or other native objects after the next map reload.",
                _lineStyle);
            if (!_mapEditor.FuseOperationsDocument)
            {
                GUILayout.Label("NATIVE FUSE ONLY", _offlineStyle);
                GUILayout.Label(
                    "RailLoader has no equivalent object-level option contract. Switch the project to FUSE schema to author options; legacy export leaves this workspace disabled.",
                    _mutedStyle);
                GUI.enabled = false;
                DrawFeatureOptionEditor();
                GUI.enabled = true;
                return;
            }

            DrawExistingFeatureOptions();
            DrawFeatureOptionEditor();
        }

        private void DrawExistingFeatureOptions()
        {
            GUILayout.Space(6f);
            GUILayout.Label("AUTHORED OPTIONS", _titleStyle);
            var options = _mapEditor.FeatureOptions;
            if (options.Count == 0)
            {
                GUILayout.Label("No package options have been authored yet.", _mutedStyle);
                return;
            }
            foreach (var option in options.Take(24))
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(
                        option.Label + "  [" + FeatureTypeLabel(option.Type) + "]  "
                        + option.Targets.Length + " targets",
                        GUILayout.Height(27f)))
                {
                    LoadFeatureOption(option);
                    _featureDeleteConfirmRuleId = string.Empty;
                }
                var confirming = string.Equals(
                    _featureDeleteConfirmRuleId,
                    option.RuleId,
                    StringComparison.OrdinalIgnoreCase);
                var old = GUI.backgroundColor;
                GUI.backgroundColor = confirming
                    ? new Color(0.90f, 0.25f, 0.18f)
                    : new Color(0.55f, 0.22f, 0.18f);
                if (GUILayout.Button(confirming ? "CONFIRM" : "DELETE", GUILayout.Width(75f), GUILayout.Height(27f)))
                {
                    if (confirming)
                    {
                        RunGameAction(() => _mapEditor.DeleteFeatureOption(option.RuleId));
                        if (string.Equals(_featureLoadedRuleId, option.RuleId, StringComparison.OrdinalIgnoreCase))
                            ResetFeatureOptionForm();
                        _featureDeleteConfirmRuleId = string.Empty;
                    }
                    else
                    {
                        _featureDeleteConfirmRuleId = option.RuleId;
                    }
                }
                GUI.backgroundColor = old;
                GUILayout.EndHorizontal();
            }
        }

        private void DrawFeatureOptionEditor()
        {
            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                string.IsNullOrWhiteSpace(_featureLoadedRuleId)
                    ? "NEW OPTION"
                    : "EDIT " + _featureLoadedRuleId,
                _titleStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("NEW", GUILayout.Width(70f)))
                ResetFeatureOptionForm();
            GUILayout.EndHorizontal();
            DrawTextField("Rule ID", ref _featureRuleId);
            DrawTextField("Setting ID", ref _featureSettingId);
            DrawTextField("Player label", ref _featureLabel);

            GUILayout.BeginHorizontal();
            GUILayout.Label("TYPE", GUILayout.Width(90f));
            if (GUILayout.Button("<", GUILayout.Width(34f)))
                _featureTypeIndex = CycleIndex(_featureTypeIndex, FeatureTypes.Length, -1);
            GUILayout.Label(FeatureTypeLabels[_featureTypeIndex], _lineStyle);
            if (GUILayout.Button(">", GUILayout.Width(34f)))
                _featureTypeIndex = CycleIndex(_featureTypeIndex, FeatureTypes.Length, 1);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("SCOPE", GUILayout.Width(90f));
            if (GUILayout.Button("<", GUILayout.Width(34f)))
                _featureScopeIndex = CycleIndex(_featureScopeIndex, FeatureScopes.Length, -1);
            GUILayout.Label(FeatureScopes[_featureScopeIndex].ToUpperInvariant(), _lineStyle);
            if (GUILayout.Button(">", GUILayout.Width(34f)))
                _featureScopeIndex = CycleIndex(_featureScopeIndex, FeatureScopes.Length, 1);
            GUILayout.EndHorizontal();

            if (FeatureTypes[_featureTypeIndex] == "bool")
            {
                _featureBoolDefault = GUILayout.Toggle(_featureBoolDefault, " Default is ON");
                _featureBoolExpected = GUILayout.Toggle(_featureBoolExpected, " Include targets when option is ON");
                _featureOperatorIndex = Mathf.Clamp(_featureOperatorIndex, 0, 1);
            }
            else if (FeatureTypes[_featureTypeIndex] == "enum")
            {
                DrawTextField("Choices (comma separated)", ref _featureChoiceValues);
                DrawTextField("Default choice", ref _featureDefaultValue);
                DrawTextField("Include when value is", ref _featureExpectedValue);
                _featureOperatorIndex = Mathf.Clamp(_featureOperatorIndex, 0, 1);
            }
            else
            {
                DrawTextField("Minimum", ref _featureMin);
                DrawTextField("Maximum", ref _featureMax);
                DrawTextField("Step", ref _featureStep);
                DrawTextField("Default value", ref _featureDefaultValue);
                DrawTextField("Comparison value", ref _featureExpectedValue);
            }
            DrawFeatureOperatorPicker();
            DrawFeatureTargetPicker();

            var old = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.15f, 0.63f, 0.75f);
            if (GUILayout.Button(
                    string.IsNullOrWhiteSpace(_featureLoadedRuleId)
                        ? "CREATE PLAYER OPTION"
                        : "SAVE PLAYER OPTION",
                    GUILayout.Height(36f)))
            {
                var option = BuildFeatureOption();
                RunGameAction(() => _mapEditor.SaveFeatureOption(_featureLoadedRuleId, option));
                _featureLoadedRuleId = option.RuleId;
            }
            GUI.backgroundColor = old;
            GUILayout.Label(
                "FUSE marks these settings as reload-required automatically. Disabled objects remain in the source file and return when the option matches again.",
                _mutedStyle);
        }

        private void DrawFeatureOperatorPicker()
        {
            var limit = FeatureTypes[_featureTypeIndex] == "number"
                ? FeatureOperators.Length
                : 2;
            _featureOperatorIndex = Mathf.Clamp(_featureOperatorIndex, 0, limit - 1);
            GUILayout.BeginHorizontal();
            GUILayout.Label("MATCH", GUILayout.Width(90f));
            if (GUILayout.Button("<", GUILayout.Width(34f)))
                _featureOperatorIndex = CycleIndex(_featureOperatorIndex, limit, -1);
            GUILayout.Label(FeatureOperators[_featureOperatorIndex], _lineStyle);
            if (GUILayout.Button(">", GUILayout.Width(34f)))
                _featureOperatorIndex = CycleIndex(_featureOperatorIndex, limit, 1);
            GUILayout.EndHorizontal();
        }

        private void DrawFeatureTargetPicker()
        {
            GUILayout.Space(6f);
            GUILayout.Label("OBJECTS CONTROLLED BY THIS OPTION", _titleStyle);
            var catalog = _mapEditor.FeatureTargets;
            var kinds = catalog.Select(target => target.Kind)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (kinds.Length == 0)
            {
                GUILayout.Label("Author track, scenery, operations, or another native object first.", _mutedStyle);
                return;
            }
            _featureTargetKindIndex = Mathf.Clamp(_featureTargetKindIndex, 0, kinds.Length - 1);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("< KIND", GUILayout.Width(70f)))
            {
                _featureTargetKindIndex = CycleIndex(_featureTargetKindIndex, kinds.Length, -1);
                _featureTargetIndex = 0;
            }
            GUILayout.Label(kinds[_featureTargetKindIndex], _lineStyle);
            if (GUILayout.Button("KIND >", GUILayout.Width(70f)))
            {
                _featureTargetKindIndex = CycleIndex(_featureTargetKindIndex, kinds.Length, 1);
                _featureTargetIndex = 0;
            }
            GUILayout.EndHorizontal();
            var candidates = catalog.Where(target => string.Equals(
                    target.Kind,
                    kinds[_featureTargetKindIndex],
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            _featureTargetIndex = Mathf.Clamp(_featureTargetIndex, 0, candidates.Length - 1);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", GUILayout.Width(34f)))
                _featureTargetIndex = CycleIndex(_featureTargetIndex, candidates.Length, -1);
            GUILayout.Label(Shorten(candidates[_featureTargetIndex].Id, 48), _lineStyle);
            if (GUILayout.Button(">", GUILayout.Width(34f)))
                _featureTargetIndex = CycleIndex(_featureTargetIndex, candidates.Length, 1);
            if (GUILayout.Button("ADD", GUILayout.Width(62f)))
            {
                var candidate = candidates[_featureTargetIndex];
                if (!_featureSelectedTargets.Any(target =>
                        string.Equals(target.Kind, candidate.Kind, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(target.Id, candidate.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    _featureSelectedTargets.Add(candidate);
                }
            }
            GUILayout.EndHorizontal();
            for (var index = 0; index < _featureSelectedTargets.Count; index++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(Shorten(_featureSelectedTargets[index].DisplayLabel, 58), _mutedStyle);
                if (GUILayout.Button("REMOVE", GUILayout.Width(72f)))
                {
                    _featureSelectedTargets.RemoveAt(index);
                    GUILayout.EndHorizontal();
                    break;
                }
                GUILayout.EndHorizontal();
            }
        }

        private TileEditorGraphSession.FeatureOptionInfo BuildFeatureOption()
        {
            var type = FeatureTypes[_featureTypeIndex];
            var option = new TileEditorGraphSession.FeatureOptionInfo
            {
                RuleId = _featureRuleId.Trim(),
                SettingId = _featureSettingId.Trim(),
                Label = _featureLabel.Trim(),
                Type = type,
                Scope = FeatureScopes[_featureScopeIndex],
                Operator = FeatureOperators[_featureOperatorIndex],
                Targets = _featureSelectedTargets.Select(target => new TileEditorGraphSession.FeatureTargetInfo
                {
                    Kind = target.Kind,
                    Id = target.Id
                }).ToArray()
            };
            if (type == "bool")
            {
                option.DefaultValue = new JValue(_featureBoolDefault);
                option.ExpectedValue = new JValue(_featureBoolExpected);
            }
            else if (type == "enum")
            {
                option.Values = _featureChoiceValues.Split(',')
                    .Select(value => value.Trim())
                    .Where(value => value.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                option.DefaultValue = new JValue(_featureDefaultValue.Trim());
                option.ExpectedValue = new JValue(_featureExpectedValue.Trim());
            }
            else
            {
                option.Min = ParseFeatureNumber(_featureMin, "slider minimum");
                option.Max = ParseFeatureNumber(_featureMax, "slider maximum");
                option.Step = ParseFeatureNumber(_featureStep, "slider step");
                option.DefaultValue = new JValue(ParseFeatureNumber(_featureDefaultValue, "default value"));
                option.ExpectedValue = new JValue(ParseFeatureNumber(_featureExpectedValue, "comparison value"));
            }
            return option;
        }

        private void LoadFeatureOption(TileEditorGraphSession.FeatureOptionInfo option)
        {
            _featureLoadedRuleId = option.RuleId;
            _featureRuleId = option.RuleId;
            _featureSettingId = option.SettingId;
            _featureLabel = option.Label;
            _featureTypeIndex = Array.IndexOf(FeatureTypes, option.Type);
            if (_featureTypeIndex < 0) _featureTypeIndex = 0;
            _featureScopeIndex = Array.IndexOf(FeatureScopes, option.Scope);
            if (_featureScopeIndex < 0) _featureScopeIndex = 0;
            _featureOperatorIndex = Array.IndexOf(FeatureOperators, option.Operator);
            if (_featureOperatorIndex < 0) _featureOperatorIndex = 0;
            _featureChoiceValues = string.Join(",", option.Values ?? Array.Empty<string>());
            _featureDefaultValue = FeatureTokenText(option.DefaultValue);
            _featureExpectedValue = FeatureTokenText(option.ExpectedValue);
            _featureBoolDefault = option.DefaultValue?.Value<bool>() ?? true;
            _featureBoolExpected = option.ExpectedValue?.Value<bool>() ?? true;
            _featureMin = FeatureNumberText(option.Min, 0d);
            _featureMax = FeatureNumberText(option.Max, 10d);
            _featureStep = FeatureNumberText(option.Step, 1d);
            _featureSelectedTargets.Clear();
            _featureSelectedTargets.AddRange(option.Targets ?? Array.Empty<TileEditorGraphSession.FeatureTargetInfo>());
        }

        private void ResetFeatureOptionForm()
        {
            _featureLoadedRuleId = string.Empty;
            _featureRuleId = "optionalFeature";
            _featureSettingId = "enableOptionalFeature";
            _featureLabel = "Optional Feature";
            _featureSelectedTargets.Clear();
            _featureDeleteConfirmRuleId = string.Empty;
        }

        private static int CycleIndex(int value, int count, int delta) =>
            count <= 0 ? 0 : (value + count + delta) % count;

        private static double ParseFeatureNumber(string value, string label)
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                || double.IsNaN(parsed) || double.IsInfinity(parsed))
                throw new InvalidOperationException("Enter a valid " + label + ".");
            return parsed;
        }

        private static string FeatureTokenText(JToken value) =>
            value == null || value.Type == JTokenType.Null
                ? string.Empty
                : value.Type == JTokenType.String ? value.Value<string>() : value.ToString();

        private static string FeatureNumberText(double? value, double fallback) =>
            (value ?? fallback).ToString("0.###", CultureInfo.InvariantCulture);

        private static string FeatureTypeLabel(string type) =>
            type == "enum" ? "CHOICE" : type == "number" ? "SLIDER" : "ON / OFF";
    }
}
