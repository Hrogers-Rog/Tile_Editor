using System;
using System.Collections.Generic;
using HarmonyLib;
using KeyValue.Runtime;
using Model;

namespace StrangeCustoms.Patching;

[HarmonyPatch]
internal static class AddVisualConditionHandler
{
	[HarmonyPatch(typeof(Car), "UpdateMaterialsForCondition")]
	[HarmonyReversePatch(/*Could not decode attribute arguments.*/)]
	private static void UpdateMaterialsForCondition(Car __instance)
	{
		throw new NotImplementedException();
	}

	[HarmonyPatch(typeof(Car), "SetupKeyValueObject")]
	[HarmonyPostfix]
	private static void Postfix(Car __instance, HashSet<IDisposable> ___Observers)
	{
		___Observers.Add(__instance.KeyValueObject.Observe("_visualCondition", (Action<Value>)delegate
		{
			UpdateMaterialsForCondition(__instance);
		}, false));
	}
}
