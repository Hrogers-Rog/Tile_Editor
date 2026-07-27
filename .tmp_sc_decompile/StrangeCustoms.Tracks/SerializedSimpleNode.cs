using System.Collections.Generic;
using SimpleGraph.Runtime;
using UnityEngine;

namespace StrangeCustoms.Tracks;

public class SerializedSimpleNode
{
	public Vector3 Position { get; set; }

	public Vector3 Rotation { get; set; }

	public string? Tag { get; set; }

	public SerializedSimpleNode()
	{
	}

	internal SerializedSimpleNode(Node node, Dictionary<int, string> tagMapping)
	{
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		Position = node.position;
		Rotation = node.eulerAngles;
		if (tagMapping.TryGetValue(node.tag, out string value))
		{
			Tag = value;
		}
	}
}
