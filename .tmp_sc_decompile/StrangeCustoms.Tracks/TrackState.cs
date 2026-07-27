using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StrangeCustoms.Tracks;

public class TrackState
{
	public GraphTracks Tracks { get; internal set; } = new GraphTracks();

	public Dictionary<string, SerializedArea> Areas { get; internal set; } = new Dictionary<string, SerializedArea>();

	public Dictionary<string, SerializedLoad> Loads { get; internal set; } = new Dictionary<string, SerializedLoad>();

	public Dictionary<string, string> Texts { get; internal set; } = new Dictionary<string, string>();

	public Dictionary<string, SerializedScenery> Scenery { get; internal set; } = new Dictionary<string, SerializedScenery>();

	public Dictionary<string, JObject> Splineys { get; internal set; } = new Dictionary<string, JObject>();

	public Dictionary<string, SerializedSimpleGraph> SimpleGraphs { get; internal set; } = new Dictionary<string, SerializedSimpleGraph>();

	public Dictionary<string, Mandela> Mandelas { get; internal set; } = new Dictionary<string, Mandela>();

	[Obsolete("Move to Tracks instead.", true)]
	[JsonIgnore]
	public Dictionary<string, SerializedNode> Nodes => Tracks.Nodes;

	[Obsolete("Move to Tracks instead.", true)]
	[JsonIgnore]
	public Dictionary<string, SerializedSegment> Segments => Tracks.Segments;

	[Obsolete("Move to Tracks instead.", true)]
	[JsonIgnore]
	public Dictionary<string, SerializedSpan> Spans { get; internal set; } = new Dictionary<string, SerializedSpan>();
}
