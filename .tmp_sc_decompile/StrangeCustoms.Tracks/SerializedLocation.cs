using System;
using Track;
using UnityEngine;

namespace StrangeCustoms.Tracks;

public class SerializedLocation
{
	public string SegmentId { get; set; }

	public float Distance { get; set; }

	public SerializedSegmentEnd End { get; set; }

	public SerializedLocation()
	{
	}

	public SerializedLocation(SerializableLocation loc)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Invalid comparison between Unknown and I4
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		SegmentId = loc.segmentId;
		Distance = loc.distance;
		End end = loc.end;
		SerializedSegmentEnd end2;
		if ((int)end != 0)
		{
			if ((int)end != 1)
			{
				throw new ArgumentException($"Invalid end {loc.end}");
			}
			end2 = SerializedSegmentEnd.End;
		}
		else
		{
			end2 = SerializedSegmentEnd.Start;
		}
		End = end2;
	}

	public static implicit operator SerializableLocation(SerializedLocation loc)
	{
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		string segmentId = loc.SegmentId;
		float distance = loc.Distance;
		return new SerializableLocation(segmentId, distance, (End)(loc.End switch
		{
			SerializedSegmentEnd.Start => 0, 
			SerializedSegmentEnd.End => 1, 
			_ => throw new ArgumentException($"Invalid track end {loc.End}"), 
		}));
	}

	internal void Validate(string type, string id, PatchingContext ctx)
	{
		if (!ctx.SegmentsById.TryGetValue(SegmentId, out TrackSegment value))
		{
			throw new SCPatchingException(type + " location on span '" + id + "' defines a segment '" + SegmentId + "' that does not seem to exist.", id + "." + type.ToLower() + ".segmentId");
		}
		if (Distance < 0f)
		{
			throw new SCPatchingException(type + " location on span '" + id + "' has a distance less than 0, which... makes not much sense", id + "." + type.ToLower() + ".distance");
		}
		float length = value.GetLength();
		if (Distance > length)
		{
			if (Distance - 0.05f > length)
			{
				throw new SCPatchingException($"{type} location on span '{id}' defines a distance '{Distance}' that exceeds segment length of {length}", id + "." + type.ToLower() + ".distance");
			}
			Distance = length - Mathf.Epsilon;
		}
	}
}
