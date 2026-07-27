using System.Collections.Generic;
using System.Linq;
using SimpleGraph.Runtime;

namespace StrangeCustoms.Tracks;

public class SerializedSimpleGraph
{
	public Dictionary<string, SerializedSimpleNode> Nodes { get; internal set; } = new Dictionary<string, SerializedSimpleNode>();

	public SerializedSimpleGraph()
	{
	}

	internal SerializedSimpleGraph(Dictionary<int, string> tagMapping, SimpleGraph graph)
	{
		Nodes = graph.Nodes.ToDictionary((Node p) => $"N{p.id}", (Node p) => new SerializedSimpleNode(p, tagMapping));
	}
}
