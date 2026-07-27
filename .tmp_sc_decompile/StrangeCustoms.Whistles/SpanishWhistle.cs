using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AssetPack.Runtime;
using Model.Definition;
using Model.Definition.Data;
using Newtonsoft.Json;
using Railloader;
using Serilog;

namespace StrangeCustoms.Whistles;

internal static class SpanishWhistle
{
	public const string AssetPackName = "@zamu/strange-customs";

	private const string audioClipPrefix = "@scac/";

	private static AssetPackRuntimeStore? lateForMeeting;

	private static ILogger logger = Log.ForContext(typeof(SpanishWhistle));

	private static List<TypedContainerItem<WhistleDefinition>>? whistles;

	private static Dictionary<string, string> audioClipMapping = null;

	internal static AssetPackRuntimeStore GetStore()
	{
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Expected O, but got Unknown
		object obj = lateForMeeting;
		if (obj == null)
		{
			AssetPackRuntimeStore val = new AssetPackRuntimeStore("@zamu/strange-customs", (StoreLocation)1);
			lateForMeeting = val;
			obj = (object)val;
		}
		return (AssetPackRuntimeStore)obj;
	}

	public static bool TryGetAudioClipPath(string identifier, out string? path)
	{
		if (!identifier.StartsWith("@scac/"))
		{
			path = null;
			return false;
		}
		return audioClipMapping.TryGetValue(identifier, out path);
	}

	public static List<TypedContainerItem<WhistleDefinition>> LoadWhistleDefinitions(bool forceRefresh = false)
	{
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_0058: Unknown result type (might be due to invalid IL or missing references)
		//IL_0158: Unknown result type (might be due to invalid IL or missing references)
		//IL_015d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0165: Unknown result type (might be due to invalid IL or missing references)
		//IL_016f: Expected O, but got Unknown
		//IL_016f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0181: Expected O, but got Unknown
		//IL_0199: Unknown result type (might be due to invalid IL or missing references)
		//IL_019e: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ab: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b6: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c3: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d3: Expected O, but got Unknown
		if (whistles != null && !forceRefresh)
		{
			return whistles;
		}
		IModdingContext moddingContext = SingletonPluginBase<StrangeCustomsPlugin>.Shared.ModdingContext;
		whistles = new List<TypedContainerItem<WhistleDefinition>>();
		IEnumerator<ModMixinto> enumerator = moddingContext.GetMixintos("whistles").GetEnumerator();
		if (enumerator.MoveNext())
		{
			List<TypedContainerItem<WhistleDefinition>> list = new List<TypedContainerItem<WhistleDefinition>>();
			Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			string key = default(string);
			do
			{
				ModMixinto current = enumerator.Current;
				try
				{
					logger.Debug<string, string>("Trying to load {Mod}/{WhistleFile}...", ((IModDefinition)((ModMixinto)(ref current)).Source).Id, ((ModMixinto)(ref current)).Mixinto);
					CustomWhistle[] array = JsonConvert.DeserializeObject<CustomWhistle[]>(File.ReadAllText(((ModMixinto)(ref current)).Mixinto));
					list.Clear();
					string directoryName = Path.GetDirectoryName(((ModMixinto)(ref current)).Mixinto);
					foreach (CustomWhistle customWhistle in array)
					{
						if (string.IsNullOrWhiteSpace(customWhistle.Name))
						{
							throw new ArgumentException("Missing name");
						}
						if (string.IsNullOrWhiteSpace(customWhistle.Clip))
						{
							throw new ArgumentException("Clip is empty");
						}
						if (!moddingContext.TryResolveFilePath(directoryName, ((IModDefinition)((ModMixinto)(ref current)).Source).Directory, customWhistle.Clip, false, ref key))
						{
							throw new ArgumentException("Invalid path '" + customWhistle.Clip + "'");
						}
						if (!dictionary.TryGetValue(key, out var value))
						{
							dictionary.Add(key, value = string.Format("{0}{1}", "@scac/", dictionary.Count));
						}
						list.Add(new TypedContainerItem<WhistleDefinition>
						{
							Definition = new WhistleDefinition
							{
								Audio = new AssetReference("@zamu/strange-customs", value),
								Model = customWhistle.Model
							},
							Identifier = "sc." + customWhistle.Name,
							Metadata = new ObjectMetadata
							{
								Name = customWhistle.Name,
								Credits = string.Empty,
								Description = customWhistle.Name,
								Tags = new List<string>()
							}
						});
					}
					logger.Debug<int, IMod>("Loaded {Count} whistles from {File}", list.Count, ((ModMixinto)(ref current)).Source);
					whistles.AddRange(list);
				}
				catch (Exception ex)
				{
					logger.Error<IMod, Exception>(ex, "Could not parse whistle JSON {File}: {ExceptionMessage}", ((ModMixinto)(ref current)).Source, ex);
				}
			}
			while (enumerator.MoveNext());
			whistles.Sort((TypedContainerItem<WhistleDefinition> a, TypedContainerItem<WhistleDefinition> b) => a.Metadata.Name.CompareTo(b.Metadata.Name));
			audioClipMapping = dictionary.ToDictionary((KeyValuePair<string, string> p) => p.Value, (KeyValuePair<string, string> p) => p.Key);
		}
		else
		{
			logger.Information("No custom whistles found.");
		}
		return whistles;
	}

	public static bool TryFind(string assetIdentifier, out TypedContainerItem<WhistleDefinition>? result)
	{
		foreach (TypedContainerItem<WhistleDefinition> item in LoadWhistleDefinitions())
		{
			if (item.Identifier == assetIdentifier)
			{
				result = item;
				return true;
			}
		}
		result = null;
		return false;
	}

	public static bool IsCustomStore(string identifier, out AssetPackRuntimeStore? result)
	{
		foreach (TypedContainerItem<WhistleDefinition> item in LoadWhistleDefinitions())
		{
			if (item.Identifier == identifier)
			{
				result = GetStore();
				return true;
			}
		}
		result = null;
		return false;
	}
}
