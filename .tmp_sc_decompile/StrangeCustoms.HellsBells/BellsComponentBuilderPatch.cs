using System;
using System.Collections.Generic;
using System.Linq;
using Audio;
using HarmonyLib;
using KeyValue.Runtime;
using Model;
using Model.ComponentBuilders;
using Model.Definition.Components;
using RollingStock;
using StrangeCustoms.Horns;
using UnityEngine;

namespace StrangeCustoms.HellsBells;

[HarmonyPatch]
internal static class BellsComponentBuilderPatch
{
	[HarmonyPatch(typeof(BellComponentBuilder), "_Build")]
	[HarmonyPostfix]
	private static void Postfix(ComponentBuilderContext ctx, BellComponent component)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		Bell bell = ((ComponentBuilderContext)(ref ctx)).GameObject.GetComponentInChildren<Bell>();
		CustomBellProfile ocClip = new CustomBellProfile
		{
			IndexedClip = bell.player.indexedClip
		};
		((ComponentBuilderContext)(ref ctx)).ObserveProperty("sc.bell.custom", (Action<Value>)delegate(Value value)
		{
			//IL_0050: Unknown result type (might be due to invalid IL or missing references)
			//IL_003d: Unknown result type (might be due to invalid IL or missing references)
			List<CustomBellProfile> list = FrenchHorn.LoadBellProfiles();
			string newBell = ((Value)(ref value)).StringValue;
			CustomBellProfile customBellProfile = list.Find((CustomBellProfile p) => p.Name == newBell);
			if (customBellProfile != null && customBellProfile.File != null)
			{
				ApplyBell(bell, ctx, customBellProfile);
			}
			else
			{
				ApplyBell(bell, ctx, ocClip);
			}
		});
	}

	[HarmonyPatch(typeof(IntegerLoopingPlayer), "PrepareClips")]
	[HarmonyReversePatch(/*Could not decode attribute arguments.*/)]
	private static void PrepareClips(IntegerLoopingPlayer instance)
	{
		throw new NotImplementedException();
	}

	private static void ApplyBell(Bell bell, ComponentBuilderContext ctx, CustomBellProfile profile)
	{
		IndexedClipDescriptor ic = profile.IndexedClip;
		if ((Object)(object)ic == (Object)null)
		{
			IntegerLoopingPlayer player = bell.player;
			IndexedClipDescriptor indexedClip = (profile.IndexedClip = Object.Instantiate<IndexedClipDescriptor>(bell.player.indexedClip));
			ic = (player.indexedClip = indexedClip);
			ic.indexes = Array.Empty<Index>();
			FileCache.Instance.LoadAudioClip(profile.File, delegate(AudioClip clip)
			{
				ic.clip = clip;
				if (profile.IndexTimes != null)
				{
					ic.indexes = ((IEnumerable<float>)profile.IndexTimes).Select((Func<float, Index>)((float s) => new Index
					{
						time = s
					})).ToArray();
				}
				if ((Object)(object)bell != (Object)null && (Object)(object)bell.player != (Object)null)
				{
					PrepareClips(bell.player);
				}
			});
		}
		else
		{
			bell.player.indexedClip = ic;
			PrepareClips(bell.player);
		}
	}
}
