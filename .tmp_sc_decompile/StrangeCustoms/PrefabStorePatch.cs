using System;
using System.IO;
using System.Reflection;
using AssetPack.Runtime;
using HarmonyLib;
using Model.Database;
using Railloader;
using Serilog;

namespace StrangeCustoms;

[HarmonyPatch]
internal static class PrefabStorePatch
{
	private const string IdentifierPrefix = "zsc://";

	private const string FolderName = "SCAssetPacks";

	[HarmonyPatch(typeof(PrefabStore), "Create")]
	[HarmonyPostfix]
	private static void AddCustomStuff(PrefabStore __result)
	{
		StrangeCustomsPlugin shared = SingletonPluginBase<StrangeCustomsPlugin>.Shared;
		if (shared == null || !((PluginBase)shared).IsEnabled)
		{
			return;
		}
		MethodInfo methodInfo = AccessTools.Method(((object)__result).GetType(), "AddStore", (Type[])null, (Type[])null);
		ILogger val = Log.ForContext(typeof(PrefabStorePatch));
		string modsBaseDirectory = shared.ModdingContext.ModsBaseDirectory;
		foreach (IMod mod in shared.ModdingContext.Mods)
		{
			string path = Path.Combine(((IModDefinition)mod).Directory, "SCAssetPacks");
			if (!Directory.Exists(path))
			{
				continue;
			}
			val.Information<string>("Add asset packs from {Directory}...", ((IModDefinition)mod).Directory);
			string[] directories = Directory.GetDirectories(path);
			foreach (string text in directories)
			{
				if (!text.StartsWith(modsBaseDirectory))
				{
					val.Error("Cannot add {Directory}: ... somehow outside the mods folder?");
					continue;
				}
				string text2 = text.Substring(modsBaseDirectory.Length + 1).Replace('\\', '/');
				val.Debug<string>("Add {Directory}", text2);
				methodInfo.Invoke(__result, new object[2]
				{
					"zsc://" + text2,
					(object)(StoreLocation)1
				});
			}
		}
	}

	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	[HarmonyPrefix]
	private static bool TransformBasePath(AssetPackRuntimeStore __instance, ref string __result)
	{
		StrangeCustomsPlugin shared = SingletonPluginBase<StrangeCustomsPlugin>.Shared;
		if (shared == null || !((PluginBase)shared).IsEnabled)
		{
			return true;
		}
		if (__instance.Identifier.StartsWith("zsc://"))
		{
			__result = Path.Combine(shared.ModdingContext.ModsBaseDirectory, __instance.Identifier.Substring("zsc://".Length));
			return false;
		}
		return true;
	}
}
