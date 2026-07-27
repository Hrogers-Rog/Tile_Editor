using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Track;
using UnityEngine;

namespace StrangeCustoms.Tracks;

public class PatchEditor
{
	private static readonly JObject resetMe = new JObject();

	private readonly string fileName;

	private JObject root;

	private JObject tracksObject;

	private UndoRedoMagic undoRedo = new UndoRedoMagic();

	public PatchEditor(string fileName)
	{
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Expected O, but got Unknown
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		//IL_0068: Unknown result type (might be due to invalid IL or missing references)
		//IL_006a: Expected O, but got Unknown
		//IL_006f: Expected O, but got Unknown
		//IL_0092: Unknown result type (might be due to invalid IL or missing references)
		//IL_009c: Expected O, but got Unknown
		//IL_00b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c3: Expected O, but got Unknown
		//IL_00e0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ea: Expected O, but got Unknown
		//IL_0107: Unknown result type (might be due to invalid IL or missing references)
		//IL_0111: Expected O, but got Unknown
		//IL_012e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0138: Expected O, but got Unknown
		this.fileName = fileName;
		root = JObject.Parse(File.ReadAllText(fileName));
		GraphPatcher.MigrateGraph(root);
		tracksObject = (JObject)root["tracks"];
		if (tracksObject == null)
		{
			JObject obj = root;
			JObject val = new JObject();
			JObject val2 = val;
			tracksObject = val;
			obj["tracks"] = (JToken)(object)val2;
		}
		if (root["splineys"] == null)
		{
			root["splineys"] = (JToken)new JObject();
		}
		if (root["scenery"] == null)
		{
			root["scenery"] = (JToken)new JObject();
		}
		if (tracksObject["nodes"] == null)
		{
			tracksObject["nodes"] = (JToken)new JObject();
		}
		if (tracksObject["segments"] == null)
		{
			tracksObject["segments"] = (JToken)new JObject();
		}
		if (tracksObject["spans"] == null)
		{
			tracksObject["spans"] = (JToken)new JObject();
		}
	}

	private void ChangeThing(string key, string id, JObject? next, bool onRoot = false)
	{
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Expected O, but got Unknown
		JObject val = (JObject)(onRoot ? root : tracksObject)[key];
		object obj;
		if (!val.ContainsKey(id))
		{
			obj = resetMe;
		}
		else
		{
			JToken obj2 = val[id];
			obj = ((obj2 != null) ? obj2.DeepClone() : null);
		}
		JToken item = (JToken)obj;
		undoRedo.Record(new UndoRedoAction
		{
			Undo = UndoRedoThing,
			Redo = UndoRedoThing,
			UndoState = (key, id, item, onRoot),
			RedoState = (key, id, next, onRoot)
		});
	}

	private void UndoRedoThing(object value)
	{
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Expected O, but got Unknown
		(string, string, JObject, bool) obj = ((string, string, JObject, bool))value;
		string item = obj.Item1;
		string item2 = obj.Item2;
		JObject item3 = obj.Item3;
		JObject val = (JObject)(obj.Item4 ? root : tracksObject)[item];
		if (item3 == resetMe)
		{
			val.Remove(item2);
		}
		else
		{
			val[item2] = (JToken)(object)item3;
		}
	}

	public void AddOrUpdateNode(string id, Vector3 position, Vector3 eulerRotation, bool flipSwitchStand = false)
	{
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		ChangeThing("nodes", id, JObject.FromObject((object)new SerializedNode
		{
			Position = position,
			FlipSwitchStand = flipSwitchStand,
			Rotation = eulerRotation
		}, GraphPatcher.Serializer));
	}

	public void AddOrUpdateSegment(string segmentId, string startId, string endId, int priority = 0, string? groupId = null, int speedLimit = 0, Style style = (Style)0, TrackClass trackClass = (TrackClass)0)
	{
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		ChangeThing("segments", segmentId, JObject.FromObject((object)new SerializedSegment
		{
			StartId = startId,
			EndId = endId,
			GroupId = groupId,
			Priority = priority,
			SpeedLimit = speedLimit,
			Style = style,
			TrackClass = trackClass
		}, GraphPatcher.Serializer));
	}

