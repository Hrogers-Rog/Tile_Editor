using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection.Emit;
using HarmonyLib;
using Model.Database;
using Model.Definition;
using Model.Definition.Data;
using UI.CarCustomizeWindow;

namespace StrangeCustoms.Whistles;

[HarmonyPatch(typeof(CarCustomizeWindow), "BuildSoundTabWhistle")]
internal static class HijackWhistlesPatch
{
	private static IEnumerable<TypedContainerItem<WhistleDefinition>> GetWhistles(IEnumerable<TypedContainerItem<WhistleDefinition>> previous)
	{
		return previous.Concat<TypedContainerItem<WhistleDefinition>>(SpanishWhistle.LoadWhistleDefinitions());
	}

	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		return new CodeMatcher(instructions, generator).MatchStartForward((CodeMatch[])(object)new CodeMatch[1] { CodeMatch.Calls(AccessTools.Method(typeof(IPrefabStore), "AllDefinitionInfosOfType", (Type[])null, new Type[1] { typeof(WhistleDefinition) })) }).ThrowIfNotMatch("Could not find call to IPrefabStore", Array.Empty<CodeMatch>()).Advance(1)
			.Insert((CodeInstruction[])(object)new CodeInstruction[1] { CodeInstruction.Call<IEnumerable<TypedContainerItem<WhistleDefinition>>>((Expression<Action<IEnumerable<TypedContainerItem<WhistleDefinition>>>>)((IEnumerable<TypedContainerItem<WhistleDefinition>> p) => GetWhistles(p))) })
			.InstructionEnumeration();
	}
}
