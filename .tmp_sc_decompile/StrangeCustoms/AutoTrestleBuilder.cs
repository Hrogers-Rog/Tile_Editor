using System;
using System.Collections.Generic;
using System.Linq;
using AutoTrestle;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace StrangeCustoms;

internal class AutoTrestleBuilder : ISplineyBuilder
{
	private AutoTrestleProfile? profile;

	public GameObject BuildSpliney(string id, Transform parentTransform, JObject data)
	{
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_005a: Unknown result type (might be due to invalid IL or missing references)
		//IL_006e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0092: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ea: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0106: Unknown result type (might be due to invalid IL or missing references)
		//IL_010e: Expected O, but got Unknown
		if ((Object)(object)profile == (Object)null)
		{
			profile = Object.FindObjectOfType<AutoTrestle>().profile;
		}
		AutoTrestleData autoTrestleData = ((JToken)data).ToObject<AutoTrestleData>();
		if (autoTrestleData?.Points == null || autoTrestleData.Points.Length == 0)
		{
			throw new ArgumentException("No points supplied");
		}
		GameObject val = new GameObject(id);
		val.SetActive(false);
		val.transform.SetParent(parentTransform, false);
		Vector3 center = autoTrestleData.Points.Aggregate(Vector3.zero, (Vector3 a, SerializedSplinePoint b) => a + b.Position) / (float)autoTrestleData.Points.Length;
		val.transform.localPosition = center;
		AutoTrestle obj = val.AddComponent<AutoTrestle>();
		obj.controlPoints = ((IEnumerable<SerializedSplinePoint>)autoTrestleData.Points).Select((Func<SerializedSplinePoint, ControlPoint>)((SerializedSplinePoint s) => new ControlPoint
		{
			position = s.Position - center,
			rotation = Quaternion.Euler(s.Rotation)
		})).ToList();
		obj.headStyle = autoTrestleData.HeadStyle;
		obj.tailStyle = autoTrestleData.TailStyle;
		obj.profile = profile;
		val.SetActive(true);
		return val;
	}
}