	public void AddOrUpdateSpan(string spanId, string lowerId, float lowerDistance, SerializedSegmentEnd lowerEnd, string upperId, float upperDistance, SerializedSegmentEnd upperEnd, bool normalize = false)
	{
		ChangeThing("spans", spanId, JObject.FromObject((object)new SerializedSpan
		{
			Lower = new SerializedLocation
			{
				SegmentId = lowerId,
				Distance = lowerDistance,
				End = lowerEnd
			},
			Upper = new SerializedLocation
			{
				SegmentId = upperId,
				Distance = upperDistance,
				End = upperEnd
			},
			Normalize = normalize
		}, GraphPatcher.Serializer));
	}

	public void AddOrUpdateSpliney(string splineyId, Func<JObject?, JObject> addOrUpdate)
	{
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Expected O, but got Unknown
		JToken obj = root["splineys"][(object)splineyId];
		JToken obj2 = ((obj != null) ? obj.DeepClone() : null);
		JObject val = (JObject)(object)((obj2 is JObject) ? obj2 : null);
		JObject next = addOrUpdate((JObject)((val != null) ? ((JToken)val).DeepClone() : null));
		ChangeThing("splineys", splineyId, next, onRoot: true);
	}

	public void AddOrUpdateScenery(string sceneryId, string modelIdentifier, Vector3 position, Vector3 eulerRotation, Vector3 scale)
	{
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		ChangeThing("scenery", sceneryId, JObject.FromObject((object)new
		{
			ModelIdentifier = modelIdentifier,
			Position = position,
			Rotation = eulerRotation,
			Scale = scale
		}), onRoot: true);
	}

	public void ResetNode(string id)
	{
		ChangeThing("nodes", id, resetMe);
	}

	public void ResetSegment(string id)
	{
		ChangeThing("segments", id, resetMe);
	}

	public void ResetSpan(string id)
	{
		ChangeThing("spans", id, resetMe);
	}

	public void ResetSpliney(string id)
	{
		ChangeThing("splineys", id, resetMe, onRoot: true);
	}

	public void ResetScenery(string id)
	{
		ChangeThing("scenery", id, resetMe, onRoot: true);
	}

	public void RemoveNode(string id)
	{
		ChangeThing("nodes", id, null);
	}

	public void RemoveSegment(string id)
	{
		ChangeThing("segments", id, null);
	}

	public void RemoveSpan(string id)
	{
		ChangeThing("spans", id, null);
	}

	public void RemoveSpliney(string id)
	{
		ChangeThing("splineys", id, null, onRoot: true);
	}

	public void RemoveScenery(string id)
	{
		ChangeThing("scenery", id, null, onRoot: true);
	}

	public Dictionary<string, JObject> GetNodes()
	{
		return GetObject(tracksObject["nodes"]);
	}

	public Dictionary<string, JObject> GetSegments()
	{
		return GetObject(tracksObject["segments"]);
	}

	public Dictionary<string, JObject> GetSpans()
	{
		return GetObject(tracksObject["spans"]);
	}

	public Dictionary<string, JObject> GetSplineys()
	{
		return GetObject(root["splineys"]);
	}

	public Dictionary<string, JObject> GetScenery()
	{
		return GetObject(root["scenery"]);
	}

	private Dictionary<string, JObject> GetObject(JToken obj)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		return ((JObject)obj).Properties().ToDictionary((Func<JProperty, string>)((JProperty p) => p.Name), (Func<JProperty, JObject>)((JProperty p) => (JObject)p.Value.DeepClone()));
	}

	public bool Undo()
	{
		return undoRedo.Undo();
	}

	public bool Redo()
	{
		return undoRedo.Redo();
	}

	public void Save()
	{
		GraphPatcher.SaveObject(root, fileName);
	}
}
