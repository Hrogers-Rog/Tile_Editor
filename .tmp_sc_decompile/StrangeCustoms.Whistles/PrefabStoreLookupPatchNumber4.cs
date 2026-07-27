using AssetPack.Runtime;
using HarmonyLib;
using Model.Database;
using Railloader;

namespace StrangeCustoms.Whistles;

[HarmonyPatch]
internal static class PrefabStoreLookupPatchNumber4
{
	[HarmonyPatch(typeof(PrefabStore), "AssetPackContainingIdentifier")]
	[HarmonyPrefix]
	private static bool Prefix(string identifier, ref AssetPackRuntimeStore __result)
	{
		StrangeCustomsPlugin shared = SingletonPluginBase<StrangeCustomsPlugin>.Shared;
		bool? flag = ((shared != null) ? new bool?(((PluginBase)shared).IsEnabled) : ((bool?)null));
		if (flag.HasValue && flag == true && SpanishWhistle.IsCustomStore(identifier, out __result))
		{
			return false;
		}
		return true;
	}
}
