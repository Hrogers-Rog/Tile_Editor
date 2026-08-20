using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Hrogers.TileEditorBridge
{
    internal sealed partial class TileEditorGraphSession
    {
        internal sealed class FeatureTargetInfo
        {
            internal string Kind = string.Empty;
            internal string Id = string.Empty;
            internal string DisplayLabel => Kind + ": " + Id;
        }

        internal sealed class FeatureOptionInfo
        {
            internal string RuleId = string.Empty;
            internal string SettingId = string.Empty;
            internal string Label = string.Empty;
            internal string Type = "bool";
            internal string Scope = "profile";
            internal string Operator = "equals";
            internal JToken DefaultValue;
            internal JToken ExpectedValue;
            internal string[] Values = Array.Empty<string>();
            internal double? Min;
            internal double? Max;
            internal double? Step;
            internal FeatureTargetInfo[] Targets = Array.Empty<FeatureTargetInfo>();
        }

        internal IReadOnlyList<FeatureTargetInfo> FeatureTargets =>
            DiscoverFeatureTargets();

        internal IReadOnlyList<FeatureOptionInfo> FeatureOptions =>
            DiscoverFeatureOptions();

        internal string SaveFeatureOption(
            string originalRuleId,
            FeatureOptionInfo option)
        {
            RequireSession();
            if (!_fuseNativeDocument)
                throw new InvalidOperationException(
                    "Per-mod feature options require the native FUSE schema.");
            ValidateFeatureOption(option);
            var knownTargets = new HashSet<string>(
                DiscoverFeatureTargets().Select(TargetKey),
                StringComparer.OrdinalIgnoreCase);
            foreach (var target in option.Targets)
            {
                if (!knownTargets.Contains(TargetKey(target)))
                {
                    throw new InvalidOperationException(
                        "Feature target no longer exists in this document: "
                        + target.DisplayLabel);
                }
            }
            var existingRules = _document["featureRules"] as JObject;
            if (existingRules?[option.RuleId] != null
                && !string.Equals(originalRuleId, option.RuleId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Another feature rule already uses ID " + option.RuleId + ".");
            }

            ExecuteOperationsEdit(
                string.IsNullOrWhiteSpace(originalRuleId)
                    ? "Create mod feature option"
                    : "Update mod feature option",
                () =>
                {
                    var settings = EnsureFeatureObject(_document, "settings");
                    var rules = EnsureFeatureObject(_document, "featureRules");
                    var oldSettingId = string.Empty;
                    if (!string.IsNullOrWhiteSpace(originalRuleId)
                        && rules[originalRuleId] is JObject oldRule)
                    {
                        oldSettingId = (string)oldRule["setting"] ?? string.Empty;
                        if (!string.Equals(originalRuleId, option.RuleId, StringComparison.OrdinalIgnoreCase))
                            rules.Remove(originalRuleId);
                    }

                    settings[option.SettingId] = BuildSettingToken(option);
                    rules[option.RuleId] = BuildFeatureRuleToken(option);
                    if (!string.IsNullOrWhiteSpace(oldSettingId)
                        && !string.Equals(oldSettingId, option.SettingId, StringComparison.OrdinalIgnoreCase)
                        && !FeatureSettingIsReferenced(rules, oldSettingId))
                    {
                        settings.Remove(oldSettingId);
                    }
                });
            return "Saved feature option " + option.RuleId
                + " with " + option.Targets.Length + " target(s). Reload the map to apply it.";
        }

        internal string DeleteFeatureOption(string ruleId)
        {
            RequireSession();
            if (!_fuseNativeDocument)
                throw new InvalidOperationException(
                    "Per-mod feature options require the native FUSE schema.");
            if (string.IsNullOrWhiteSpace(ruleId))
                throw new InvalidOperationException("Select a feature rule first.");
            var rules = _document["featureRules"] as JObject;
            var rule = rules?[ruleId] as JObject;
            if (rule == null)
                throw new InvalidOperationException("Feature rule was not found: " + ruleId);
            var settingId = (string)rule["setting"] ?? string.Empty;
            ExecuteOperationsEdit("Delete mod feature option", () =>
            {
                rules.Remove(ruleId);
                if (!FeatureSettingIsReferenced(rules, settingId))
                    (_document["settings"] as JObject)?.Remove(settingId);
            });
            return "Removed feature rule " + ruleId + ".";
        }

        private IReadOnlyList<FeatureOptionInfo> DiscoverFeatureOptions()
        {
            var values = new List<FeatureOptionInfo>();
            if (!_fuseNativeDocument || _document == null)
                return values;
            var settings = _document["settings"] as JObject;
            var rules = _document["featureRules"] as JObject;
            if (rules == null)
                return values;
            foreach (var property in rules.Properties().OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (!(property.Value is JObject rule))
                    continue;
                var settingId = (string)rule["setting"] ?? string.Empty;
                var setting = settings?[settingId] as JObject;
                values.Add(new FeatureOptionInfo
                {
                    RuleId = property.Name,
                    SettingId = settingId,
                    Label = (string)setting?["label"] ?? settingId,
                    Type = NormalizeFeatureType((string)setting?["type"]),
                    Scope = (string)setting?["scope"] ?? "profile",
                    Operator = (string)rule["operator"] ?? "equals",
                    DefaultValue = setting?["default"]?.DeepClone(),
                    ExpectedValue = rule["value"]?.DeepClone(),
                    Values = (setting?["values"] as JArray)?.Values<string>().ToArray()
                             ?? Array.Empty<string>(),
                    Min = (double?)setting?["min"],
                    Max = (double?)setting?["max"],
                    Step = (double?)setting?["step"],
                    Targets = ReadFeatureTargets(rule["targets"] as JObject)
                });
            }
            return values;
        }

        private IReadOnlyList<FeatureTargetInfo> DiscoverFeatureTargets()
        {
            var targets = new List<FeatureTargetInfo>();
            if (!_fuseNativeDocument || _document == null)
                return targets;
            AddFeatureTargets(targets, "trackNodes", _document["tracks"]?["nodes"] as JObject);
            AddFeatureTargets(targets, "trackSegments", _document["tracks"]?["segments"] as JObject);
            AddFeatureTargets(targets, "trackSpans", _document["tracks"]?["spans"] as JObject);
            AddFeatureTargets(targets, "trackAreas", _document["tracks"]?["areas"] as JObject);
            AddFeatureTargets(targets, "loads", _document["operations"]?["loads"] as JObject);
            AddFeatureTargets(targets, "industries", _document["operations"]?["industries"] as JObject);
            AddIndustryComponentTargets(targets, _document["operations"]?["industries"] as JObject);
            AddFeatureTargets(targets, "loaders", _document["operations"]?["loaders"] as JObject);
            AddFeatureTargets(targets, "turntables", _document["operations"]?["turntables"] as JObject);
            AddFeatureTargets(targets, "stations", _document["operations"]?["stations"] as JObject);
            AddFeatureTargets(targets, "scenery", _document["world"]?["scenery"] as JObject);
            AddFeatureTargets(targets, "splineys", _document["world"]?["splineys"] as JObject);
            AddFeatureTargets(targets, "waterSurfaces", _document["world"]?["waterSurfaces"] as JObject);
            AddFeatureTargets(targets, "telegraphPoles", _document["world"]?["telegraphPoles"] as JObject);
            AddFeatureTargets(targets, "mapLabels", _document["world"]?["mapLabels"] as JObject);
            AddFeatureTargets(targets, "mapMasks", _document["world"]?["mapMasks"] as JObject);
            AddFeatureTargets(targets, "mapTiles", _document["world"]?["mapTiles"] as JObject);
            AddFeatureTargets(targets, "sceneClones", _document["world"]?["sceneClones"] as JObject);
            AddFeatureTargets(targets, "progressions", _document["progression"]?["progressions"] as JObject);
            AddFeatureTargets(targets, "mapFeatures", _document["progression"]?["mapFeatures"] as JObject);
            AddFeatureTargets(targets, "whistles", _document["audio"]?["whistles"] as JObject);
            AddFeatureTargets(targets, "horns", _document["audio"]?["horns"] as JObject);
            AddFeatureTargets(targets, "bells", _document["audio"]?["bells"] as JObject);
            return targets
                .OrderBy(target => target.Kind, StringComparer.OrdinalIgnoreCase)
                .ThenBy(target => target.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static void AddFeatureTargets(List<FeatureTargetInfo> result, string kind, JObject dictionary)
        {
            if (dictionary == null)
                return;
            result.AddRange(dictionary.Properties().Select(property => new FeatureTargetInfo
            {
                Kind = kind,
                Id = property.Name
            }));
        }

        private static void AddIndustryComponentTargets(List<FeatureTargetInfo> result, JObject industries)
        {
            if (industries == null)
                return;
            foreach (var industry in industries.Properties())
            {
                if (!(industry.Value?["components"] is JObject components))
                    continue;
                foreach (var component in components.Properties())
                {
                    result.Add(new FeatureTargetInfo
                    {
                        Kind = "industryComponents",
                        Id = industry.Name + "/" + component.Name
                    });
                }
            }
        }

        private static FeatureTargetInfo[] ReadFeatureTargets(JObject targets)
        {
            if (targets == null)
                return Array.Empty<FeatureTargetInfo>();
            return targets.Properties()
                .SelectMany(property => (property.Value as JArray)?.Values<string>()
                    .Select(id => new FeatureTargetInfo { Kind = property.Name, Id = id })
                    ?? Enumerable.Empty<FeatureTargetInfo>())
                .ToArray();
        }

        private static JObject BuildSettingToken(FeatureOptionInfo option)
        {
            var token = new JObject
            {
                ["type"] = option.Type,
                ["label"] = option.Label,
                ["scope"] = option.Scope,
                ["default"] = option.DefaultValue?.DeepClone() ?? JValue.CreateNull(),
                ["reloadRequired"] = true
            };
            if (option.Type == "enum")
                token["values"] = new JArray(option.Values);
            if (option.Type == "number")
            {
                token["min"] = option.Min;
                token["max"] = option.Max;
                token["step"] = option.Step;
            }
            return token;
        }

        private static JObject BuildFeatureRuleToken(FeatureOptionInfo option)
        {
            var grouped = new JObject();
            foreach (var group in option.Targets.GroupBy(target => target.Kind, StringComparer.OrdinalIgnoreCase))
                grouped[group.Key] = new JArray(group.Select(target => target.Id).Distinct(StringComparer.OrdinalIgnoreCase));
            return new JObject
            {
                ["setting"] = option.SettingId,
                ["operator"] = option.Operator,
                ["value"] = option.ExpectedValue?.DeepClone() ?? JValue.CreateNull(),
                ["targets"] = grouped
            };
        }

        private static JObject EnsureFeatureObject(JObject parent, string name)
        {
            if (!(parent[name] is JObject value))
            {
                value = new JObject();
                parent[name] = value;
            }
            return value;
        }

        private static bool FeatureSettingIsReferenced(JObject rules, string settingId)
        {
            if (rules == null || string.IsNullOrWhiteSpace(settingId))
                return false;
            return rules.Properties().Any(property =>
                string.Equals((string)property.Value?["setting"], settingId, StringComparison.OrdinalIgnoreCase));
        }

        private static void ValidateFeatureOption(FeatureOptionInfo option)
        {
            if (option == null)
                throw new ArgumentNullException(nameof(option));
            if (!ValidFeatureId(option.RuleId))
                throw new InvalidOperationException("Rule ID must be a valid FUSE ID without spaces.");
            if (!ValidFeatureId(option.SettingId))
                throw new InvalidOperationException("Setting ID must be a valid FUSE ID without spaces.");
            if (string.IsNullOrWhiteSpace(option.Label))
                throw new InvalidOperationException("Player-facing option label is required.");
            if (option.Targets == null || option.Targets.Length == 0)
                throw new InvalidOperationException("Add at least one authored object to this option.");
            if (option.Type == "enum" && (option.Values == null || option.Values.Length < 2))
                throw new InvalidOperationException("A choice option needs at least two comma-separated values.");
            if (option.Type == "enum"
                && (!option.Values.Contains(option.DefaultValue?.Value<string>(), StringComparer.Ordinal)
                    || !option.Values.Contains(option.ExpectedValue?.Value<string>(), StringComparer.Ordinal)))
            {
                throw new InvalidOperationException(
                    "The default and comparison values must both be listed choices.");
            }
            if (option.Type == "number"
                && (!option.Min.HasValue || !option.Max.HasValue || !option.Step.HasValue
                    || option.Min.Value > option.Max.Value || option.Step.Value <= 0d))
                throw new InvalidOperationException("A slider needs min <= max and a step greater than zero.");
        }

        private static bool ValidFeatureId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || !char.IsLetterOrDigit(value[0]))
                return false;
            return value.All(character => char.IsLetterOrDigit(character)
                                          || character == '.' || character == '_'
                                          || character == ':' || character == '-');
        }

        private static string NormalizeFeatureType(string type)
        {
            if (string.Equals(type, "enum", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "choice", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "select", StringComparison.OrdinalIgnoreCase))
                return "enum";
            if (string.Equals(type, "number", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "float", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "double", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "int", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "integer", StringComparison.OrdinalIgnoreCase))
                return "number";
            return "bool";
        }

        private static string TargetKey(FeatureTargetInfo target) =>
            (target?.Kind ?? string.Empty) + "\u001f" + (target?.Id ?? string.Empty);
    }
}
