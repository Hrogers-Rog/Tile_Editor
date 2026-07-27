using HarmonyLib;
using Helpers;
using Railloader;
using UnityEngine;

namespace StrangeCustoms.Tracks;

[HarmonyPatch(typeof(ObjectPicker), "QueryTooltipInfo")]
internal static class MoreTooltipInfo
{
	private static void Postfix(Ray ray, ref TooltipInfo __result)
	{
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0052: Unknown result type (might be due to invalid IL or missing references)
		//IL_009a: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b0: Unknown result type (might be due to invalid IL or missing references)
		StrangeCustomsPlugin shared = SingletonPluginBase<StrangeCustomsPlugin>.Shared;
		RaycastHit val = default(RaycastHit);
		if (shared != null && shared.Settings != null && shared.Settings.AllowTrackAutoReload && Physics.Raycast(ray, ref val, 100f, (1 << Layers.Terrain) | (1 << Layers.Track)))
		{
			Vector3 val2 = WorldTransformer.WorldToGame(((RaycastHit)(ref val)).point);
			_ = ((Component)((RaycastHit)(ref val)).collider).gameObject.layer;
			if (__result.Text == null)
			{
				__result.Text = string.Empty;
			}
			else
			{
				__result.Text += "\n";
			}
			__result.Text += $"{val2.x:F0}/{val2.y:F0}/{val2.z:F0}";
		}
	}
}
