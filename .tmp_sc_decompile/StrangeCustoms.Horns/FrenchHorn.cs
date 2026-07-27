using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Messages;
using Game.State;
using HarmonyLib;
using KeyValue.Runtime;
using Model;
using Model.ComponentBuilders;
using Model.Definition;
using Model.Definition.Components;
using Model.Definition.Data;
using Newtonsoft.Json;
using Railloader;
using Serilog;
using StrangeCustoms.HellsBells;
using UI.Builder;
using UI.CarCustomizeWindow;

namespace StrangeCustoms.Horns;

[HarmonyPatch]
internal static class FrenchHorn
{
	private static ILogger logger = Log.ForContext(typeof(FrenchHorn));

	private static List<CustomHornProfile> horns = null;

	private static List<CustomBellProfile> bells = null;

	internal const string HornKey = "horn.custom";

	internal const string BellKey = "sc.bell.custom";

	internal const string DefaultName = "Default";

	[HarmonyPatch(typeof(HornComponentBuilder), "Build")]
	[HarmonyPostfix]
	private static void WireUpBuilder(ComponentBuilderContext ctx, Component component)
	{
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Expected O, but got Unknown
		((ComponentBuilderContext)(ref ctx)).GameObject.AddComponent<HornController>().Configure((HornComponent)component);
	}

	[HarmonyPatch(typeof(CarCustomizeWindow), "BuildSoundTab")]
	[HarmonyPostfix]
	private static void AddHornUI(UIPanelBuilder builder, Car ____car)
	{
		StrangeCustomsPlugin shared = SingletonPluginBase<StrangeCustomsPlugin>.Shared;
		if (shared == null || !((PluginBase)shared).IsEnabled)
		{
			return;
		}
		if (((ObjectDefinition)____car.Definition).Components.OfType<HornComponent>().FirstOrDefault() != null)
		{
			LoadHornFiles();
			((UIPanelBuilder)(ref builder)).AddSection("Horn", (Action<UIPanelBuilder>)delegate(UIPanelBuilder val2)
			{
				//IL_0051: Unknown result type (might be due to invalid IL or missing references)
				//IL_0056: Unknown result type (might be due to invalid IL or missing references)
				List<string> names = horns.Select((CustomHornProfile s) => s.Name).ToList();
				Value val = ____car.KeyValueObject["horn.custom"];
				string stringValue = ((Value)(ref val)).StringValue;
				int num = names.IndexOf(stringValue);
				if (num == -1)
				{
					num = 0;
				}
				((UIPanelBuilder)(ref val2)).AddField("Horn", ((UIPanelBuilder)(ref val2)).AddDropdown(names, num, (Action<int>)delegate(int s)
				{
					//IL_0021: Unknown result type (might be due to invalid IL or missing references)
					//IL_002b: Unknown result type (might be due to invalid IL or missing references)
					StateManager.ApplyLocal((IGameMessage)(object)new PropertyChange(____car.id, "horn.custom", (IPropertyValue)(object)new StringPropertyValue(names[s])));
				}));
			}, 0f);
		}
		if (((ObjectDefinition)____car.Definition).Components.OfType<BellComponent>().FirstOrDefault() == null)
		{
			return;
		}
		LoadBellProfiles();
		((UIPanelBuilder)(ref builder)).AddSection("Bell", (Action<UIPanelBuilder>)delegate(UIPanelBuilder val2)
		{
			//IL_0051: Unknown result type (might be due to invalid IL or missing references)
			//IL_0056: Unknown result type (might be due to invalid IL or missing references)
			List<string> names = bells.Select((CustomBellProfile s) => s.Name).ToList();
			Value val = ____car.KeyValueObject["sc.bell.custom"];
			string stringValue = ((Value)(ref val)).StringValue;
			int num = names.IndexOf(stringValue);
			if (num == -1)
			{
				num = 0;
			}
			((UIPanelBuilder)(ref val2)).AddField("Bell", ((UIPanelBuilder)(ref val2)).AddDropdown(names, num, (Action<int>)delegate(int s)
			{
				//IL_0021: Unknown result type (might be due to invalid IL or missing references)
				//IL_002b: Unknown result type (might be due to invalid IL or missing references)
				StateManager.ApplyLocal((IGameMessage)(object)new PropertyChange(____car.id, "sc.bell.custom", (IPropertyValue)(object)new StringPropertyValue(names[s])));
			}));
		}, 0f);
	}

