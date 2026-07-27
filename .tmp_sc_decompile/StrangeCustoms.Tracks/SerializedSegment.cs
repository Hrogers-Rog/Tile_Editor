using Track;

namespace StrangeCustoms.Tracks;

public class SerializedSegment
{
	public Style Style { get; set; }

	public TrackClass TrackClass { get; set; }

	public string StartId { get; set; }

	public string EndId { get; set; }

	public int Priority { get; set; }

	public int SpeedLimit { get; set; } = 45;

	public string? GroupId { get; set; }

	public SerializedSegment()
	{
	}

	public SerializedSegment(TrackSegment trackSegment)
	{
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		StartId = trackSegment.a.id;
		EndId = trackSegment.b.id;
		Style = trackSegment.style;
		TrackClass = trackSegment.trackClass;
		Priority = trackSegment.priority;
		SpeedLimit = trackSegment.speedLimit;
		GroupId = trackSegment.groupId;
	}

	internal void ApplyTo(TrackSegment trackSegment)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		trackSegment.style = Style;
		trackSegment.trackClass = TrackClass;
		trackSegment.priority = Priority;
		trackSegment.groupId = GroupId;
		trackSegment.speedLimit = SpeedLimit;
		trackSegment.InvalidateCurve();
	}
}
