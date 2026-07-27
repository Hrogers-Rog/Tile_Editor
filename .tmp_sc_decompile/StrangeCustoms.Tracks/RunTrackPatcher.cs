using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Progression;
using HarmonyLib;
using Model.OpsNew;
using Railloader;

namespace StrangeCustoms.Tracks;

[HarmonyPatch]
internal static class RunTrackPatcher
{
	private static IEnumerable<MethodBase> TargetMethods()
	{
		yield return AccessTools.Method(typeof(ProgressionManager), "Awake", (Type[])null, (Type[])null);
		yield return AccessTools.Method(typeof(MapFeatureManager), "Awake", (Type[])null, (Type[])null);
		yield return AccessTools.Method(typeof(OpsController), "Awake", (Type[])null, (Type[])null);
	}

	private static void Prefix()
	{
		SingletonPluginBase<StrangeCustomsPlugin>.Shared?.RunGraphPatcher();
	}
}
