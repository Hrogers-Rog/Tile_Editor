using System.Collections.Generic;
using Game.Messages;
using Game.State;
using HarmonyLib;
using Model;
using Railloader;
using UnityEngine;

namespace StrangeCustoms.Patching;

[HarmonyPatch(typeof(TrainController), "HandleCreateCarsAsTrain")]
internal static class CarsAdded
{
	private static void Postfix(List<Car> __result)
	{
		//IL_00b3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bd: Unknown result type (might be due to invalid IL or missing references)
		Settings settings = SingletonPluginBase<StrangeCustomsPlugin>.Shared?.Settings;
		StrangeCustomsPlugin shared = SingletonPluginBase<StrangeCustomsPlugin>.Shared;
		if (!(((shared != null) ? new bool?(((PluginBase)shared).IsEnabled) : ((bool?)null)) ?? false))
		{
			return;
		}
		bool? flag = settings?.RandomizeVisualCondition;
		if (!flag.HasValue || flag != true || !StateManager.IsHost)
		{
			return;
		}
		using (StateManager.TransactionScope())
		{
			foreach (Car item in __result)
			{
				StateManager.ApplyLocal((IGameMessage)(object)new PropertyChange(item.KeyValueObject.RegisteredId, "_visualCondition", (IPropertyValue)(object)new FloatPropertyValue(Mathf.Clamp01(Random.Range(settings.RandomMinimumValue, settings.RandomMaximumValue)))));
			}
		}
	}
}
