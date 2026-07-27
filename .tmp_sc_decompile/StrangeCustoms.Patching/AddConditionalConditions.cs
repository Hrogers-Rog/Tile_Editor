using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using KeyValue.Runtime;
using Model;
using Railloader;
using Serilog;
using UnityEngine;

namespace StrangeCustoms.Patching;

[HarmonyPatch]
internal static class AddConditionalConditions
{
	private static MethodBase TargetMethod()
	{
		return AccessTools.GetDeclaredMethods(typeof(Car)).Single((MethodInfo s) => s.Name.Contains("<UpdateMaterialsForCondition>g__Apply"));
	}

	private static float GetCondition(float originalCondition, Car car)
	{
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		if (!((Object)(object)car.KeyValueObject == (Object)null) && !car.ghost)
		{
			StrangeCustomsPlugin shared = SingletonPluginBase<StrangeCustomsPlugin>.Shared;
			if (((shared != null) ? new bool?(((PluginBase)shared).IsEnabled) : ((bool?)null)) ?? false)
			{
				Value val = car.KeyValueObject["_visualCondition"];
				float num = ((Value)(ref val)).FloatValueOrDefault(originalCondition);
				if (SingletonPluginBase<StrangeCustomsPlugin>.Shared.Settings.DecoupleConditionLimits)
				{
					return num;
				}
				return Mathf.Min(originalCondition, num);
			}
		}
		return originalCondition;
	}

	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
	{
		//IL_015b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0165: Expected O, but got Unknown
		//IL_0171: Unknown result type (might be due to invalid IL or missing references)
		//IL_017b: Expected O, but got Unknown
		MethodInfo inverseLerp = SymbolExtensions.GetMethodInfo((Expression<Action>)(() => Mathf.InverseLerp(0f, 0f, 0f)));
		MethodInfo methodInfo = SymbolExtensions.GetMethodInfo((Expression<Action>)(() => GetCondition(0f, null)));
		List<CodeInstruction> list = instructions.ToList();
		int num = list.FindIndex((CodeInstruction c) => CodeInstructionExtensions.Calls(c, inverseLerp));
		if (num == -1)
		{
			Log.ForContext(typeof(AddConditionalConditions)).Error("Could not find call to InverseLerp. Patch failed.");
			return list;
		}
		_ = list[num];
		Label label = generator.DefineLabel();
		list[num] = CodeInstructionExtensions.WithLabels(list[num], new Label[1] { label });
		list.Insert(num++, new CodeInstruction(OpCodes.Ldarg_0, (object)null));
		list.Insert(num++, new CodeInstruction(OpCodes.Call, (object)methodInfo));
		return list;
	}
}
