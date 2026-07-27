using System.Collections.Generic;

namespace StrangeCustoms.Tracks;

public class GraphTracks
{
	public Dictionary<string, SerializedNode> Nodes { get; internal set; } = new Dictionary<string, SerializedNode>();

	public Dictionary<string, SerializedSegment> Segments { get; internal set; } = new Dictionary<string, SerializedSegment>();

	public Dictionary<string, SerializedSpan> Spans { get; internal set; } = new Dictionary<string, SerializedSpan>();
}