	internal static List<CustomBellProfile> LoadBellProfiles(bool forceRefresh = false)
	{
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		if (bells != null && !forceRefresh)
		{
			return bells;
		}
		IModdingContext moddingContext = SingletonPluginBase<StrangeCustomsPlugin>.Shared.ModdingContext;
		bells = new List<CustomBellProfile>();
		IEnumerator<ModMixinto> enumerator = moddingContext.GetMixintos("bells").GetEnumerator();
		if (enumerator.MoveNext())
		{
			List<CustomBellProfile> list = new List<CustomBellProfile>();
			string file = default(string);
			do
			{
				ModMixinto current = enumerator.Current;
				try
				{
					logger.Debug<string, string>("Trying to load {Mod}/{BellFile}...", ((IModDefinition)((ModMixinto)(ref current)).Source).Id, ((ModMixinto)(ref current)).Mixinto);
					CustomBellProfile[] array = JsonConvert.DeserializeObject<CustomBellProfile[]>(File.ReadAllText(((ModMixinto)(ref current)).Mixinto));
					list.Clear();
					string directoryName = Path.GetDirectoryName(((ModMixinto)(ref current)).Mixinto);
					foreach (CustomBellProfile customBellProfile in array)
					{
						if (string.IsNullOrWhiteSpace(customBellProfile.Name))
						{
							throw new ArgumentException("Missing name");
						}
						if (!moddingContext.TryResolveFilePath(directoryName, ((IModDefinition)((ModMixinto)(ref current)).Source).Directory, customBellProfile.File, false, ref file))
						{
							throw new ArgumentException("Invalid audio clip file name '" + customBellProfile.File + "'");
						}
						customBellProfile.File = file;
						list.Add(customBellProfile);
					}
					logger.Debug<int, IMod>("Loaded {Count} from {File}", list.Count, ((ModMixinto)(ref current)).Source);
					bells.AddRange(list);
				}
				catch (Exception ex)
				{
					logger.Error<IMod, string>(ex, "Could not parse bell JSON {File}: {ExceptionMessage}", ((ModMixinto)(ref current)).Source, ex.Message);
				}
			}
			while (enumerator.MoveNext());
			bells.Sort((CustomBellProfile a, CustomBellProfile b) => a.Name.CompareTo(b.Name));
			bells.Insert(0, new CustomBellProfile
			{
				Name = "Default",
				File = null
			});
		}
		else
		{
			logger.Information("No custom horns found.");
		}
		return bells;
	}

	internal static List<CustomHornProfile> LoadHornFiles(bool forceRefresh = false)
	{
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		if (horns != null && !forceRefresh)
		{
			return horns;
		}
		IModdingContext moddingContext = SingletonPluginBase<StrangeCustomsPlugin>.Shared.ModdingContext;
		horns = new List<CustomHornProfile>();
		IEnumerator<ModMixinto> enumerator = moddingContext.GetMixintos("horns").GetEnumerator();
		if (enumerator.MoveNext())
		{
			List<CustomHornProfile> list = new List<CustomHornProfile>();
			string file = default(string);
			do
			{
				ModMixinto current = enumerator.Current;
				try
				{
					logger.Debug<string, string>("Trying to load {Mod}/{HornFile}...", ((IModDefinition)((ModMixinto)(ref current)).Source).Id, ((ModMixinto)(ref current)).Mixinto);
					CustomHornProfile[] array = JsonConvert.DeserializeObject<CustomHornProfile[]>(File.ReadAllText(((ModMixinto)(ref current)).Mixinto));
					list.Clear();
					string directoryName = Path.GetDirectoryName(((ModMixinto)(ref current)).Mixinto);
					foreach (CustomHornProfile customHornProfile in array)
					{
						if (string.IsNullOrWhiteSpace(customHornProfile.Name))
						{
							throw new ArgumentException("Missing name");
						}
						int? num = customHornProfile.Layers?.Length;
						if (!num.HasValue || num != 2)
						{
							throw new ArgumentException($"Expected exactly two layers, but got {customHornProfile.Layers?.Length}");
						}
						for (int j = 0; j < customHornProfile.Layers.Length; j++)
						{
							CustomHornLayer customHornLayer = customHornProfile.Layers[j];
							if (!moddingContext.TryResolveFilePath(directoryName, ((IModDefinition)((ModMixinto)(ref current)).Source).Directory, customHornProfile.Layers[j].File, false, ref file))
							{
								throw new ArgumentException("Invalid layer '" + customHornLayer.File + "'");
							}
							customHornLayer.File = file;
							num = customHornLayer.Keyframes?.Length;
							if (!num.HasValue || num.GetValueOrDefault() <= 0)
							{
								throw new ArgumentException($"Layer {j} does not define keyframes");
							}
							customHornProfile.Layers[j] = customHornLayer;
						}
						list.Add(customHornProfile);
					}
					logger.Debug<int, IMod>("Loaded {Count} from {File}", list.Count, ((ModMixinto)(ref current)).Source);
					horns.AddRange(list);
				}
				catch (Exception ex)
				{
					logger.Error<IMod, string>(ex, "Could not parse horn JSON {File}: {ExceptionMessage}", ((ModMixinto)(ref current)).Source, ex.Message);
				}
			}
			while (enumerator.MoveNext());
			horns.Sort((CustomHornProfile a, CustomHornProfile b) => a.Name.CompareTo(b.Name));
			horns.Insert(0, new CustomHornProfile
			{
				Name = "Default"
			});
		}
		else
		{
			logger.Information("No custom horns found.");
		}
		return horns;
	}
}
