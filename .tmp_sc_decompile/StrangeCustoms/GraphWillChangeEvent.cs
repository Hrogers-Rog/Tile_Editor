using System.Collections.Generic;
using StrangeCustoms.Tracks;

namespace StrangeCustoms;

public struct GraphWillChangeEvent
{
	public TrackState State;

	private readonly List<PathSegment> segments;

	internal GraphWillChangeEvent(TrackState state, List<PathSegment> segments)
	{
		State = state;
		this.segments = segments;
	}

	public void MarkChanged(params string[] path)
	{
		segments.Add(new PathSegment(path));
	}
}
