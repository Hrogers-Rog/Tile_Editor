using System.Linq;
using System.Reflection;
using HarmonyLib;
using Model.OpsNew;
using StrangeCustoms.Tracks.Industries;
using UI;

namespace StrangeCustoms.Patching;

[HarmonyPatch]
internal static class ExtendPickerData
{
	private static MethodBase TargetMethod()
	{
		return AccessTools.GetDeclaredMethods(typeof(DropdownLocationPickerRowData)).Single((MethodInfo s) => s.Name.Contains("TitleForComponent"));
	}

	public static bool Prefix(IndustryComponent ic, ref string __result)
	{
		if (ic is ICustomIndustryTitle customIndustryTitle && !string.IsNullOrEmpty(customIndustryTitle.Title))
		{
			__result = customIndustryTitle.Title;
			return false;
		}
		return true;
	}
}
