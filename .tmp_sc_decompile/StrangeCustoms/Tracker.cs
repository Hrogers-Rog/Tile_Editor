using System;
using GalaSoft.MvvmLight.Messaging;
using Track;
using UnityEngine;

namespace StrangeCustoms;

internal class Tracker : MonoBehaviour
{
	private TrackNode[] nodes;

	private Plane[] frustrumPlanes = (Plane[])(object)new Plane[6];

	private void OnEnable()
	{
		Messenger.Default.Register<GraphDidChangeEvent>((object)this, (Action<GraphDidChangeEvent>)OnGraphDidChange);
		nodes = Object.FindObjectsByType<TrackNode>((FindObjectsInactive)1, (FindObjectsSortMode)0);
	}

	private void OnDisable()
	{
		Messenger.Default.Unregister((object)this);
	}

	private void OnGraphDidChange(GraphDidChangeEvent @event)
	{
		nodes = Object.FindObjectsByType<TrackNode>((FindObjectsInactive)1, (FindObjectsSortMode)0);
	}

	private void OnGUI()
	{
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0055: Unknown result type (might be due to invalid IL or missing references)
		//IL_005a: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		//IL_0070: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Unknown result type (might be due to invalid IL or missing references)
		//IL_007f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0084: Unknown result type (might be due to invalid IL or missing references)
		//IL_0086: Unknown result type (might be due to invalid IL or missing references)
		//IL_0094: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f3: Unknown result type (might be due to invalid IL or missing references)
		Camera main = Camera.main;
		GeometryUtility.CalculateFrustumPlanes(main, frustrumPlanes);
		Vector3 position = ((Component)main).transform.position;
		TrackNode[] array = nodes;
		Bounds val2 = default(Bounds);
		foreach (TrackNode val in array)
		{
			if (!Object.op_Implicit((Object)(object)val) || !Object.op_Implicit((Object)(object)((Component)val).transform))
			{
				continue;
			}
			Vector3 position2 = ((Component)val).transform.position;
			((Bounds)(ref val2))._002Ector(position2, Vector3.one);
			if (GeometryUtility.TestPlanesAABB(frustrumPlanes, val2))
			{
				Vector3 val3 = main.WorldToScreenPoint(position2);
				if (!(val3.x < 0f) && !(val3.y < 0f) && !(val3.x > (float)Screen.width) && !(val3.y > (float)Screen.height) && !(Vector3.SqrMagnitude(position - position2) > 25000f))
				{
					GUI.Label(new Rect(val3.x, (float)Screen.height - val3.y, 200f, 30f), val.id);
				}
			}
		}
	}
}
