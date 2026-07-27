using System;
using Game.Messages;
using Game.State;
using HarmonyLib;
using KeyValue.Runtime;
using Model;
using UI.Builder;
using UI.CarInspector;
using UnityEngine;

namespace StrangeCustoms.Patching;

[HarmonyPatch(typeof(CarInspector), "PopulateEquipmentPanel")]
internal static class AddVisualConditionSlider
{
	private static void Postfix(UIPanelBuilder builder, Car ____car)
	{
		KeyValueObject kvo = ____car.KeyValueObject;
		IConfigurableElement val = ((UIPanelBuilder)(ref builder)).AddField("V-Condition", ((UIPanelBuilder)(ref builder)).AddSlider((Func<float>)delegate
		{
			//IL_000b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0010: Unknown result type (might be due to invalid IL or missing references)
			Value val2 = kvo["_visualCondition"];
			return ((Value)(ref val2)).FloatValueOrDefault(1f);
		}, (Func<string>)delegate
		{
			//IL_000b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0010: Unknown result type (might be due to invalid IL or missing references)
			Value val2 = kvo["_visualCondition"];
			return (((Value)(ref val2)).FloatValueOrDefault(1f) * 100f).ToString("0'%'");
		}, (Action<float>)delegate(float v)
		{
			//IL_0011: Unknown result type (might be due to invalid IL or missing references)
			//IL_001b: Unknown result type (might be due to invalid IL or missing references)
			StateManager.ApplyLocal((IGameMessage)(object)new PropertyChange(kvo.RegisteredId, "_visualCondition", (IPropertyValue)(object)new FloatPropertyValue(v)));
		}, 0f, 1f, false, (Action<float>)null));
		((Transform)val.RectTransform).SetSiblingIndex(((Transform)val.RectTransform).GetSiblingIndex() - 2);
	}
}
