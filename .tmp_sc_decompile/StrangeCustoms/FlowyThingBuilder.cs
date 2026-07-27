using System;
using System.Collections.Generic;
using System.Linq;
using GalaSoft.MvvmLight.Messaging;
using Game.Events;
using HarmonyLib;
using Helpers;
using Map.Runtime.MaskComponents;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace StrangeCustoms;

public class FlowyThingBuilder : ISplineyBuilder
{
	private Dictionary<string, SplineProfile> profiles = new Dictionary<string, SplineProfile>();

	internal static FieldRef<RiverBuilder, SplineProfile> splineProfile = AccessTools.FieldRefAccess<RiverBuilder, SplineProfile>("splineProfile");

	private Transform rivers;

	public FlowyThingBuilder()
	{
		Messenger.Default.Register<MapWillUnloadEvent>((object)this, (Action<MapWillUnloadEvent>)OnMapWillUnload);
	}

	private void OnMapWillUnload(MapWillUnloadEvent @event)
	{
		profiles.Clear();
	}

	public GameObject BuildSpliney(string id, Transform parentTransform, JObject data)
	{
		//IL_013d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0143: Expected O, but got Unknown
		//IL_0163: Unknown result type (might be due to invalid IL or missing references)
		//IL_0187: Unknown result type (might be due to invalid IL or missing references)
		//IL_0195: Unknown result type (might be due to invalid IL or missing references)
		//IL_019a: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a6: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_01bd: Unknown result type (might be due to invalid IL or missing references)
		if (profiles.Count == 0)
		{
			profiles = (from s in Object.FindObjectsByType<RiverBuilder>((FindObjectsInactive)1, (FindObjectsSortMode)0)
				select splineProfile.Invoke(s)).Distinct().ToDictionary((SplineProfile p) => ((Object)p).name);
		}
		FlowyData flowyData = ((JToken)data).ToObject<FlowyData>();
		if (flowyData?.Points == null || flowyData.Points.Length == 0)
		{
			throw new ArgumentException("No points supplied");
		}
		if (!profiles.TryGetValue(flowyData.Profile, out SplineProfile value))
		{
			throw new ArgumentException("Cannot find profile '" + flowyData.Profile + "' (available: " + string.Join(", ", profiles.Keys.Select((string p) => '"' + p + '"')) + ")");
		}
		if ((Object)(object)rivers == (Object)null)
		{
			rivers = GameObject.Find("World/Rivers").transform;
		}
		GameObject val = new GameObject(id);
		val.SetActive(false);
		val.transform.SetParent(rivers, false);
		Vector3 center = flowyData.Points.Aggregate(Vector3.zero, (Vector3 a, SerializedRiverPoint b) => a + b.Position) / (float)flowyData.Points.Length;
		val.transform.localPosition = center;
		RiverPath obj = val.AddComponent<RiverPath>();
		obj.style = flowyData.Style;
		obj.points = ((IEnumerable<SerializedRiverPoint>)flowyData.Points).Select((Func<SerializedRiverPoint, Point>)((SerializedRiverPoint s) => new Point(s.Position - center, s.Rotation, s.Width))).ToList();
		obj.yOffset = flowyData.OffsetY;
		splineProfile.Invoke(val.AddComponent<RiverBuilder>()) = value;
		val.SetActive(true);
		return val;
	}
}
