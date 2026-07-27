using Track;
using UnityEngine;

namespace StrangeCustoms.Tracks;

public class SerializedNode
{
	public Vector3 Position { get; set; }

	public Vector3 Rotation { get; set; }

	public bool FlipSwitchStand { get; set; }

	public SerializedNode()
	{
	}

	public SerializedNode(TrackNode trackNode)
	{
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		Position = ((Component)trackNode).transform.localPosition;
		Rotation = ((Component)trackNode).transform.eulerAngles;
		FlipSwitchStand = trackNode.flipSwitchStand;
	}
}
