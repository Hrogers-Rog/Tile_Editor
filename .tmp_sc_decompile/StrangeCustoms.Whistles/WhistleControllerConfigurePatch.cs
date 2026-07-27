using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Threading;
using System.Threading.Tasks;
using AssetPack.Runtime;
using HarmonyLib;
using Model.Database;
using Model.Definition;
using Model.Definition.Data;
using Railloader;
using RollingStock.Steam;
using UnityEngine;

namespace StrangeCustoms.Whistles;

[HarmonyPatch(typeof(WhistleController))]
internal static class WhistleControllerConfigurePatch
{
	private static WhistleDefinition FakeItTillYouMakeIt(IPrefabStore store, string definitionIdentifier, out ObjectMetadata metadata)
	{
		StrangeCustomsPlugin shared = SingletonPluginBase<StrangeCustomsPlugin>.Shared;
		bool? flag = ((shared != null) ? new bool?(((PluginBase)shared).IsEnabled) : ((bool?)null));
		if (!flag.HasValue || flag != true || !SpanishWhistle.TryFind(definitionIdentifier, out TypedContainerItem<WhistleDefinition> result))
		{
			return store.DefinitionForIdentifier<WhistleDefinition>(definitionIdentifier, ref metadata);
		}
		metadata = result.Metadata;
		return result.Definition;
	}

	private static async Task<LoadedAssetReference<AudioClip>> LoadAudioClip(IPrefabStore store, AbsoluteAssetReference assetReference, CancellationToken ct)
	{
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		StrangeCustomsPlugin shared = SingletonPluginBase<StrangeCustomsPlugin>.Shared;
		bool? flag = ((shared != null) ? new bool?(((PluginBase)shared).IsEnabled) : ((bool?)null));
		if (flag.HasValue && flag == true && ((AbsoluteAssetReference)(ref assetReference)).AssetPackIdentifier == "@zamu/strange-customs")
		{
			if (!SpanishWhistle.TryGetAudioClipPath(((AbsoluteAssetReference)(ref assetReference)).AssetIdentifier, out string path))
			{
				throw new ArgumentException("Cannot find custom whistle '" + ((AbsoluteAssetReference)(ref assetReference)).AssetIdentifier + "' (???)");
			}
			TaskCompletionSource<AudioClip> taskCompletionSource = new TaskCompletionSource<AudioClip>();
			FileCache.Instance.LoadAudioClip(path, taskCompletionSource.SetResult);
			AudioClip obj = await taskCompletionSource.Task;
			ct.ThrowIfCancellationRequested();
			return new LoadedAssetReference<AudioClip>(obj, SpanishWhistle.GetStore(), ((AbsoluteAssetReference)(ref assetReference)).AssetIdentifier);
		}
		return await store.LoadAssetAsync<AudioClip>(((AbsoluteAssetReference)(ref assetReference)).AssetPackIdentifier, ((AbsoluteAssetReference)(ref assetReference)).AssetIdentifier, ct);
	}

	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	[HarmonyPatch("Configure", new Type[] { typeof(WhistleCustomizationSettings) })]
	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		return new CodeMatcher(instructions, generator).MatchStartForward((CodeMatch[])(object)new CodeMatch[1] { CodeMatch.Calls(AccessTools.Method(typeof(IPrefabStore), "DefinitionForIdentifier", new Type[2]
		{
			typeof(string),
			typeof(ObjectMetadata).MakeByRefType()
		}, new Type[1] { typeof(WhistleDefinition) })) }).SetOperandAndAdvance((object)AccessTools.Method(typeof(WhistleControllerConfigurePatch), "FakeItTillYouMakeIt", (Type[])null, (Type[])null)).SearchForward((Func<CodeInstruction, bool>)((CodeInstruction c) => CodeInstructionExtensions.Calls(c, AccessTools.Method(typeof(PrefabStoreExtensions), "LoadAssetAsync", (Type[])null, new Type[1] { typeof(AudioClip) }))))
			.SetOperandAndAdvance((object)AccessTools.Method(typeof(WhistleControllerConfigurePatch), "LoadAudioClip", (Type[])null, (Type[])null))
			.InstructionEnumeration();
	}
}
