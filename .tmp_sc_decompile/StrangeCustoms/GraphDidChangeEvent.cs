using StrangeCustoms.Tracks;

namespace StrangeCustoms;

public struct GraphDidChangeEvent(TrackState state)
{
	public TrackState State = state;
}
